using System.Collections.Generic;
using System.IO;

namespace ReplayLogger
{
    internal static class CollectorPhasesSettingsManager
    {
        private static IReadOnlyList<string> currentSettings = System.Array.Empty<string>();

        public static IReadOnlyList<string> CurrentSettings => currentSettings;

        public static IReadOnlyList<string> RefreshSettings()
        {
            currentSettings = CollectorPhasesIntegration.GetSettingsLines();
            return currentSettings;
        }

        public static void Reset() => currentSettings = System.Array.Empty<string>();

        public static void WriteSettingsWithSeparator(StreamWriter writer, string separator = "---------------------------------------------------")
        {
            if (writer == null)
            {
                return;
            }

            var lines = RefreshSettings();
            if (lines == null || lines.Count == 0)
            {
                return;
            }

            foreach (string line in lines)
            {
                LogWrite.EncryptedLine(writer, line);
            }

            if (!string.IsNullOrEmpty(separator))
            {
                LogWrite.EncryptedLine(writer, separator);
            }
        }
    }
}
