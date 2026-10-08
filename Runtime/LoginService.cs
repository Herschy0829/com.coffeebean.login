using System;
using System.Collections.Generic;
using System.Text;

namespace CoffeeBean
{
    /// <summary>
    /// 登录门面：业务只跟这个类打交道。
    ///
    /// 三层结构（每层都能单独替换/单独测试）：
    /// <code>
    ///   LoginService（本类：状态机 + 事件 + 游客转正流程）
    ///        ├─ ILoginProvider  ：拿第三方凭据（Google / Apple / 自定义）—— 依赖平台 SDK
    ///        └─ ILoginBackend   ：把凭据换成会话（Firebase Auth / 自建服务）—— 依赖后端
    /// </code>
    ///
    /// 约定：
    /// · **所有回调都在主线程**（内部统一走 <c>LoginMainThread.Run</c>）；
    /// · 登录是**串行**的：已有登录流程在跑时再调用会以 <see cref="LoginErrorCode.ProviderUnavailable"/> 之外的
    ///   方式直接返回失败（<see cref="LoginState.SigningIn"/> 期间拒绝重入），避免弹两次 Google 窗口；
    /// · 用户取消不算错误（<see cref="LoginErrorCode.Cancelled"/>），业务侧不要弹报错框。
    /// </summary>
    public sealed class LoginService
    {
        private readonly Dictionary<LoginProviderKind, ILoginProvider> _byKind =
            new Dictionary<LoginProviderKind, ILoginProvider>();

        private readonly List<ILoginProvider> _providers = new List<ILoginProvider>();
        private readonly ILoginBackend _backend;

        private LoginSettings _settings;
        private LoginState _state = LoginState.NotInitialized;
        private LoginUser _user;
        private bool _initialized;
        private bool _busy;

        private static LoginService _instance;

        /// <summary>
        /// 默认实例：按工程里装了什么自动装配（见 <see cref="LoginFactory.CreateDefault"/>）。
        /// 想自己控制装配（测试/自建后端）就 <c>new LoginService(providers, backend)</c>，不要用这个。
        /// </summary>
        public static LoginService Instance
        {
            get
            {
                if (_instance == null) _instance = LoginFactory.CreateDefault();
                return _instance;
            }
        }

        /// <summary>用指定的提供者列表与后端构造。</summary>
        public LoginService(IEnumerable<ILoginProvider> providers, ILoginBackend backend, LoginSettings settings = null)
        {
            _backend = backend != null ? backend : new UnavailableLoginBackend("backend 为 null");
            _settings = settings;

            if (providers != null)
            {
                foreach (ILoginProvider provider in providers)
                {
                    if (provider == null) continue;
                    _providers.Add(provider);
                    _byKind[provider.Kind] = provider;
                }
            }
        }

        // ========== 只读状态 ==========

        /// <summary>当前状态。</summary>
        public LoginState State => _state;

        /// <summary>当前用户（未登录为 null；返回的是内部实例的副本，可安全持有）。</summary>
        public LoginUser CurrentUser => _user != null ? _user.Clone() : null;

        /// <summary>是否已登录。</summary>
        public bool IsSignedIn => _user != null && !_user.IsEmpty;

        /// <summary>是否已初始化。</summary>
        public bool IsInitialized => _initialized;

        /// <summary>是否正在登录。</summary>
        public bool IsBusy => _busy;

        /// <summary>后端（高级用法：直接调后面的接口/读后端自身状态）。</summary>
        public ILoginBackend Backend => _backend;

        /// <summary>配置（可能为 null = 用默认行为）。</summary>
        public LoginSettings Settings => _settings;

        /// <summary>已注册的登录方式。</summary>
        public IReadOnlyList<ILoginProvider> Providers => _providers;

        /// <summary>
        /// 按类型找已注册的提供者（典型用法：拿外部凭据入口）
        /// <c>service.GetProvider&lt;ExternalCredentialProvider&gt;()?.Submit(idToken);</c>
        /// </summary>
        public T GetProvider<T>() where T : class, ILoginProvider
        {
            for (int i = 0; i < _providers.Count; i++)
            {
                var typed = _providers[i] as T;
                if (typed != null) return typed;
            }
            return null;
        }

        /// <summary>当前平台/环境下**真正可用**的登录方式（Guest 只要后端可用就算可用）。</summary>
        public List<LoginProviderKind> AvailableProviders
        {
            get
            {
                var result = new List<LoginProviderKind>();
                if (_backend.IsAvailable) result.Add(LoginProviderKind.Guest);
                foreach (ILoginProvider provider in _providers)
                {
                    if (provider.IsAvailable) result.Add(provider.Kind);
                }
                return result;
            }
        }

        // ========== 事件 ==========

        /// <summary>状态变化。</summary>
        public event Action<LoginState> OnStateChanged;

        /// <summary>登录成功（含游客转正成功）。</summary>
        public event Action<LoginUser> OnSignedIn;

        /// <summary>用户信息变化（登录成功、绑定后补全邮箱/名字等）。</summary>
        public event Action<LoginUser> OnUserChanged;

        /// <summary>已登出。</summary>
        public event Action OnSignedOut;

        /// <summary>失败（错误码 + 描述）。</summary>
        public event Action<LoginErrorCode, string> OnFailed;

        // ========== 生命周期 ==========

        /// <summary>
        /// 初始化：装配状态、接后端事件、按配置决定是否自动游客登录。
        /// 可以重复调用（用于换 settings），但正在登录时会被忽略。
        /// </summary>
        public void Initialize(LoginSettings settings = null)
        {
            if (_busy)
            {
                LoginLog.Warn("正在登录中，Initialize 被忽略（等这次流程结束再调）。");
                return;
            }

            if (settings != null) _settings = settings;

            LoginMainThread.Capture();   // 记录主线程 id（后面所有回调都靠它切回主线程）

            _backend.StateChanged -= OnBackendStateChanged;
            _backend.StateChanged += OnBackendStateChanged;

            LoginUser backendUser = _backend.CurrentUser;
            if (backendUser != null && !backendUser.IsEmpty)
            {
                SetUser(backendUser, LoginState.SignedIn, fireSignedIn: false);
            }
            else
            {
                SetState(LoginState.SignedOut);
            }

            _initialized = true;
            LoginLog.Log("初始化完成。后端 = " + _backend.Name
                         + (("可用") == (_backend.IsAvailable ? "可用" : "不可用") ? string.Empty : "（不可用：" + _backend.UnavailableReason + "）"));
            LoginLog.Log(DescribeProviders());

            if (_settings != null)
            {
                foreach (string issue in _settings.Validate(_backend.IsAvailable))
                {
                    LoginLog.Warn("配置问题：" + issue);
                }

                if (_settings.SignInAsGuestOnStart && !IsSignedIn)
                {
                    SignInAsGuest();
                }
            }
        }

        // ========== 登录 ==========

        /// <summary>按登录方式登录（Guest 走匿名登录；其余走 provider → backend）。</summary>
        public void SignIn(LoginProviderKind kind, Action<LoginResult> onDone = null)
        {
            if (!EnsureReady(kind, onDone)) return;

            if (_busy)
            {
                Complete(LoginResult.Fail(LoginErrorCode.Unknown, "已有登录流程在进行中，请等它结束（避免同时弹两个授权窗口）。"), onDone);
                return;
            }

            if (kind == LoginProviderKind.Guest)
            {
                _busy = true;
                SetState(LoginState.SigningIn);
                _backend.SignInAnonymously(
                    user => FinishSignIn(user, onDone),
                    (code, error) => FinishFailure(code, error, onDone));
                return;
            }

            if (kind == LoginProviderKind.Email)
            {
                Complete(LoginResult.Fail(LoginErrorCode.InvalidCredential, "邮箱登录请用 SignInWithEmail(email, password, onDone)。"), onDone);
                return;
            }

            ILoginProvider provider;
            if (!_byKind.TryGetValue(kind, out provider) || provider == null)
            {
                Complete(LoginResult.Fail(LoginErrorCode.ProviderUnavailable,
                    "没有注册 " + kind + " 登录方式（可用的：" + JoinKinds(AvailableProviders) + "）。"), onDone);
                return;
            }

            if (!provider.IsAvailable)
            {
                Complete(LoginResult.Fail(LoginErrorCode.ProviderUnavailable,
                    kind + " 在当前环境不可用：" + provider.UnavailableReason), onDone);
                return;
            }

            _busy = true;
            SetState(LoginState.SigningIn);
            LoginLog.Debug_("开始登录：" + kind);

            provider.SignIn(
                credential => _backend.SignInWithCredential(credential,
                    user => FinishSignIn(user, onDone),
                    (code, error) => FinishFailure(code, error, onDone)),
                (code, error) => FinishFailure(code, error, onDone));
        }

        /// <summary>游客（匿名）登录：任何可用的后端都必须支持，业务应该在启动时先调它。</summary>
        public void SignInAsGuest(Action<LoginResult> onDone = null) => SignIn(LoginProviderKind.Guest, onDone);

        /// <summary>Google 登录（等价 <c>SignIn(LoginProviderKind.Google)</c>）。</summary>
        public void SignInWithGoogle(Action<LoginResult> onDone = null) => SignIn(LoginProviderKind.Google, onDone);

        /// <summary>邮箱 + 密码登录（需要后端支持，如 Firebase Email/Password）。</summary>
        public void SignInWithEmail(string email, string password, Action<LoginResult> onDone = null)
        {
            if (!EnsureReady(LoginProviderKind.Email, onDone)) return;
            if (_busy)
            {
                Complete(LoginResult.Fail(LoginErrorCode.Unknown, "已有登录流程在进行中。"), onDone);
                return;
            }

            _busy = true;
            SetState(LoginState.SigningIn);
            _backend.SignInWithEmail(email, password,
                user => FinishSignIn(user, onDone),
                (code, error) => FinishFailure(code, error, onDone));
        }

        /// <summary>
        /// **游客转正**：把第三方账号绑定到当前会话（保留 uid，进度不丢）。
        /// 未登录时直接失败；后端若返回"该凭据已被别的账号占用"，错误码是
        /// <see cref="LoginErrorCode.AccountExists"/> —— 这时业务应该改成"登录那个账号"（或做账号合并）。
        /// </summary>
        public void Link(LoginProviderKind kind, Action<LoginResult> onDone = null)
        {
            if (!EnsureReady(kind, onDone)) return;

            if (!IsSignedIn)
            {
                Complete(LoginResult.Fail(LoginErrorCode.NotSignedIn, "绑定前必须先有会话（先 SignInAsGuest）。"), onDone);
                return;
            }

            if (_busy)
            {
                Complete(LoginResult.Fail(LoginErrorCode.Unknown, "已有登录流程在进行中。"), onDone);
                return;
            }

            ILoginProvider provider;
            if (kind == LoginProviderKind.Guest || !_byKind.TryGetValue(kind, out provider) || provider == null)
            {
                Complete(LoginResult.Fail(LoginErrorCode.ProviderUnavailable, "不能绑定 " + kind + "。"), onDone);
                return;
            }

            if (!provider.IsAvailable)
            {
                Complete(LoginResult.Fail(LoginErrorCode.ProviderUnavailable,
                    kind + " 在当前环境不可用：" + provider.UnavailableReason), onDone);
                return;
            }

            _busy = true;
            SetState(LoginState.SigningIn);
            LoginLog.Debug_("开始绑定：" + kind + " → 当前用户 " + _user);

            provider.SignIn(
                credential => _backend.LinkWithCredential(credential,
                    user => FinishSignIn(user, onDone),
                    (code, error) => FinishFailure(code, error, onDone)),
                (code, error) => FinishFailure(code, error, onDone));
        }

        /// <summary>Google 转正（等价 <c>Link(LoginProviderKind.Google)</c>）。</summary>
        public void LinkWithGoogle(Action<LoginResult> onDone = null) => Link(LoginProviderKind.Google, onDone);

        /// <summary>登出（只清会话；不删账号）。</summary>
        public void SignOut()
        {
            _backend.SignOut();
            SetUser(null, LoginState.SignedOut, fireSignedIn: false);
            var handler = OnSignedOut;
            if (handler != null) LoginMainThread.Run(handler);
        }

        /// <summary>取 ID token（给**自建服务**校验身份用；<paramref name="forceRefresh"/> = 强制刷新）。</summary>
        public void GetIdToken(Action<string> onSuccess,
            Action<LoginErrorCode, string> onError = null,
            bool forceRefresh = false)
        {
            if (!_initialized)
            {
                Fail(LoginErrorCode.NotInitialized, "LoginService 还没 Initialize。", onError);
                return;
            }
            if (!IsSignedIn)
            {
                Fail(LoginErrorCode.NotSignedIn, "未登录，取不到 ID token。", onError);
                return;
            }

            _backend.GetIdToken(forceRefresh,
                token => { if (onSuccess != null) LoginMainThread.Run(() => onSuccess(token)); },
                (code, error) => Fail(code, error, onError));
        }

        // ========== 诊断 ==========

        /// <summary>把"当前可用的登录方式 + 每项为什么不可用 + 后端状态"整理成一段可读文本（设置窗口/日志用）。</summary>
        public string DescribeProviders()
        {
            var sb = new StringBuilder();
            sb.Append("后端：").Append(_backend.Name)
              .Append(_backend.IsAvailable ? "（可用）" : "（不可用：" + _backend.UnavailableReason + "）");
            sb.AppendLine();
            sb.Append("游客/匿名：").Append(_backend.IsAvailable ? "可用" : "不可用（后端不可用）");

            foreach (ILoginProvider provider in _providers)
            {
                sb.AppendLine();
                sb.Append(provider.Kind).Append("：");
                sb.Append(provider.IsAvailable ? "可用" : "不可用（" + provider.UnavailableReason + "）");
            }

            if (_providers.Count == 0)
            {
                sb.AppendLine();
                sb.Append("第三方登录：一个都没装配（没装 Google Sign-In 插件，或编辑器模拟已关闭）");
            }

            return sb.ToString();
        }

        // ========== 内部 ==========

        private bool EnsureReady(LoginProviderKind kind, Action<LoginResult> onDone)
        {
            if (!_initialized)
            {
                Complete(LoginResult.Fail(LoginErrorCode.NotInitialized, "LoginService 还没 Initialize（先在启动流程里调一次）。"), onDone);
                return false;
            }
            if (!_backend.IsAvailable && kind != LoginProviderKind.Guest)
            {
                // 第三方登录理论上能用（后端可能只是本平台不可用），这里仍放行，让具体错误在下面暴露；
                // 但后端完全不可用时明确提示，避免业务以为"代码没生效"。
                LoginLog.Debug_("后端不可用：" + _backend.UnavailableReason);
            }
            return true;
        }

        private void FinishSignIn(LoginUser user, Action<LoginResult> onDone)
        {
            _busy = false;
            if (user == null || user.IsEmpty)
            {
                FinishFailure(LoginErrorCode.BackendError, "后端返回了空用户。", onDone);
                return;
            }

            bool alreadySignedIn = IsSignedIn;
            SetUser(user, LoginState.SignedIn, fireSignedIn: false);
            LoginLog.Log("登录成功：" + _user);

            var signedIn = OnSignedIn;
            if (signedIn != null)
            {
                LoginUser snapshot = _user.Clone();
                LoginMainThread.Run(() => signedIn(snapshot));
            }
            if (alreadySignedIn)
            {
                LoginLog.Debug_("（本次是游客转正/换账号：uid 可能已变化，业务应重新拉取该账号的数据）");
            }

            Complete(LoginResult.Ok(CurrentUser), onDone);
        }

        private void FinishFailure(LoginErrorCode code, string error, Action<LoginResult> onDone)
        {
            _busy = false;
            SetState(LoginState.Failed);
            if (code == LoginErrorCode.Cancelled)
            {
                LoginLog.Log("用户取消了登录（不是错误）。");
            }
            else
            {
                LoginLog.Warn("登录失败：" + code + " —— " + error);
            }

            var handler = OnFailed;
            if (handler != null)
            {
                LoginMainThread.Run(() => handler(code, error));
            }

            Complete(LoginResult.Fail(code, error), onDone);
        }

        private void Fail(LoginErrorCode code, string error, Action<LoginErrorCode, string> onError)
        {
            LoginLog.Warn(error);
            if (onError != null) LoginMainThread.Run(() => onError(code, error));
        }

        private void Complete(LoginResult result, Action<LoginResult> onDone)
        {
            if (onDone == null) return;
            LoginMainThread.Run(() => onDone(result));
        }

        private void OnBackendStateChanged(LoginUser user)
        {
            if (user == null || user.IsEmpty)
            {
                SetUser(null, LoginState.SignedOut, fireSignedIn: false);
                var signedOut = OnSignedOut;
                if (signedOut != null) LoginMainThread.Run(signedOut);
                return;
            }

            SetUser(user, LoginState.SignedIn, fireSignedIn: false);
        }

        private void SetState(LoginState state)
        {
            if (_state == state) return;
            _state = state;
            var handler = OnStateChanged;
            if (handler != null)
            {
                LoginState snapshot = state;
                LoginMainThread.Run(() => handler(snapshot));
            }
        }

        private void SetUser(LoginUser user, LoginState state, bool fireSignedIn)
        {
            _user = user;
            SetState(state);
            if (user == null || user.IsEmpty) return;

            var changed = OnUserChanged;
            if (changed != null)
            {
                LoginUser snapshot = user.Clone();
                LoginMainThread.Run(() => changed(snapshot));
            }

            if (!fireSignedIn) return;
            var signedIn = OnSignedIn;
            if (signedIn != null)
            {
                LoginUser snapshot = user.Clone();
                LoginMainThread.Run(() => signedIn(snapshot));
            }
        }

        private static string JoinKinds(List<LoginProviderKind> kinds)
        {
            if (kinds == null || kinds.Count == 0) return "无";
            var sb = new StringBuilder();
            for (int i = 0; i < kinds.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(kinds[i]);
            }
            return sb.ToString();
        }
    }
}
