using System;
using System.Reflection;
using System.Threading.Tasks;

namespace CoffeeBean
{
    /// <summary>
    /// Google Sign-In 提供者（**反射实现**，不硬引用任何插件）。
    ///
    /// ## 为什么是反射
    /// 现状（2026-09 官方文档核实）：
    /// · Android 官方推荐 **[Credential Manager](https://developer.android.com/identity/sign-in/credential-manager-siwg)**
    ///   （`androidx.credentials` + `googleid`），旧的 Google Sign-In for Android 已被标记 deprecated 并从 Play services
    ///   Auth SDK 移除中；
    /// · Google **没有**为 Credential Manager 提供任何 Unity 包，Unity 侧要自己写 Android 插件桥接；
    /// · 曾经通用的 Unity 插件 `googlesamples/google-signin-unity` 仓库**已 archived**（只读），且没有官方 UPM 包。
    ///
    /// 所以本模块**不硬绑定**某个插件：如果工程里装了那个（仍能用的）`GoogleSignIn` 插件，本类通过反射自动接上；
    /// 走 Credential Manager 新路线的工程则用 <see cref="ExternalCredentialProvider"/> 把
    /// 自己桥接回来的 idToken 交给登录流程（见 README 的「Android 新路线」）。
    ///
    /// ## 拿到的凭据
    /// · Android：**只有 idToken**（Google 的 Credential Manager 不返回 accessToken / serverAuthCode）→
    ///   后端要用 `GetCredential(idToken, null)`；
    /// · iOS：idToken + accessToken 都有；
    /// · 没有 idToken 就是失败（Firebase 会以 invalid-credential 拒绝）。
    /// </summary>
    public sealed class GoogleSignInReflectionProvider : ILoginProvider
    {
        private const string PluginTypeName = "GoogleSignIn.GoogleSignIn, GoogleSignIn";

        private readonly object _instance;                 // GoogleSignIn.DefaultInstance
        private readonly MethodInfo _signInMethod;         // Task<GoogleSignInUser> SignIn()

        public GoogleSignInReflectionProvider()
        {
            Type pluginType = FindType(PluginTypeName);
            if (pluginType == null)
            {
                UnavailableReason = "未检测到 GoogleSignIn 插件（" + PluginTypeName + "）。" +
                                    "Android 新路线请用 ExternalCredentialProvider 接 Credential Manager 桥；" +
                                    "或装 google-signin-unity 插件（该仓库已 archived）。";
                return;
            }

            PropertyInfo defaultInstance = pluginType.GetProperty("DefaultInstance",
                BindingFlags.Public | BindingFlags.Static);
            _instance = defaultInstance != null ? defaultInstance.GetValue(null, null) : null;
            if (_instance == null)
            {
                UnavailableReason = "GoogleSignIn.DefaultInstance 取不到（插件没初始化？）。";
                return;
            }

            _signInMethod = pluginType.GetMethod("SignIn", BindingFlags.Public | BindingFlags.Instance, null,
                Type.EmptyTypes, null);
            if (_signInMethod == null)
            {
                UnavailableReason = "GoogleSignIn 插件里找不到无参 SignIn() 方法（插件版本不兼容）。";
                return;
            }

            UnavailableReason = string.Empty;
        }

        public LoginProviderKind Kind => LoginProviderKind.Google;

        public string ProviderId => "google.com";

        public bool IsAvailable => string.IsNullOrEmpty(UnavailableReason);

        public string UnavailableReason { get; }

        public void SignIn(Action<LoginCredential> onSuccess, Action<LoginErrorCode, string> onError)
        {
            if (!IsAvailable)
            {
                if (onError != null) onError(LoginErrorCode.ProviderUnavailable, UnavailableReason);
                return;
            }

            object result;
            try
            {
                result = _signInMethod.Invoke(_instance, null);
            }
            catch (Exception e)
            {
                if (onError != null) onError(LoginErrorCode.Unknown, "调用 GoogleSignIn.SignIn() 失败：" + e.Message);
                return;
            }

            var task = result as Task;
            if (task == null)
            {
                if (onError != null) onError(LoginErrorCode.Unknown, "GoogleSignIn.SignIn() 没返回 Task（插件版本不兼容）。");
                return;
            }

            task.ContinueWith(t => LoginMainThread.Run(() =>
            {
                if (t.IsCanceled)
                {
                    if (onError != null) onError(LoginErrorCode.Cancelled, "用户取消了 Google 登录。");
                    return;
                }
                if (t.IsFaulted)
                {
                    string message = t.Exception != null && t.Exception.GetBaseException() != null
                        ? t.Exception.GetBaseException().Message
                        : "未知错误";
                    // 插件在用户取消时抛的是 "canceled" 文本；这里归类成 Cancelled，避免业务弹报错
                    bool cancelled = message.IndexOf("cancel", StringComparison.OrdinalIgnoreCase) >= 0
                                     || message.IndexOf("取消", StringComparison.Ordinal) >= 0;
                    if (onError != null)
                    {
                        onError(cancelled ? LoginErrorCode.Cancelled : LoginErrorCode.Unknown,
                            "Google 登录失败：" + message);
                    }
                    return;
                }

                object user = ReadResult(t);
                if (user == null)
                {
                    if (onError != null) onError(LoginErrorCode.Unknown, "Google 登录没有返回用户信息。");
                    return;
                }

                string idToken = ReadString(user, "IdToken");
                if (string.IsNullOrEmpty(idToken))
                {
                    if (onError != null)
                    {
                        onError(LoginErrorCode.InvalidCredential,
                            "Google 登录没有返回 idToken（Firebase 需要它）。Android 上请确认服务端 client id 用的是 **Web** 类型，" +
                            "并在插件里勾了 RequestIdToken。");
                    }
                    return;
                }

                if (onSuccess == null) return;
                onSuccess(new LoginCredential
                {
                    Kind = LoginProviderKind.Google,
                    ProviderId = "google.com",
                    IdToken = idToken,
                    AccessToken = ReadString(user, "AccessToken"),
                    ServerAuthCode = ReadString(user, "AuthCode"),
                    Email = ReadString(user, "Email"),
                    DisplayName = ReadString(user, "DisplayName"),
                    PhotoUrl = ReadString(user, "ImageUrl"),
                });
            }));
        }

        private static Type FindType(string assemblyQualifiedName)
        {
            try
            {
                return Type.GetType(assemblyQualifiedName, throwOnError: false);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>从 <c>Task&lt;T&gt;</c> 里取 <c>Result</c>（反射，避免依赖插件的具体类型）。</summary>
        private static object ReadResult(Task task)
        {
            PropertyInfo result = task.GetType().GetProperty("Result");
            return result != null ? result.GetValue(task, null) : null;
        }

        private static string ReadString(object target, string propertyName)
        {
            if (target == null) return null;
            PropertyInfo property = target.GetType().GetProperty(propertyName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property == null) return null;
            object value = property.GetValue(target, null);
            return value != null ? value.ToString() : null;
        }
    }
}
