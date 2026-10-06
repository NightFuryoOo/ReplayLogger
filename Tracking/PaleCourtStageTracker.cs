using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using HKHealthManager = global::HealthManager;

namespace ReplayLogger
{
    internal sealed class PaleCourtStageTracker : ITrackerLifecycle
    {
        private const int MinBossHp = 100;
        private const int MaxEntries = 1024;

        private readonly List<StageEntry> entries = new();
        private readonly List<string> lines = new();
        private readonly Dictionary<int, Watched> watched = new();
        private readonly Dictionary<string, int> ordinalByName = new(StringComparer.Ordinal);
        private readonly List<int> removeBuffer = new();
        private readonly StringBuilder columnsBuilder = new();
        private string lastColumns;

        internal bool HasData => entries.Count > 0;

        internal IReadOnlyList<string> InlineLines => lines;

        void ITrackerLifecycle.Reset() => Reset();
        void ITrackerLifecycle.Write(StreamWriter writer) => Write(writer);

        internal void Reset()
        {
            entries.Clear();
            lines.Clear();
            watched.Clear();
            ordinalByName.Clear();
            removeBuffer.Clear();
            lastColumns = null;
        }

        internal void Observe(HKHealthManager healthManager, string arena, long nowUnixTime, long baseUnixTime, int attempt)
        {
            if (healthManager == null || string.IsNullOrEmpty(arena))
            {
                return;
            }

            int id = healthManager.GetInstanceID();
            if (watched.ContainsKey(id))
            {
                return;
            }

            int hp = healthManager.hp;
            if (hp < MinBossHp)
            {
                return;
            }

            Watched state = new Watched(healthManager, ResolveName(healthManager), hp);
            watched[id] = state;
            Add(arena, attempt, nowUnixTime, baseUnixTime, "Appear", state.Name, $"hp={hp}");
        }

        internal void Poll(string arena, long nowUnixTime, long baseUnixTime, int attempt, Dictionary<HKHealthManager, (int maxHP, int lastHP)> tracked)
        {
            if (string.IsNullOrEmpty(arena))
            {
                return;
            }

            foreach (KeyValuePair<HKHealthManager, (int maxHP, int lastHP)> pair in tracked)
            {
                if (pair.Key != null && pair.Value.maxHP >= MinBossHp)
                {
                    Observe(pair.Key, arena, nowUnixTime, baseUnixTime, attempt);
                }
            }

            removeBuffer.Clear();
            foreach (KeyValuePair<int, Watched> pair in watched)
            {
                Watched state = pair.Value;
                HKHealthManager manager = state.Manager;
                if (manager == null)
                {
                    Add(arena, attempt, nowUnixTime, baseUnixTime, "Gone", state.Name, "destroyed");
                    removeBuffer.Add(pair.Key);
                    continue;
                }

                bool active = manager.gameObject != null && manager.gameObject.activeInHierarchy;
                if (active != state.Active)
                {
                    state.Active = active;
                    Add(arena, attempt, nowUnixTime, baseUnixTime, active ? "Back" : "Gone", state.Name, active ? $"hp={manager.hp}" : "inactive");
                }

                int hp = Math.Max(0, manager.hp);
                if (hp > state.LastHp)
                {
                    Add(arena, attempt, nowUnixTime, baseUnixTime, "HpUp", state.Name, $"hp {state.LastHp}->{hp}");
                    state.Zeroed = false;
                }

                state.LastHp = hp;

                if (manager.isDead)
                {
                    if (!state.Dead)
                    {
                        state.Dead = true;
                        Add(arena, attempt, nowUnixTime, baseUnixTime, "Dead", state.Name, "hp=" + hp);
                    }
                }
                else
                {
                    state.Dead = false;
                    if (hp == 0 && !state.Zeroed)
                    {
                        state.Zeroed = true;
                        Add(arena, attempt, nowUnixTime, baseUnixTime, "HpZero", state.Name, string.Empty);
                    }
                }
            }

            for (int i = 0; i < removeBuffer.Count; i++)
            {
                watched.Remove(removeBuffer[i]);
            }
        }

        internal void NoteColumns(string arena, IReadOnlyList<HKHealthManager> columns, long nowUnixTime, long baseUnixTime, int attempt)
        {
            if (columns == null || columns.Count == 0 || string.IsNullOrEmpty(arena))
            {
                return;
            }

            columnsBuilder.Clear();
            for (int i = 0; i < columns.Count; i++)
            {
                HKHealthManager manager = columns[i];
                if (i > 0)
                {
                    columnsBuilder.Append(';');
                }

                if (manager == null)
                {
                    columnsBuilder.Append('?');
                    continue;
                }

                Observe(manager, arena, nowUnixTime, baseUnixTime, attempt);
                columnsBuilder.Append(watched.TryGetValue(manager.GetInstanceID(), out Watched state) ? state.Name : BaseName(manager));
            }

            string text = columnsBuilder.ToString();
            if (string.Equals(text, lastColumns, StringComparison.Ordinal))
            {
                return;
            }

            lastColumns = text;
            Add(arena, attempt, nowUnixTime, baseUnixTime, "Columns", "-", text);
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
                batch.Add("Boss Stages:");
                for (int i = 0; i < lines.Count; i++)
                {
                    batch.Add("  " + lines[i]);
                }

                batch.Add("---------------------------------------------------");
                LogWrite.EncryptedLines(writer, batch);
            }
            finally
            {
                TempObjectPools.ReturnStringList(batch);
            }
        }

        private void Add(string arena, int attempt, long nowUnixTime, long baseUnixTime, string eventName, string name, string detail)
        {
            if (entries.Count >= MaxEntries)
            {
                return;
            }

            entries.Add(new StageEntry(arena, attempt, Math.Max(0, nowUnixTime - baseUnixTime), eventName, name, detail));
            string attemptCell = attempt > 0 ? $" {attempt}*|" : string.Empty;
            lines.Add($"|{arena}|{attemptCell}+{Math.Max(0, nowUnixTime - baseUnixTime)}|{eventName}|{name}|{detail}");
        }

        private static string BaseName(HKHealthManager manager)
        {
            string raw = manager.gameObject != null ? manager.gameObject.name : null;
            if (string.IsNullOrEmpty(raw))
            {
                raw = "Unnamed";
            }

            string name = raw.Replace("(Clone)", string.Empty).Trim().Replace('|', '/').Replace(':', ' ').Replace(';', ' ');
            switch (name)
            {
                case "White Defender":
                    return "Ogrim";
                case "Dryya2":
                    return "Dryya";
                default:
                    return name;
            }
        }

        private string ResolveName(HKHealthManager manager)
        {
            string raw = BaseName(manager);
            ordinalByName.TryGetValue(raw, out int count);
            ordinalByName[raw] = count + 1;
            return count == 0 ? raw : $"{raw}#{count + 1}";
        }

        private sealed class Watched
        {
            internal Watched(HKHealthManager manager, string name, int hp)
            {
                Manager = manager;
                Name = name;
                LastHp = hp;
                Active = true;
            }

            internal HKHealthManager Manager { get; }
            internal string Name { get; }
            internal int LastHp { get; set; }
            internal bool Active { get; set; }
            internal bool Dead { get; set; }
            internal bool Zeroed { get; set; }
        }

        private readonly struct StageEntry
        {
            internal StageEntry(string arena, int attempt, long deltaMs, string eventName, string name, string detail)
            {
                Arena = arena;
                Attempt = attempt;
                DeltaMs = deltaMs;
                Event = eventName;
                Name = name;
                Detail = detail;
            }

            internal string Arena { get; }
            internal int Attempt { get; }
            internal long DeltaMs { get; }
            internal string Event { get; }
            internal string Name { get; }
            internal string Detail { get; }
        }
    }
}
