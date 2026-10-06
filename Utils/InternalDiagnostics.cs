using Modding;

namespace ReplayLogger
{

    internal static class InternalDiagnostics
    {
        internal static void Info(string message)
        {
            Logger.Log(message);
        }

        internal static void Warn(string message)
        {
            Logger.LogWarn(message);
        }

        internal static void Error(string message)
        {
            Logger.LogError(message);
        }
    }
}
