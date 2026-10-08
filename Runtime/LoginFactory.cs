using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoffeeBean
{
    /// <summary>
    /// 默认装配工厂：按"**装了哪个 SDK 就用哪个实现**"的规则自动接好，
    /// 通过**全名反射**加载 SDK 适配程序集 —— 主程序集不引用 Firebase / Google Sign-In，
    /// 因此没装它们也能编译（这正是把适配层分成独立 asmdef 的目的）。
    ///
    /// 装配顺序（每一步失败都有明确降级与原因，不会静默什么都不做）：
    /// 后端：Firebase（装了 `com.google.firebase.auth` / 定义了 <c>COFFEEBEAN_FIREBASE_AUTH</c>）
    ///        → 开发后端（仅编辑器 / DEVELOPMENT_BUILD）
    ///        → <see cref="UnavailableLoginBackend"/>
    /// 登录方式：Google Sign-In 插件（装了就用真的）
    ///        → 编辑器模拟 Google（<see cref="LoginSettings.SimulateGoogleInEditor"/>）
    /// </summary>
    public static class LoginFactory
    {
        /// <summary>Firebase 后端适配类型的程序集限定名（反射加载，避免主程序集硬引用 SDK）。</summary>
        public const string FirebaseBackendTypeName = "CoffeeBean.FirebaseLoginBackend, CoffeeBean.Login.Firebase";

        /// <summary>按当前工程"装了什么"装配一个可用的 LoginService（并暴露外部凭据入口给平台桥用）。</summary>
        public static LoginService CreateDefault(LoginSettings settings = null)
        {
            ExternalCredentialProvider external;
            return CreateDefault(settings, out external);
        }

        /// <summary>
        /// 同 <see cref="CreateDefault(LoginSettings)"/>，额外把 <see cref="ExternalCredentialProvider"/>
        /// 交出来 —— Android Credential Manager / iOS GoogleSignIn 这类"自己写的平台桥"要往它里面
        /// <c>Submit(idToken)</c>（见 README「Android 新路线」）。
        /// </summary>
        public static LoginService CreateDefault(LoginSettings settings, out ExternalCredentialProvider externalProvider)
        {
            settings = settings != null ? settings : LoginSettings.LoadFromResources();
            ILoginBackend backend = CreateBackend(settings);
            List<ILoginProvider> providers = CreateProviders(settings, out externalProvider);
            return new LoginService(providers, backend, settings);
        }

        /// <summary>装配后端（Firebase → 开发后端 → 不可用占位）。</summary>
        public static ILoginBackend CreateBackend(LoginSettings settings)
        {
            if (settings == null || settings.EnableFirebaseBackend)
            {
                string reason;
                ILoginBackend firebase = TryCreate<ILoginBackend>(FirebaseBackendTypeName, out reason);
                if (firebase != null)
                {
                    LoginLog.Log("后端 = Firebase（" + firebase.Name + "）");
                    return firebase;
                }
                LoginLog.Debug_("Firebase 后端不可用：" + reason);
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            LoginLog.Warn("没有可用的真后端 → 回退到【开发后端（内存）】。功能可用，但账号不跨设备、token 不校验；" +
                          "正式环境请装 Firebase SDK 或实现自己的 ILoginBackend。");
            return new DevelopmentLoginBackend();
#else
            return new UnavailableLoginBackend(
                "没有可用的登录后端。请二选一：① 装 Firebase Unity SDK（并让 COFFEEBEAN_FIREBASE_AUTH 生效）；" +
                "② 自己实现 ILoginBackend 并显式 new LoginService(providers, backend)。");
#endif
        }

        /// <summary>
        /// 装配第三方登录方式：
        /// · Google：装了（已 archived 的）`GoogleSignIn` Unity 插件就反射接上；没装则留空 —— 走
        ///   Credential Manager 新路线的工程用 <paramref name="externalProvider"/> 手动 Submit；
        /// · 编辑器里再补一个模拟 Google（没有真 SDK 也能跑通整套 UI 流程）。
        /// </summary>
        public static List<ILoginProvider> CreateProviders(LoginSettings settings, out ExternalCredentialProvider externalProvider)
        {
            var providers = new List<ILoginProvider>();

            var pluginGoogle = new GoogleSignInReflectionProvider();
            if (pluginGoogle.IsAvailable)
            {
                LoginLog.Log("登录方式 = GoogleSignIn 插件（反射接入；注意该插件仓库已 archived）");
                providers.Add(pluginGoogle);
            }
            else
            {
                LoginLog.Debug_(pluginGoogle.UnavailableReason);
            }

            // 总是给一个外部凭据入口：平台桥（Credential Manager / iOS GoogleSignIn / 自建）都往它 Submit
            externalProvider = new ExternalCredentialProvider(LoginProviderKind.Google, "google.com");
            providers.Add(externalProvider);

#if UNITY_EDITOR
            // 批处理（跑测试/CI）时不加模拟提供者，保证"可用方式"是确定的
            if (!pluginGoogle.IsAvailable && settings != null && settings.SimulateGoogleInEditor && !Application.isBatchMode)
            {
                providers.Add(new SimulatedGoogleProvider(settings));
            }
#endif
            // Guest 不需要 provider：由 LoginService 直接调用后端的匿名登录（任何后端都必须支持）。
            return providers;
        }

        /// <summary>反射创建一个适配层实例；失败返回 null 并给出可读原因（设置窗口会显示它）。</summary>
        public static T TryCreate<T>(string assemblyQualifiedTypeName, out string reason) where T : class
        {
            reason = null;
            if (string.IsNullOrEmpty(assemblyQualifiedTypeName))
            {
                reason = "类型名为空";
                return null;
            }

            Type type = null;
            try
            {
                type = Type.GetType(assemblyQualifiedTypeName, throwOnError: false);
            }
            catch (Exception e)
            {
                reason = "类型解析失败：" + e.Message;
                return null;
            }

            if (type == null)
            {
                reason = "类型未找到（SDK 未安装 / 编译符号未打开 / 适配程序集未参与编译）：" + assemblyQualifiedTypeName;
                return null;
            }

            try
            {
                var instance = Activator.CreateInstance(type) as T;
                if (instance == null)
                {
                    reason = type.FullName + " 没有实现 " + typeof(T).Name + "（或无参构造）";
                    return null;
                }
                return instance;
            }
            catch (Exception e)
            {
                reason = "实例化失败：" + e.Message;
                return null;
            }
        }
    }

    /// <summary>
    /// "没有后端"的占位实现：所有操作都以 <see cref="LoginErrorCode.ProviderUnavailable"/> 失败，
    /// 但**不会抛异常**，且原因可读 —— 设置窗口/日志能把"为什么登录不了"直接说清楚，
    /// 而不是让业务收到一个 NullReferenceException。
    /// </summary>
    public sealed class UnavailableLoginBackend : ILoginBackend
    {
        private readonly string _reason;

        public UnavailableLoginBackend(string reason)
        {
            _reason = string.IsNullOrEmpty(reason) ? "没有可用的登录后端。" : reason;
        }

        public string Name => "Unavailable";

        public bool IsAvailable => false;

        public string UnavailableReason => _reason;

        public LoginUser CurrentUser => null;

        public event Action<LoginUser> StateChanged;

        public void SignInWithCredential(LoginCredential credential, Action<LoginUser> onSuccess, Action<LoginErrorCode, string> onError)
            => Fail(onError);

        public void SignInAnonymously(Action<LoginUser> onSuccess, Action<LoginErrorCode, string> onError)
            => Fail(onError);

        public void LinkWithCredential(LoginCredential credential, Action<LoginUser> onSuccess, Action<LoginErrorCode, string> onError)
            => Fail(onError);

        public void SignInWithEmail(string email, string password, Action<LoginUser> onSuccess, Action<LoginErrorCode, string> onError)
            => Fail(onError);

        public void SignOut()
        {
        }

        public void GetIdToken(bool forceRefresh, Action<string> onSuccess, Action<LoginErrorCode, string> onError)
            => Fail(onError);

        private void Fail(Action<LoginErrorCode, string> onError)
        {
            LoginLog.Error(_reason);
            if (onError != null) onError(LoginErrorCode.ProviderUnavailable, _reason);
        }
    }
}
