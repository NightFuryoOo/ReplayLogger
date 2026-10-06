using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using HKHealthManager = global::HealthManager;

namespace ReplayLogger
{
    internal sealed class BossSpawnHpTracker : ITrackerLifecycle
    {
        private const int MinTrackedHp = 100;
        private const int MaxEntries = 256;
        private const string ArenaPrefix = "GG_";

        private readonly List<SpawnEntry> entries = new();
        private readonly HashSet<int> seenInstanceIds = new();

        internal bool HasData => entries.Count > 0;

        void ITrackerLifecycle.Reset() => Reset();
        void ITrackerLifecycle.Write(StreamWriter writer) => Write(writer);

        internal void Reset()
        {
            entries.Clear();
            seenInstanceIds.Clear();
        }

        internal void Record(HKHealthManager healthManager, long nowUnixTime, long baseUnixTime, int attemptNumber = 0)
        {
            if (healthManager == null || entries.Count >= MaxEntries)
            {
                return;
            }

            int hp = healthManager.hp;
            if (hp < MinTrackedHp)
            {
                return;
            }

            GameObject host = healthManager.gameObject;
            if (host == null || !host.scene.IsValid())
            {
                return;
            }

            string sceneName = host.scene.name;
            if (string.IsNullOrEmpty(sceneName) || !sceneName.StartsWith(ArenaPrefix, StringComparison.Ordinal))
            {
                return;
            }

            if (!seenInstanceIds.Add(healthManager.GetInstanceID()))
            {
                return;
            }

            entries.Add(new SpawnEntry(sceneName, host.name ?? string.Empty, hp, Math.Max(0, nowUnixTime - baseUnixTime), attemptNumber));
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
                batch.Add("Boss Spawn HP:");
                for (int i = 0; i < entries.Count; i++)
                {
                    SpawnEntry entry = entries[i];
                    string name = entry.ObjectName.Replace('|', '/').Replace(':', ' ');
                    string attempt = entry.Attempt > 0 ? $" {entry.Attempt}*|" : string.Empty;
                    batch.Add($"  |{entry.Arena}|{attempt}+{entry.DeltaMs}|{name}: {entry.Hp}");
                }

                batch.Add("---------------------------------------------------");
                LogWrite.EncryptedLines(writer, batch);
            }
            finally
            {
                TempObjectPools.ReturnStringList(batch);
            }
        }

        private readonly struct SpawnEntry
        {
            internal SpawnEntry(string arena, string objectName, int hp, long deltaMs, int attempt)
            {
                Arena = arena;
                ObjectName = objectName;
                Hp = hp;
                DeltaMs = deltaMs;
                Attempt = attempt;
            }

            internal string Arena { get; }
            internal string ObjectName { get; }
            internal int Hp { get; }
            internal long DeltaMs { get; }
            internal int Attempt { get; }
        }
    }
}
