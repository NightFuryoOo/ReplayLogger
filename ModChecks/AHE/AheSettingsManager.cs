using System;
using System.IO;

namespace ReplayLogger
{
    
    
    
    internal static class AheSettingsManager
    {
        private static AllHallownestEnhancedToggleSnapshot currentSnapshot = AllHallownestEnhancedToggleSnapshot.Unavailable;

        public static AllHallownestEnhancedToggleSnapshot CurrentSnapshot => currentSnapshot;

        public static AllHallownestEnhancedToggleSnapshot RefreshSnapshot()
        {
            currentSnapshot = AllHallownestEnhancedIntegration.GetToggleSnapshot();
            return currentSnapshot;
        }

        public static void Reset() => currentSnapshot = AllHallownestEnhancedToggleSnapshot.Unavailable;

        public static string BuildSettingsLine()
        {
            AllHallownestEnhancedToggleSnapshot snapshot = currentSnapshot;
            if (!snapshot.Available)
            {
                return "AHE Settings: unavailable";
            }

            return $"AHE Settings: Main Switch={ToOnOff(snapshot.MainSwitch)}, Strengthen All Boss={ToOnOff(snapshot.StrengthenAllBoss)}, Strengthen All Monsters={ToOnOff(snapshot.StrengthenAllMonsters)}, GG Boss Original HP={ToOnOff(snapshot.OriginalHp)}";
        }

        public static void WriteSettingsWithSeparator(StreamWriter writer, string separator = "---------------------------------------------------")
        {
            if (writer == null)
            {
                return;
            }

            string line = BuildSettingsLine();
            if (string.IsNullOrEmpty(line))
            {
                return;
            }

                LogWrite.EncryptedLine(writer, line);
            if (!string.IsNullOrEmpty(separator))
            {
                LogWrite.EncryptedLine(writer, separator);
            }
        }

        private static string ToOnOff(bool value) => value ? "On" : "Off";
    }
}
