using System;

namespace ReplayLogger
{
    internal sealed class SavedLogInfo
    {
        internal SavedLogInfo(string sourcePath, string rootFolder, string bossFolder, string difficultyFolder)
        {
            SourcePath = sourcePath;
            RootFolder = rootFolder;
            BossFolder = bossFolder;
            DifficultyFolder = difficultyFolder;
        }

        internal string SourcePath { get; }
        internal string RootFolder { get; }
        internal string BossFolder { get; }
        internal string DifficultyFolder { get; }
    }

    internal static class SavedLogTracker
    {
        private static readonly object Sync = new();
        private static SavedLogInfo lastSaved;

        internal static void Record(string path)
        {
            Record(path, null, null, null);
        }

        internal static void Record(string path, string rootFolder, string bossFolder, string difficultyFolder)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            lock (Sync)
            {
                lastSaved = new SavedLogInfo(path, rootFolder, bossFolder, difficultyFolder);
            }
        }

        internal static bool TryGet(out SavedLogInfo info)
        {
            lock (Sync)
            {
                info = lastSaved;
            }

            return info != null && !string.IsNullOrWhiteSpace(info.SourcePath);
        }
    }
}
