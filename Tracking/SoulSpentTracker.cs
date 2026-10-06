using System;
using System.Collections.Generic;
using System.IO;

namespace ReplayLogger
{
    public sealed class SoulSpentTracker : ITrackerLifecycle
    {
        internal const string DefaultMainSource = "Spell";
        internal const string DefaultReserveSource = "Other";

        private readonly List<string> changes = new();
        private readonly Dictionary<string, Optional<int>> lastObservedMainBySource = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Optional<int>> lastObservedReserveBySource = new(StringComparer.Ordinal);
        private readonly List<string> mainSourceOrder = new();
        private readonly List<string> reserveSourceOrder = new();

        internal bool HasData => mainSourceOrder.Count > 0 || reserveSourceOrder.Count > 0;

        void ITrackerLifecycle.Reset() => Reset();
        void ITrackerLifecycle.Write(StreamWriter writer) => Write(writer);

        internal void Reset()
        {
            changes.Clear();
            lastObservedMainBySource.Clear();
            lastObservedReserveBySource.Clear();
            mainSourceOrder.Clear();
            reserveSourceOrder.Clear();
        }

        internal void RecordMainSpend(string arenaName, long lastUnixTime, long nowUnixTime, string source, string rawOwnerPath, int amount)
        {
            if (amount <= 1)
            {
                return;
            }

            bool unclassified = source == null;
            RecordSpend(lastObservedMainBySource, mainSourceOrder, "Main Vessel", arenaName, lastUnixTime, nowUnixTime, unclassified ? DefaultMainSource : source, unclassified ? rawOwnerPath : null, amount);
        }

        internal void RecordReserveSpend(string arenaName, long lastUnixTime, long nowUnixTime, string source, string rawOwnerPath, int amount)
        {
            if (amount <= 1)
            {
                return;
            }

            bool unclassified = source == null;
            RecordSpend(lastObservedReserveBySource, reserveSourceOrder, "Reserve Vessel", arenaName, lastUnixTime, nowUnixTime, unclassified ? DefaultReserveSource : source, unclassified ? rawOwnerPath : null, amount);
        }

        private void RecordSpend(
            Dictionary<string, Optional<int>> table,
            List<string> order,
            string vesselLabel,
            string arenaName,
            long lastUnixTime,
            long nowUnixTime,
            string source,
            string rawOwnerPath,
            int amount)
        {
            if (!table.TryGetValue(source, out Optional<int> previous))
            {
                previous = Optional<int>.None;
                table[source] = previous;
                order.Add(source);
            }

            Optional<int> value = new(amount);
            if (value == previous)
            {
                return;
            }

            bool isFirstObservation = !previous.HasValue;
            bool unclassified = !string.IsNullOrEmpty(rawOwnerPath);

            if (isFirstObservation && !unclassified)
            {
                table[source] = value;
                return;
            }

            string arena = ArenaNormalization.NormalizeLenient(arenaName);
            long delta = nowUnixTime - lastUnixTime;
            string descriptor = isFirstObservation
                ? $"{vesselLabel} ({source}) (Observed): {OptionalFormatting.FormatOptionalInt(value)}"
                : $"{vesselLabel} ({source}) (Observed): {OptionalFormatting.FormatOptionalInt(previous)} -> {OptionalFormatting.FormatOptionalInt(value)}";

            if (!string.IsNullOrEmpty(rawOwnerPath))
            {
                descriptor += $" [fsm owner: {rawOwnerPath}]";
            }

            changes.Add($"|{arena}|+{delta}|{descriptor}");
            table[source] = value;
        }

        internal void Write(StreamWriter writer)
        {
            if (writer == null || !HasData)
            {
                return;
            }

            List<string> batch = TempObjectPools.RentStringList(changes.Count + mainSourceOrder.Count + reserveSourceOrder.Count + 4);
            try
            {
                batch.Add("Soul Spent:");
                foreach (string source in mainSourceOrder)
                {
                    batch.Add($"  Main Vessel ({source}) (Observed): {OptionalFormatting.FormatOptionalInt(lastObservedMainBySource[source])}");
                }

                foreach (string source in reserveSourceOrder)
                {
                    batch.Add($"  Reserve Vessel ({source}) (Observed): {OptionalFormatting.FormatOptionalInt(lastObservedReserveBySource[source])}");
                }

                batch.Add("  Changes:");
                if (changes.Count == 0)
                {
                    batch.Add("    (none)");
                }
                else
                {
                    foreach (string change in changes)
                    {
                        batch.Add($"    {change}");
                    }
                }

                batch.Add("---------------------------------------------------");
                LogWrite.EncryptedLines(writer, batch);
            }
            finally
            {
                TempObjectPools.ReturnStringList(batch);
            }
        }
    }
}
