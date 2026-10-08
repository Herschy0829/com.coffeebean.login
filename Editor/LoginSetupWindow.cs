using System.Collections.Generic;
using System.IO;
using CoffeeBean.EditorTools;
using UnityEditor;
using UnityEngine;

namespace CoffeeBean.Login.EditorTools
{
    /// <summary>
    /// 登录模块自检窗口（入口统一收敛到 CoffeeBean Hub：Window &gt; CoffeeBean → Login · 登录配置）。
    ///
    /// 它做三件事：
    /// ① 把**当前工程的真实状态**跑一遍 <see cref="LoginSetupValidator"/>，逐条告诉你怎么修；
    /// ② 显示运行时视角的"后端 + 可用登录方式"（和 <c>LoginService.DescribeProviders()</c> 一致）；
    /// ③ 提供几个高频动作：创建 LoginSettings / 一键开关 Firebase 编译符号 / 复制 debug keystore 的 SHA-1
    ///    （SHA-1 没登记到 Firebase 控制台是 Android 上最常见的失败原因，而它最容易漏）。
    /// </summary>
    [CoffeeBeanTool("登录配置自检", "Google + Firebase 登录：工程配置逐条自检、一键开关编译符号、取 SHA-1", "Login")]
    public sealed class LoginSetupWindow : EditorWindow
    {
        private LoginSetupContext _context;
        private List<LoginSetupIssue> _issues = new List<LoginSetupIssue>();
        private Vector2 _scroll;
        private string _runtimeSummary = string.Empty;
        private string _releaseKeystorePath = string.Empty;
        private string _releaseKeystoreAlias = string.Empty;
        private string _releaseKeystoreStorePass = string.Empty;
        private string _releaseKeystoreKeyPass = string.Empty;

        public static void Open()
        {
            var window = GetWindow<LoginSetupWindow>("CoffeeBean Login");
            window.Refresh();
        }

        private void OnEnable() => Refresh();

        private void Refresh()
        {
            _context = LoginSetupEnvironment.Capture();
            _issues = LoginSetupValidator.Validate(_context);
            _runtimeSummary = BuildRuntimeSummary();
            Repaint();
        }

        private static string BuildRuntimeSummary()
        {
            // 只做"装配 + 描述"，不初始化（不触发任何网络/登录）
            LoginService service = LoginFactory.CreateDefault(null, out ExternalCredentialProvider _);
            return service.DescribeProviders();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("CoffeeBean Login —— 工程配置自检", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            if (GUILayout.Button("重新自检", GUILayout.Height(24))) Refresh();

            EditorGUILayout.Space(6);
            DrawSummary();

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("自检结果", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(320));
            foreach (LoginSetupIssue issue in _issues)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(LevelIcon(issue.Level) + " " + issue.Title, EditorStyles.boldLabel);
                if (!string.IsNullOrEmpty(issue.Detail)) EditorGUILayout.LabelField(issue.Detail, EditorStyles.wordWrappedMiniLabel);
                if (!string.IsNullOrEmpty(issue.Fix)) EditorGUILayout.LabelField("怎么修：" + issue.Fix, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(6);
            DrawActions();

            EditorGUILayout.Space(6);
            DrawKeystoreHelper();
        }

        private void DrawSummary()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("当前构建目标：" + EditorUserBuildSettings.activeBuildTarget
                                       + "   包名：" + (string.IsNullOrEmpty(_context.ApplicationIdentifier) ? "(未知)" : _context.ApplicationIdentifier)
                                       + "   minSdk：" + _context.AndroidMinSdkVersion);
            EditorGUILayout.LabelField("配置文件：" + DescribeConfigFiles());
            EditorGUILayout.LabelField("SDK：" + DescribeSdks());
            EditorGUILayout.LabelField("编译符号：COFFEEBEAN_FIREBASE_AUTH = " + (_context.FirebaseDefineOn ? "开" : "关"));
            EditorGUILayout.LabelField("运行时可用的登录方式：");
            EditorGUILayout.LabelField(_runtimeSummary, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();
        }

        private string DescribeConfigFiles()
        {
            string json = _context.HasGoogleServicesJson
                ? _context.GoogleServicesJsonPath + "（" + (_context.GoogleServicesPackageNames.Count > 0
                    ? string.Join(", ", _context.GoogleServicesPackageNames.ToArray())
                    : "没解析出 package_name") + "）"
                : "缺 google-services.json";
            string plist = _context.HasGoogleServiceInfoPlist ? _context.GoogleServiceInfoPlistPath : "缺 GoogleService-Info.plist";
            return json + "；" + plist;
        }

        private string DescribeSdks()
        {
            return "Firebase.Auth=" + (_context.HasFirebaseAuthAssembly ? "有" : "无")
                   + "，Firebase.App=" + (_context.HasFirebaseAppAssembly ? "有" : "无")
                   + "，GoogleSignIn 插件=" + (_context.HasGoogleSignInAssembly ? "有" : "无");
        }

        private void DrawActions()
        {
            EditorGUILayout.LabelField("常用动作", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("① 创建设置资产 (LoginSettings)"))
            {
                CreateOrSelectSettingsAsset();
            }
            if (GUILayout.Button("② 打开 Firebase 控制台"))
            {
                Application.OpenURL("https://console.firebase.google.com/");
            }
            if (GUILayout.Button("③ 打开 Google Cloud 凭据页"))
            {
                Application.OpenURL("https://console.cloud.google.com/apis/credentials");
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(_context.FirebaseDefineOn ? "④ 关闭 Firebase 后端符号" : "④ 启用 Firebase 后端符号"))
            {
                LoginSetupEnvironment.SetDefine("COFFEEBEAN_FIREBASE_AUTH", !_context.FirebaseDefineOn, out string report);
                LoginLog.Log("COFFEEBEAN_FIREBASE_AUTH：" + report);
                Refresh();
            }
            if (GUILayout.Button("⑤ 复制 debug keystore SHA-1"))
            {
                if (string.IsNullOrEmpty(_context.DebugKeystoreSha1))
                {
                    EditorUtility.DisplayDialog("没取到 SHA-1",
                        "没能在本机找到 / 解析 debug.keystore。\n\n手动命令（Windows）：\n" +
                        "keytool -list -v -keystore %USERPROFILE%\\.android\\debug.keystore -alias androiddebugkey " +
                        "-storepass android -keypass android", "知道了");
                }
                else
                {
                    EditorGUIUtility.systemCopyBuffer = _context.DebugKeystoreSha1;
                    EditorUtility.DisplayDialog("已复制 SHA-1",
                        _context.DebugKeystoreSha1 + "\n\n粘到：Firebase 控制台 → 项目设置 → 你的 Android 应用 → " +
                        "「SHA 证书指纹」→ 添加指纹，然后**重新下载 google-services.json**。", "好的");
                }
            }
            if (GUILayout.Button("⑥ 打开模块 README"))
            {
                string readme = FindReadmePath();
                if (!string.IsNullOrEmpty(readme)) Application.OpenURL("file://" + Path.GetFullPath(readme));
                else Application.OpenURL("https://github.com/Herschy0829/com.coffeebean.login#readme");
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawKeystoreHelper()
        {
            EditorGUILayout.LabelField("release keystore SHA-1（也要登记到 Firebase，别只登记 debug）", EditorStyles.boldLabel);
            _releaseKeystorePath = EditorGUILayout.TextField("keystore 路径", _releaseKeystorePath);
            _releaseKeystoreAlias = EditorGUILayout.TextField("别名 alias", _releaseKeystoreAlias);
            _releaseKeystoreStorePass = EditorGUILayout.PasswordField("storePassword", _releaseKeystoreStorePass);
            _releaseKeystoreKeyPass = EditorGUILayout.PasswordField("keyPassword", _releaseKeystoreKeyPass);

            if (GUILayout.Button("读取 SHA-1"))
            {
                string sha1 = LoginSetupEnvironment.TryGetKeystoreSha1(_releaseKeystorePath, _releaseKeystoreAlias,
                    _releaseKeystoreStorePass, _releaseKeystoreKeyPass);
                if (string.IsNullOrEmpty(sha1))
                {
                    EditorUtility.DisplayDialog("读不到 SHA-1", "路径/别名/口令检查一下；也可以手动跑 keytool。", "知道了");
                }
                else
                {
                    EditorGUIUtility.systemCopyBuffer = sha1;
                    EditorUtility.DisplayDialog("已复制 SHA-1", sha1, "好的");
                }
            }
        }

        private void CreateOrSelectSettingsAsset()
        {
            const string path = LoginSetupEnvironment.DefaultSettingsAssetPath;
            var existing = AssetDatabase.LoadAssetAtPath<LoginSettings>(path);
            if (existing != null)
            {
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                return;
            }

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var settings = CreateInstance<LoginSettings>();
            AssetDatabase.CreateAsset(settings, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = settings;
            LoginLog.Log("已创建 " + path + "：把 GoogleWebClientId 填上（Web 类型的 OAuth 客户端 ID）。");
            Refresh();
        }

        private static string FindReadmePath()
        {
            // 包在 Packages/ 下时是 Packages/com.coffeebean.login/README.md；
            // 本地 file: 引用时可能是相对路径，这里两种都试一遍。
            string[] candidates =
            {
                "Packages/com.coffeebean.login/README.md",
                "Packages/com.coffeebean.login/README.md.txt",
            };
            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        private static string LevelIcon(LoginSetupLevel level)
        {
            switch (level)
            {
                case LoginSetupLevel.Error: return "✗";
                case LoginSetupLevel.Warning: return "!";
                default: return "·";
            }
        }
    }
}
