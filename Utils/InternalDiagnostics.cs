using System.Diagnostics;

namespace ReplayLogger
{
    internal static class InternalDiagnostics
    {
        [Conditional("REPLAYLOGGER_INTERNAL_DIAGNOSTICS")]
        internal static void Info(string message)
        {
        }

        [Conditional("REPLAYLOGGER_INTERNAL_DIAGNOSTICS")]
        internal static void Warn(string message)
        {
        }

        [Conditional("REPLAYLOGGER_INTERNAL_DIAGNOSTICS")]
        internal static void Error(string message)
        {
        }
    }
}
