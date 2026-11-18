using System;

namespace ProbablyUnifiedHotkeys
{
    /// <summary>
    /// 统一异常防护网：拦截外部模组及运行时可能抛出的异常，自动记录结构化日志并平稳降级。
    /// </summary>
    public static class SafeExecution
    {
        public static bool SafeExecute(Action? action, string context)
        {
            if (action == null) return false;
            try
            {
                action();
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"{context} failed", ex);
                return false;
            }
        }

        public static bool SafeExecute<T>(Action<T>? action, T arg, string context)
        {
            if (action == null) return false;
            try
            {
                action(arg);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"{context} failed", ex);
                return false;
            }
        }

        public static TResult SafeFunc<TResult>(Func<TResult>? func, TResult fallback, string context)
        {
            if (func == null) return fallback;
            try
            {
                return func();
            }
            catch (Exception ex)
            {
                ModLogger.Error($"{context} failed", ex);
                return fallback;
            }
        }
    }
}
