using System.Collections.Generic;
using CoffeeBean.Login.EditorTools;
using NUnit.Framework;

namespace CoffeeBean.Login.Tests
{
    /// <summary>
    /// 自检逻辑测试（纯函数，不依赖真实 Firebase 工程）：
    /// 把"接 Google + Firebase 登录"最容易踩的几类配置错逐条锁住 ——
    /// 这些错在编辑器里不报错，只在真机上失败，所以必须有测试兜着。
    /// </summary>
    public class LoginSetupValidatorTests
    {
        /// <summary>一份"全都对"的工程现状。</summary>
        private static LoginSetupContext GoodAndroidContext()
        {
            var ctx = new LoginSetupContext
            {
                TargetAndroid = true,
                HasGoogleServicesJson = true,
                GoogleServicesJsonPath = "Assets/google-services.json",
                HasFirebaseAuthAssembly = true,
                HasFirebaseAppAssembly = true,
                HasGoogleSignInAssembly = true,
                FirebaseDefineOn = true,
                HasSettingsAsset = true,
                GoogleWebClientId = "123456-web.apps.googleusercontent.com",
                ApplicationIdentifier = "com.demo.game",
                AndroidMinSdkVersion = 24,
                DebugKeystoreSha1 = "AA:BB:CC",
            };
            ctx.GoogleServicesPackageNames.Add("com.demo.game");
            return ctx;
        }

        private static bool HasError(List<LoginSetupIssue> issues, string titleKeyword)
            => issues.Exists(i => i.Level == LoginSetupLevel.Error && i.Title.Contains(titleKeyword));

        private static bool HasWarning(List<LoginSetupIssue> issues, string titleKeyword)
            => issues.Exists(i => i.Level == LoginSetupLevel.Warning && i.Title.Contains(titleKeyword));

        [Test]
        public void Validate_AllGood_ReturnsSingleInfo()
        {
            List<LoginSetupIssue> issues = LoginSetupValidator.Validate(GoodAndroidContext());

            Assert.AreEqual(1, issues.Count, string.Join("\n", issues.ConvertAll(i => i.ToString()).ToArray()));
            Assert.AreEqual(LoginSetupLevel.Info, issues[0].Level);
            Assert.AreEqual(0, LoginSetupIssue.CountErrors(issues));
        }

        [Test]
        public void Validate_AndroidWithoutGoogleServicesJson_IsError()
        {
            LoginSetupContext ctx = GoodAndroidContext();
            ctx.HasGoogleServicesJson = false;
            ctx.GoogleServicesPackageNames.Clear();

            List<LoginSetupIssue> issues = LoginSetupValidator.Validate(ctx);

            Assert.IsTrue(HasError(issues, "google-services.json"), string.Join("\n", issues.ConvertAll(i => i.ToString()).ToArray()));
        }

        [Test]
        public void Validate_PackageNameMismatch_IsError_AndMentionsBothNames()
        {
            LoginSetupContext ctx = GoodAndroidContext();
            ctx.GoogleServicesPackageNames.Clear();
            ctx.GoogleServicesPackageNames.Add("com.other.app");

            List<LoginSetupIssue> issues = LoginSetupValidator.Validate(ctx);

            LoginSetupIssue issue = issues.Find(i => i.Title.Contains("包名不匹配"));
            Assert.IsNotNull(issue, string.Join("\n", issues.ConvertAll(i => i.ToString()).ToArray()));
            Assert.AreEqual(LoginSetupLevel.Error, issue.Level);
            StringAssert.Contains("com.demo.game", issue.Detail);
            StringAssert.Contains("com.other.app", issue.Detail);
        }

        [Test]
        public void Validate_LowMinSdk_IsWarning()
        {
            LoginSetupContext ctx = GoodAndroidContext();
            ctx.AndroidMinSdkVersion = 23;

            List<LoginSetupIssue> issues = LoginSetupValidator.Validate(ctx);

            Assert.IsTrue(HasWarning(issues, "minSdk"), string.Join("\n", issues.ConvertAll(i => i.ToString()).ToArray()));
            Assert.IsFalse(HasError(issues, "minSdk"), "minSdk 低只是警告（官方给的替代修法还有 minify / r8）");
        }

        [Test]
        public void Validate_FirebaseAssemblyWithoutDefine_IsWarning()
        {
            LoginSetupContext ctx = GoodAndroidContext();
            ctx.FirebaseDefineOn = false;

            List<LoginSetupIssue> issues = LoginSetupValidator.Validate(ctx);

            Assert.IsTrue(HasWarning(issues, "没启用"), string.Join("\n", issues.ConvertAll(i => i.ToString()).ToArray()));
        }

        [Test]
        public void Validate_DefineWithoutFirebaseAssembly_IsError()
        {
            LoginSetupContext ctx = GoodAndroidContext();
            ctx.HasFirebaseAuthAssembly = false;
            ctx.HasFirebaseAppAssembly = false;
            ctx.FirebaseDefineOn = true;

            List<LoginSetupIssue> issues = LoginSetupValidator.Validate(ctx);

            Assert.IsTrue(HasError(issues, "不一致"), string.Join("\n", issues.ConvertAll(i => i.ToString()).ToArray()));
        }

        [Test]
        public void Validate_NoFirebaseAtAll_IsWarning_NotError()
        {
            LoginSetupContext ctx = GoodAndroidContext();
            ctx.HasFirebaseAuthAssembly = false;
            ctx.HasFirebaseAppAssembly = false;
            ctx.FirebaseDefineOn = false;

            List<LoginSetupIssue> issues = LoginSetupValidator.Validate(ctx);

            Assert.IsTrue(HasWarning(issues, "没有登录后端"), string.Join("\n", issues.ConvertAll(i => i.ToString()).ToArray()));
            Assert.AreEqual(0, LoginSetupIssue.CountErrors(issues), "没装 Firebase 只是警告（编辑器有开发后端）");
        }

        [Test]
        public void Validate_NoGoogleSource_IsWarning_AndPointsToCredentialManager()
        {
            LoginSetupContext ctx = GoodAndroidContext();
            ctx.HasGoogleSignInAssembly = false;

            List<LoginSetupIssue> issues = LoginSetupValidator.Validate(ctx);

            LoginSetupIssue issue = issues.Find(i => i.Title.Contains("没有 Google 登录来源"));
            Assert.IsNotNull(issue, string.Join("\n", issues.ConvertAll(i => i.ToString()).ToArray()));
            Assert.AreEqual(LoginSetupLevel.Warning, issue.Level);
            StringAssert.Contains("Credential Manager", issue.Fix);
            StringAssert.Contains("ExternalCredentialProvider", issue.Fix);
        }

        [Test]
        public void Validate_MissingSettingsAsset_IsWarning()
        {
            LoginSetupContext ctx = GoodAndroidContext();
            ctx.HasSettingsAsset = false;

            List<LoginSetupIssue> issues = LoginSetupValidator.Validate(ctx);

            Assert.IsTrue(HasWarning(issues, "LoginSettings"), string.Join("\n", issues.ConvertAll(i => i.ToString()).ToArray()));
        }

        [Test]
        public void Validate_PluginWithoutWebClientId_IsError()
        {
            LoginSetupContext ctx = GoodAndroidContext();
            ctx.GoogleWebClientId = null;

            List<LoginSetupIssue> issues = LoginSetupValidator.Validate(ctx);

            Assert.IsTrue(HasError(issues, "GoogleWebClientId"), string.Join("\n", issues.ConvertAll(i => i.ToString()).ToArray()));
        }

        [Test]
        public void Validate_IosWithoutPlist_IsError()
        {
            LoginSetupContext ctx = GoodAndroidContext();
            ctx.TargetAndroid = false;
            ctx.TargetIos = true;
            ctx.HasGoogleServiceInfoPlist = false;
            ctx.HasGoogleServicesJson = false;

            List<LoginSetupIssue> issues = LoginSetupValidator.Validate(ctx);

            Assert.IsTrue(HasError(issues, "GoogleService-Info.plist"), string.Join("\n", issues.ConvertAll(i => i.ToString()).ToArray()));
        }

        [Test]
        public void Validate_IosWithoutClientId_IsWarning()
        {
            LoginSetupContext ctx = GoodAndroidContext();
            ctx.TargetAndroid = false;
            ctx.TargetIos = true;
            ctx.HasGoogleServiceInfoPlist = true;
            ctx.GoogleIosClientId = null;

            List<LoginSetupIssue> issues = LoginSetupValidator.Validate(ctx);

            Assert.IsTrue(HasWarning(issues, "GoogleIosClientId"), string.Join("\n", issues.ConvertAll(i => i.ToString()).ToArray()));
        }

        [Test]
        public void ExtractPackageNames_ParsesAllAndDeduplicates()
        {
            const string json = "{\"client\":[{\"client_info\":{\"android_client_info\":{\"package_name\":\"com.a.b\"}}}," +
                                "{\"client_info\":{\"android_client_info\":{\"package_name\":\"com.c.d\"}}}," +
                                "{\"client_info\":{\"android_client_info\":{\"package_name\":\"com.a.b\"}}}]}";

            List<string> names = LoginSetupValidator.ExtractPackageNames(json);

            CollectionAssert.AreEqual(new[] { "com.a.b", "com.c.d" }, names.ToArray());
        }

        [Test]
        public void ExtractPackageNames_EmptyInput_ReturnsEmpty()
        {
            Assert.AreEqual(0, LoginSetupValidator.ExtractPackageNames(null).Count);
            Assert.AreEqual(0, LoginSetupValidator.ExtractPackageNames(string.Empty).Count);
        }

        [Test]
        public void NormalizeSha1_FormatsUppercaseWithColons()
        {
            Assert.AreEqual("AA:BB:CC:DD", LoginSetupEnvironment.NormalizeSha1("aa:bb:cc:dd"));
            Assert.AreEqual("AA:BB:CC:DD", LoginSetupEnvironment.NormalizeSha1("AABBCCDD"));
            Assert.AreEqual("AA:BB:CC:DD", LoginSetupEnvironment.NormalizeSha1("AA BB CC DD"));
        }

        [Test]
        public void CountErrors_CountsOnlyErrors()
        {
            var issues = new List<LoginSetupIssue>
            {
                LoginSetupIssue.Error("a", "b", "c"),
                LoginSetupIssue.Warning("d", "e", "f"),
                LoginSetupIssue.Info("g", "h"),
            };

            Assert.AreEqual(1, LoginSetupIssue.CountErrors(issues));
            Assert.AreEqual(0, LoginSetupIssue.CountErrors(null));
        }

        [Test]
        public void ContainsDefine_MatchesWholeTokens()
        {
            Assert.IsTrue(LoginSetupEnvironment.ContainsDefine("A;B;COFFEEBEAN_FIREBASE_AUTH;C", "COFFEEBEAN_FIREBASE_AUTH"));
            Assert.IsTrue(LoginSetupEnvironment.ContainsDefine("COFFEEBEAN_FIREBASE_AUTH", "COFFEEBEAN_FIREBASE_AUTH"));
            Assert.IsFalse(LoginSetupEnvironment.ContainsDefine("COFFEEBEAN_FIREBASE_AUTH_X", "COFFEEBEAN_FIREBASE_AUTH"),
                "不能把前缀相同的符号误判为已定义");
            Assert.IsFalse(LoginSetupEnvironment.ContainsDefine(null, "X"));
        }
    }
}
