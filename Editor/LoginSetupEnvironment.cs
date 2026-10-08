using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CoffeeBean.Login.EditorTools
{
    /// <summary>
    /// 把"工程现状"采集进 <see cref="LoginSetupContext"/>（判断逻辑在 <see cref="LoginSetupValidator"/>，纯函数、可单测）。
    /// 另外提供两个真机上最容易踩的辅助：**本机 debug keystore 的 SHA-1**、任意 keystore 的 SHA-1。
    /// </summary>
    public static class LoginSetupEnvironment
    {
        /// <summary>LoginSettings 资产的默认路径。</summary>
        public const string DefaultSettingsAssetPath = "Assets/Resources/CoffeeBean/LoginSettings.asset";

        private static readonly string[] GoogleServicesJsonCandidates =
        {
            "Assets/google-services.json",
            "Assets/Plugins/Android/google-services.json",
            "Assets/StreamingAssets/google-services.json",
        };

        public static LoginSetupContext Capture()
        {
            var ctx = new LoginSetupContext();

            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            ctx.TargetAndroid = target == BuildTarget.Android;
            ctx.TargetIos = target == BuildTarget.iOS;
            ctx.AndroidMinSdkVersion = (int)PlayerSettings.Android.minSdkVersion;
            try
            {
                ctx.ApplicationIdentifier = PlayerSettings.GetApplicationIdentifier(
                    NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(target)));
            }
            catch (Exception e)
            {
                LoginLog.Debug_("取包名失败：" + e.Message);
            }

            // ---- 配置文件 ----
            foreach (string candidate in GoogleServicesJsonCandidates)
            {
                if (!File.Exists(candidate)) continue;
                ctx.HasGoogleServicesJson = true;
                ctx.GoogleServicesJsonPath = candidate;
                try
                {
                    ctx.GoogleServicesPackageNames = LoginSetupValidator.ExtractPackageNames(File.ReadAllText(candidate));
                }
                catch (Exception e)
                {
                    LoginLog.Warn("读 " + candidate + " 失败：" + e.Message);
                }
                break;
            }

            const string plistPath = "Assets/GoogleService-Info.plist";
            if (File.Exists(plistPath))
            {
                ctx.HasGoogleServiceInfoPlist = true;
                ctx.GoogleServiceInfoPlistPath = plistPath;
            }

            // ---- SDK 程序集（反射扫描已加载程序集；Unity 会加载工程里所有托管程序集）----
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name = assembly.GetName().Name;
                if (name == "Firebase.Auth") ctx.HasFirebaseAuthAssembly = true;
                else if (name == "Firebase.App") ctx.HasFirebaseAppAssembly = true;
                else if (name == "GoogleSignIn") ctx.HasGoogleSignInAssembly = true;
            }

            // ---- 编译符号 ----
            try
            {
                string defines = PlayerSettings.GetScriptingDefineSymbols(ActiveNamedBuildTarget(target));
                ctx.FirebaseDefineOn = ContainsDefine(defines, "COFFEEBEAN_FIREBASE_AUTH");
            }
            catch (Exception e)
            {
                LoginLog.Debug_("读编译符号失败：" + e.Message);
            }

            // ---- 模块配置资产 ----
            var settings = AssetDatabase.LoadAssetAtPath<LoginSettings>(DefaultSettingsAssetPath);
            if (settings != null)
            {
                ctx.HasSettingsAsset = true;
                ctx.GoogleWebClientId = settings.GoogleWebClientId;
                ctx.GoogleIosClientId = settings.GoogleIosClientId;
            }

            ctx.DebugKeystoreSha1 = TryGetDebugKeystoreSha1();
            return ctx;
        }

        /// <summary>当前激活构建目标的 NamedBuildTarget（编译符号/包名 API 都要它）。</summary>
        public static NamedBuildTarget ActiveNamedBuildTarget()
            => NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget));

        private static NamedBuildTarget ActiveNamedBuildTarget(BuildTarget target)
            => NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(target));

        /// <summary>把符号写进（或移出）所有平台 —— 本模块的适配程序集不带平台限制，全平台都该一致。</summary>
        public static void SetDefine(string define, bool enable, out string report)
        {
            var lines = new List<string>();
            foreach (NamedBuildTarget target in new[]
                     {
                         NamedBuildTarget.Android, NamedBuildTarget.iOS, NamedBuildTarget.Standalone,
                     })
            {
                string defines = PlayerSettings.GetScriptingDefineSymbols(target);
                bool has = ContainsDefine(defines, define);
                if (enable == has)
                {
                    lines.Add(target.TargetName + ": 已是 " + (enable ? "开" : "关"));
                    continue;
                }

                string updated = enable ? AppendDefine(defines, define) : RemoveDefine(defines, define);
                PlayerSettings.SetScriptingDefineSymbols(target, updated);
                lines.Add(target.TargetName + ": " + (enable ? "已打开" : "已关闭"));
            }

            report = string.Join("；", lines.ToArray());
        }

        public static bool ContainsDefine(string defines, string define)
        {
            if (string.IsNullOrEmpty(defines)) return false;
            string[] parts = defines.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                if (string.Equals(part.Trim(), define, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static string AppendDefine(string defines, string define)
        {
            if (string.IsNullOrEmpty(defines)) return define;
            return defines.TrimEnd(';', ',', ' ') + ";" + define;
        }

        private static string RemoveDefine(string defines, string define)
        {
            if (string.IsNullOrEmpty(defines)) return defines;
            var kept = new List<string>();
            foreach (string part in defines.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!string.Equals(part.Trim(), define, StringComparison.Ordinal)) kept.Add(part.Trim());
            }
            return string.Join(";", kept.ToArray());
        }

        /// <summary>本机 Android debug keystore 的 SHA-1（取不到返回 null：keytool 不可用/文件不存在）。</summary>
        public static string TryGetDebugKeystoreSha1()
        {
            string keystore = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".android", "debug.keystore");
            if (!File.Exists(keystore)) return null;
            return TryGetKeystoreSha1(keystore, "androiddebugkey", "android", "android");
        }

        /// <summary>用 keytool 读任意 keystore 的 SHA-1（release 签名也要登记到 Firebase，别只登记 debug）。</summary>
        public static string TryGetKeystoreSha1(string keystorePath, string alias, string storePassword, string keyPassword)
        {
            if (string.IsNullOrEmpty(keystorePath) || !File.Exists(keystorePath)) return null;

            string keytool = ResolveKeytool();
            if (string.IsNullOrEmpty(keytool)) return null;

            try
            {
                var info = new ProcessStartInfo
                {
                    FileName = keytool,
                    Arguments = "-list -v -keystore \"" + keystorePath + "\" -alias " + alias
                                + " -storepass " + storePassword + " -keypass " + keyPassword,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using (Process process = Process.Start(info))
                {
                    if (process == null) return null;
                    string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                    process.WaitForExit(10000);
                    // keytool 会把 SHA1 打成 "SHA1: AA:BB:..."，也可能写成 "SHA-1:"
                    Match match = Regex.Match(output, "SHA-?1:\\s*([0-9A-Fa-f:]{20,})");
                    if (match.Success) return NormalizeSha1(match.Groups[1].Value);
                    LoginLog.Warn("keytool 输出里没找到 SHA1（keystore/别名/口令是否正确？）");
                    return null;
                }
            }
            catch (Exception e)
            {
                LoginLog.Warn("调用 keytool 失败：" + e.Message);
                return null;
            }
        }

        /// <summary>统一成 Firebase 控制台要求的 `AA:BB:...` 大写冒号格式。</summary>
        public static string NormalizeSha1(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            string hex = raw.Replace(":", string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
            var parts = new List<string>();
            for (int i = 0; i + 1 < hex.Length; i += 2) parts.Add(hex.Substring(i, 2));
            return string.Join(":", parts.ToArray());
        }

        private static string ResolveKeytool()
        {
            // Unity 自带 OpenJDK 里的 keytool 最稳（用户机器上不一定有 JDK）
            string[] candidates =
            {
                Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines/AndroidPlayer/OpenJDK/bin/keytool.exe"),
                Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines/AndroidPlayer/OpenJDK/bin/keytool"),
                "keytool",
            };

            foreach (string candidate in candidates)
            {
                if (candidate == "keytool") return candidate;   // 交给 PATH
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }
    }
}
