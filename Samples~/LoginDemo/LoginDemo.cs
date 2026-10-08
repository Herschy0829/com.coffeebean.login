using UnityEngine;

namespace CoffeeBean.Login.Samples
{
    /// <summary>
    /// 登录模块示例：**在编辑器里就能把整套流程点一遍**（不需要装 Firebase / Google SDK）。
    ///
    /// 用法：新建空场景 → 挂本脚本 → 挂一个 LoginSettings（可留空）→ Play。
    /// 编辑器里会自动用「开发后端 + 模拟 Google」，所以：
    ///   · 游客登录 → 拿到一个 dev uid；
    ///   · 模拟 Google 登录 → 换成 google.com 账号；
    ///   · 绑定（游客转正）→ uid 不变、IsAnonymous 变 false；
    ///   · 取 ID token → 拿到假的 dev token（真后端在真机上才有真 token）。
    ///
    /// 真机上（装了 Firebase + Google 登录）这套按钮代码**一个字都不用改** ——
    /// 变的只是工厂装配出来的 provider / 后端。
    /// </summary>
    public sealed class LoginDemo : MonoBehaviour
    {
        [Tooltip("留空则用 Resources/CoffeeBean/LoginSettings（没有就用默认行为）")]
        public LoginSettings Settings;

        [Tooltip("启动时自动初始化（真项目里应该在启动流程里做，这里只是方便点着玩）")]
        public bool InitializeOnStart = true;

        private LoginService _service;
        private ExternalCredentialProvider _external;
        private string _status = "未初始化";

        private void Start()
        {
            _service = LoginFactory.CreateDefault(Settings, out _external);

            _service.OnStateChanged += state => _status = "状态：" + state;
            _service.OnSignedIn += user => _status = "登录成功：" + user;
            _service.OnSignedOut += () => _status = "已登出";
            _service.OnFailed += (code, error) => _status = "失败[" + code + "]：" + error;

            if (InitializeOnStart) _service.Initialize(Settings);
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 560, 460));

            GUILayout.Label("<b>CoffeeBean Login Demo</b>", GUILayout.Height(22));
            GUILayout.Label("状态：" + _status);
            GUILayout.Label("当前用户：" + (_service != null && _service.CurrentUser != null
                ? _service.CurrentUser.ToString()
                : "(未登录)"));
            GUILayout.Space(6);

            if (_service == null)
            {
                GUILayout.Label("LoginService 还没建好。");
                GUILayout.EndArea();
                return;
            }

            if (GUILayout.Button("① 初始化 (Initialize)", GUILayout.Height(28)))
            {
                _service.Initialize(Settings);
            }

            if (GUILayout.Button("② 游客登录 (SignInAsGuest)", GUILayout.Height(28)))
            {
                _service.SignInAsGuest(result => _status = "游客登录 → " + result);
            }

            if (GUILayout.Button("③ Google 登录 (SignInWithGoogle)", GUILayout.Height(28)))
            {
                _service.SignInWithGoogle(result => _status = "Google 登录 → " + result);
            }

            if (GUILayout.Button("④ 游客转正 (LinkWithGoogle)", GUILayout.Height(28)))
            {
                _service.LinkWithGoogle(result => _status = "绑定 → " + result);
            }

            if (GUILayout.Button("⑤ 取 ID token（给自建服务验证用）", GUILayout.Height(28)))
            {
                _service.GetIdToken(
                    token => _status = "ID token（前 40 字符）：" + (token != null && token.Length > 40 ? token.Substring(0, 40) + "…" : token),
                    (code, error) => _status = "取 token 失败[" + code + "]：" + error,
                    forceRefresh: true);
            }

            if (GUILayout.Button("⑥ 登出 (SignOut)", GUILayout.Height(28)))
            {
                _service.SignOut();
            }

            GUILayout.Space(8);
            GUILayout.Label("<b>平台桥怎么喂凭据</b>（Android Credential Manager / iOS 桥）", GUILayout.Height(20));
            GUILayout.Label("先点 ③ 发起登录，再由平台桥回调：", EditorHint());
            if (GUILayout.Button("模拟平台桥回传 idToken（外部凭据入口）", GUILayout.Height(26)))
            {
                if (_external == null)
                {
                    _status = "没有外部凭据入口（provider 未注册）";
                }
                else if (!_external.HasPendingRequest)
                {
                    _status = "先点「③ Google 登录」把流程挂起，再回传凭据";
                }
                else
                {
                    _external.Submit("id-token-from-platform-bridge", null, null, "bridge@example.com", "Bridge User");
                }
            }

            GUILayout.Space(8);
            GUILayout.Label("<b>当前装配</b>", GUILayout.Height(20));
            GUILayout.Label(_service.DescribeProviders(), GUILayout.Height(110));

            GUILayout.EndArea();
        }

        private static string EditorHint()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return "Java 侧：GoogleIdTokenCredential.createFrom(...).getIdToken() → UnitySendMessage → C# 调 external.Submit(idToken)";
#elif UNITY_IOS && !UNITY_EDITOR
            return "iOS 侧：GIDSignIn.sharedInstance.signIn(withPresenting:) → idToken/accessToken → Submit(idToken, accessToken)";
#else
            return "（本平台没有原生 Google 弹窗：编辑器用模拟 provider，桌面用你自己拿到的 token 走 Submit）";
#endif
        }
    }
}
