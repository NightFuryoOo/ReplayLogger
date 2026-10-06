using System;
using System.Collections.Generic;
using System.IO;

namespace ReplayLogger
{
    internal sealed class BossDifficultyTracker : ITrackerLifecycle
    {
        private const int MaxEntries = 512;

        private readonly List<DifficultyEntry> entries = new();
        private readonly Dictionary<string, int> lastLevelByAttempt = new(StringComparer.Ordinal);

        internal bool HasData => entries.Count > 0;

        void ITrackerLifecycle.Reset() => Reset();
        void ITrackerLifecycle.Write(StreamWriter writer) => Write(writer);

        private bool hasLast;
        private string lastArena;
        private int lastAttempt;
        private int lastLevel;

        internal void Reset()
        {
            entries.Clear();
            lastLevelByAttempt.Clear();
            hasLast = false;
            lastArena = null;
        }

        internal void Observe(string arenaName, int attemptNumber, int level, long nowUnixTime, long baseUnixTime)
        {
            if (string.IsNullOrEmpty(arenaName) || entries.Count >= MaxEntries)
            {
                return;
            }

            if (hasLast && lastLevel == level && lastAttempt == attemptNumber && ReferenceEquals(lastArena, arenaName))
            {
                return;
            }

            string label = LevelLabel(level);
            if (label == null)
            {
                return;
            }

            hasLast = true;
            lastArena = arenaName;
            lastAttempt = attemptNumber;
            lastLevel = level;

            string key = arenaName + "|" + attemptNumber;
            bool seenBefore = lastLevelByAttempt.TryGetValue(key, out int previousLevel);
            if (seenBefore && previousLevel == level)
            {
                return;
            }

            lastLevelByAttempt[key] = level;
            entries.Add(new DifficultyEntry(arenaName, attemptNumber, Math.Max(0, nowUnixTime - baseUnixTime), label, seenBefore));
        }

        internal void Write(StreamWriter writer)
        {
            if (writer == null || entries.Count == 0)
            {
                return;
            }

            List<string> batch = TempObjectPools.RentStringList(entries.Count + 2);
            try
            {
                batch.Add("Boss Difficulty:");
                for (int i = 0; i < entries.Count; i++)
                {
                    DifficultyEntry entry = entries[i];
                    batch.Add($"  |{entry.Arena}| {entry.Attempt}*|+{entry.DeltaMs}|Level: {entry.Label}{(entry.Changed ? " (changed)" : string.Empty)}");
                }

                batch.Add("---------------------------------------------------");
                LogWrite.EncryptedLines(writer, batch);
            }
            finally
            {
                TempObjectPools.ReturnStringList(batch);
            }
        }

        private static string LevelLabel(int level)
        {
            switch (level)
            {
                case 0:
                    return "Attuned";
                case 1:
                    return "Ascended";
                case 2:
                case 3:
                    return "Radiant";
                default:
                    return null;
            }
        }

        private readonly struct DifficultyEntry
        {
            internal DifficultyEntry(string arena, int attempt, long deltaMs, string label, bool changed)
            {
                Arena = arena;
                Attempt = attempt;
                DeltaMs = deltaMs;
                Label = label;
                Changed = changed;
            }

            internal string Arena { get; }
            internal int Attempt { get; }
            internal long DeltaMs { get; }
            internal string Label { get; }
            internal bool Changed { get; }
        }
    }
}
