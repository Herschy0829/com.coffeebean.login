using System;
using System.Collections.Generic;

namespace CoffeeBean
{
    /// <summary>
    /// **外部凭据入口**：把"用任何方式拿到的第三方凭据"喂进登录流程。
    ///
    /// 这是给下面这些场景准备的落点（它们都拿不到"官方 Unity 插件"）：
    /// · **Android Credential Manager**（官方当前推荐）：自己写 Java/Kotlin 插件，
    ///   在 `GoogleIdTokenCredential.createFrom(...).getIdToken()` 之后用
    ///   `UnityPlayer.UnitySendMessage("...", "OnGoogleIdToken", idToken)` 回传 C#，再调 <see cref="Submit"/>；
    /// · **iOS GoogleSignIn-iOS**（官方推荐）：自己在 `Plugins/iOS` 里桥 `signIn(withPresenting:)`，
    ///   idToken + accessToken 一起回传；
    /// · **Sign in with Apple / 自建 OAuth / 微信等**：同样 `Submit` 即可，后端按 providerId 处理；
    /// · **测试/自动化**：直接 `Submit` 一个假 token（配合开发后端）跑 UI 流程。
    ///
    /// 用法：注册到 <see cref="LoginService"/>，业务先调 <c>SignIn(LoginProviderKind.Google)</c>，
    /// 该调用会**挂起等待**；桥接层拿到 token 后调 <see cref="Submit"/>（或 <see cref="SubmitError"/>）把它唤醒。
    /// </summary>
    public sealed class ExternalCredentialProvider : ILoginProvider
    {
        private readonly object _gate = new object();
        private Action<LoginCredential> _pendingSuccess;
        private Action<LoginErrorCode, string> _pendingError;

        public ExternalCredentialProvider(LoginProviderKind kind = LoginProviderKind.Google,
            string providerId = "google.com")
        {
            Kind = kind;
            ProviderId = providerId;
        }

        public LoginProviderKind Kind { get; }

        public string ProviderId { get; }

        /// <summary>永远可用：token 由外部给，能不能给出来是业务自己的事。</summary>
        public bool IsAvailable => true;

        public string UnavailableReason => string.Empty;

        /// <summary>是否有一次等待中的登录（用来避免重复弹窗）。</summary>
        public bool HasPendingRequest
        {
            get
            {
                lock (_gate) return _pendingSuccess != null;
            }
        }

        public void SignIn(Action<LoginCredential> onSuccess, Action<LoginErrorCode, string> onError)
        {
            lock (_gate)
            {
                if (_pendingSuccess != null)
                {
                    // 已有一次在等：直接把新的调用判失败，避免把回调覆盖掉（旧回调永远不回来 = 卡死 UI）
                    if (onError != null)
                    {
                        onError(LoginErrorCode.Unknown,
                            "已有一个等待中的外部登录请求（先 Submit/SubmitError 或超时，再发起新的）。");
                    }
                    return;
                }

                _pendingSuccess = onSuccess;
                _pendingError = onError;
            }

            LoginLog.Log("ExternalCredentialProvider：等待外部（平台桥/测试）提交 " + ProviderId + " 凭据…");
        }

        /// <summary>平台桥拿到凭据后调用它（idToken 必填；Android 上 accessToken 通常没有，传 null）。</summary>
        public void Submit(string idToken, string accessToken = null, string serverAuthCode = null,
            string email = null, string displayName = null, string photoUrl = null,
            Dictionary<string, string> extra = null)
        {
            if (string.IsNullOrEmpty(idToken) && string.IsNullOrEmpty(accessToken))
            {
                SubmitError(LoginErrorCode.InvalidCredential, "Submit 既没有 idToken 也没有 accessToken。");
                return;
            }

            Action<LoginCredential> success;
            lock (_gate)
            {
                success = _pendingSuccess;
                _pendingSuccess = null;
                _pendingError = null;
            }

            if (success == null)
            {
                LoginLog.Warn("ExternalCredentialProvider：收到凭据但没有等待中的登录请求（被丢弃）。"
                              + "业务应先调 SignIn(LoginProviderKind." + Kind + ")。");
                return;
            }

            LoginLog.Log("ExternalCredentialProvider：收到外部凭据（" + ProviderId + "）。");
            LoginMainThread.Run(() => success(new LoginCredential
            {
                Kind = Kind,
                ProviderId = ProviderId,
                IdToken = idToken,
                AccessToken = accessToken,
                ServerAuthCode = serverAuthCode,
                Email = email,
                DisplayName = displayName,
                PhotoUrl = photoUrl,
                Extra = extra,
            }));
        }

        /// <summary>平台桥失败/用户取消时调用它。</summary>
        public void SubmitError(LoginErrorCode code, string message)
        {
            Action<LoginErrorCode, string> error;
            lock (_gate)
            {
                error = _pendingError;
                _pendingSuccess = null;
                _pendingError = null;
            }

            LoginLog.Warn("ExternalCredentialProvider：外部登录失败（" + code + "）：" + message);
            if (error == null) return;
            LoginMainThread.Run(() => error(code, message));
        }
    }
}
