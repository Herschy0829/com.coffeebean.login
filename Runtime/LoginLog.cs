using UnityEngine;

namespace CoffeeBean
{
    /// <summary>可开关的模块日志（与其它模块一致：一个总开关，前缀统一，便于在设备日志里 grep）。</summary>
    public static class LoginLog
    {
        /// <summary>全局日志开关（默认开；发布前可关）。</summary>
        public static bool Enabled = true;

        /// <summary>详细日志（打印凭据是否带 token、provider 可用性矩阵等；默认关）。</summary>
        public static bool Verbose;

        public static void Log(string message)
        {
            if (Enabled) Debug.Log("[CoffeeBean.Login] " + message);
        }

        public static void Debug_(string message)
        {
            if (Enabled && Verbose) Debug.Log("[CoffeeBean.Login] " + message);
        }

        public static void Warn(string message)
        {
            if (Enabled) Debug.LogWarning("[CoffeeBean.Login] " + message);
        }

        public static void Error(string message)
        {
            if (Enabled) Debug.LogError("[CoffeeBean.Login] " + message);
        }

        public static void Error(string message, System.Exception exception)
        {
            if (Enabled) Debug.LogError("[CoffeeBean.Login] " + message + " —— " + exception);
        }
    }
}
