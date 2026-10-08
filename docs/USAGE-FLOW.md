# 使用流程（接入到游戏里的完整姿势）

> 面向"把登录接进真实项目"的人。看完这份 + [README](../README.md) 就能落地；
> 平台配置分别见 [SETUP-FIREBASE](SETUP-FIREBASE.md) / [SETUP-GOOGLE](SETUP-GOOGLE.md)。

---

## 1. 一张图看懂流程

```
                         ┌──────────────── 平台侧（Google）────────────────┐
用户点「Google 登录」 →  │ Android Credential Manager / iOS GoogleSignIn   │
                         │            ↓  idToken（Android 无 accessToken） │
                         └────────────────────────────────────────────────┘
                                            ↓
                    ILoginProvider.SignIn → LoginCredential
                                            ↓
                         ┌──────────── 后端（Firebase Auth）──────────────┐
                         │ GoogleAuthProvider.GetCredential(idToken, at)  │
                         │ FirebaseAuth.SignInWithCredentialAsync         │
                         │        → FirebaseUser(uid, email, ...)         │
                         └────────────────────────────────────────────────┘
                                            ↓
                    LoginResult + OnSignedIn（业务：拉存档、进主城）

游客转正：SignInAnonymously →（有 uid）→ LinkWithCredential(Google) → uid 不变、IsAnonymous=false
```

**为什么要分三层**：Google（平台 SDK）与 Firebase（后端）的变更频率完全不同 ——
官方自己就在 2024→2026 把 Android 的 Google 登录从 `play-services-auth` 换成了 Credential Manager。
分成两层后，这种变更只影响 `ILoginProvider`，登录业务代码一行不动。

---

## 2. 启动集成（推荐顺序）

```csharp
using CoffeeBean;
using UnityEngine;

public sealed class GameLaunch : MonoBehaviour
{
    [SerializeField] private LoginSettings _settings;

    private IEnumerator Start()
    {
        // 1) 配置表（如果工程用了 com.coffeebean.excel）
        yield return ConfigTableRuntime.PreloadAll();

        // 2) 装配 + 初始化登录（自动游客登录，若 settings.SignInAsGuestOnStart）
        LoginService login = LoginFactory.CreateDefault(_settings);
        login.Initialize(_settings);

        // 3) 等第一次登录结束（游客也算）—— 之后的存档/服务端请求才带得上身份
        bool done = false;
        if (!login.IsSignedIn)
        {
            login.SignInAsGuest(_ => done = true);   // Initialize 已自动发起时，这里会是"进行中"被拒，
            while (!done && !login.IsBusy) yield return null;   // 所以用 IsSignedIn/IsBusy 兜一下
        }
        while (login.IsBusy) yield return null;

        // 4) 可选：把 ID token 交给自建服务换自己的会话
        if (login.IsSignedIn)
        {
            string token = null;
            login.GetIdToken(t => token = t, (code, msg) => Debug.LogWarning(msg), forceRefresh: true);
            while (token == null) yield return null;
            // MyServer.Login(token);
        }

        Debug.Log(login.DescribeProviders());   // 平台/装配问题一眼可见
    }
}
```

**要点**：
- `Initialize` 之后**不要**假设已经登录 —— 游客登录是异步的，用 `OnSignedIn` / `IsSignedIn` 判断。
- `DescribeProviders()` 建议在启动日志里打一条：出问题时能立刻区分"SDK 没装 / 符号没开 / 凭据没填"。

---

## 3. 三个典型场景

### 3.1 首屏（游客自动登录 + 静默升级）

```csharp
// 用户什么都没做就已经有账号（游客），能云存档、能进游戏
login.SignInAsGuest(r => Debug.Log("游客 uid = " + r.User.UserId));

// 到"设置/领奖/交易"这类需要正式账号的地方再引导绑定
login.LinkWithGoogle(r =>
{
    if (r.Success) Toast("已绑定 Google，换设备也能找回进度");
    else if (r.Code == LoginErrorCode.AccountExists) ShowAccountConflict(r);   // 见 3.3
    else if (r.Code != LoginErrorCode.Cancelled) Toast("绑定失败：" + r.Error);
});
```

### 3.2 老玩家直接用 Google 登录（跳过游客）

```csharp
login.SignInWithGoogle(r =>
{
    if (r.Success) { LoadCloudSave(r.User.UserId); }
    else if (r.Code == LoginErrorCode.Cancelled) { /* 保持当前游客账号，别弹错 */ }
    else ShowRetry(r);   // NetworkError / BackendError 可重试
});
```

> 注意：如果当前已有游客会话，**直接 `SignInWithGoogle` 会把会话换成那个 Google 账号**（游客进度留在旧 uid 上）。
> 想合并进度就用 `LinkWithGoogle`；想主动放弃游客进度才用 `SignInWithGoogle`。

### 3.3 账号冲突（`AccountExists`：Google 号已经属于另一个账号）

Firebase 会返回 `credential-already-in-use` / `email-already-in-use`，本模块统一映射成
`LoginErrorCode.AccountExists`。两条产品路线：

| 路线 | 做法 | 代价 |
|---|---|---|
| **A. 引导登录已有账号**（推荐，最省事） | 提示"这个 Google 账号已有进度，是否切换过去？"→ 切走后用 `SignInWithGoogle` | 当前游客进度会留在旧 uid（可服务端按设备号合并） |
| **B. 服务端合并** | 自建服务用两个 uid 的 ID token 校验身份后，做存档合并再 `Link` | 要写服务端逻辑（本模块只负责把身份拿准） |

---

## 4. ID token 与自建服务（重要）

`GetIdToken` 拿到的是 Firebase 签发的 JWT。**客户端不可信**，服务端必须验签：

```
客户端：login.GetIdToken(t => HttpPost("/login", t), forceRefresh: true)
服务端：Firebase Admin SDK → VerifyIdTokenAsync(token) → 得到 uid → 签自己的会话
```

- 给服务端时**一定 `forceRefresh: true`**（缓存的 token 可能只剩几十秒有效期）。
- token 里能拿到 `uid` / `aud`（应是你的 **Web** client id）/ `iss`（`https://securetoken.google.com/<projectId>`）。
- 自检窗口的验证清单里有一条"粘到 jwt.io 看 aud"。

---

## 5. 错误码处理表

| 错误码 | 含义 | 建议处理 |
|---|---|---|
| `None` | 成功 | — |
| `Cancelled` | 用户取消授权 | **静默**，不要弹错误框 |
| `NetworkError` | 网络问题 | 提示 + 可重试（`LoginResult.CanRetry == true`） |
| `BackendError` | 后端业务错误（限流、账号禁用、需要重新登录等） | 提示 + 可重试；`RequiresRecentLogin` 类要走重新认证 |
| `InvalidCredential` | 凭据非法（idToken 过期/audience 不对/错用 Android client id） | 重试一次；仍失败 → 检查 `GoogleWebClientId` 是否 **Web** 类型 |
| `ProviderUnavailable` | 当前平台没有可用登录方式 | 打 `DescribeProviders()`；不要反复重试 |
| `AccountExists` | 该账号已属于另一个账号 | 走 3.3 的冲突流程 |
| `AlreadyLinked` | 当前账号已绑定过该方式 | 提示"已绑定"，无需处理 |
| `NotSignedIn` | 未登录却调了需要登录的接口 | 调用顺序问题（先游客登录/初始化） |
| `NotInitialized` | 没调 `Initialize` | 启动流程里补 |

---

## 6. UI 侧的三个约定

1. **登录中要禁用按钮**：`login.IsBusy`（或订阅 `OnStateChanged == SigningIn`）；本模块会拒绝并发登录，
   但 UI 不该让用户点出第二次。
2. **取消不报错**：`Cancelled` 时 UI 应该"什么都没发生"。
3. **账号切换要重载数据**：`OnSignedIn` 触发时 uid 可能变了（转正/切号），业务应重新拉取该账号的存档，
   不能只刷新头部头像。

---

## 7. 开发期怎么联调（不装任何 SDK）

| 阶段 | 用什么 | 能验证什么 |
|---|---|---|
| UI 先行 | 编辑器里的**模拟 Google**（默认开）+ **开发后端**（内存） | 整套 UI 流程、状态机、事件、错误分支 |
| 接后端前 | 自建 `ILoginBackend` 假实现（或直接用开发后端） | 你的业务代码与后端解耦程度 |
| 真机联调 | Firebase + 真 Google 弹窗 | 真 token、真 uid、控制台用户 |
| 服务端联调 | `GetIdToken` + Admin SDK 验签 | 自建会话链路 |

关闭模拟：`LoginSettings.SimulateGoogleInEditor = false`（真机桥就绪后）。

---

## 8. 调试开关

```csharp
LoginLog.Enabled = true;    // 默认开
LoginLog.Verbose = true;    // 额外打印：排队、凭据摘要、provider 可用性细节
```

设备日志里 grep `[CoffeeBean.Login]` 即可看到完整链路。
