using System;
using System.Threading;
using UnityEngine;

namespace CoffeeBean
{
    /// <summary>
    /// 模块统一的"把回调切回主线程"入口（内部使用；业务一般不需要直接调）。
    ///
    /// **为什么不直接用 tools 的 <c>MainThreadDispatcher.RunOnMainThread</c>**：
    /// 它的主线程 id 只在 <c>Awake</c>（播放模式）或显式 <c>EnsureReady()</c> 时记录；
    /// **EditMode / 单元测试里两者都不会发生**，于是 <c>IsMainThread</c> 恒为 false，
    /// 回调被投进队列却没有人泵 → 测试拿到不到回调（本模块 20 个用例踩过这个坑）。
    ///
    /// 这里改成：
    /// · 首次调用（正常是 <see cref="LoginService.Initialize"/>，游戏主线程）**记录主线程 id**；
    /// · 已记录且当前就在主线程 → **直接执行**（EditMode 测试因此能同步拿到回调）；
    /// · 真·跨线程（Firebase 的 Task 回调可能在工作线程）→ 交给 tools 的 dispatcher 泵回主线程。
    ///
    /// 注意：<c>EnsureReady()</c> 若在**工作线程**上被调用，会把工作线程 id 记成"主线程"（它自己的实现如此），
    /// 所以本类不去调它，而是自己记录 —— 只在首次 <see cref="Run"/> 时记一次。
    /// </summary>
    public static class LoginMainThread
    {
        private static int _mainThreadId;

        /// <summary>记录主线程 id（幂等；应在主线程上调用一次，例如 <c>LoginService.Initialize</c>）。</summary>
        public static void Capture()
        {
            if (_mainThreadId == 0) _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        /// <summary>当前是否在主线程（尚未记录时按"是"处理，保证初始化前的调用也是同步的）。</summary>
        public static bool IsMainThread
            => _mainThreadId == 0 || Thread.CurrentThread.ManagedThreadId == _mainThreadId;

        /// <summary>切回主线程执行（已是主线程则立即执行）。</summary>
        public static void Run(Action action)
        {
            if (action == null) return;

            if (_mainThreadId == 0) Capture();
            if (IsMainThread)
            {
                action();
                return;
            }

            MainThreadDispatcher.Post(action);
        }
    }
}
