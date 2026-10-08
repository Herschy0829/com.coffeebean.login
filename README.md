# CoffeeBean Login（com.coffeebean.login）

账号登录模块：**Google 账号登录 + Firebase 登录**，可插拔、可降级、带工程配置自检。

- **三层解耦**：业务只认 `LoginService`；`ILoginProvider` 负责"拿到第三方凭据"，`ILoginBackend` 负责"把凭据换成会话"。换后端不动登录方式代码，反之亦然。
- **不装 SDK 也能跑**：没装 Firebase 时自动降级到开发后端（仅编辑器/Development 构建），没装 Google SDK 时用编辑器模拟登录 —— **UI 和流程可以先做起来**，真机再换真实现。
- **装了自动生效**：Firebase 适配层在独立程序集里（`versionDefines` + `defineConstraints`），装了就编译、没装就整体跳过；Google 插件用**反射**接（不硬引用任何插件）。
- **游客转正**：匿名登录 → 绑定 Google（保留 uid，进度不丢）；账号冲突（`AccountExists`）有明确错误码与处理指引。
- **配置自检**：`Window > CoffeeBean > 登录配置自检` 逐条检查 `google-services.json` / plist / 包名匹配 / SHA-1 / 编译符号，并直接给出修法（这几个坑在编辑器里**不报错**、只在真机失败）。
- **独立可用**：不装 CoffeeBean Core 也能单独安装使用；装了 Core 会通过 Bridge 程序集自动注册 `LoginService`。

## 安装

```json
{
  "dependencies": {
    "com.coffeebean.login": "https://github.com/Herschy0829/com.coffeebean.login.git#v0.1.0"
  }
}
```

依赖：`com.coffeebean.tools`（日志 + 主线程调度）。Firebase / Google 登录 SDK **不是**本模块的依赖（见下）。

## 快速使用（3 步）

```csharp
using CoffeeBean;

// 1) 启动：装配 + 初始化（会自动游客登录，如果 LoginSettings.SignInAsGuestOnStart = true）
LoginService login = LoginFactory.CreateDefault(LoginSettings.LoadFromResources());
login.Initialize();

// 2) Google 登录（Android/iOS 真机；编辑器里是模拟登录）
login.SignInWithGoogle(result =>
{
    if (result.Success) Debug.Log("登录成功: " + result.User);
    else if (result.Code == LoginErrorCode.Cancelled) { /* 用户自己取消，别弹报错 */ }
    else Debug.LogWarning("登录失败[" + result.Code + "]: " + result.Error);
});

// 3) 游客转正（把 Google 账号绑到当前游客账号上，uid 不变）
login.LinkWithGoogle(result => Debug.Log("绑定结果: " + result));
```

拿 ID token 交给**自建服务**校验（后端验证身份的标准做法）：

```csharp
login.GetIdToken(token => { /* POST 给服务器，服务器用 Firebase Admin SDK 验签 */ },
                 (code, msg) => Debug.LogWarning(msg),
                 forceRefresh: true);
```

## 使用流程（完整）

```
启动阶段
  ├─ LoginFactory.CreateDefault(settings)        装配：后端（Firebase/开发）+ 登录方式（Google/模拟/外部）
  ├─ login.Initialize(settings)                  接后端事件；按配置自动游客登录
  └─ 可选：login.GetIdToken(..., forceRefresh: true)   带 token 调自己的登录/存档接口

用户点"用 Google 登录"
  ├─ login.SignInWithGoogle(cb)                  ┐
  │    └─ ILoginProvider.SignIn()                │ Android：Credential Manager / iOS：GoogleSignIn
  │         └─ 拿到 idToken（Android 没有 accessToken，传 null）
  ├─ ILoginBackend.SignInWithCredential(cred)    │ Firebase：GoogleAuthProvider.GetCredential(idToken, accessToken)
  │    └─ FirebaseAuth.SignInWithCredentialAsync │ → 成功得 FirebaseUser（uid/email/displayName）
  └─ cb(LoginResult)                             回调在主线程；同时触发 OnSignedIn / OnStateChanged

游客转正（推荐流程：先游客、后绑定）
  ├─ 启动就 SignInAsGuest()                     拿到真实 uid（游客也能有云存档）
  ├─ 用户点绑定 → login.LinkWithGoogle(cb)
  │    └─ FirebaseAuth.CurrentUser.LinkWithCredentialAsync
  │         ├─ 成功：uid 不变、IsAnonymous=false   ← 进度/存档不丢，这是转正的意义
  │         └─ AccountExists：该 Google 号已属于另一个账号 → 提示"登录该账号"或做账号合并
  └─ 之后 GetIdToken 拿到的 token 就带着正式账号身份

登出
  └─ login.SignOut()                             只清本地会话；不删账号
```

**错误码怎么处理**（`LoginErrorCode`）：`Cancelled` 静默；`NetworkError` / `BackendError` 可重试；`ProviderUnavailable` 说明当前平台没有可用的登录方式（把 `login.DescribeProviders()` 打出来看原因）；`AccountExists` 走"登录已有账号"分支；`NotSignedIn` 是调用顺序问题。

## Google 登录（2026-09 官方现状，重要）

官方文档核实：**Android 侧推荐的 Credential Manager，旧的 Google Sign-In for Android 已被标记 deprecated 并从 Play services Auth SDK 移除中**；而 Google **没有**为 Credential Manager 提供任何 Unity 包；曾经通用的 Unity 插件 `googlesamples/google-signin-unity` **仓库已 archived**。所以本模块的做法是：

| 路线 | 怎么做 | 适用 |
|---|---|---|
| **A. 自己桥 Credential Manager（推荐，可持续）** | 写一个最小 Android 插件：`GetGoogleIdOption.setServerClientId(<Web client id>)` → `GoogleIdTokenCredential.getIdToken()` → `UnitySendMessage` 回 C# → **`externalProvider.Submit(idToken)`** | Android 新工程 |
| **B. 装（已 archived 的）Google Sign-In Unity 插件** | 什么都不用改：本模块用反射自动接上 `GoogleSignIn.DefaultInstance.SignIn()` | 存量工程、想最快跑通 |
| **C. 编辑器/桌面** | 编辑器里自动用**模拟 Google**（`LoginSettings.SimulateGoogleInEditor`）；桌面上官方没有 Google 弹窗，用你自己拿到的 token 走 `Submit` | 开发期 |

路线 A 的接法（本模块已经把落点准备好了，**不需要改框架**）：

```csharp
// 装配时把外部凭据入口留出来
LoginService login = LoginFactory.CreateDefault(settings, out ExternalCredentialProvider external);

// Unity 侧发起（会挂起等待平台桥回传）
login.SignInWithGoogle(result => { /* 平台桥 Submit 之后才会回调 */ });

// Android Java 插件通过 UnitySendMessage 把 idToken 送回来后：
external.Submit(idToken);                 // Android：只有 idToken
// iOS：external.Submit(idToken, accessToken)
```

完整的 Android Java 骨架 / iOS 步骤见 **[docs/SETUP-GOOGLE.md](docs/SETUP-GOOGLE.md)**。

## Firebase 登录

- **安装**：Firebase Unity SDK（当前 13.17.0 整包 / 单包页 13.16.0；所有 Firebase 包与本模块的 EDM4U 必须**同版本**）。官方只支持 `.unitypackage` 或**本地 `.tgz`** 两种方式，**没有**官方 git UPM 地址。
- **配置**：`google-services.json`（Android）与 `GoogleService-Info.plist`（iOS）放到 **`Assets/` 根目录**。
- **启用本模块的 Firebase 适配层**：
  - 用 UPM `.tgz` 安装 → `versionDefines` 自动打开 `COFFEEBEAN_FIREBASE_AUTH`；
  - 用 `.unitypackage` 安装 → 没有 UPM 包，需要手动加编译符号：Hub 工具里点「④ 启用 Firebase 后端符号」（或 Player Settings 手动加）。
- **初始化**：本模块替你调 `FirebaseApp.CheckAndFixDependenciesAsync()` 并只在 `DependencyStatus.Available` 后放行后续调用（**不需要**你写 `FirebaseApp.Create()`）。
- 支持的登录方式：Google（凭据）、匿名（游客）、邮箱+密码；`FetchProvidersForEmailAsync` 之类的 identifier-first 流程需要业务自己扩（模块只暴露了主流路径）。

逐步配置（控制台 + 工程）见 **[docs/SETUP-FIREBASE.md](docs/SETUP-FIREBASE.md)**。

## 自检工具（强烈建议先跑一遍）

`Window > CoffeeBean > Login · 登录配置自检`：

- 逐条检查：`google-services.json` 是否存在、**包名是否与 PlayerSettings 一致**、`GoogleService-Info.plist`、`LoginSettings` 是否缺、`GoogleWebClientId` 是否为空
- 检查 Android minSdk（Firebase 官方下限 **API 24**；官方 troubleshooting 明确 minSdk 23 会在 dex 阶段炸）
- 检查 Firebase 程序集与编译符号是否一致（装了没开 / 开了没装 都会指出来）
- 一键取 **debug keystore 的 SHA-1**（release keystore 也能读）——SHA-1 没登记到 Firebase 是 Android 上最常见的失败原因
- 一键创建 `LoginSettings` 资产、一键开关 Firebase 编译符号

## 目录结构

```
Runtime/
├── LoginService.cs            门面：状态机 + 事件 + 游客转正（业务只用这个）
├── LoginContracts.cs          ILoginProvider（拿凭据）/ ILoginBackend（换会话）
├── LoginModels.cs             枚举 + LoginUser / LoginCredential / LoginResult
├── LoginSettings.cs           配置资产（OAuth client id、启动行为、编辑器模拟）
├── LoginFactory.cs            按"装了什么"自动装配（反射，不硬引用 SDK）
├── LoginLog.cs
├── Providers/
│   ├── GoogleSignInReflectionProvider.cs   兼容 archived 的 GoogleSignIn 插件（反射）
│   ├── ExternalCredentialProvider.cs       外部凭据入口（Credential Manager / iOS 桥 / 测试）
│   └── SimulatedGoogleProvider.cs          编辑器模拟 Google（仅编辑器）
├── Development/DevelopmentLoginBackend.cs  开发后端（内存，仅编辑器 / DEVELOPMENT_BUILD）
├── Firebase/                  Firebase 后端适配（独立程序集，装了 Firebase 才编译）
└── Bridge/                    Core 可选集成（注册 LoginService）
Editor/
├── LoginSetupValidator.cs     纯函数自检（可单测）
├── LoginSetupEnvironment.cs   采集工程现状 + keytool 读 SHA-1
└── LoginSetupWindow.cs        Hub 工具窗口
Samples~/LoginDemo/            编辑器里就能点完整流程的示例
```

## 平台差异（照官方文档）

| | Android | iOS | 编辑器 / 桌面 |
|---|---|---|---|
| 匿名/游客 | ✅ | ✅ | ✅（桌面 Firebase 是 beta，仅开发期） |
| 邮箱+密码 | ✅ | ✅ | ✅ |
| Google 弹窗 | ✅（Credential Manager，自定义桥） | ✅（GoogleSignIn-iOS） | ❌ 官方不支持 → 用模拟/自传 token |
| 凭据登录 `SignInWithCredential` | ✅ | ✅ | ✅（token 自己拿到即可） |

Firebase Unity SDK 官方下限：**Unity 2021+、Android API 24+、iOS 15+**，桌面端整体是 **beta（仅开发期，不要拿去发布）**。

## 常见坑（都在自检窗口里自动查）

1. `google-services.json` 的 `package_name` 与 PlayerSettings 包名不一致 → Android 构建直接失败（`No matching client found for package name`）。
2. Android 没登记 SHA-1（debug 与 release 都要）→ Google 登录失败。
3. minSdk 23 → dex 阶段炸；官方要求/建议 **24**。
4. Firebase 用 `.unitypackage` 装、但没加 `COFFEEBEAN_FIREBASE_AUTH` → 后端静默不生效（`DescribeProviders()` 会显示"开发后端"）。
5. 同一个工程**只能有一份 EDM4U**，且 Firebase 各包版本必须一致，导入方式**不能混用**（全 unitypackage 或全 UPM）。
6. iOS 不要关 method swizzling；自定义 AppDelegate 用 Info.plist 的 `FirebaseAppDelegateClassName`。
7. 编辑器里点 Google 登录拿到的是**假 token**：真后端会以 `invalid-credential` 拒绝 —— 这是预期行为（用开发后端跑 UI，真机验证真账号）。

## 测试

EditMode 测试（`Tests/`）：门面状态机 / 事件 / 串行保护 / 错误码映射 / 游客转正保留 uid / 外部凭据入口 / 开发后端 / 反射 provider / 工厂装配 / 自检器规则。
跑：`powershell -File scripts/run-editmode-tests.ps1 -Assembly CoffeeBean.Login.Tests`（或在 `dev` 工程 Test Runner 里跑）。
**真 SDK 的行为不在单测里**（要真机 + 真控制台配置），见文档里的验证清单。

## 版本约定

- SemVer + git tag `vX.Y.Z`；`package.json` 的 `version` 与 `Runtime/Bridge/Bridge.cs` 里 `CoffeeBeanModule` 的 `Version` **必须一致**。
- 每个版本在 `CHANGELOG.md` 记录，并在 GitHub 建 Release（正文取自 CHANGELOG 该版本条目）。

## License

[MIT](LICENSE.md)
