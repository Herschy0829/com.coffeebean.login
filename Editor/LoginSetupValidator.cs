using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CoffeeBean.Login.EditorTools
{
    /// <summary>
    /// 采集到的工程现状（把"环境"与"判断"分开，判断逻辑就能单测，不用真的有个 Firebase 工程）。
    /// </summary>
    public sealed class LoginSetupContext
    {
        // ---- 构建目标 ----
        public bool TargetAndroid;
        public bool TargetIos;
        public int AndroidMinSdkVersion;

        // ---- 配置文件 ----
        public bool HasGoogleServicesJson;          // Assets/google-services.json（或 Plugins/Android 下）
        public string GoogleServicesJsonPath;
        public List<string> GoogleServicesPackageNames = new List<string>();   // json 里的 package_name 列表
        public bool HasGoogleServiceInfoPlist;      // Assets/GoogleService-Info.plist
        public string GoogleServiceInfoPlistPath;

        // ---- SDK / 程序集 ----
        public bool HasFirebaseAuthAssembly;
        public bool HasFirebaseAppAssembly;
        public bool HasGoogleSignInAssembly;

        // ---- 编译符号 ----
        public bool FirebaseDefineOn;               // COFFEEBEAN_FIREBASE_AUTH（Firebase 走 .unitypackage 装时必须手动开）

        // ---- 模块配置 ----
        public bool HasSettingsAsset;
        public string GoogleWebClientId;
        public string GoogleIosClientId;

        // ---- 工程标识 ----
        public string ApplicationIdentifier;        // com.company.app

        /// <summary>本机 debug keystore 的 SHA-1（取不到为 null；Android 侧配置要用）。</summary>
        public string DebugKeystoreSha1;

        public bool GoogleConfigured => !string.IsNullOrEmpty(GoogleWebClientId);
    }

    /// <summary>
    /// 登录模块自检器（**纯函数**：只吃 <see cref="LoginSetupContext"/>，不碰 Unity API）。
    ///
    /// 为什么值得单独做：接 Google + Firebase 登录，90% 的时间都花在"某个配置文件/指纹/符号没对上"，
    /// 而且这些错在编辑器里**不报错**、只在真机上失败（`ApiException: 10` / `invalid-credential`）。
    /// 把已知的坑逐条自动检查出来，比写在文档里靠人记要可靠。
    /// </summary>
    public static class LoginSetupValidator
    {
        /// <summary>
        /// Unity 侧 Android 最低 API：Firebase Unity SDK 官方平台下限是 **Android API 24**；
        /// 而且官方 troubleshooting 明确写 minSdk 23 会在 dex 阶段炸（`mergeExtDexDebug` "Failed to transform"）。
        /// </summary>
        public const int MinimumAndroidSdk = 24;

        public static List<LoginSetupIssue> Validate(LoginSetupContext ctx)
        {
            var issues = new List<LoginSetupIssue>();
            if (ctx == null) return issues;

            ValidateBackend(ctx, issues);
            ValidateSettings(ctx, issues);
            ValidateGoogle(ctx, issues);
            ValidateAndroid(ctx, issues);
            ValidateIos(ctx, issues);

            if (issues.Count == 0)
            {
                issues.Add(LoginSetupIssue.Info("自检通过", "当前配置没发现明显问题。"));
            }
            return issues;
        }

        private static void ValidateBackend(LoginSetupContext ctx, List<LoginSetupIssue> issues)
        {
            if (!ctx.HasFirebaseAuthAssembly && !ctx.FirebaseDefineOn)
            {
                issues.Add(LoginSetupIssue.Warning(
                    "没有登录后端（Firebase 未安装）",
                    "工程里找不到 Firebase.Auth 程序集，Firebase 后端不会编译；编辑器里会回退到开发后端（内存，不跨设备）。",
                    "要真后端：装 Firebase Unity SDK（含 Authentication）+ 放好 google-services.json / GoogleService-Info.plist；" +
                    "或自己实现 ILoginBackend 并在启动时 new LoginService(providers, backend)。"));
            }

            if (ctx.HasFirebaseAuthAssembly && !ctx.FirebaseDefineOn)
            {
                issues.Add(LoginSetupIssue.Warning(
                    "Firebase 装了但登录模块没启用它",
                    "Firebase.Auth 程序集在场，但编译符号 COFFEEBEAN_FIREBASE_AUTH 没打开 —— " +
                    "CoffeeBean.Login.Firebase 适配程序集不会参与编译，后端仍然是开发后端。",
                    "本窗口点「启用 Firebase 后端」自动加符号（Android/iOS/Standalone 都会加）；" +
                    "若 Firebase 是通过 UPM 包安装的，versionDefines 会自动打开，无需手改。"));
            }

            if (!ctx.HasFirebaseAppAssembly && ctx.FirebaseDefineOn)
            {
                issues.Add(LoginSetupIssue.Error(
                    "编译符号与程序集不一致",
                    "COFFEEBEAN_FIREBASE_AUTH 已打开，但找不到 Firebase.App 程序集 —— 适配程序集会编译失败。",
                    "要么装齐 Firebase Unity SDK（App + Auth），要么把这个符号去掉。"));
            }
        }

        private static void ValidateSettings(LoginSetupContext ctx, List<LoginSetupIssue> issues)
        {
            if (!ctx.HasSettingsAsset)
            {
                issues.Add(LoginSetupIssue.Warning(
                    "没有 LoginSettings 资产",
                    "Resources/CoffeeBean/LoginSettings.asset 不存在：GoogleWebClientId 等参数只能靠代码传，" +
                    "Hub 工具也没法帮你检查配置。",
                    "本窗口点「创建 LoginSettings」生成资产（默认落在 Assets/Resources/CoffeeBean/LoginSettings.asset）。"));
            }
        }

        private static void ValidateGoogle(LoginSetupContext ctx, List<LoginSetupIssue> issues)
        {
            if (!ctx.HasGoogleSignInAssembly)
            {
                issues.Add(LoginSetupIssue.Warning(
                    "没有 Google 登录来源",
                    "工程里没检测到 GoogleSignIn 插件。现状（2026-09 官方文档）：Android 官方推荐 Credential Manager，" +
                    "但 Google **没有** 提供 Unity 包；老插件 googlesamples/google-signin-unity 仓库已 archived。",
                    "两条路选一条：① 自己写 Android 插件桥 Credential Manager（GetGoogleIdOption.setServerClientId(Web client id) → " +
                    "GoogleIdTokenCredential.getIdToken() → UnitySendMessage），C# 侧用 ExternalCredentialProvider.Submit(idToken) 交给本模块；" +
                    "② 装（仍可用的）google-signin-unity 插件，本模块会用反射自动接上。详见 README「Google 登录」一节。"));
            }

            if (ctx.HasGoogleSignInAssembly && !ctx.GoogleConfigured)
            {
                issues.Add(LoginSetupIssue.Error(
                    "没填 GoogleWebClientId",
                    "Google 登录要靠它拿 idToken（Firebase 的 GoogleAuthProvider 需要 idToken）；为空时真机上会 " +
                    "以 invalid-credential 失败。Credential Manager 路线下它就是 GetGoogleIdOption 的 serverClientId。",
                    "Google Cloud Console → API 和服务 → 凭据 → 创建「OAuth 客户端 ID」→ 类型选 **Web 应用**，" +
                    "把生成的客户端 ID 填进 LoginSettings.GoogleWebClientId。"));
            }
        }

        private static void ValidateAndroid(LoginSetupContext ctx, List<LoginSetupIssue> issues)
        {
            if (!ctx.TargetAndroid) return;

            if (!ctx.HasGoogleServicesJson)
            {
                issues.Add(LoginSetupIssue.Error(
                    "Android 缺 google-services.json",
                    "Firebase 在 Android 上从 Assets/google-services.json 读配置；缺了它 FirebaseApp 初始化会失败" +
                    "（日志里通常是 \"Failed to read Firebase options from the app's resources\"）。",
                    "Firebase 控制台 → 项目设置 → 你的 Android 应用 → 下载 google-services.json，" +
                    "放到 Assets/google-services.json（本模块的构建钩子会把它复制进 Gradle 的 assets）。"));
            }
            else if (!string.IsNullOrEmpty(ctx.ApplicationIdentifier)
                     && ctx.GoogleServicesPackageNames.Count > 0
                     && !ctx.GoogleServicesPackageNames.Contains(ctx.ApplicationIdentifier))
            {
                issues.Add(LoginSetupIssue.Error(
                    "google-services.json 与包名不匹配",
                    "当前 PlayerSettings 的包名是 " + ctx.ApplicationIdentifier + "，而 google-services.json 里只有：" +
                    string.Join(", ", ctx.GoogleServicesPackageNames.ToArray()) +
                    " —— Android 上会以 \"No matching client found for package name\" 直接构建失败。",
                    "① 在 Firebase 控制台按这个包名新增一个 Android 应用并重新下载 json；② 或把 PlayerSettings 的包名改成 json 里的那个。"));
            }

            if (ctx.AndroidMinSdkVersion > 0 && ctx.AndroidMinSdkVersion < MinimumAndroidSdk)
            {
                issues.Add(LoginSetupIssue.Warning(
                    "Android minSdk 过低",
                    "当前 minSdkVersion = " + ctx.AndroidMinSdkVersion + "，低于 Google 登录（Credential Manager / GIS）要求的 "
                    + MinimumAndroidSdk + "。",
                    "Player Settings → Android → Minimum API Level 调到 " + MinimumAndroidSdk + " 及以上。"));
            }

            if (string.IsNullOrEmpty(ctx.DebugKeystoreSha1))
            {
                issues.Add(LoginSetupIssue.Warning(
                    "取不到 debug keystore 的 SHA-1",
                    "没能在本机找到/解析 debug.keystore（keytool 不可用？）。",
                    "手动执行：keytool -list -v -keystore %USERPROFILE%\\.android\\debug.keystore -alias androiddebugkey " +
                    "-storepass android -keypass android，把 SHA1 填到 Firebase 控制台该 Android 应用的「SHA 证书指纹」。"));
            }
        }

        private static void ValidateIos(LoginSetupContext ctx, List<LoginSetupIssue> issues)
        {
            if (!ctx.TargetIos) return;

            if (!ctx.HasGoogleServiceInfoPlist)
            {
                issues.Add(LoginSetupIssue.Error(
                    "iOS 缺 GoogleService-Info.plist",
                    "Firebase 在 iOS 上从 Assets/GoogleService-Info.plist 读配置；缺了它 FirebaseApp 初始化会失败。",
                    "Firebase 控制台 → 项目设置 → 你的 iOS 应用 → 下载 GoogleService-Info.plist，" +
                    "放到 Assets/GoogleService-Info.plist。"));
            }

            if (ctx.HasGoogleSignInAssembly && string.IsNullOrEmpty(ctx.GoogleIosClientId))
            {
                issues.Add(LoginSetupIssue.Warning(
                    "iOS 没填 GoogleIosClientId",
                    "iOS 上 Google 登录需要 CLIENT_ID（通常还会配反向 client id 作为 URL scheme）。",
                    "Google Cloud Console 建一个 iOS 类型的 OAuth 客户端 ID，填进 LoginSettings.GoogleIosClientId；" +
                    "并把反向 client id（com.googleusercontent.apps.xxxx）加进 Info.plist 的 CFBundleURLTypes。"));
            }
        }

        /// <summary>
        /// 从 google-services.json 文本里抽出所有 <c>package_name</c>（不引第三方 JSON 库：
        /// 这里只要这一列值，正则足够且便于单测）。
        /// </summary>
        public static List<string> ExtractPackageNames(string json)
        {
            var names = new List<string>();
            if (string.IsNullOrEmpty(json)) return names;
            foreach (Match match in Regex.Matches(json, "\"package_name\"\\s*:\\s*\"([^\"]+)\""))
            {
                string value = match.Groups[1].Value;
                if (!string.IsNullOrEmpty(value) && !names.Contains(value)) names.Add(value);
            }
            return names;
        }
    }
}
