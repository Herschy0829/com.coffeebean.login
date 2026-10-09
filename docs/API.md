# API 参考（com.coffeebean.login v0.1.0）

> 这是本模块**全部公开接口**的说明。用法与流程见 [../README.md](../README.md) 与 [USAGE-FLOW.md](USAGE-FLOW.md)。
> 命名空间统一是 **`CoffeeBean`**。

## 0. 类型地图

```
业务
 └── LoginService                        ← 你只跟它打交道（状态机 + 事件 + 转正）
       ├── ILoginProvider   × N          ← 拿第三方凭据（Google / 外部桥 / 自定义）
       ├── ILoginBackend     × 1         ← 拿凭据换会话（Firebase / 开发 / 自建）
       └── LoginSettings                 ← ScriptableObject 配置（client id、启动行为）
辅助：
   LoginFactory                          装配（按"装了什么"自动选实现）
   LoginUser / LoginCredential / LoginResult / 三个枚举   数据模型
   LoginLog                              日志开关
   LoginMainThread                       主线程回调（内部用；自定义 provider 切线程可用）
```

---

## 1. `LoginService`（门面，`sealed class`）

### 1.1 创建

| 成员 | 说明 |
|---|---|
| `static LoginService Instance { get; }` | 默认实例，内部走 `LoginFactory.CreateDefault()`（自动装配）。**懒创建**，第一次访问才装配。 |
| `LoginService(IEnumerable<ILoginProvider> providers, ILoginBackend backend, LoginSettings settings = null)` | 自己控制装配（测试、自建后端、只想要某个 provider）。`backend` 传 null 会退化成 `UnavailableLoginBackend`。`providers` 里 **Kind 相同只保留最后一个**。 |

### 1.2 只读状态

| 成员 | 类型 | 说明 |
|---|---|---|
| `State` | `LoginState` | 状态机当前值 |
| `CurrentUser` | `LoginUser` | 未登录为 `null`；返回的是**内部实例的副本**，可安全持有/缓存 |
| `IsSignedIn` | `bool` | `CurrentUser != null && !IsEmpty` |
| `IsInitialized` | `bool` | 是否调过 `Initialize` |
| `IsBusy` | `bool` | 是否正在登录/绑定（UI 用它禁用按钮） |
| `Backend` | `ILoginBackend` | 后端实例（高级用法，可读后端自身状态） |
| `Settings` | `LoginSettings` | 当前配置（可能 null = 默认行为） |
| `Providers` | `IReadOnlyList<ILoginProvider>` | 所有已注册 provider（含不可用的） |
| `GetProvider<T>()` | `T` | 按类型取 provider，典型：`GetProvider<ExternalCredentialProvider>()` |
| `AvailableProviders` | `List<LoginProviderKind>` | 当前环境**真正可用**的方式；`Guest` 只要后端可用就在里面 |

### 1.3 事件（全部在主线程触发）

| 事件 | 何时触发 |
|---|---|
| `OnStateChanged(LoginState)` | 状态值**变化**时（同值不重复触发） |
| `OnSignedIn(LoginUser)` | 登录成功**或转正/换账号成功**（此时 uid 可能变了，业务应重拉该账号数据） |
| `OnUserChanged(LoginUser)` | 只要用户对象被写入就触发（登录、转正、后端刷新资料） |
| `OnSignedOut()` | 主动 `SignOut()` **或**后端侧会话失效（被踢/换账号） |
| `OnFailed(LoginErrorCode, string)` | 登录/绑定失败（**用户取消也会走这里**，码是 `Cancelled`） |

### 1.4 生命周期

| 方法 | 说明 |
|---|---|
| `Initialize(LoginSettings settings = null)` | 接后端事件、同步后端已有会话、打印诊断、跑 `settings.Validate`；若 `SignInAsGuestOnStart` 且未登录则**自动** `SignInAsGuest()`。可重复调用（换 settings），**正在登录时会被忽略并告警**。 |

### 1.5 登录 / 绑定 / 登出 / token

| 方法 | 说明 |
|---|---|
| `SignIn(LoginProviderKind kind, Action<LoginResult> onDone = null)` | 统一入口。`Guest` → 后端匿名登录；`Email` → **直接报错**并提示用 `SignInWithEmail`；其余 → provider 拿凭据 → 后端换会话 |
| `SignInAsGuest(Action<LoginResult> onDone = null)` | 游客/匿名登录（任何可用后端都必须支持；建议启动时先调，拿到真实 uid） |
| `SignInWithGoogle(Action<LoginResult> onDone = null)` | Google 登录 |
| `SignInWithEmail(string email, string password, Action<LoginResult> onDone = null)` | 邮箱 + 密码登录 |
| `Link(LoginProviderKind kind, Action<LoginResult> onDone = null)` | **游客转正**：把第三方账号绑到当前会话（保留 uid）。未登录 → `NotSignedIn`；绑 `Guest` → `ProviderUnavailable` |
| `LinkWithGoogle(Action<LoginResult> onDone = null)` | 同上，Google 快捷方式 |
| `SignOut()` | 调后端 `SignOut()` → 用户置空 → 状态 `SignedOut` → 触发 `OnSignedOut`（只清会话，**不删账号**） |
| `GetIdToken(Action<string> onSuccess, Action<LoginErrorCode, string> onError = null, bool forceRefresh = false)` | 取 ID token 交给**自建服务**验签。未初始化 → `NotInitialized`；未登录 → `NotSignedIn`。给服务端时用 `forceRefresh: true` |
| `DescribeProviders()` | 返回一段可读文本：后端是谁/是否可用 + 每个方式的可用性与原因（启动日志、设置窗口都用它） |

### 1.6 行为约定（重要）

1. **回调一定在主线程**，且 `onDone` 只会被调用一次。
2. **串行**：`SigningIn` 期间再发起登录会被拒绝（`LoginErrorCode.Unknown` + "已有登录流程在进行中"）—— 避免弹两次授权窗口。UI 仍应用 `IsBusy` 禁用按钮。
3. **取消不是错误**：`Cancelled` 时业务不要弹报错框。
4. **未初始化**时所有登录/绑定都返回 `NotInitialized`（`GetIdToken` 同）。
5. **失败后 `CurrentUser` 仍为 null**，状态是 `Failed`（可重试）。
6. `SignIn`/`Link` 在 provider 缺失或不可用时返回 `ProviderUnavailable`，描述里会带上**可用的方式列表**或**不可用原因**。

---

## 2. `ILoginProvider`（第三方登录适配层）

| 成员 | 说明 |
|---|---|
| `LoginProviderKind Kind { get; }` | 面向业务的登录方式（决定 `SignIn(kind)` 路由到谁） |
| `string ProviderId { get; }` | 后端提供者 id（Firebase 的 `google.com` / `apple.com`…） |
| `bool IsAvailable { get; }` | 当前平台/环境是否可用（未装 SDK 就 false，**不要抛异常**） |
| `string UnavailableReason { get; }` | 不可用原因（日志/设置窗口显示） |
| `void SignIn(Action<LoginCredential> onSuccess, Action<LoginErrorCode,string> onError)` | 发起登录拿凭据 |

**实现契约**：回调必须**切回主线程**、**只会被调一次**、不会同时成功又失败；用户取消 → `onError(Cancelled, …)`；拿不到 idToken → `onError(InvalidCredential, …)`。

**内置实现**：

| 实现 | 何时可用 | 说明 |
|---|---|---|
| `GoogleSignInReflectionProvider` | 工程里存在 `GoogleSignIn.GoogleSignIn`（那个已 archived 的插件） | **反射**接插件，不硬引用；`Kind = Google`，`ProviderId = google.com`；自动把插件给的 `IdToken/AccessToken/AuthCode/Email/DisplayName/ImageUrl` 填进凭据；无 idToken 时报 `InvalidCredential` |
| `ExternalCredentialProvider` | 总是可用 | **外部凭据入口**：平台桥（Android Credential Manager / iOS GoogleSignIn-iOS / 自建）把 token 喂进来。见 §6.1 |
| `SimulatedGoogleProvider` | **仅编辑器**（且 `SimulateGoogleInEditor = true`、非批处理） | 假的 provider：生成假 idToken，用来在没装任何 SDK 时跑通 UI 流程 |

---

## 3. `ILoginBackend`（后端适配层）

| 成员 | 说明 |
|---|---|
| `string Name { get; }` | 后端名（日志/诊断显示） |
| `bool IsAvailable { get; }` | 当前环境是否可用（如 Firebase 配置缺失 → false） |
| `string UnavailableReason { get; }` | 不可用原因 |
| `LoginUser CurrentUser { get; }` | 后端侧当前用户（未登录 null） |
| `event Action<LoginUser> StateChanged` | 后端登录状态变化（Firebase 的 `StateChanged` 映射到这里；`null` = 已登出） |
| `SignInWithCredential(LoginCredential, onSuccess, onError)` | 用第三方凭据登录（该凭据已属于某账号时直接登录那个账号） |
| `SignInAnonymously(onSuccess, onError)` | 匿名（游客）登录 |
| `LinkWithCredential(LoginCredential, onSuccess, onError)` | 把凭据**绑到当前会话**（转正的关键） |
| `SignInWithEmail(email, password, onSuccess, onError)` | 邮箱登录（不支持时返回 `ProviderUnavailable`） |
| `SignOut()` | 注销（只清本地会话） |
| `GetIdToken(bool forceRefresh, onSuccess, onError)` | 取 ID token |

**内置实现**：

| 实现 | 编译条件 | 说明 |
|---|---|---|
| `FirebaseLoginBackend` | 独立程序集 `CoffeeBean.Login.Firebase`，需 `COFFEEBEAN_FIREBASE_AUTH` | 官方流程：`CheckAndFixDependenciesAsync()` → `FirebaseAuth.DefaultInstance`；**就绪前的请求排队**；Google 走 `GoogleAuthProvider.GetCredential(idToken, accessToken)`；`AuthError` → 模块错误码按枚举名映射。额外属性 `IsReady`（依赖检查是否完成） |
| `DevelopmentLoginBackend` | `#if UNITY_EDITOR \|\| DEVELOPMENT_BUILD` | 内存实现，不发网络。游客 uid 持久化在 PlayerPrefs（同设备同 uid）；Google 用 idToken 哈希当 uid；转正保留 uid |
| `UnavailableLoginBackend` | 总是存在 | "没有后端"占位：所有操作以 `ProviderUnavailable` 失败并打印可读原因，**不抛异常** |

---

## 4. `LoginSettings`（`ScriptableObject`）

创建：`Assets > Create > CoffeeBean > Login Settings`；放 `Resources/CoffeeBean/LoginSettings.asset` 可被自动加载。
`LoadFromResources(path = "CoffeeBean/LoginSettings")` 返回实例或 null；`DefaultResourcePath` 是默认路径常量。

| 字段 | 默认 | 作用 |
|---|---|---|
| `GoogleWebClientId` | 空 | OAuth **Web** 客户端 ID（Android 的 `serverClientId` / `requestIdToken` 就是它，决定 idToken 的 audience） |
| `GoogleIosClientId` | 空 | OAuth iOS 客户端 ID（iOS 用） |
| `EnableFirebaseBackend` | `true` | 装了 Firebase 时是否启用 Firebase 后端（关掉 = 只用自定义后端） |
| `SignInAsGuestOnStart` | `true` | `Initialize` 时自动游客登录（**已接线**） |
| `RefreshTokenOnStart` | `false` | 启动时强制刷新一次 ID token —— ⚠️ **当前版本尚未接线**（见 §8） |
| `IdTokenRefreshMarginSeconds` | `300` | token 提前刷新边界 —— ⚠️ **当前版本尚未接线**（见 §8） |
| `SimulateGoogleInEditor` | `true` | 编辑器里加模拟 Google provider（仅编辑器生效） |
| `SimulatedEmail` | `editor@local` | 模拟登录用的假邮箱 |

`Validate(bool firebaseBackendActive = true)` → `List<string>`：返回配置问题（未填 Web client id、margin 为负、开了自动游客但后端不可用）。设置窗口与测试共用它，避免两处判断漂移。

---

## 5. `LoginFactory`（装配）

| 成员 | 说明 |
|---|---|
| `CreateDefault(LoginSettings settings = null)` | 装配一个可用 `LoginService` |
| `CreateDefault(LoginSettings settings, out ExternalCredentialProvider externalProvider)` | 同上，并把**外部凭据入口**交出来（平台桥要用） |
| `CreateBackend(LoginSettings)` | 只装配后端 |
| `CreateProviders(LoginSettings, out ExternalCredentialProvider)` | 只装配 provider 列表 |
| `TryCreate<T>(string assemblyQualifiedTypeName, out string reason)` | 反射创建（SDK 适配层用），失败给可读原因 |
| `FirebaseBackendTypeName` | Firebase 适配层的程序集限定名常量 |

**降级顺序（每一步都会打日志说明原因）**：

```
后端   Firebase（装了 SDK / 符号开着）
        → DevelopmentLoginBackend（仅编辑器 / DEVELOPMENT_BUILD）
        → UnavailableLoginBackend（真机无后端时，明确报错而不是静默失败）
方式   GoogleSignIn 插件（反射；装了就用）
        + ExternalCredentialProvider（总是注册）
        + SimulatedGoogleProvider（仅编辑器、可关）
        （Guest 不需要 provider：直接走后端匿名登录）
```

---

## 6. 数据模型

### 6.1 `ExternalCredentialProvider`（外部凭据入口）

| 成员 | 说明 |
|---|---|
| `ExternalCredentialProvider(LoginProviderKind kind = Google, string providerId = "google.com")` | 可复用给别的平台（如 Sign in with Apple） |
| `bool HasPendingRequest { get; }` | 是否有一次等待中的登录（避免重复弹窗） |
| `void Submit(string idToken, string accessToken = null, string serverAuthCode = null, string email = null, string displayName = null, string photoUrl = null, Dictionary<string,string> extra = null)` | **平台桥拿到 token 后调它**（Android 只传 idToken；iOS 传 idToken + accessToken）。没有等待中的请求时只告警、不抛异常 |
| `void SubmitError(LoginErrorCode code, string message)` | 平台桥失败/用户取消时调它 |

规则：**同一次只能有一个等待中的请求**；已有请求时再次 `SignIn` 会立即失败（避免把旧回调覆盖掉导致 UI 卡死）。

### 6.2 枚举

**`LoginProviderKind`**：`None=0` / `Guest=1` / `Google=2` / `Apple=3`（**预留，未内置适配**）/ `Email=4` / `Custom=99`（自己实现 provider 后注册）。

**`LoginState`**：`NotInitialized` → `SignedOut` / `SigningIn` / `SignedIn` / `Failed`。

**`LoginErrorCode`**（业务按码分支，别解析字符串）：

| 码 | 含义 | 建议处理 |
|---|---|---|
| `None` | 成功 | — |
| `NotInitialized` | 没调 `Initialize` | 启动流程里补 |
| `ProviderUnavailable` | 该方式在当前环境不可用 | 打 `DescribeProviders()`，**别重试** |
| `Cancelled` | 用户取消 | **静默** |
| `NetworkError` | 网络错误 | 提示 + 可重试（`CanRetry = true`） |
| `InvalidCredential` | 凭据非法（idToken 过期/audience 不对/误用 Android client id） | 可再试一次；仍失败查 `GoogleWebClientId` 是否 **Web** 类型 |
| `AccountExists` | 该账号属于另一个账号（转正时） | 走"登录已有账号"或服务端合并 |
| `AlreadyLinked` | 当前账号已绑过该方式 | 提示"已绑定" |
| `NotSignedIn` | 未登录却调了需要登录的接口 | 调用顺序问题（先游客登录） |
| `BackendError` | 后端业务错误（限流/账号禁用/需重新登录） | 提示 + 可重试 |
| `Unknown` | 其它（含"已有登录流程在进行中"） | 看描述文本 |

### 6.3 `LoginUser`

字段：`UserId`（后端 uid，**游客也是真实 uid**）、`DisplayName`、`Email`、`PhotoUrl`、`IsAnonymous`、`ProviderId`（`google.com` / `anonymous` / `password`…）。
成员：`IsEmpty`（`UserId` 为空）、`Clone()`、`ToString()`（`provider:uid <email>`）。

### 6.4 `LoginCredential`

字段：`Kind`、`ProviderId`、`IdToken`、`AccessToken`、`ServerAuthCode`、`Email`、`DisplayName`、`PhotoUrl`、`Extra`、`Password`。
成员：`HasToken`（idToken 或 accessToken 至少有一个）、`ToString()`（**不打印 token 本体**，只说有没有）。

### 6.5 `LoginResult`

字段：`Success`、`Code`、`Error`（描述，**不要拿它做分支**）、`User`（成功时；已 Clone）。
成员：`LoginResult.Ok(user)` / `LoginResult.Fail(code, error)`（静态构造）、`CanRetry`（`NetworkError`/`BackendError` 为 true）。

---

## 7. 扩展：自己接一种登录方式 / 一个后端

**自定义 provider**（例如微信、Sign in with Apple）：

```csharp
public sealed class MyAppleProvider : ILoginProvider
{
    public LoginProviderKind Kind => LoginProviderKind.Apple;
    public string ProviderId => "apple.com";
    public bool IsAvailable => /* 本平台是否支持 */ true;
    public string UnavailableReason => string.Empty;

    public void SignIn(Action<LoginCredential> onSuccess, Action<LoginErrorCode, string> onError)
    {
        MySdk.SignIn(identityToken => LoginMainThread.Run(() => onSuccess(new LoginCredential
        {
            Kind = LoginProviderKind.Apple, ProviderId = "apple.com",
            IdToken = identityToken, Email = MySdk.LastEmail,
        })), err => LoginMainThread.Run(() => onError(LoginErrorCode.Cancelled, err)));
    }
}

var login = new LoginService(new ILoginProvider[] { new MyAppleProvider() }, backend, settings);
```

**自定义后端**（自建账号服务）：实现 `ILoginBackend` 的 6 个方法即可，`LoginService` 其余逻辑（状态机、事件、转正、串行保护）全部复用。

---

## 8. 已知边界与未接线项（诚实清单）

| 项 | 现状 |
|---|---|
| `LoginSettings.RefreshTokenOnStart` | **已声明、还没接线** —— 目前不会在启动时刷新 token。需要的话业务自己调 `GetIdToken(forceRefresh: true)` |
| `LoginSettings.IdTokenRefreshMarginSeconds` | **已声明、还没接线** —— 没有 token 缓存/过期时间跟踪，只是 `Validate` 会检查它非负 |
| `LoginProviderKind.Apple` | 枚举预留，**没有内置适配程序集**（要自己写 provider，见 §7） |
| 账号管理类接口 | **不做**：改密、改邮箱、删账号、邮箱验证、`FetchProvidersForEmailAsync`（identifier-first）都要业务直接用 Firebase SDK，本模块只覆盖登录链路 |
| 服务端验签 | **不做**：`GetIdToken` 只负责把 token 给你，验签是 Firebase Admin SDK 的事 |
| `LoginCredential.Password` | 目前没人填（邮箱登录走 `SignInWithEmail`，不经过 provider 层）；保留给自定义 provider |
