using System;

namespace CoffeeBean.EditorTools
{
    /// <summary>
    /// CoffeeBean 工具窗口标记：Editor 工具窗口类打上此标记后，会被 CoffeeBean Hub
    /// （Window &gt; CoffeeBean）通过**反射按全名**自动发现并列出入口。
    ///
    /// 这里为什么要复制一份同命名空间、同名的定义：
    /// 为了让本模块的 Editor 程序集**不必引用 com.coffeebean.core**（保持 Core 可选、
    /// 模块独立）。Hub 用反射按类型全名匹配，因此各模块各自持有一份同名副本即可，
    /// 无需编译期依赖。请勿删除本文件，除非你确定本模块不需要在 Hub 中提供工具入口。
    ///
    /// 用法：
    /// <code>
    /// [CoffeeBeanTool("我的工具", "一句话说明", "Login")]
    /// public sealed class CMyToolWindow : EditorWindow { public static void Open() =&gt; GetWindow&lt;CMyToolWindow&gt;("我的工具"); }
    /// </code>
    /// 注意：打上标记后请**不要**再加重複的 [MenuItem]，入口统一收敛到 Hub。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class CoffeeBeanToolAttribute : Attribute
    {
        /// <summary>工具标题（Hub 导航列表显示）。</summary>
        public string Title { get; }

        /// <summary>工具描述（Hub 中悬停/副标题显示）。</summary>
        public string Description { get; }

        /// <summary>所属模块显示名（分组用，如 "Excel" / "Purchase"）。</summary>
        public string Module { get; }

        public CoffeeBeanToolAttribute(string title, string description = "", string module = "")
        {
            Title = title;
            Description = description;
            Module = module;
        }
    }
}
