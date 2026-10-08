#if UNITY_EDITOR
using System;

namespace CoffeeBean
{
    /// <summary>
    /// **编辑器专用**的模拟 Google 登录：不装 Google SDK 也能把
    /// 「点登录 → 拿到凭据 → 换后端会话 → 刷新 UI」整条链路跑通（UI 先行开发用）。
    ///
    /// 它给出的是**假 idToken**：真后端（Firebase）会以 <c>invalid-credential</c> 拒绝，
    /// 所以要么配 <see cref="DevelopmentLoginBackend"/>，要么在编辑器里只验证 UI 流程。
    /// 打包后本类型不存在（<c>#if UNITY_EDITOR</c>）。
    /// </summary>
    public sealed class SimulatedGoogleProvider : ILoginProvider
    {
        private readonly LoginSettings _settings;

        public SimulatedGoogleProvider(LoginSettings settings)
        {
            _settings = settings;
        }

        public LoginProviderKind Kind => LoginProviderKind.Google;

        public string ProviderId => "google.com";

        public bool IsAvailable => true;

        public string UnavailableReason => string.Empty;

        public void SignIn(Action<LoginCredential> onSuccess, Action<LoginErrorCode, string> onError)
        {
            string email = _settings != null && !string.IsNullOrEmpty(_settings.SimulatedEmail)
                ? _settings.SimulatedEmail
                : "editor@local";

            string id = Math.Abs(email.GetHashCode()).ToString("x8");
            LoginLog.Warn("使用【模拟 Google 登录】（仅编辑器，假 idToken）：" + email
                          + "。真后端会拒绝这个 token —— 这只是为了让 UI 流程能跑。");

            if (onSuccess == null) return;
            onSuccess(new LoginCredential
            {
                Kind = LoginProviderKind.Google,
                ProviderId = "google.com",
                IdToken = "simulated-id-token:" + id,
                AccessToken = "simulated-access-token",
                Email = email,
                DisplayName = "Editor Simulated",
            });
        }
    }
}
#endif
