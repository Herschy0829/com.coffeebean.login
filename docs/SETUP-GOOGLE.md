# Google 登录接入（Android / iOS / 编辑器）

> 本文依据 2026-09 的官方文档核实结果编写。**结论会过时，请以官方页面为准**，出处见每节末尾。
>
> 一句话现状：**Android 的官方推荐已经是 Credential Manager**（旧的 Google Sign-In for Android 被标记 deprecated 并从
> Play services Auth SDK 移除中）；Google **没有**提供 Credential Manager 的 Unity 包，所以 Unity 侧要自己桥；
> 曾经通用的 `googlesamples/google-signin-unity` 插件**仓库已 archived**（仍能用）。

---

## 0. 先决定走哪条路

| 路线 | 成本 | 适用 |
|---|---|---|
| **A. 自己桥 Credential Manager**（推荐） | 一个 Java/Kotlin 文件 + 一个 C# 桥；约半天 | 新工程、想长期可维护 |
| **B. 用（已 archived 的）GoogleSignIn Unity 插件** | 装插件即可，本模块反射自动接上 | 存量工程、先跑通 |
| **C. 只做编辑器联调** | 零成本：本模块自带模拟 Google | UI 先行开发 |

三条路的**C# 侧登录代码完全一样**（`login.SignInWithGoogle(cb)`），区别只在"idToken 从哪来"。

---

## 1. 公共前提：OAuth 客户端 ID 与 SHA-1

1. **Google Cloud Console** → API 和服务 → 凭据 → 创建凭据 → **OAuth 客户端 ID**：
   - 先配好"OAuth 同意屏幕"；
   - **Android 类型**：填包名 + **SHA-1**（debug 与 release 各来一个）；
   - **iOS 类型**：填 Bundle ID；
   - **Web 类型**：这个是关键 —— Android 的 `setServerClientId(...)` / 旧插件的 `WebClientId` 要的就是它，
     它决定 idToken 的 audience（**填错成 Android client id 是最常见错误**）。
2. 把两个 SHA-1（debug/release）填到 **Firebase 控制台** → 项目设置 → 你的 Android 应用 →「SHA 证书指纹」，
   然后**重新下载 `google-services.json`**（启用 Google 登录后 json 里才有 OAuth client 信息）。
3. 取 SHA-1：本模块 Hub 工具 `登录配置自检` → 「⑤ 复制 debug keystore SHA-1」，release 用下面的输入框读取。

```
# 手动命令（Windows）
keytool -list -v -keystore %USERPROFILE%\.android\debug.keystore -alias androiddebugkey -storepass android -keypass android
```

把得到的 SHA1 写进 `LoginSettings.GoogleWebClientId`（Web 类型那个）：

```
Window > CoffeeBean > 登录配置自检 > ① 创建设置资产  →  在 Inspector 里填 GoogleWebClientId
```

---

## 2. 路线 A：Android Credential Manager（推荐）

### 2.1 Gradle 依赖

官方 Firebase Android 指南给的组合（版本按官方原文；`credentials` 目前最新稳定是 1.6.0，文档示例出现过 1.7.0-alpha03）：

```gradle
implementation "androidx.credentials:credentials:1.3.0"
implementation "androidx.credentials:credentials-play-services-auth:1.3.0"
implementation "com.google.android.libraries.identity.googleid:googleid:1.1.1"
```

Unity 侧加依赖的两种方式（官方对 EDM4U vs mainTemplate **没有表态**，任选其一）：

- **EDM4U（推荐）**：在 `Assets/Plugins/Android/mainTemplate.gradle` 里加 `dependencies { ... }`，
  或写一个 `Dependencies.xml` 让 EDM4U 解析；
- 或直接在自定义 `mainTemplate.gradle` 里加 `implementation`。

> ⚠️ 工程里**只能有一份 EDM4U**（Firebase 的 `.unitypackage` 自带一份；第三方插件也常自带）。

### 2.2 Android 插件（Java/Kotlin）

关键几步（官方 [Sign in with Google](https://developer.android.com/identity/sign-in/credential-manager-siwg) +
[Firebase Android 指南](https://firebase.google.com/docs/auth/android/google-signin)）：

```java
// 1) 建请求：serverClientId 必须是 **Web** 类型的 OAuth 客户端 ID
GetGoogleIdOption googleIdOption = new GetGoogleIdOption.Builder()
        .setFilterByAuthorizedAccounts(true)      // 用户取消/无账号时可再试 false 走"添加账号"
        .setServerClientId(WEB_CLIENT_ID)
        .setAutoSelectEnabled(false)
        .build();

GetCredentialRequest request = new GetCredentialRequest.Builder()
        .addCredentialOption(googleIdOption)
        .build();

// 2) 取凭据
credentialManager.getCredentialAsync(activity, request, null, executor,
    new CredentialManagerCallback<GetCredentialResponse, GetCredentialException>() {
      @Override public void onResult(GetCredentialResponse response) {
        Credential credential = response.getCredential();
        if (Credential.TYPE_GOOGLE_ID_TOKEN_CREDENTIAL.equals(credential.getType())) {
          GoogleIdTokenCredential idTokenCredential =
              GoogleIdTokenCredential.createFrom(credential.getData());
          String idToken = idTokenCredential.getIdToken();      // ← 交给 Firebase 的就是它
          // 回传 Unity：
          UnityPlayer.currentActivity.runOnUiThread(() ->
              UnityPlayer.UnitySendMessage("CoffeeBeanLoginBridge", "OnGoogleIdToken", idToken));
        }
      }
      @Override public void onFailure(GetCredentialException e) {
        // 用户取消 / 没有可用凭据 / 配置错误：区分对待（取消不要当错误上报）
        UnityPlayer.UnitySendMessage("CoffeeBeanLoginBridge", "OnGoogleError",
            e.getClass().getSimpleName() + ": " + e.getMessage());
      }
    });

// 3) 登出时还要清凭据提供方状态
credentialManager.clearCredentialState(new ClearCredentialStateRequest(), null, executor, callback);
```

> **注意**：Android 的 Credential Manager **只返回 idToken**，没有 accessToken、没有 serverAuthCode ——
> 所以 Firebase 侧写成 `GoogleAuthProvider.getCredential(idToken, null)`（官方 Android 示例第二参就是 `null`）。
> 离线访问（serverAuthCode）已经拆到 `AuthorizationClient` API，本模块不涉及。

### 2.3 C# 侧接进本模块（不用改框架）

```csharp
using CoffeeBean;
using UnityEngine;

/// 挂在场景里的一个 GameObject 上（名字要和 UnitySendMessage 的第一个参数一致）
public sealed class CoffeeBeanLoginBridge : MonoBehaviour
{
    public LoginSettings Settings;
    private LoginService _login;
    private ExternalCredentialProvider _external;

    private void Start()
    {
        _login = LoginFactory.CreateDefault(Settings, out _external);
        _login.Initialize(Settings);
        // 可选：让 LoginSettings.SimulateGoogleInEditor = false（真机桥已就绪）
    }

    public void SignInGoogle() => _login.SignInWithGoogle(OnDone);

    // ↓↓↓ Android 插件回调（UnitySendMessage 只能传一个字符串参数）
    public void OnGoogleIdToken(string idToken) => _external.Submit(idToken);          // Android：无 accessToken
    public void OnGoogleError(string message)   => _external.SubmitError(
        message != null && message.ToLowerInvariant().Contains("cancel")
            ? LoginErrorCode.Cancelled
            : LoginErrorCode.Unknown, message);

    private void OnDone(LoginResult result)
    {
        if (result.Success) Debug.Log("登录成功: " + result.User);
        else if (result.Code == LoginErrorCode.Cancelled) { /* 静默 */ }
        else Debug.LogWarning("登录失败[" + result.Code + "]: " + result.Error);
    }
}
```

把 `CoffeeBeanLoginBridge` 的 GameObject 放在首个场景里（`UnitySendMessage` 按名字找对象）。

---

## 3. 路线 B：用（已 archived 的）GoogleSignIn Unity 插件

1. 从 `googlesamples/google-signin-unity`（**仓库已 archived**，最后更新 2024-08）取 `.unitypackage` 导入；
2. 在插件的配置里填：
   - **Web client id**（上面那个 Web 类型 ID）；
   - **RequestIdToken = true**（不勾就拿不到 idToken，Firebase 直接失败）；
3. **C# 不用写任何胶水**：本模块 `GoogleSignInReflectionProvider` 会反射接上
   `GoogleSignIn.DefaultInstance.SignIn()`，`login.SignInWithGoogle(cb)` 直接就通。

> 该插件是 archived 状态 + 依赖已被官方 deprecated 的 play-services-auth —— **仅作过渡**，
> 新工程请走路线 A。

---

## 4. iOS

1. **加 GoogleSignIn-iOS**（CocoaPods `pod 'GoogleSignIn'` 或 SPM product `GoogleSignIn`；
   官方 Get started 页示例版本 9.0.0，最新为 10.0.0）。若用 CocoaPods，注意 GoogleSignIn-iOS **≥ 7.1.0**
   （2024-05 起 Apple 强制 Privacy Manifest）。
2. **Info.plist**：
   - `GIDClientID` = iOS 类型的 client id；
   - `CFBundleURLTypes` → `CFBundleURLSchemes` 加一条 = `GoogleService-Info.plist` 里的 **`REVERSED_CLIENT_ID`**；
   - 要拿 ID token 做后端认证时再加 `GIDServerClientID`（= Web client id）。
3. **AppDelegate 转交 URL 回调**（Unity 导出 Xcode 工程后改 `UnityAppController`）：
   ```objc
   - (BOOL)application:(UIApplication *)app openURL:(NSURL *)url options:(NSDictionary<UIApplicationOpenURLOptionsKey,id> *)options {
     return [[GIDSignIn sharedInstance] handleURL:url];
   }
   ```
   （Scene 生命周期则在 `scene:openURLContexts:` 里转发。）
4. 拿 token → 回传 Unity：`GIDSignIn.sharedInstance.signIn(withPresenting:)` 的回调里
   `user.idToken?.tokenString`（**必填**）与 `user.accessToken.tokenString`（iOS **有**），
   然后 `external.Submit(idToken, accessToken)`。
5. **不要关闭 method swizzling**（官方明确 "Swizzling is required by the SDK"）。

---

## 5. 验证清单（真机）

- [ ] 打开 App → 日志里出现 `[CoffeeBean.Login] 后端 = Firebase Auth` 且 `Firebase Auth 就绪`
- [ ] 点 Google 登录 → 弹出系统账号选择框
- [ ] 成功后日志：`Firebase 登录成功：google.com:<uid> <email>`
- [ ] Firebase 控制台 → Authentication → Users 里能看到该用户，Provider = google.com
- [ ] `GetIdToken(forceRefresh: true)` 的 token 粘到 https://jwt.io 能看到 `aud` = 你的 **Web client id**
- [ ] 取消授权 → 代码里是 `Cancelled`，**没有**弹错误提示
- [ ] 游客转正：先游客登录 → 绑 Google → `uid` 不变、`IsAnonymous=false`
- [ ] 重复用同一个 Google 号在另一台设备绑定到别的游客账号 → 得到 `AccountExists`（走"登录已有账号"分支）

## 6. 官方出处

- Android Credential Manager / Sign in with Google：<https://developer.android.com/identity/sign-in/credential-manager-siwg>
- Firebase Android Google 登录（含 Gradle 版本）：<https://firebase.google.com/docs/auth/android/google-signin>
- Firebase Unity Google 登录（说明 Unity 不自带 Google UI）：<https://firebase.google.com/docs/auth/unity/google-signin>
- iOS Google 登录（Info.plist / AppDelegate / API）：<https://firebase.google.com/docs/auth/ios/google-signin>
- 旧 Android Google Sign-In 迁移说明（deprecated）：<https://developers.android.com/identity/sign-in/legacy-gsi-migration>
- `google-signin-unity`（已 archived）：<https://github.com/googlesamples/google-signin-unity>
- GoogleSignIn-iOS：<https://github.com/google/GoogleSignIn-iOS>
