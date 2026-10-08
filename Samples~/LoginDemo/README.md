# Login Demo（示例）

**在编辑器里就能把整套登录流程点一遍**（不需要装 Firebase / Google SDK）。

## 怎么跑

1. 新建（或打开）一个空场景，建一个空 GameObject，挂上 `LoginDemo.cs`；
2. 可选：把 `Assets/Resources/CoffeeBean/LoginSettings.asset` 拖到 `Settings` 字段
   （没有就走默认行为：编辑器 + 开发后端 + 模拟 Google）；
3. Play，然后按顺序点：
   - **① 初始化** → 顶部会显示后端与可用登录方式；
   - **② 游客登录** → 得到一个 dev uid（同时写进 PlayerPrefs，重启还是同一个）；
   - **③ Google 登录** → 编辑器里走**模拟 Google**（假 token）；
   - **④ 游客转正** → 注意看"当前用户"的 uid **没变**、`IsAnonymous` 变 false；
   - **⑤ 取 ID token** → 拿到 dev token（真后端在真机上才有真 token）；
   - **⑥ 登出** → 状态复位。

## 真机上有什么不同

**按钮代码一个字都不用改**，变的只是 `LoginFactory` 装配出来的实现：

| | 编辑器 | Android / iOS 真机 |
|---|---|---|
| 后端 | `DevelopmentLoginBackend`（内存） | `FirebaseLoginBackend` |
| Google | `SimulatedGoogleProvider`（假 token） | Credential Manager / GoogleSignIn-iOS 桥 → `ExternalCredentialProvider.Submit(idToken)` |

## 平台桥那一段怎么用

示例里的 **「模拟平台桥回传 idToken」** 按钮演示的是真机上要走的路径：

1. 先点 **③ Google 登录**（此时登录流程会挂起等平台回传）；
2. 平台桥（Android 的 `UnitySendMessage` / iOS 的原生回调）把 idToken 送进来，等价于本例的
   `external.Submit("id-token-from-platform-bridge", null, ...)`；
3. 之后才会收到 `SignInWithGoogle` 的回调。

完整接法见 [../docs/SETUP-GOOGLE.md](../docs/SETUP-GOOGLE.md) 与 [../docs/USAGE-FLOW.md](../docs/USAGE-FLOW.md)。
