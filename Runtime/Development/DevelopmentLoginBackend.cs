#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoffeeBean
{
    /// <summary>
    /// **开发/演示用**的后端：全部在内存里，不发任何网络请求。
    ///
    /// 用途：在还没有接 Firebase（或后端还没上线）时，让 UI/流程/存档链路先跑通。
    /// · 游客登录：给一个**持久化在 PlayerPrefs 里的假 uid**，语义与真实后端一致（同一台设备同一个 uid）；
    /// · Google 登录：用 idToken 的哈希当 uid，能体现"同一个 Google 账号 → 同一个 uid"；
    /// · 绑定（游客转正）：保留游客 uid，只是把 IsAnonymous 置 false 并补上邮箱/名字 —— 与真实账号合并语义一致。
    ///
    /// **打包限制**：整个类型包在 <c>#if UNITY_EDITOR || DEVELOPMENT_BUILD</c> 里，Release 包里不存在，
    /// 不会被误用。
    /// </summary>
    public sealed class DevelopmentLoginBackend : ILoginBackend
    {
        private const string DevUidPrefKey = "CoffeeBean.Login.DevelopmentUserId";

        public string Name => "Development（内存开发后端）";

        public bool IsAvailable => true;

        public string UnavailableReason => string.Empty;

        public LoginUser CurrentUser { get; private set; }

        public event Action<LoginUser> StateChanged;

        public void SignInAnonymously(Action<LoginUser> onSuccess, Action<LoginErrorCode, string> onError)
        {
            string uid = PlayerPrefs.GetString(DevUidPrefKey, string.Empty);
            if (string.IsNullOrEmpty(uid))
            {
                uid = "dev-anon-" + Guid.NewGuid().ToString("N").Substring(0, 12);
                PlayerPrefs.SetString(DevUidPrefKey, uid);
                PlayerPrefs.Save();
            }

            LoginLog.Log("开发后端：匿名登录 → " + uid);
            SetUser(new LoginUser { UserId = uid, IsAnonymous = true, ProviderId = "anonymous" });
            if (onSuccess != null) onSuccess(CurrentUser);
        }

        public void SignInWithCredential(LoginCredential credential, Action<LoginUser> onSuccess, Action<LoginErrorCode, string> onError)
        {
            if (credential == null || !credential.HasToken)
            {
                Fail(onError, LoginErrorCode.InvalidCredential, "开发后端：凭据缺少 token。");
                return;
            }

            var user = new LoginUser
            {
                UserId = "dev-" + credential.ProviderId + "-" + ShortHash(credential.IdToken),
                DisplayName = credential.DisplayName,
                Email = credential.Email,
                PhotoUrl = credential.PhotoUrl,
                IsAnonymous = false,
                ProviderId = credential.ProviderId,
            };
            LoginLog.Log("开发后端：凭据登录 → " + user);
            SetUser(user);
            if (onSuccess != null) onSuccess(CurrentUser);
        }

        public void LinkWithCredential(LoginCredential credential, Action<LoginUser> onSuccess, Action<LoginErrorCode, string> onError)
        {
            if (CurrentUser == null)
            {
                Fail(onError, LoginErrorCode.NotSignedIn, "开发后端：绑定前必须先有会话（先游客登录）。");
                return;
            }
            if (credential == null || !credential.HasToken)
            {
                Fail(onError, LoginErrorCode.InvalidCredential, "开发后端：凭据缺少 token。");
                return;
            }

            LoginUser merged = CurrentUser.Clone();
            merged.IsAnonymous = false;
            merged.ProviderId = credential.ProviderId;
            merged.Email = credential.Email;
            merged.DisplayName = credential.DisplayName;
            merged.PhotoUrl = credential.PhotoUrl;

            LoginLog.Log("开发后端：游客转正（保留 uid " + merged.UserId + "，绑定 " + credential.ProviderId + "）");
            SetUser(merged);
            if (onSuccess != null) onSuccess(CurrentUser);
        }

        public void SignInWithEmail(string email, string password, Action<LoginUser> onSuccess, Action<LoginErrorCode, string> onError)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                Fail(onError, LoginErrorCode.InvalidCredential, "开发后端：邮箱与密码都不能为空。");
                return;
            }

            SetUser(new LoginUser
            {
                UserId = "dev-password-" + ShortHash(email),
                Email = email,
                IsAnonymous = false,
                ProviderId = "password",
            });
            if (onSuccess != null) onSuccess(CurrentUser);
        }

        public void SignOut()
        {
            LoginLog.Log("开发后端：登出。");
            SetUser(null);
        }

        public void GetIdToken(bool forceRefresh, Action<string> onSuccess, Action<LoginErrorCode, string> onError)
        {
            if (CurrentUser == null)
            {
                Fail(onError, LoginErrorCode.NotSignedIn, "开发后端：未登录，取不到 ID token。");
                return;
            }

            string token = "dev-id-token:" + CurrentUser.UserId + ":"
                           + DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                           + (forceRefresh ? ":refreshed" : string.Empty);
            if (onSuccess != null) onSuccess(token);
        }

        private void SetUser(LoginUser user)
        {
            CurrentUser = user;
            var handler = StateChanged;
            if (handler != null) handler(user);
        }

        private static void Fail(Action<LoginErrorCode, string> onError, LoginErrorCode code, string message)
        {
            LoginLog.Warn(message);
            if (onError != null) onError(code, message);
        }

        private static string ShortHash(string text)
        {
            if (string.IsNullOrEmpty(text)) return "0";
            return Math.Abs(text.GetHashCode()).ToString("x8");
        }
    }
}
#endif
