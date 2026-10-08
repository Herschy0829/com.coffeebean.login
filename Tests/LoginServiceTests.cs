using NUnit.Framework;

namespace CoffeeBean.Login.Tests
{
    /// <summary>
    /// 登录门面的行为测试：状态机、事件、游客转正、串行保护、错误码映射。
    /// 全部用假 provider / 假后端，**不碰网络、不碰 SDK**（真 SDK 的行为在真机验证，见 README）。
    /// </summary>
    public class LoginServiceTests
    {
        private FakeLoginProvider _provider;
        private FakeLoginBackend _backend;
        private LoginService _service;

        [SetUp]
        public void SetUp()
        {
            _provider = new FakeLoginProvider();
            _backend = new FakeLoginBackend();
            _service = new LoginService(new[] { _provider }, _backend);
            LoginLog.Enabled = false;   // 测试时别刷日志
        }

        [TearDown]
        public void TearDown()
        {
            LoginLog.Enabled = true;
        }

        [Test]
        public void NotInitialized_SignIn_FailsWithNotInitialized()
        {
            LoginResult result = null;
            _service.SignInAsGuest(r => result = r);

            Assert.IsNotNull(result);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(LoginErrorCode.NotInitialized, result.Code);
            Assert.AreEqual(0, _backend.AnonymousCount, "未初始化就不该碰后端");
        }

        [Test]
        public void Initialize_WithoutSettings_IsSignedOut()
        {
            _service.Initialize();

            Assert.IsTrue(_service.IsInitialized);
            Assert.AreEqual(LoginState.SignedOut, _service.State);
            Assert.IsFalse(_service.IsSignedIn);
            Assert.IsNull(_service.CurrentUser);
            Assert.AreEqual(0, _backend.AnonymousCount, "没给 settings 就不该自动游客登录");
        }

        [Test]
        public void SignInAsGuest_Succeeds_AndFiresEvents()
        {
            _service.Initialize();
            LoginUser signedIn = null;
            LoginState lastState = LoginState.NotInitialized;
            _service.OnSignedIn += u => signedIn = u;
            _service.OnStateChanged += s => lastState = s;

            LoginResult result = null;
            _service.SignInAsGuest(r => result = r);

            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual(1, _backend.AnonymousCount);
            Assert.IsTrue(_service.IsSignedIn);
            Assert.IsTrue(_service.CurrentUser.IsAnonymous);
            Assert.AreEqual(LoginState.SignedIn, lastState);
            Assert.IsNotNull(signedIn);
            Assert.AreEqual(_service.CurrentUser.UserId, signedIn.UserId);
        }

        [Test]
        public void SignIn_Google_PassesCredentialToBackend()
        {
            _service.Initialize();

            LoginResult result = null;
            _service.SignIn(LoginProviderKind.Google, r => result = r);

            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual(1, _provider.SignInCount);
            Assert.AreEqual(1, _backend.CredentialCount);
            Assert.IsNotNull(_backend.LastCredential);
            Assert.AreEqual("google.com", _backend.LastCredential.ProviderId);
            Assert.AreEqual("fake-id-token", _backend.LastCredential.IdToken);
            Assert.IsFalse(_service.CurrentUser.IsAnonymous);
            Assert.AreEqual("google.com", _service.CurrentUser.ProviderId);
        }

        [Test]
        public void SignIn_UnavailableProvider_FailsWithProviderUnavailable()
        {
            _provider.IsAvailable = false;
            _provider.UnavailableReason = "没装插件";
            _service.Initialize();

            LoginResult result = null;
            _service.SignIn(LoginProviderKind.Google, r => result = r);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(LoginErrorCode.ProviderUnavailable, result.Code);
            StringAssert.Contains("没装插件", result.Error);
            Assert.AreEqual(0, _provider.SignInCount, "不可用的 provider 不该被调用");
        }

        [Test]
        public void SignIn_UnknownKind_FailsWithProviderUnavailable()
        {
            _service.Initialize();

            LoginResult result = null;
            _service.SignIn(LoginProviderKind.Apple, r => result = r);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(LoginErrorCode.ProviderUnavailable, result.Code);
            StringAssert.Contains("Apple", result.Error);
        }

        [Test]
        public void SignIn_UserCancel_ReportsCancelled_AndStateFailed()
        {
            _provider.FailInstead = true;
            _provider.ErrorCode = LoginErrorCode.Cancelled;
            _service.Initialize();

            LoginErrorCode? failedCode = null;
            _service.OnFailed += (code, _) => failedCode = code;

            LoginResult result = null;
            _service.SignIn(LoginProviderKind.Google, r => result = r);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(LoginErrorCode.Cancelled, result.Code);
            Assert.IsFalse(result.CanRetry, "用户取消不该建议重试");
            Assert.AreEqual(LoginState.Failed, _service.State);
            Assert.AreEqual(LoginErrorCode.Cancelled, failedCode);
            Assert.AreEqual(0, _backend.CredentialCount, "取消时不该把凭据交给后端");
        }

        [Test]
        public void SignIn_BackendFailure_PropagatesCode()
        {
            _backend.FailCode = LoginErrorCode.NetworkError;
            _service.Initialize();

            LoginResult result = null;
            _service.SignIn(LoginProviderKind.Google, r => result = r);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(LoginErrorCode.NetworkError, result.Code);
            Assert.IsTrue(result.CanRetry, "网络错应该建议重试");
            Assert.AreEqual(LoginState.Failed, _service.State);
            Assert.IsFalse(_service.IsSignedIn);
        }

        [Test]
        public void SignIn_WhileBusy_IsRejected_AndProviderCalledOnce()
        {
            _provider.HangInstead = true;
            _service.Initialize();

            LoginResult first = null;
            _service.SignIn(LoginProviderKind.Google, r => first = r);
            Assert.IsTrue(_service.IsBusy, "第一次登录应该处于进行中");
            Assert.IsNull(first, "挂起时不该有回调");

            LoginResult second = null;
            _service.SignIn(LoginProviderKind.Google, r => second = r);

            Assert.IsNotNull(second);
            Assert.IsFalse(second.Success, "进行中再点登录应该被拒绝（避免弹两次授权窗口）");
            Assert.AreEqual(1, _provider.SignInCount);

            _provider.CompleteHang();
            Assert.IsNotNull(first);
            Assert.IsTrue(first.Success);
            Assert.IsFalse(_service.IsBusy);
        }

        [Test]
        public void Link_WhenNotSignedIn_FailsWithNotSignedIn()
        {
            _service.Initialize();

            LoginResult result = null;
            _service.Link(LoginProviderKind.Google, r => result = r);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(LoginErrorCode.NotSignedIn, result.Code);
            Assert.AreEqual(0, _provider.SignInCount);
        }

        [Test]
        public void Link_AsGuest_KeepsUid_AndClearsAnonymous()
        {
            _service.Initialize();
            _service.SignInAsGuest();
            string guestUid = _service.CurrentUser.UserId;

            LoginResult result = null;
            _service.LinkWithGoogle(r => result = r);

            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual(1, _backend.LinkCount);
            Assert.IsFalse(_service.CurrentUser.IsAnonymous, "转正后不该还是匿名");
            Assert.AreEqual(guestUid, _service.CurrentUser.UserId, "转正必须保留 uid（进度不能丢）");
        }

        [Test]
        public void Link_GuestKind_IsRejected()
        {
            _service.Initialize();
            _service.SignInAsGuest();

            LoginResult result = null;
            _service.Link(LoginProviderKind.Guest, r => result = r);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(LoginErrorCode.ProviderUnavailable, result.Code);
        }

        [Test]
        public void SignInWithEmail_UsesBackend()
        {
            _service.Initialize();

            LoginResult result = null;
            _service.SignInWithEmail("a@b.com", "pw", r => result = r);

            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual(1, _backend.EmailCount);
            Assert.AreEqual("a@b.com", _service.CurrentUser.Email);
        }

        [Test]
        public void SignIn_EmailKindViaSignIn_IsRejectedWithHint()
        {
            _service.Initialize();

            LoginResult result = null;
            _service.SignIn(LoginProviderKind.Email, r => result = r);

            Assert.IsFalse(result.Success);
            StringAssert.Contains("SignInWithEmail", result.Error);
        }

        [Test]
        public void GetIdToken_BeforeSignIn_FailsNotSignedIn()
        {
            _service.Initialize();

            LoginErrorCode? code = null;
            _service.GetIdToken(_ => Assert.Fail("不该成功"), (c, _) => code = c);

            Assert.AreEqual(LoginErrorCode.NotSignedIn, code);
            Assert.AreEqual(0, _backend.TokenCount);
        }

        [Test]
        public void GetIdToken_AfterSignIn_ReturnsToken_AndPassesForceRefresh()
        {
            _service.Initialize();
            _service.SignInAsGuest();

            string token = null;
            _service.GetIdToken(t => token = t, null, forceRefresh: true);

            Assert.AreEqual("fake-id-token", token);
            Assert.AreEqual(1, _backend.TokenCount);
            Assert.IsTrue(_backend.LastForceRefresh);
        }

        [Test]
        public void SignOut_ResetsState_AndFiresSignedOut()
        {
            _service.Initialize();
            _service.SignInAsGuest();

            bool signedOut = false;
            _service.OnSignedOut += () => signedOut = true;

            _service.SignOut();

            Assert.IsTrue(signedOut);
            Assert.AreEqual(1, _backend.SignOutCount);
            Assert.AreEqual(LoginState.SignedOut, _service.State);
            Assert.IsFalse(_service.IsSignedIn);
            Assert.IsNull(_service.CurrentUser);
        }

        [Test]
        public void BackendExternalSignOut_UpdatesState()
        {
            _service.Initialize();
            _service.SignInAsGuest();
            Assert.IsTrue(_service.IsSignedIn);

            bool signedOut = false;
            _service.OnSignedOut += () => signedOut = true;

            _backend.RaiseExternalSignOut();   // 模拟 Firebase 侧会话失效/被踢

            Assert.IsTrue(signedOut);
            Assert.AreEqual(LoginState.SignedOut, _service.State);
            Assert.IsFalse(_service.IsSignedIn);
        }

        [Test]
        public void AvailableProviders_IncludesGuestAndUsableProviders()
        {
            var apple = new FakeLoginProvider { Kind = LoginProviderKind.Apple, ProviderId = "apple.com", IsAvailable = false };
            var service = new LoginService(new ILoginProvider[] { _provider, apple }, _backend);
            service.Initialize();

            var available = service.AvailableProviders;

            CollectionAssert.Contains(available, LoginProviderKind.Guest);
            CollectionAssert.Contains(available, LoginProviderKind.Google);
            CollectionAssert.DoesNotContain(available, LoginProviderKind.Apple);
        }

        [Test]
        public void AvailableProviders_WithoutBackend_HasNoGuest()
        {
            var service = new LoginService(new ILoginProvider[0], new UnavailableLoginBackend("没有后端"));
            service.Initialize();

            Assert.IsFalse(service.Backend.IsAvailable);
            CollectionAssert.DoesNotContain(service.AvailableProviders, LoginProviderKind.Guest);
        }

        [Test]
        public void DescribeProviders_MentionsBackendAndReasons()
        {
            _provider.IsAvailable = false;
            _provider.UnavailableReason = "没装 Google 插件";
            _service.Initialize();

            string text = _service.DescribeProviders();

            StringAssert.Contains("Fake", text);
            StringAssert.Contains("没装 Google 插件", text);
        }

        [Test]
        public void GetProvider_ReturnsRegisteredTypedProvider()
        {
            ExternalCredentialProvider external = null;
            var service = new LoginService(new ILoginProvider[] { _provider, external = new ExternalCredentialProvider() }, _backend);

            Assert.AreSame(external, service.GetProvider<ExternalCredentialProvider>());
            Assert.IsNull(service.GetProvider<GoogleSignInReflectionProvider>(), "没注册的类型应该返回 null");
        }
    }
}
