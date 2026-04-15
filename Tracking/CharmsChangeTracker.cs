using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ReplayLogger
{
    internal sealed class CharmsChangeTracker
    {
        private readonly HashSet<int> equipped = new();
        private readonly HashSet<int> currentBuffer = new();
        private readonly List<string> changes = new();
        private readonly List<string> inlineEvents = new();

        public IReadOnlyList<string> Changes => changes;
        public IReadOnlyList<string> InlineEvents => inlineEvents;

        public void Reset()
        {
            equipped.Clear();
            changes.Clear();
            inlineEvents.Clear();
            SnapshotCurrent();
        }

        public void Update(string arenaName, long lastUnixTime, long nowUnixTime, StreamWriter writer = null)
        {
            if (PlayerData.instance?.equippedCharms == null)
            {
                return;
            }

            currentBuffer.Clear();
            foreach (int charm in PlayerData.instance.equippedCharms)
            {
                currentBuffer.Add(charm);
            }

            foreach (int charm in currentBuffer)
            {
                if (!equipped.Contains(charm))
                {
                    long delta = nowUnixTime - lastUnixTime;
                    changes.Add($"|{arenaName ?? "UnknownArena"}|+{delta}|Equipped {FormatCharm(charm)}");
                    LogInline(writer, arenaName, delta, $"Equipped {FormatCharm(charm)}");
                }
            }

            foreach (int charm in equipped)
            {
                if (!currentBuffer.Contains(charm))
                {
                    long delta = nowUnixTime - lastUnixTime;
                    changes.Add($"|{arenaName ?? "UnknownArena"}|+{delta}|Unequipped {FormatCharm(charm)}");
                    LogInline(writer, arenaName, delta, $"Unequipped {FormatCharm(charm)}");
                }
            }

            equipped.Clear();
            foreach (int c in currentBuffer)
            {
                equipped.Add(c);
            }
        }

        public void Write(StreamWriter writer, string separator = "---------------------------------------------------")
        {
            if (writer == null)
            {
                return;
            }

            List<string> batch = TempObjectPools.RentStringList(changes.Count + 3);
            try
            {
                batch.Add("Charms:");
                if (changes.Count == 0)
                {
                    batch.Add("  (no changes)");
                }
                else
                {
                    foreach (string entry in changes)
                    {
                        batch.Add(entry);
                    }
                }

                batch.Add(string.Empty);
                if (!string.IsNullOrEmpty(separator))
                {
                    batch.Add(separator);
                }

                LogWrite.EncryptedLines(writer, batch);
            }
            finally
            {
                TempObjectPools.ReturnStringList(batch);
            }
        }

        private void SnapshotCurrent()
        {
            equipped.Clear();
            if (PlayerData.instance?.equippedCharms != null)
            {
                foreach (int c in PlayerData.instance.equippedCharms)
                {
                    equipped.Add(c);
                }
            }
        }

        private void LogInline(StreamWriter writer, string arenaName, long delta, string action)
        {
            string arena = string.IsNullOrEmpty(arenaName) ? "UnknownArena" : arenaName;
            string entry = $"Charms|{arena}|+{delta}|{action}";
            inlineEvents.Add(entry);
        }

        private static string FormatCharm(int charmId)
        {
            try
            {
                return ((Charm)charmId).ToString();
            }
            catch
            {
                return $"Charm {charmId}";
            }
        }
    }
}
