#if COFFEEBEAN_CORE
// ============================================================================
// CoffeeBean 模块标识 + Core 生命周期集成（Bridge 模式）
//
// 主 Runtime 程序集（CoffeeBean.Login）不引用 Core，因此本模块不装 Core 也能独立编译、独立使用；
// 本 Bridge 程序集带 defineConstraints: ["COFFEEBEAN_CORE"]，只在装了 Core 时才参与编译，
// 从而把模块标记暴露给 Core 的程序集扫描（Hub 的模块列表 / 依赖拓扑都靠它）。
// ============================================================================
using CoffeeBean;

[assembly: CoffeeBeanModule(
    "com.coffeebean.login",
    "0.1.0",
    DisplayName = "Login",
    Description = "账号登录：Google 账号登录 + Firebase 登录（可插拔 provider / 后端），含游客转正、ID token 获取与平台配置自检。",
    // 只声明直接依赖的 CoffeeBean 模块；Core 必须列在这里（拓扑排序读的就是这个数组）。
    Dependencies = new[] { "com.coffeebean.tools", "com.coffeebean.core" }
)]

namespace CoffeeBean
{
    /// <summary>
    /// Core 集成：把登录门面注册进服务注册表，其它模块即可
    /// <c>context.Services.Get&lt;LoginService&gt;()</c> 使用，而无需引用本模块程序集。
    ///
    /// **注意**：这里只注册、不初始化 —— 初始化要用工程自己的 <see cref="LoginSettings"/>
    /// （OAuth client id / 是否自动游客登录），属于游戏启动流程的决策，由业务调用
    /// <c>LoginService.Instance.Initialize(settings)</c>。
    /// </summary>
    public sealed class LoginModule : ICoffeeBeanModule
    {
        public void OnLoad(CoffeeBeanContext context)
        {
            context.Services.Register<LoginService>(LoginService.Instance);
            context.Log("CoffeeBean.Login integrated (backend: " + LoginService.Instance.Backend.Name + ").");
        }

        public void OnStart()
        {
        }

        public void OnShutdown()
        {
        }
    }
}
#endif
