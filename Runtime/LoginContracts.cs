using System;

namespace CoffeeBean
{
    /// <summary>
    /// 第三方登录方式适配层：**只负责"拿到凭据"**（弹 Google 登录、拿 Sign in with Apple 的 identityToken…），
    /// 不关心后端是谁。实现方必须是线程安全的一次性调用（内部自己处理 SDK 的异步）。
    ///
    /// 回调约定（两个适配层一致，业务侧不用记两套）：
    /// · <paramref name="onSuccess"/> / <paramref name="onError"/> **一定在主线程被调用**（实现方负责切线程）；
    /// · 二者**只会被调用一次**，且不会同时调用；
    /// · 用户取消 → <c>onError(LoginErrorCode.Cancelled, ...)</c>，业务侧不要弹报错。
    /// </summary>
    public interface ILoginProvider
    {
        /// <summary>面向业务的登录方式。</summary>
        LoginProviderKind Kind { get; }

        /// <summary>后端提供者 id（Firebase 的 provider id，如 <c>google.com</c>；自定义后端可自定）。</summary>
        string ProviderId { get; }

        /// <summary>当前平台/环境下是否可用（未装 SDK、平台不支持的返回 false，不要抛异常）。</summary>
        bool IsAvailable { get; }

        /// <summary>不可用原因（给设置窗口/日志用；可用时可为空）。</summary>
        string UnavailableReason { get; }

        /// <summary>发起登录，拿到第三方凭据。</summary>
        void SignIn(Action<LoginCredential> onSuccess, Action<LoginErrorCode, string> onError);
    }

    /// <summary>
    /// 后端适配层：把第三方凭据换成"会话/用户"。默认实现是 Firebase Auth
    /// （<c>CoffeeBean.Login.Firebase</c> 程序集，装了 Firebase 才编译）；也可以换成自建账号服务。
    ///
    /// 为什么不让 <see cref="ILoginProvider"/> 直接登录：第三方只负责证明"这个人是谁"，
    /// 会话由后端签发 —— 两层分开后，换后端不动第三方代码，换第三方也不动后端代码。
    /// </summary>
    public interface ILoginBackend
    {
        /// <summary>后端名（日志/设置窗口显示用）。</summary>
        string Name { get; }

        /// <summary>后端在当前环境是否可用（例如 Firebase 配置文件缺失时返回 false）。</summary>
        bool IsAvailable { get; }

        /// <summary>不可用原因。</summary>
        string UnavailableReason { get; }

        /// <summary>当前用户（未登录返回 null）。</summary>
        LoginUser CurrentUser { get; }

        /// <summary>后端侧登录状态变化（登录/登出/换账号；Firebase 的 StateChanged 映射到这里）。</summary>
        event Action<LoginUser> StateChanged;

        /// <summary>用第三方凭据登录（凭据已存在该后端时会直接登录该账号）。</summary>
        void SignInWithCredential(LoginCredential credential, Action<LoginUser> onSuccess, Action<LoginErrorCode, string> onError);

        /// <summary>匿名登录（游客）。</summary>
        void SignInAnonymously(Action<LoginUser> onSuccess, Action<LoginErrorCode, string> onError);

        /// <summary>把第三方凭据**绑定到当前（游客）账号**：游客转正的关键一步。</summary>
        void LinkWithCredential(LoginCredential credential, Action<LoginUser> onSuccess, Action<LoginErrorCode, string> onError);

        /// <summary>邮箱 + 密码登录（后端不支持时返回 <see cref="LoginErrorCode.ProviderUnavailable"/>）。</summary>
        void SignInWithEmail(string email, string password, Action<LoginUser> onSuccess, Action<LoginErrorCode, string> onError);

        /// <summary>注销（只清本地会话；不删账号）。</summary>
        void SignOut();

        /// <summary>取当前用户的 ID Token（给自建服务验证身份用；<paramref name="forceRefresh"/> = 强制刷新）。</summary>
        void GetIdToken(bool forceRefresh, Action<string> onSuccess, Action<LoginErrorCode, string> onError);
    }
}
