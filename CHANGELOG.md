# Changelog

## [0.1.0] - 2026-09-19

### Added（首个版本）

**三层可插拔结构**（业务只认 `LoginService`）：

- `ILoginProvider`：拿第三方凭据（Google / 外部桥 / 自定义）；`ILoginBackend`：把凭据换成会话
  （Firebase Auth / 自建服务）。两层各自可替换、可单测。
- `LoginService`：状态机（`NotInitialized / SignedOut / SigningIn / SignedIn / Failed`）、事件
  （`OnStateChanged / OnSignedIn / OnUserChanged / OnSignedOut / OnFailed`）、**串行保护**（进行中拒绝并发登录，
  避免弹两次授权窗口）、**游客转正**（`Link` 保留 uid）、`GetIdToken(forceRefresh)`。

**Google 登录**（依据 2026-09 官方文档现状设计）：

- `GoogleSignInReflectionProvider`：**反射**接住（已 archived 但可用的）`GoogleSignIn` Unity 插件 ——
  不硬引用、没装就没有。
- `ExternalCredentialProvider`：外部凭据入口，供 **Android Credential Manager**（官方当前推荐）/
  iOS GoogleSignIn-iOS / 自建 OAuth 的平台桥把 idToken 喂进来（`Submit(idToken, accessToken)`）。
- `SimulatedGoogleProvider`：编辑器模拟登录（假 token），让 UI/流程在没有 SDK 时先跑通。

**Firebase 登录后端**（独立程序集 `CoffeeBean.Login.Firebase`，`versionDefines` + `defineConstraints`）：

- 自动做官方要求的 `CheckAndFixDependenciesAsync` 依赖检查，**检查完成前把请求排队**（启动即游客登录不会失败）；
- `SignInWithCredential`（Google）/ `SignInAnonymously` / `SignInWithEmailAndPassword` /
  `LinkWithCredential`（游客转正）/ `TokenAsync` / `SignOut` / `StateChanged`；
- Firebase `AuthError` → 模块错误码映射（**按枚举名匹配**，避免换 SDK 版本就编译不过）。
- **没装 Firebase 也能编译**：适配程序集整体跳过，自动降级到开发后端。

**开发后端** `DevelopmentLoginBackend`（`#if UNITY_EDITOR || DEVELOPMENT_BUILD`）：
内存实现，游客 uid 持久化在 PlayerPrefs、转正保留 uid —— 与真后端语义一致，便于 UI 先行开发。

**工程配置自检**（`Window > CoffeeBean > 登录配置自检`）：

- `LoginSetupValidator`（纯函数、可单测）：缺 `google-services.json` / plist、
  **`google-services.json` 包名与 PlayerSettings 不一致**、Android minSdk < 24、Firebase 程序集与编译符号不一致、
  缺 `LoginSettings` / 缺 `GoogleWebClientId`、取不到 debug keystore SHA-1 …… 逐条给"怎么修"。
- `LoginSetupEnvironment`：采集工程现状 + 用 `keytool` 读 debug/release keystore 的 SHA-1（一键复制）。
- 一键创建设置资产、一键开关 `COFFEEBEAN_FIREBASE_AUTH` 编译符号。

**文档**：

- `README.md`：安装 / 快速使用 / 完整流程 / Google 三条路线对比 / Firebase 启用方式 / 自检工具 / 常见坑。
- `docs/SETUP-FIREBASE.md`：SDK 安装（.unitypackage 与 UPM 本地 tz 两条官方路线）、配置文件位置、
  控制台步骤、平台下限、官方 troubleshooting 摘录。
- `docs/SETUP-GOOGLE.md`：Android Credential Manager 完整步骤 + Java 骨架 + C# 桥接；
  iOS Info.plist / AppDelegate / API；兼容的 archived 插件路线；真机验证清单。
- `docs/USAGE-FLOW.md`：启动集成顺序、三个典型场景（游客升级 / 老玩家直登 / 账号冲突）、
  ID token 与自建服务、错误码处理表、UI 约定、开发期联调矩阵。

**测试**：门面状态机与事件、串行保护、错误码传递、游客转正保留 uid、外部凭据入口
（含"重复登录被拒"与"无凭据 Submit"）、开发后端、反射 provider、工厂装配、自检器规则与工具函数。

### Notes（依赖与边界，写清楚免得误用）

- 依赖 `com.coffeebean.tools`；**不依赖** Firebase / Google SDK（装了自动生效，没装可编译可运行）。
- 官方现状（2026-09 核实）：Android 推荐 **Credential Manager**，旧 Google Sign-In for Android 已 deprecated；
  `googlesamples/google-signin-unity` 仓库**已 archived**；Firebase Unity SDK 无**官方** git UPM 地址；
  桌面端 Firebase 是 **beta，官方限定仅开发期**；官方平台下限 Unity 2021+ / Android API 24+ / iOS 15+。
- 本模块**不做**：账号删除/改密/邮箱验证等 Firebase 用户管理接口（只暴露登录链路）；
  服务端 ID token 验签（那是 Firebase Admin SDK 的事）。
