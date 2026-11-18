using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ProbablyUnifiedHotkeys
{
    /// <summary>
    /// 统一结构化日志门面：提供标准格式化输出、无头单测防崩隔离与动态调试日志开关。
    /// </summary>
    public static class ModLogger
    {
        private const string Prefix = "[ProbablyUnifiedHotkeys]";

        /// <summary>
        /// 供离线 .NET 8 单元测试环境屏蔽 Unity 原生底层日志 ECall。
        /// </summary>
        internal static bool SuppressUnityLogForTests { get; set; }

        /// <summary>
        /// 供单元测试或宿主环境捕获格式化后的日志输出。
        /// </summary>
        internal static Action<string>? TestLogSink { get; set; }

        /// <summary>
        /// 动态调试日志启用判定委托（接通 ModFeatureConfig.EnableDebugLogging）。
        /// </summary>
        public static Func<bool>? DebugEnabledProvider { get; set; }

        public static void Info(string message)
        {
            string formatted = $"{Prefix}[INFO] {message}";
            if (SuppressUnityLogForTests)
            {
                TestLogSink?.Invoke(formatted);
                return;
            }
            LogInternal(formatted);
        }

        public static void Warn(string message, Exception? ex = null)
        {
            string formatted = ex != null
                ? $"{Prefix}[WARN] {message}: {ex}"
                : $"{Prefix}[WARN] {message}";

            if (SuppressUnityLogForTests)
            {
                TestLogSink?.Invoke(formatted);
                return;
            }
            LogWarningInternal(formatted);
        }

        public static void Error(string message, Exception? ex = null)
        {
            string formatted = ex != null
                ? $"{Prefix}[ERROR] {message}: {ex}"
                : $"{Prefix}[ERROR] {message}";

            if (SuppressUnityLogForTests)
            {
                TestLogSink?.Invoke(formatted);
                return;
            }
            LogErrorInternal(formatted);
        }

        public static void Debug(string message)
        {
            if (DebugEnabledProvider == null || !DebugEnabledProvider())
            {
                return;
            }

            string formatted = $"{Prefix}[DEBUG] {message}";
            if (SuppressUnityLogForTests)
            {
                TestLogSink?.Invoke(formatted);
                return;
            }
            LogInternal(formatted);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void LogInternal(string message)
        {
            UnityEngine.Debug.Log(message);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void LogWarningInternal(string message)
        {
            UnityEngine.Debug.LogWarning(message);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void LogErrorInternal(string message)
        {
            UnityEngine.Debug.LogError(message);
        }
    }
}
