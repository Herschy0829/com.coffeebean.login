#if COFFEEBEAN_FIREBASE_AUTH
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase;
using Firebase.Auth;

namespace CoffeeBean
{
    /// <summary>
    /// Firebase Auth 后端适配层（**只在装了 Firebase 且定义了 <c>COFFEEBEAN_FIREBASE_AUTH</c> 时编译**）。
    ///
    /// 依据官方文档（Firebase Unity SDK 13.x）：
    /// · **不需要**手工 <c>FirebaseApp.Create()</c>：默认实例会按配置文件自动创建；
    /// · 但**必须**先 <c>FirebaseApp.CheckAndFixDependenciesAsync()</c> 并确认
    ///   <c>DependencyStatus.Available</c>（Android 上 Google Play 服务版本过旧会在这里暴露）；
    /// · Google 登录在 Unity 里**没有官方 UI**：idToken 由平台侧（Android Credential Manager / iOS GoogleSignIn）
    ///   取好后交给 <c>GoogleAuthProvider.GetCredential(idToken, accessToken)</c> —— 本模块的
    ///   <see cref="ILoginProvider"/> 正是干这件事的；
    /// · 桌面/编辑器上 <c>SignInWithProviderAsync</c> 系列不可用，但"自己拿到 token 再换凭据"这条路可用。
    ///
    /// 线程：Firebase 的 Task 不保证回主线程，本类统一用 tools 的
    /// <c>LoginMainThread.Run</c> 回调，符合 <see cref="ILoginBackend"/> 的约定。
    /// </summary>
    public sealed class FirebaseLoginBackend : ILoginBackend
    {
        private readonly Queue<Action> _pending = new Queue<Action>();

        private FirebaseAuth _auth;
        private bool _ready;
        private string _unavailable;

        public FirebaseLoginBackend()
        {
            try
            {
                FirebaseApp.CheckAndFixDependenciesAsync().ContinueWith(task => LoginMainThread.Run(() =>
                {
                    if (task.IsCanceled)
                    {
                        _unavailable = "Firebase 依赖检查被取消。";
                        LoginLog.Error(_unavailable);
                        DrainPending();
                        return;
                    }
                    if (task.IsFaulted)
                    {
                        _unavailable = "Firebase 依赖检查失败：" + Describe(task.Exception);
                        LoginLog.Error(_unavailable);
                        DrainPending();
                        return;
                    }
                    if (task.Result != DependencyStatus.Available)
                    {
                        _unavailable = "Firebase 依赖不可用（DependencyStatus = " + task.Result + "）。"
                                       + "Android 上通常是 Google Play 服务版本过旧。";
                        LoginLog.Error(_unavailable);
                        DrainPending();
                        return;
                    }

                    try
                    {
                        _auth = FirebaseAuth.DefaultInstance;
                        _auth.StateChanged += OnAuthStateChanged;
                        _ready = true;
                        LoginLog.Log("Firebase Auth 就绪；当前用户 = "
                                     + (_auth.CurrentUser != null ? _auth.CurrentUser.UserId : "无"));
                    }
                    catch (Exception e)
                    {
                        _unavailable = "取 FirebaseAuth.DefaultInstance 失败：" + e.Message
                                       + "（配置文件 google-services.json / GoogleService-Info.plist 是否放在 Assets/ 根目录？）";
                        LoginLog.Error(_unavailable);
                    }
                    DrainPending();
                }));
            }
            catch (Exception e)
            {
                _unavailable = "初始化 Firebase 失败：" + e.Message;
                LoginLog.Error(_unavailable);
            }
        }

        public string Name => "Firebase Auth";

        /// <summary>依赖检查完成前先按"可用"算（<see cref="ILoginBackend.IsAvailable"/> 是同步 API，不能等异步）。</summary>
        public bool IsAvailable => _unavailable == null;

        public string UnavailableReason => _unavailable ?? string.Empty;

        /// <summary>依赖检查是否已完成（业务一般不需要关心；诊断用）。</summary>
        public bool IsReady => _ready;

        public LoginUser CurrentUser => ToLoginUser(_auth != null ? _auth.CurrentUser : null);

        public event Action<LoginUser> StateChanged;

        // ========== ILoginBackend ==========

        public void SignInWithCredential(LoginCredential credential, Action<LoginUser> onSuccess, Action<LoginErrorCode, string> onError)
        {
            if (credential == null)
            {
                Fail(onError, LoginErrorCode.InvalidCredential, "凭据为 null。");
                return;
            }

            Credential firebaseCredential = ToFirebaseCredential(credential, onError);
            if (firebaseCredential == null) return;

            WhenReady(() =>
            {
                LoginLog.Debug_("Firebase：SignInWithCredentialAsync(" + credential.ProviderId + ")");
                _auth.SignInWithCredentialAsync(firebaseCredential).ContinueWith(task => LoginMainThread.Run(() =>
                {
                    if (!CheckTask(task, "凭据登录", onError)) return;
                    Succeed(task.Result, onSuccess);
                }));
            }, onError);
        }

        public void SignInAnonymously(Action<LoginUser> onSuccess, Action<LoginErrorCode, string> onError)
        {
            WhenReady(() =>
            {
                LoginLog.Debug_("Firebase：SignInAnonymouslyAsync()");
                _auth.SignInAnonymouslyAsync().ContinueWith(task => LoginMainThread.Run(() =>
                {
                    if (!CheckTask(task, "匿名登录", onError)) return;
                    Succeed(task.Result != null ? task.Result.User : null, onSuccess);
                }));
            }, onError);
        }

        public void LinkWithCredential(LoginCredential credential, Action<LoginUser> onSuccess, Action<LoginErrorCode, string> onError)
        {
            if (credential == null)
            {
                Fail(onError, LoginErrorCode.InvalidCredential, "凭据为 null。");
                return;
            }

            Credential firebaseCredential = ToFirebaseCredential(credential, onError);
            if (firebaseCredential == null) return;

            WhenReady(() =>
            {
                FirebaseUser current = _auth.CurrentUser;
                if (current == null)
                {
                    Fail(onError, LoginErrorCode.NotSignedIn, "当前没有 Firebase 会话，无法绑定（先 SignInAsGuest）。");
                    return;
                }

                LoginLog.Debug_("Firebase：LinkWithCredentialAsync(" + credential.ProviderId + ")，当前 uid = " + current.UserId);
                current.LinkWithCredentialAsync(firebaseCredential).ContinueWith(task => LoginMainThread.Run(() =>
                {
                    if (!CheckTask(task, "绑定凭据", onError)) return;
                    Succeed(task.Result != null ? task.Result.User : null, onSuccess);
                }));
            }, onError);
        }

        public void SignInWithEmail(string email, string password, Action<LoginUser> onSuccess, Action<LoginErrorCode, string> onError)
        {
            WhenReady(() =>
            {
                LoginLog.Debug_("Firebase：SignInWithEmailAndPasswordAsync(" + email + ")");
                _auth.SignInWithEmailAndPasswordAsync(email, password).ContinueWith(task => LoginMainThread.Run(() =>
                {
                    if (!CheckTask(task, "邮箱登录", onError)) return;
                    Succeed(task.Result != null ? task.Result.User : null, onSuccess);
                }));
            }, onError);
        }

        public void SignOut()
        {
            if (_auth == null) return;
            _auth.SignOut();
            LoginLog.Log("Firebase：已登出。");
        }

        public void GetIdToken(bool forceRefresh, Action<string> onSuccess, Action<LoginErrorCode, string> onError)
        {
            WhenReady(() =>
            {
                FirebaseUser user = _auth.CurrentUser;
                if (user == null)
                {
                    Fail(onError, LoginErrorCode.NotSignedIn, "Firebase 当前没有登录用户。");
                    return;
                }

                user.TokenAsync(forceRefresh).ContinueWith(task => LoginMainThread.Run(() =>
                {
                    if (!CheckTask(task, "取 ID token", onError)) return;
                    if (onSuccess != null) onSuccess(task.Result);
                }));
            }, onError);
        }

        // ========== 内部 ==========

        private Credential ToFirebaseCredential(LoginCredential credential, Action<LoginErrorCode, string> onError)
        {
            switch (credential.Kind)
            {
                case LoginProviderKind.Google:
                    if (string.IsNullOrEmpty(credential.IdToken))
                    {
                        Fail(onError, LoginErrorCode.InvalidCredential,
                            "Google 凭据缺 idToken。Android 的 Credential Manager 会给 idToken（accessToken 可以为 null）；" +
                            "检查 Google 登录插件/桥接是否真的把 idToken 传回来了。");
                        return null;
                    }
                    // Android：只有 idToken（accessToken 传 null）；iOS：两个都有
                    return GoogleAuthProvider.GetCredential(credential.IdToken, credential.AccessToken);

                case LoginProviderKind.Email:
                    Fail(onError, LoginErrorCode.InvalidCredential, "邮箱登录请走 SignInWithEmail。");
                    return null;

                default:
                    Fail(onError, LoginErrorCode.ProviderUnavailable,
                        "Firebase 后端没有内置 " + credential.Kind + " 的凭据转换：自定义方式请在这里扩展（OAuthProvider 等）。");
                    return null;
            }
        }

        private void WhenReady(Action action, Action<LoginErrorCode, string> onError)
        {
            if (_unavailable != null)
            {
                Fail(onError, LoginErrorCode.ProviderUnavailable, _unavailable);
                return;
            }
            if (_ready && _auth != null)
            {
                action();
                return;
            }

            LoginLog.Debug_("Firebase 尚未就绪，排队等待依赖检查完成。");
            _pending.Enqueue(() =>
            {
                if (_unavailable != null)
                {
                    Fail(onError, LoginErrorCode.ProviderUnavailable, _unavailable);
                    return;
                }
                action();
            });
        }

        private void DrainPending()
        {
            while (_pending.Count > 0)
            {
                Action action = _pending.Dequeue();
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    LoginLog.Error("执行排队的 Firebase 操作失败：" + e.Message, e);
                }
            }
        }

        private bool CheckTask<T>(Task<T> task, string operation, Action<LoginErrorCode, string> onError)
        {
            if (task.IsCanceled)
            {
                Fail(onError, LoginErrorCode.Cancelled, operation + "被取消。");
                return false;
            }
            if (task.IsFaulted)
            {
                LoginErrorCode code = MapFirebaseError(task.Exception, out string message);
                Fail(onError, code, operation + "失败：" + message);
                return false;
            }
            return true;
        }

        private void Succeed(FirebaseUser user, Action<LoginUser> onSuccess)
        {
            LoginUser loginUser = ToLoginUser(user);
            if (loginUser == null || loginUser.IsEmpty)
            {
                LoginLog.Warn("Firebase 返回了空用户（登录可能没真正生效）。");
            }
            else
            {
                LoginLog.Log("Firebase 登录成功：" + loginUser);
            }
            if (onSuccess != null) onSuccess(loginUser);
        }

        private void Fail(Action<LoginErrorCode, string> onError, LoginErrorCode code, string message)
        {
            LoginLog.Warn(message);
            if (onError != null) onError(code, message);
        }

        private void OnAuthStateChanged(object sender, EventArgs e)
        {
            LoginUser user = CurrentUser;
            LoginLog.Debug_("Firebase StateChanged → " + (user != null ? user.ToString() : "(空)"));
            var handler = StateChanged;
            if (handler != null) handler(user);
        }

        private static LoginUser ToLoginUser(FirebaseUser user)
        {
            if (user == null) return null;
            return new LoginUser
            {
                UserId = user.UserId,
                DisplayName = user.DisplayName,
                Email = user.Email,
                PhotoUrl = user.PhotoUrl != null ? user.PhotoUrl.ToString() : null,
                IsAnonymous = user.IsAnonymous,
                ProviderId = FirstProviderId(user),
            };
        }

        private static string FirstProviderId(FirebaseUser user)
        {
            try
            {
                if (user.ProviderData != null)
                {
                    foreach (IUserInfo info in user.ProviderData)
                    {
                        if (info != null && !string.IsNullOrEmpty(info.ProviderId)) return info.ProviderId;
                    }
                }
            }
            catch (Exception)
            {
                // ProviderData 在个别平台/版本上可能抛：不影响主流程，退回按匿名判断
            }
            return user.IsAnonymous ? "anonymous" : string.Empty;
        }

        /// <summary>
        /// Firebase 错误 → 模块错误码。
        ///
        /// **刻意用枚举名字符串比较**（而不是直接 switch 枚举成员）：<c>AuthError</c> 的成员在不同 SDK 版本里
        /// 有增减，写死成员名会让"换个 Firebase 版本就编译不过"。这里只按官方文档里稳定出现的语义分类，
        /// 认不出来的一律 <see cref="LoginErrorCode.BackendError"/>（保留原始描述给业务看）。
        /// </summary>
        private static LoginErrorCode MapFirebaseError(AggregateException exception, out string message)
        {
            message = Describe(exception);
            var firebaseException = exception != null ? exception.GetBaseException() as FirebaseException : null;
            if (firebaseException == null) return LoginErrorCode.Unknown;

            string name = ((AuthError)firebaseException.ErrorCode).ToString();
            message = name + "：" + firebaseException.Message;

            switch (name)
            {
                case "InvalidCredential":
                case "InvalidProviderId":
                case "InvalidEmail":
                case "WrongPassword":
                case "UserNotFound":
                    return LoginErrorCode.InvalidCredential;

                case "CredentialAlreadyInUse":
                case "EmailAlreadyInUse":
                case "AccountExistsWithDifferentCredential":
                    return LoginErrorCode.AccountExists;

                case "ProviderAlreadyLinked":
                    return LoginErrorCode.AlreadyLinked;

                case "NetworkRequestFailed":
                    return LoginErrorCode.NetworkError;

                case "TooManyRequests":
                case "UserDisabled":
                case "OperationNotAllowed":
                case "RequiresRecentLogin":
                    return LoginErrorCode.BackendError;

                default:
                    return LoginErrorCode.BackendError;
            }
        }

        private static string Describe(Exception exception)
        {
            if (exception == null) return "(无异常信息)";
            Exception baseException = exception.GetBaseException();
            return baseException != null ? baseException.Message : exception.Message;
        }
    }
}
#endif
