using System;

namespace CoffeeBean.Login.Tests
{
    /// <summary>可编程的假 provider：想成功/失败/不可用/取消都行，并记录被调用次数。</summary>
    public sealed class FakeLoginProvider : ILoginProvider
    {
        public LoginProviderKind Kind { get; set; } = LoginProviderKind.Google;

        public string ProviderId { get; set; } = "google.com";

        public bool IsAvailable { get; set; } = true;

        public string UnavailableReason { get; set; } = "fake provider unavailable";

        /// <summary>成功时给出的凭据（null → 给一个默认的假 Google 凭据）。</summary>
        public LoginCredential Credential;

        /// <summary>true → 走失败回调。</summary>
        public bool FailInstead;

        /// <summary>失败时用的错误码。</summary>
        public LoginErrorCode ErrorCode = LoginErrorCode.Cancelled;

        /// <summary>SignIn 被调用了几次。</summary>
        public int SignInCount;

        /// <summary>true → SignIn 不回调（模拟"授权窗口还开着"），测试再手动 <see cref="CompleteHang"/>。</summary>
        public bool HangInstead;

        private Action<LoginCredential> _hangingSuccess;
        private Action<LoginErrorCode, string> _hangingError;

        /// <summary>把挂起的 SignIn 收尾（成功）。</summary>
        public void CompleteHang(LoginCredential credential = null)
        {
            Action<LoginCredential> success = _hangingSuccess;
            _hangingSuccess = null;
            _hangingError = null;
            if (success != null) success(credential ?? Credential ?? new LoginCredential
            {
                Kind = Kind,
                ProviderId = ProviderId,
                IdToken = "fake-id-token",
            });
        }

        /// <summary>把挂起的 SignIn 收尾（失败）。</summary>
        public void CompleteHangWithError(LoginErrorCode code = LoginErrorCode.Cancelled)
        {
            Action<LoginErrorCode, string> error = _hangingError;
            _hangingSuccess = null;
            _hangingError = null;
            if (error != null) error(code, "fake provider error");
        }

        public void SignIn(Action<LoginCredential> onSuccess, Action<LoginErrorCode, string> onError)
        {
            SignInCount++;

            if (HangInstead)
            {
                _hangingSuccess = onSuccess;
                _hangingError = onError;
                return;
            }

            if (FailInstead)
            {
                if (onError != null) onError(ErrorCode, "fake provider error");
                return;
            }

            if (onSuccess == null) return;
            onSuccess(Credential != null
                ? Credential
                : new LoginCredential
                {
                    Kind = Kind,
                    ProviderId = ProviderId,
                    IdToken = "fake-id-token",
                    AccessToken = "fake-access-token",
                    Email = "fake@example.com",
                    DisplayName = "Fake User",
                });
        }
    }

    /// <summary>可编程的假后端：记录所有调用，可配置返回的用户或失败码。</summary>
    public sealed class FakeLoginBackend : ILoginBackend
    {
        public string Name => "Fake";

        public bool IsAvailable { get; set; } = true;

        public string UnavailableReason { get; set; } = "fake backend unavailable";

        public LoginUser CurrentUser { get; private set; }

        public event Action<LoginUser> StateChanged;

        public int AnonymousCount;
        public int CredentialCount;
        public int LinkCount;
        public int EmailCount;
        public int TokenCount;
        public int SignOutCount;

        public LoginCredential LastCredential;
        public bool LastForceRefresh;

        /// <summary>非 None → 所有登录类调用都按它失败。</summary>
        public LoginErrorCode FailCode = LoginErrorCode.None;

        public string Token = "fake-id-token";

        /// <summary>模拟后端侧登出/换账号（测试 StateChanged 通路）。</summary>
        public void RaiseExternalSignOut()
        {
            CurrentUser = null;
            var handler = StateChanged;
            if (handler != null) handler(null);
        }

        public void SignInWithCredential(LoginCredential credential, Action<LoginUser> onSuccess, Action<LoginErrorCode, string> onError)
        {
            CredentialCount++;
            LastCredential = credential;
            if (CheckFail(onError)) return;
            SetUser(new LoginUser
            {
                UserId = "uid-" + (credential != null ? credential.ProviderId : "?"),
                Email = credential != null ? credential.Email : null,
                DisplayName = credential != null ? credential.DisplayName : null,
                ProviderId = credential != null ? credential.ProviderId : null,
                IsAnonymous = false,
            });
            if (onSuccess != null) onSuccess(CurrentUser);
        }

        public void SignInAnonymously(Action<LoginUser> onSuccess, Action<LoginErrorCode, string> onError)
        {
            AnonymousCount++;
            if (CheckFail(onError)) return;
            SetUser(new LoginUser { UserId = "uid-anon", IsAnonymous = true, ProviderId = "anonymous" });
            if (onSuccess != null) onSuccess(CurrentUser);
        }

        public void LinkWithCredential(LoginCredential credential, Action<LoginUser> onSuccess, Action<LoginErrorCode, string> onError)
        {
            LinkCount++;
            LastCredential = credential;
            if (CheckFail(onError)) return;
            LoginUser merged = CurrentUser != null ? CurrentUser.Clone() : new LoginUser { UserId = "uid-anon" };
            merged.IsAnonymous = false;
            merged.ProviderId = credential != null ? credential.ProviderId : null;
            merged.Email = credential != null ? credential.Email : null;
            SetUser(merged);
            if (onSuccess != null) onSuccess(CurrentUser);
        }

        public void SignInWithEmail(string email, string password, Action<LoginUser> onSuccess, Action<LoginErrorCode, string> onError)
        {
            EmailCount++;
            if (CheckFail(onError)) return;
            SetUser(new LoginUser { UserId = "uid-email", Email = email, ProviderId = "password", IsAnonymous = false });
            if (onSuccess != null) onSuccess(CurrentUser);
        }

        public void SignOut()
        {
            SignOutCount++;
            SetUser(null);
        }

        public void GetIdToken(bool forceRefresh, Action<string> onSuccess, Action<LoginErrorCode, string> onError)
        {
            TokenCount++;
            LastForceRefresh = forceRefresh;
            if (CheckFail(onError)) return;
            if (onSuccess != null) onSuccess(Token);
        }

        private bool CheckFail(Action<LoginErrorCode, string> onError)
        {
            if (FailCode == LoginErrorCode.None) return false;
            if (onError != null) onError(FailCode, "fake backend failure");
            return true;
        }

        private void SetUser(LoginUser user)
        {
            CurrentUser = user;
            var handler = StateChanged;
            if (handler != null) handler(user);
        }
    }
}
