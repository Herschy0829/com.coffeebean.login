# Firebase 登录接入（Unity）

> 依据 2026-09 官方文档核实（版本与出处见文末）。**版本号会变，装机前请核对官方 release notes。**

---

## 1. 装 SDK

Firebase Unity SDK 当前 **13.17.0**（整包）；单包页最新 **13.16.0**（`com.google.firebase.auth`）。
**所有 Firebase 包 + EDM4U 必须同版本**，且**不能混用导入方式**（全 `.unitypackage` 或全 UPM）。

### 方式 1：整包 `.unitypackage`（官方给首次使用者的推荐）

1. 下载整包 zip（官方 Release 正文里的地址）：
   `https://dl.google.com/firebase/sdk/unity/firebase_unity_sdk_13.17.0.zip`
2. Unity → `Assets > Import Package > Custom Package` 导入需要的包
   （单包下载页：<https://developers.google.com/unity/packages>，例如
   `https://dl.google.com/firebase/sdk/unity/dotnet4/FirebaseAuth_13.16.0.unitypackage`）。
3. 导入后代码落在 `Assets/Firebase/...`（**不是** `Packages/`）。

### 方式 2：UPM（官方只支持**本地 `.tgz`**）

从 <https://developers.google.com/unity/archive> 下 `.tgz`，**按顺序**装：

1. `com.google.external-dependency-manager`（1.2.189）
2. `com.google.firebase.app`
3. `com.google.firebase.auth`（用 Database/Storage 时它要在前）

装法：Package Manager → **Add package from tarball**，或写进 `Packages/manifest.json`：

```json
"com.google.firebase.auth": "file:../GooglePackages/com.google.firebase.auth-13.16.0.tgz"
```

> ⚠️ `https://github.com/firebase/firebase-unity-sdk.git?path=...` 这种 **git UPM 引用没有官方支持**
> （官方只讲 `.unitypackage` 与 `.tgz`）。
> ⚠️ **EDM4U 是必需的**（Android 的 AAR / iOS 的 CocoaPods 全靠它解析），且工程里**只能有一份**。

### 本模块的适配层怎么被启用

| 安装方式 | 编译符号 `COFFEEBEAN_FIREBASE_AUTH` | 说明 |
|---|---|---|
| UPM `.tgz` | **自动**（`versionDefines` 监听 `com.google.firebase.auth`） | 什么都不用做 |
| `.unitypackage` | **要手动加**（没有 UPM 包，`versionDefines` 不会触发） | Hub → 登录配置自检 → 「④ 启用 Firebase 后端符号」 |

想知道有没有生效：`login.DescribeProviders()` 里后端应该是 `Firebase Auth`；如果是
`Development（内存开发后端）` 就说明符号没开（或 SDK 没装）。

---

## 2. 放配置文件

| 平台 | 文件 | 放哪 |
|---|---|---|
| Android | `google-services.json` | **`Assets/` 根目录**（本模块自检也认 `Assets/Plugins/Android/`） |
| iOS | `GoogleService-Info.plist` | **`Assets/` 根目录** |
| 桌面/编辑器 | `StreamingAssets/google-services-desktop.json` | Firebase **自动生成**；没生成就先手动建 `Assets/StreamingAssets/` 目录再重试 |

文件名不要带 `(1)` 之类的后缀（官方排查手册明确点出这个）。

---

## 3. 控制台配置步骤

1. <https://console.firebase.google.com/> 建项目（或选已有）。
2. **加 Android 应用**：包名必须与 Unity PlayerSettings 的**完全一致**
   （本模块自检会直接对比 `google-services.json` 里的 `package_name`），然后加 **SHA-1**（debug + release 各一）。
3. **加 iOS 应用**：Bundle ID 一致。
4. **Authentication → 开始使用**，开启需要的登录方式：
   - **Google**（本模块主力）
   - **匿名**（游客登录必需）
   - **电子邮件/密码**（可选）
5. **下载 `google-services.json` / `GoogleService-Info.plist`**，放到 `Assets/` 根目录。
   ⚠️ **启用 Google 登录之后要重新下载一次**（json 里新增了 OAuth client 信息）。
6. 可选：Authentication → Settings → 开启「匿名账号 30 天自动清理」（升级 Identity Platform 后可用；
   被 link 成正式账号的不会被删）。

---

## 4. 初始化（本模块已代劳）

官方要求：**不需要**手工 `FirebaseApp.Create()`（默认实例按配置自动创建），
但**必须**做依赖检查：

```csharp
FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task => {
    if (task.Result != DependencyStatus.Available) { /* 不可用：Android 上多半是 Google Play 服务太旧 */ }
    var auth = FirebaseAuth.DefaultInstance;   // 到这里才能用
});
```

`FirebaseLoginBackend` 已经做了这件事：在你调 `login.Initialize()` 之前它就开始检查，
检查完成前过来的登录请求会**排队**，完成后统一放行（所以启动时立刻 `SignInAsGuest()` 不会失败）。

---

## 5. 平台下限与矩阵

| | Android | iOS | 编辑器 / Windows / macOS / Linux |
|---|---|---|---|
| Authentication | ✅ | ✅ | ✅（**beta：仅开发期，官方明确不要拿去发布**） |
| Google 弹窗登录 | ✅（自己桥） | ✅ | ❌（官方不支持 `SignInWithProviderAsync` 系列；用自传 token + `SignInWithCredentialAsync`） |

官方下限：**Unity 2021+、Android API 24+、iOS 15+**；Apple 侧工程还需要 Xcode 26.2+ / CocoaPods 1.12.0+。

---

## 6. 常见坑（官方 troubleshooting 摘录 + 本模块自检覆盖）

| 现象 | 原因 | 修法 |
|---|---|---|
| `Cannot fit requested classes in a single dex file` | dex 溢出 | 先开 Player Settings → Android → Publishing → **Minify**；仍不够再开 multidex |
| Gradle `:launcher:mergeExtDexDebug` "Failed to transform" | **minSdk 23** | minSdk 提到 **24**；或开 minification；或在 `settingsTemplate.gradle` 指定 `com.android.tools:r8:8.3.37` |
| Android 构建报 `No matching client found for package name` | json 的 package_name 与工程包名不一致 | 自检窗口会直接列出两边名字；改包名或重新下 json |
| Firebase 相关类找不到 / 依赖解析失败 | EDM4U 缺失或重复；Firebase 各包版本不一致；导入方式混用 | 只留一份 EDM4U，全部同版本、同一种导入方式，跑 **Assets > External Dependency Manager > Android Resolver > Resolve** |
| 编辑器里报找不到 `google-services.json` | 文件不在 `Assets/` 根目录、或名字带 `(1)` | 放对位置 + 重新下载 |
| iOS `pod install` 因 locale 失败 | CocoaPods 1.11+ 的已知问题 | 终端手动 `pod install` 后开 `.xcworkspace`；或降到 1.10.2；或 `export LANG=en_US.UTF-8` |
| macOS 编辑器首次运行被拦 | Gatekeeper 阻止 `FirebaseCppApp-*.bundle` | 系统设置 → 隐私与安全性 → 仍要打开 |
| iOS 登录回调不回来 | 关了 method swizzling / 没转交 URL | 不要禁用 swizzling；自定义 AppDelegate 用 Info.plist 的 `FirebaseAppDelegateClassName` |
| 登录错误码与预期不符 | 2023-09-15 后新建项目默认开**邮箱枚举保护** | 不要依赖"用户不存在"这类更细的错误码 |

---

## 7. 验证清单

- [ ] `login.DescribeProviders()` 显示后端 = `Firebase Auth`
- [ ] 匿名登录成功，Firebase 控制台 Users 里出现匿名用户
- [ ] 断网点登录 → `LoginErrorCode.NetworkError`（可重试）
- [ ] `GetIdToken(forceRefresh: true)` 的 token 在 jwt.io 里 `aud` 正确、`iss` = `https://securetoken.google.com/<projectId>`
- [ ] 服务端用 **Firebase Admin SDK** `VerifyIdTokenAsync` 验签通过（这是"ID token 交给自建服务"的标准姿势）

## 8. 官方出处

- Unity 安装与初始化：<https://firebase.google.com/docs/unity/setup> ・ [UPM 备选](https://firebase.google.com/docs/unity/setup-alternative)
- 版本与平台矩阵：<https://firebase.google.com/support/release-notes/unity> ・ [learn-more](https://firebase.google.com/docs/unity/learn-more)
- Auth API 参考：<https://firebase.google.com/docs/reference/unity/class/firebase/auth/firebase-auth>
- 排错 FAQ：<https://firebase.google.com/docs/unity/troubleshooting-faq> ・ [构建调试指南](https://firebase.google.com/docs/unity/build-debug-guide)
- 匿名登录 / 邮箱登录（含自动清理、枚举保护）：<https://firebase.google.com/docs/auth/unity/anonymous-auth> ・ [password-auth](https://firebase.google.com/docs/auth/unity/password-auth)
