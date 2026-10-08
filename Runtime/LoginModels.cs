using System;
using System.Collections.Generic;

namespace CoffeeBean
{
    /// <summary>登录方式（对外只暴露"用户可见的登录方式"，不暴露 SDK 细节）。</summary>
    public enum LoginProviderKind
    {
        /// <summary>未指定。</summary>
        None = 0,

        /// <summary>游客/匿名（无需任何第三方 SDK，任何平台可用）。</summary>
        Guest = 1,

        /// <summary>Google 账号。</summary>
        Google = 2,

        /// <summary>Apple 账号（Sign in with Apple；模块预留，未内置适配程序集）。</summary>
        Apple = 3,

        /// <summary>邮箱 + 密码（由后端实现提供，如 Firebase Email/Password）。</summary>
        Email = 4,

        /// <summary>工程自己接入的方式（实现 <see cref="ILoginProvider"/> 后注册即可）。</summary>
        Custom = 99,
    }

    /// <summary>登录服务的状态机。</summary>
    public enum LoginState
    {
        /// <summary>还没初始化（未指定后端或未调用 Initialize）。</summary>
        NotInitialized = 0,

        /// <summary>已初始化、当前未登录。</summary>
        SignedOut = 1,

        /// <summary>正在登录（第三方弹窗/后端请求进行中）。</summary>
        SigningIn = 2,

        /// <summary>已登录。</summary>
        SignedIn = 3,

        /// <summary>最近一次登录失败（可重试；<c>CurrentUser</c> 仍为 null）。</summary>
        Failed = 4,
    }

    /// <summary>登录错误码（业务按码做提示，不要解析字符串）。</summary>
    public enum LoginErrorCode
    {
        None = 0,

        /// <summary>服务未初始化。</summary>
        NotInitialized,

        /// <summary>该登录方式在当前平台/SDK 环境不可用（未装 SDK、平台不支持）。</summary>
        ProviderUnavailable,

        /// <summary>用户主动取消（关闭了 Google 弹窗）。**不要弹错误提示**。</summary>
        Cancelled,

        /// <summary>网络错误（可重试）。</summary>
        NetworkError,

        /// <summary>凭据非法（idToken 过期/被伪造）。</summary>
        InvalidCredential,

        /// <summary>该账号已存在（转正/绑定时需要先登录原账号再合并）。</summary>
        AccountExists,

        /// <summary>当前账号已绑定过该方式。</summary>
        AlreadyLinked,

        /// <summary>未登录却调用了需要登录的接口。</summary>
        NotSignedIn,

        /// <summary>后端（Firebase/自建服务）返回的业务错误。</summary>
        BackendError,

        /// <summary>其它未分类错误。</summary>
        Unknown,
    }

    /// <summary>登录成功后的用户信息（与具体后端解耦；Firebase 实现会把 uid/email 填进来）。</summary>
    public sealed class LoginUser
    {
        /// <summary>后端用户唯一 id（Firebase 的 <c>uid</c>；游客模式下也是真实 uid，不是本地随机数）。</summary>
        public string UserId;

        /// <summary>显示名。</summary>
        public string DisplayName;

        /// <summary>邮箱（Google 登录通常有；匿名/游客没有）。</summary>
        public string Email;

        /// <summary>头像 URL（Google 登录通常有）。</summary>
        public string PhotoUrl;

        /// <summary>是否匿名账号（游客）。</summary>
        public bool IsAnonymous;

        /// <summary>提供者 id：<c>google.com</c> / <c>anonymous</c> / <c>password</c> …</summary>
        public string ProviderId;

        /// <summary>是否为空用户。</summary>
        public bool IsEmpty => string.IsNullOrEmpty(UserId);

        public override string ToString()
        {
            if (IsEmpty) return "(not signed in)";
            return (ProviderId ?? "?") + ":" + UserId + (string.IsNullOrEmpty(Email) ? string.Empty : " <" + Email + ">");
        }

        /// <summary>复制一份（避免把内部实例暴露出去被业务改）。</summary>
        public LoginUser Clone()
        {
            return new LoginUser
            {
                UserId = UserId,
                DisplayName = DisplayName,
                Email = Email,
                PhotoUrl = PhotoUrl,
                IsAnonymous = IsAnonymous,
                ProviderId = ProviderId,
            };
        }
    }

    /// <summary>
    /// 第三方登录凭据：<see cref="ILoginProvider"/> 拿到的、要交给 <see cref="ILoginBackend"/> 去换会话的东西。
    ///
    /// **为什么要有这层**：Google 登录（拿到 idToken/accessToken）与"换成后端会话"是**两件事**：
    /// 前者依赖平台 SDK，后者依赖后端（Firebase Auth / 自建服务）。分开后：
    /// · 换后端不用改 Google 代码；· 换登录方式不用改后端代码；· 单元测试可以只 mock 一边。
    /// </summary>
    public sealed class LoginCredential
    {
        /// <summary>来自哪种登录方式。</summary>
        public LoginProviderKind Kind;

        /// <summary>后端提供者 id：Google = <c>google.com</c>，匿名 = <c>anonymous</c>，邮箱 = <c>password</c>。</summary>
        public string ProviderId;

        /// <summary>OIDC ID Token（Google 登录的核心产物；Firebase 的 <c>GoogleAuthProvider.GetCredential</c> 要用它）。</summary>
        public string IdToken;

        /// <summary>OAuth Access Token（可选；Firebase 允许传 null）。</summary>
        public string AccessToken;

        /// <summary>服务端授权码（可选；要"用 Google 换自己的后端会话"时用）。</summary>
        public string ServerAuthCode;

        /// <summary>邮箱（Google 登录通常直接给，省一次后端请求）。</summary>
        public string Email;

        /// <summary>显示名。</summary>
        public string DisplayName;

        /// <summary>头像 URL。</summary>
        public string PhotoUrl;

        /// <summary>额外透传字段（自定义 provider 用）。</summary>
        public Dictionary<string, string> Extra;

        /// <summary>邮箱登录用：密码。</summary>
        public string Password;

        /// <summary>该凭据是否可用（后端实现拿它做前置校验；匿名不需要 idToken）。</summary>
        public bool HasToken => !string.IsNullOrEmpty(IdToken) || !string.IsNullOrEmpty(AccessToken);

        public override string ToString()
        {
            return Kind + "(" + (ProviderId ?? "?") + ", idToken=" + (string.IsNullOrEmpty(IdToken) ? "-" : "有")
                   + ", accessToken=" + (string.IsNullOrEmpty(AccessToken) ? "-" : "有") + ")";
        }
    }

    /// <summary>登录结果（一次登录/绑定调用的产物）。</summary>
    public sealed class LoginResult
    {
        /// <summary>是否成功。</summary>
        public bool Success;

        /// <summary>错误码（成功时为 <see cref="LoginErrorCode.None"/>）。</summary>
        public LoginErrorCode Code;

        /// <summary>错误描述（用于日志与兜底提示；**不要**拿它做分支判断）。</summary>
        public string Error;

        /// <summary>成功时的用户。</summary>
        public LoginUser User;

        /// <summary>失败是否值得重试（网络类为 true；用户取消为 false）。</summary>
        public bool CanRetry => Code == LoginErrorCode.NetworkError || Code == LoginErrorCode.BackendError;

        public static LoginResult Ok(LoginUser user)
        {
            return new LoginResult { Success = true, Code = LoginErrorCode.None, User = user };
        }

        public static LoginResult Fail(LoginErrorCode code, string error)
        {
            return new LoginResult { Success = false, Code = code, Error = error };
        }

        public override string ToString()
        {
            return Success ? "OK " + (User != null ? User.ToString() : "-") : "FAIL " + Code + ": " + Error;
        }
    }
}
