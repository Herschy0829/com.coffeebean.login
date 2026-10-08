using System.Collections.Generic;
using UnityEngine;

namespace CoffeeBean
{
    /// <summary>
    /// 登录模块配置（ScriptableObject：`Create → CoffeeBean → Login Settings`）。
    ///
    /// **它只管"客户端要什么参数"**，不管密钥：Firebase 的配置文件（`google-services.json` /
    /// `GoogleService-Info.plist`）由 Firebase SDK 自己读，这里只放 OAuth 的 client id 之类
    /// 必须由代码传给 SDK 的东西。
    ///
    /// 放到 `Resources/CoffeeBean/LoginSettings.asset` 即可被 <see cref="LoadFromResources"/> 自动找到
    /// （或自己持有引用传给 <see cref="LoginService.Initialize"/>）。
    /// </summary>
    [CreateAssetMenu(menuName = "CoffeeBean/Login Settings", fileName = "LoginSettings")]
    public sealed class LoginSettings : ScriptableObject
    {
        /// <summary>Resources 下的默认路径（不含扩展名）。</summary>
        public const string DefaultResourcePath = "CoffeeBean/LoginSettings";

        [Header("Google 登录")]
        [Tooltip("OAuth **Web** 客户端 ID（Google Cloud Console → 凭据 → OAuth 2.0 客户端 ID → 网页应用）。" +
                 "Android 上就是 requestIdToken / serverClientId 要的那个 id；它决定 idToken 的 audience。")]
        public string GoogleWebClientId;

        [Tooltip("OAuth iOS 客户端 ID（iOS 用；Android 留空）。")]
        public string GoogleIosClientId;

        [Header("后端")]
        [Tooltip("装了 Firebase 时是否启用 Firebase 后端（关掉 = 只走自定义后端）。")]
        public bool EnableFirebaseBackend = true;

        [Header("启动行为")]
        [Tooltip("启动时自动游客登录（拿到一个真实 uid，之后再绑定 Google；强烈建议开）。")]
        public bool SignInAsGuestOnStart = true;

        [Tooltip("已登录时是否在启动阶段强制刷新一次 ID token。")]
        public bool RefreshTokenOnStart;

        [Tooltip("ID token 提前多久刷新（秒）——后端校验前留出的安全边界。")]
        public int IdTokenRefreshMarginSeconds = 300;

        [Header("开发期")]
        [Tooltip("编辑器里用**模拟 Google 登录**（不需要装 Google SDK，就能把 UI 流程跑通；" +
                 "需要配一个「开发后端」来接收模拟凭据）。仅编辑器生效，打包后无效。")]
        public bool SimulateGoogleInEditor = true;

        [Tooltip("模拟登录的假邮箱（仅编辑器）。")]
        public string SimulatedEmail = "editor@local";

        /// <summary>从 Resources 加载配置；没有就返回 null（调用方决定用默认值）。</summary>
        public static LoginSettings LoadFromResources(string path = DefaultResourcePath)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var settings = Resources.Load<LoginSettings>(path);
            if (settings == null)
            {
                LoginLog.Debug_("未在 Resources/" + path + " 找到 LoginSettings，使用默认值。");
            }
            return settings;
        }

        /// <summary>
        /// 自检：返回"配置问题"列表（空 = 没问题）。设置窗口与测试都用它，避免两处判断漂移。
        /// </summary>
        public List<string> Validate(bool firebaseBackendActive = true)
        {
            var issues = new List<string>();

            if (firebaseBackendActive && string.IsNullOrEmpty(GoogleWebClientId))
            {
                issues.Add("未填 GoogleWebClientId：Android/iOS 上 Google 登录拿不到 idToken（Firebase 会以 " +
                           "invalid-credential 失败）。到 Google Cloud Console 建一个 **Web** 类型的 OAuth 客户端 ID 填进来。");
            }

            if (IdTokenRefreshMarginSeconds < 0)
            {
                issues.Add("IdTokenRefreshMarginSeconds 不能为负。");
            }

            if (SignInAsGuestOnStart && !firebaseBackendActive)
            {
                issues.Add("SignInAsGuestOnStart = true，但当前没有可用的后端：游客登录会失败（需要一个会签发会话的后端）。");
            }

            return issues;
        }
    }
}
