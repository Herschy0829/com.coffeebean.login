using NUnit.Framework;
using UnityEngine;

namespace CoffeeBean.Login.Tests
{
    /// <summary>
    /// provider / 后端实现的行为测试：
    /// 外部凭据入口（Android Credential Manager / iOS 桥的落点）、开发后端、反射 Google provider、工厂装配。
    /// </summary>
    public class LoginProviderTests
    {
        private const string DevUidPrefKey = "CoffeeBean.Login.DevelopmentUserId";

        [SetUp]
        public void SetUp()
        {
            LoginLog.Enabled = false;
            PlayerPrefs.DeleteKey(DevUidPrefKey);
        }

        [TearDown]
        public void TearDown()
        {
            LoginLog.Enabled = true;
        }

        // ========== ExternalCredentialProvider ==========

        [Test]
        public void ExternalProvider_SignInThenSubmit_ReturnsCredential()
        {
            var provider = new ExternalCredentialProvider();
            LoginCredential received = null;
            provider.SignIn(c => received = c, (_, __) => Assert.Fail("不该失败"));

            Assert.IsTrue(provider.HasPendingRequest);
            Assert.IsNull(received, "Submit 之前不该有凭据");

            provider.Submit("id-token-from-android", null, null, "a@b.com", "Alice", "http://avatar");

            Assert.IsFalse(provider.HasPendingRequest);
            Assert.IsNotNull(received);
            Assert.AreEqual(LoginProviderKind.Google, received.Kind);
            Assert.AreEqual("google.com", received.ProviderId);
            Assert.AreEqual("id-token-from-android", received.IdToken);
            Assert.IsNull(received.AccessToken, "Android Credential Manager 只给 idToken");
            Assert.AreEqual("a@b.com", received.Email);
        }

        [Test]
        public void ExternalProvider_SubmitWithoutPending_DoesNotThrow()
        {
            var provider = new ExternalCredentialProvider();
            Assert.DoesNotThrow(() => provider.Submit("token", null));
            Assert.DoesNotThrow(() => provider.SubmitError(LoginErrorCode.Cancelled, "x"));
        }

        [Test]
        public void ExternalProvider_SecondSignInWhilePending_IsRejected()
        {
            var provider = new ExternalCredentialProvider();
            provider.SignIn(_ => { }, (_, __) => { });

            bool secondFailed = false;
            provider.SignIn(_ => Assert.Fail("第二次不该成功"), (code, _) =>
            {
                secondFailed = true;
                Assert.AreEqual(LoginErrorCode.Unknown, code);
            });

            Assert.IsTrue(secondFailed, "已有等待中的请求时，第二次 SignIn 必须被拒绝（否则回调被覆盖 → UI 卡死）");
        }

        [Test]
        public void ExternalProvider_SubmitError_ReportsError()
        {
            var provider = new ExternalCredentialProvider();
            LoginErrorCode? code = null;
            provider.SignIn(_ => Assert.Fail("不该成功"), (c, _) => code = c);

            provider.SubmitError(LoginErrorCode.Cancelled, "用户关了窗口");

            Assert.AreEqual(LoginErrorCode.Cancelled, code);
            Assert.IsFalse(provider.HasPendingRequest);
        }

        [Test]
        public void ExternalProvider_SubmitWithoutAnyToken_ReportsInvalidCredential()
        {
            var provider = new ExternalCredentialProvider();
            LoginErrorCode? code = null;
            provider.SignIn(_ => Assert.Fail("不该成功"), (c, _) => code = c);

            provider.Submit(null, null);

            Assert.AreEqual(LoginErrorCode.InvalidCredential, code);
        }

        // ========== 反射 Google provider ==========

        [Test]
        public void ReflectionGoogleProvider_WithoutPlugin_IsUnavailable_WithReason()
        {
            var provider = new GoogleSignInReflectionProvider();

            Assert.AreEqual(LoginProviderKind.Google, provider.Kind);
            if (provider.IsAvailable)
            {
                Assert.Ignore("本工程装了 GoogleSignIn 插件，跳过「未装时不可用」的断言");
            }

            Assert.IsFalse(string.IsNullOrEmpty(provider.UnavailableReason));
            StringAssert.Contains("Credential Manager", provider.UnavailableReason);

            LoginErrorCode? code = null;
            provider.SignIn(_ => Assert.Fail("不可用时不该成功"), (c, _) => code = c);
            Assert.AreEqual(LoginErrorCode.ProviderUnavailable, code);
        }

        // ========== 开发后端 ==========

        [Test]
        public void DevelopmentBackend_AnonymousLogin_ReusesSameUid()
        {
            var backend = new DevelopmentLoginBackend();

            LoginUser first = null;
            backend.SignInAnonymously(u => first = u, (_, __) => Assert.Fail("不该失败"));
            Assert.IsNotNull(first);
            Assert.IsTrue(first.IsAnonymous);
            Assert.IsTrue(first.UserId.StartsWith("dev-anon-"), first.UserId);

            backend.SignOut();
            LoginUser second = null;
            backend.SignInAnonymously(u => second = u, (_, __) => Assert.Fail("不该失败"));

            Assert.AreEqual(first.UserId, second.UserId, "同一设备必须拿到同一个 uid（PlayerPrefs 持久化）");
        }

        [Test]
        public void DevelopmentBackend_Link_PreservesUid_AndFillsProfile()
        {
            var backend = new DevelopmentLoginBackend();
            backend.SignInAnonymously(_ => { }, (_, __) => { });
            string uid = backend.CurrentUser.UserId;

            LoginUser linked = null;
            backend.LinkWithCredential(new LoginCredential
            {
                Kind = LoginProviderKind.Google,
                ProviderId = "google.com",
                IdToken = "abc",
                Email = "x@y.com",
                DisplayName = "X",
            }, u => linked = u, (_, __) => Assert.Fail("不该失败"));

            Assert.AreEqual(uid, linked.UserId);
            Assert.IsFalse(linked.IsAnonymous);
            Assert.AreEqual("x@y.com", linked.Email);
            Assert.AreEqual("google.com", linked.ProviderId);
        }

        [Test]
        public void DevelopmentBackend_LinkWithoutSession_Fails()
        {
            var backend = new DevelopmentLoginBackend();
            LoginErrorCode? code = null;

            backend.LinkWithCredential(new LoginCredential { IdToken = "abc", ProviderId = "google.com" },
                _ => Assert.Fail("不该成功"), (c, _) => code = c);

            Assert.AreEqual(LoginErrorCode.NotSignedIn, code);
        }

        [Test]
        public void DevelopmentBackend_GetIdToken_RequiresSignIn()
        {
            var backend = new DevelopmentLoginBackend();
            LoginErrorCode? code = null;
            backend.GetIdToken(false, _ => Assert.Fail("不该成功"), (c, _) => code = c);
            Assert.AreEqual(LoginErrorCode.NotSignedIn, code);

            backend.SignInAnonymously(_ => { }, (_, __) => { });
            string token = null;
            backend.GetIdToken(true, t => token = t, (_, __) => Assert.Fail("不该失败"));
            StringAssert.StartsWith("dev-id-token:", token);
            StringAssert.Contains(":refreshed", token);
        }

        // ========== 工厂装配 ==========

        [Test]
        public void Factory_CreateDefault_AlwaysProvidesExternalEntry()
        {
            LoginService service = LoginFactory.CreateDefault(null, out ExternalCredentialProvider external);

            Assert.IsNotNull(external, "外部凭据入口必须总是存在（Credential Manager / iOS 桥要靠它）");
            Assert.AreSame(external, service.GetProvider<ExternalCredentialProvider>());
            Assert.IsNotNull(service.Backend, "后端必须总是有一个（最差是 UnavailableLoginBackend）");
        }

        [Test]
        public void Factory_CreateProviders_AddsSimulatedGoogleOnlyOutsideBatchMode()
        {
            LoginSettings settings = ScriptableObject.CreateInstance<LoginSettings>();
            settings.SimulateGoogleInEditor = true;

            System.Collections.Generic.List<ILoginProvider> providers =
                LoginFactory.CreateProviders(settings, out ExternalCredentialProvider external);

            Assert.IsNotNull(external, "外部凭据入口总是要有");
            bool hasSimulated = providers.Exists(p => p is SimulatedGoogleProvider);

            if (Application.isBatchMode)
            {
                Assert.IsFalse(hasSimulated, "批处理/CI 下不该加模拟 provider（保证「可用方式」是确定的）");
            }
            else
            {
                Assert.IsTrue(hasSimulated, "编辑器交互运行时应该补一个模拟 Google，便于先跑通 UI 流程");
            }

            Object.DestroyImmediate(settings);
        }

        [Test]
        public void Factory_TryCreate_ReportsReasonForMissingType()
        {
            string reason;
            ILoginBackend backend = LoginFactory.TryCreate<ILoginBackend>("No.Such.Type, No.Such.Assembly", out reason);

            Assert.IsNull(backend);
            Assert.IsFalse(string.IsNullOrEmpty(reason));
        }
    }
}
