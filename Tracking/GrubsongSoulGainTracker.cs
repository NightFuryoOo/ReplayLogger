using System.Collections.Generic;
using System.IO;

namespace ReplayLogger
{
    internal sealed class GrubsongSoulGainTracker : ITrackerLifecycle
    {
        private readonly List<string> changes = new();
        private Optional<int> lastObservedMainGain = Optional<int>.None;
        private Optional<int> lastObservedReserveGain = Optional<int>.None;

        internal bool HasData => lastObservedMainGain.HasValue || lastObservedReserveGain.HasValue;

        void ITrackerLifecycle.Reset() => Reset();
        void ITrackerLifecycle.Write(StreamWriter writer) => Write(writer);

        internal void Reset()
        {
            changes.Clear();
            lastObservedMainGain = Optional<int>.None;
            lastObservedReserveGain = Optional<int>.None;
        }

        internal void RecordObservedGain(string arenaName, long lastUnixTime, long nowUnixTime, int mainGained, int reserveGained)
        {
            string arena = ArenaNormalization.NormalizeLenient(arenaName);
            long delta = nowUnixTime - lastUnixTime;

            if (mainGained != 0)
            {
                Optional<int> value = new(mainGained);
                if (value != lastObservedMainGain)
                {
                    if (lastObservedMainGain.HasValue)
                    {
                        string descriptor = $"Main Vessel (Observed): {OptionalFormatting.FormatOptionalInt(lastObservedMainGain)} -> {OptionalFormatting.FormatOptionalInt(value)}";
                        changes.Add($"|{arena}|+{delta}|{descriptor}");
                    }

                    lastObservedMainGain = value;
                }
            }

            if (reserveGained != 0)
            {
                Optional<int> value = new(reserveGained);
                if (value != lastObservedReserveGain)
                {
                    if (lastObservedReserveGain.HasValue)
                    {
                        string descriptor = $"Reserve Vessel (Observed): {OptionalFormatting.FormatOptionalInt(lastObservedReserveGain)} -> {OptionalFormatting.FormatOptionalInt(value)}";
                        changes.Add($"|{arena}|+{delta}|{descriptor}");
                    }

                    lastObservedReserveGain = value;
                }
            }
        }

        internal void Write(StreamWriter writer)
        {
            if (writer == null || !HasData)
            {
                return;
            }

            List<string> batch = TempObjectPools.RentStringList(changes.Count + 6);
            try
            {
                batch.Add("Grubsong Soul Gain:");
                batch.Add($"  Main Vessel (Observed): {OptionalFormatting.FormatOptionalInt(lastObservedMainGain)}");
                batch.Add($"  Reserve Vessel (Observed): {OptionalFormatting.FormatOptionalInt(lastObservedReserveGain)}");
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
