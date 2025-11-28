using System;
using System.Collections.Generic;
using System.IO;

namespace ReplayLogger
{
    internal sealed class DebugModEventsTracker
    {
        private readonly List<string> events = new();
        private bool previousVisible;

        public IReadOnlyList<string> Events => events;

        public void Reset(bool initialVisible = false)
        {
            events.Clear();
            previousVisible = initialVisible;
        }

        public void Update(StreamWriter writer, string arenaName, long lastUnixTime)
        {
            if (!DebugModIntegration.TryGetUiVisible(out bool visible))
            {
                return;
            }

            if (visible && !previousVisible)
            {
                long unixTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                string entry = $"|{arenaName ?? "UnknownArena"}|+{unixTime - lastUnixTime}|DebugMod UI opened";
                events.Add(entry);
                writer?.WriteLine(KeyloggerLogEncryption.EncryptLog($"DebugModUI|+{unixTime - lastUnixTime}|Opened"));
            }

            previousVisible = visible;
        }
    }
}
