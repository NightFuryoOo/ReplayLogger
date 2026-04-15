using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ReplayLogger
{
    internal sealed class DebugModEventsTracker
    {
        private readonly List<string> events = new();
        private bool hasUiState;
        private bool lastUiVisible;

        internal IReadOnlyList<string> Events => events;

        internal void Reset()
        {
            events.Clear();
            hasUiState = false;
            lastUiVisible = false;
        }

        internal void Reset(bool initialDebugUiVisible)
        {
            events.Clear();
            hasUiState = true;
            lastUiVisible = initialDebugUiVisible;
        }

        internal void Update(StreamWriter writer, string arenaName, long lastUnixTime, DebugModFrameSnapshot snapshot, long nowUnixTime)
        {
            if (writer == null)
            {
                return;
            }

            if (!snapshot.Available)
            {
                return;
            }

            bool uiVisible = snapshot.UiVisible;

            if (!hasUiState)
            {
                hasUiState = true;
                lastUiVisible = uiVisible;
                return;
            }

            if (uiVisible == lastUiVisible)
            {
                return;
            }

            string from = lastUiVisible ? "On" : "Off";
            string to = uiVisible ? "On" : "Off";
            long delta = Math.Max(0, nowUnixTime - lastUnixTime);
            events.Add($"  |{NormalizeArena(arenaName)}|+{delta}|UI: {from} -> {to}");
            lastUiVisible = uiVisible;
        }

        private static string NormalizeArena(string arenaName)
        {
            return string.IsNullOrWhiteSpace(arenaName) ? "UnknownArena" : arenaName;
        }
    }

    internal static class DebugModEventsWriter
    {
        internal static void Write(StreamWriter writer, IReadOnlyList<string> events)
        {
            if (writer == null)
            {
                return;
            }

            List<string> batch = TempObjectPools.RentStringList((events?.Count ?? 0) + 1);
            try
            {
                batch.Add("DebugMod UI Events:");
                if (events == null || events.Count == 0)
                {
                    batch.Add("  (none)");
                }
                else
                {
                    foreach (string entry in events)
                    {
                        batch.Add(entry ?? string.Empty);
                    }
                }

                LogWrite.EncryptedLines(writer, batch);
            }
            finally
            {
                TempObjectPools.ReturnStringList(batch);
            }
        }
    }

    internal sealed class DebugHotkeysTracker
    {
        private readonly List<string> bindings = new();
        private readonly List<string> activations = new();
        private readonly Dictionary<KeyCode, List<string>> actionsByKey = new();

        internal IReadOnlyList<string> Bindings => bindings;
        internal IReadOnlyList<string> Activations => activations;
        internal IReadOnlyDictionary<KeyCode, List<string>> ActionsByKey => actionsByKey;

        internal void Reset()
        {
            bindings.Clear();
            activations.Clear();
            actionsByKey.Clear();
        }

        internal void InitializeBindings()
        {
            bindings.Clear();
            activations.Clear();
            actionsByKey.Clear();

            if (!DebugModIntegration.TryGetHotkeyBindings(out IReadOnlyDictionary<string, KeyCode> hotkeys) || hotkeys == null)
            {
                return;
            }

            foreach (KeyValuePair<string, KeyCode> pair in hotkeys.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                string actionName = pair.Key ?? string.Empty;
                KeyCode keyCode = pair.Value;
                if (!actionsByKey.TryGetValue(keyCode, out List<string> actions))
                {
                    actions = new List<string>();
                    actionsByKey[keyCode] = actions;
                }

                actions.Add(actionName);
                bindings.Add($"  {actionName}: {keyCode}");
            }
        }

        internal void TrackActivation(KeyCode keyCode, string arenaName, long lastUnixTime, long unixTime)
        {
            long delta = Math.Max(0, unixTime - lastUnixTime);
            string arena = string.IsNullOrWhiteSpace(arenaName) ? "UnknownArena" : arenaName;

            if (!actionsByKey.TryGetValue(keyCode, out List<string> actions) || actions == null || actions.Count == 0)
            {
                return;
            }

            foreach (string action in actions)
            {
                activations.Add($"  |{arena}|+{delta}|{action} ({keyCode})");
            }
        }
    }

    internal static class DebugHotKeysWriter
    {
        internal static void Write(StreamWriter writer, IReadOnlyList<string> bindings, IReadOnlyList<string> activations)
        {
            if (writer == null)
            {
                return;
            }

            List<string> batch = TempObjectPools.RentStringList((bindings?.Count ?? 0) + (activations?.Count ?? 0) + 4);
            try
            {
                batch.Add("Debug Hotkeys:");
                batch.Add("  Bindings:");
                if (bindings == null || bindings.Count == 0)
                {
                    batch.Add("    (none)");
                }
                else
                {
                    foreach (string binding in bindings)
                    {
                        batch.Add(binding ?? string.Empty);
                    }
                }

                batch.Add("  Activations:");
                if (activations == null || activations.Count == 0)
                {
                    batch.Add("    (none)");
                }
                else
                {
                    foreach (string activation in activations)
                    {
                        batch.Add(activation ?? string.Empty);
                    }
                }

                LogWrite.EncryptedLines(writer, batch);
            }
            finally
            {
                TempObjectPools.ReturnStringList(batch);
            }
        }
    }

    internal sealed class DebugMenuTracker
    {
        private readonly List<string> entries = new();
        private bool hasUiState;
        private bool lastUiVisible;
        private bool hasCheatSnapshot;
        private DebugCheatToggleSnapshot lastCheatSnapshot;
        internal IReadOnlyList<string> Entries => entries;

        internal void Reset()
        {
            entries.Clear();
            hasUiState = false;
            lastUiVisible = false;
            hasCheatSnapshot = false;
            lastCheatSnapshot = DebugCheatToggleSnapshot.Unavailable;
        }

        internal void Reset(bool initialDebugUiVisible)
        {
            entries.Clear();
            hasUiState = true;
            lastUiVisible = initialDebugUiVisible;
            hasCheatSnapshot = false;
            lastCheatSnapshot = DebugCheatToggleSnapshot.Unavailable;

            if (DebugModIntegration.TryGetCheatToggleSnapshot(out DebugCheatToggleSnapshot snapshot) && snapshot.Available)
            {
                hasCheatSnapshot = true;
                lastCheatSnapshot = snapshot;
            }
        }

        internal void Update(StreamWriter writer, string arenaName, long lastUnixTime, DebugModFrameSnapshot snapshot, long nowUnixTime)
        {
            if (writer == null)
            {
                return;
            }

            if (!snapshot.Available)
            {
                return;
            }

            bool uiVisible = snapshot.UiVisible;
            if (!hasUiState)
            {
                hasUiState = true;
                lastUiVisible = uiVisible;
            }
            else if (uiVisible != lastUiVisible)
            {
                LogChange(writer, arenaName, lastUnixTime, nowUnixTime, "DebugMod UI", lastUiVisible ? "On" : "Off", uiVisible ? "On" : "Off");
                lastUiVisible = uiVisible;
            }

            DebugCheatToggleSnapshot cheatSnapshot = snapshot.CheatSnapshot;
            if (!cheatSnapshot.Available)
            {
                return;
            }

            if (!hasCheatSnapshot)
            {
                hasCheatSnapshot = true;
                lastCheatSnapshot = cheatSnapshot;
                return;
            }

            if (cheatSnapshot.InfiniteSoul != lastCheatSnapshot.InfiniteSoul)
            {
                LogChange(writer, arenaName, lastUnixTime, nowUnixTime, "Infinite Soul", OnOff(lastCheatSnapshot.InfiniteSoul), OnOff(cheatSnapshot.InfiniteSoul));
            }

            if (cheatSnapshot.InfiniteHp != lastCheatSnapshot.InfiniteHp)
            {
                LogChange(writer, arenaName, lastUnixTime, nowUnixTime, "Infinite HP", OnOff(lastCheatSnapshot.InfiniteHp), OnOff(cheatSnapshot.InfiniteHp));
            }

            if (cheatSnapshot.Noclip != lastCheatSnapshot.Noclip)
            {
                LogChange(writer, arenaName, lastUnixTime, nowUnixTime, "Noclip", OnOff(lastCheatSnapshot.Noclip), OnOff(cheatSnapshot.Noclip));
            }

            if (cheatSnapshot.KeyBindLock != lastCheatSnapshot.KeyBindLock)
            {
                LogChange(writer, arenaName, lastUnixTime, nowUnixTime, "Key Bind Lock", OnOff(lastCheatSnapshot.KeyBindLock), OnOff(cheatSnapshot.KeyBindLock));
            }

            lastCheatSnapshot = cheatSnapshot;
        }

        internal void LogManualChange(StreamWriter writer, string arenaName, long lastUnixTime, long nowUnixTime, string name, string fromValue, string toValue)
        {
            if (writer == null)
            {
                return;
            }

            string settingName = string.IsNullOrWhiteSpace(name) ? "Manual Change" : name;
            string from = string.IsNullOrWhiteSpace(fromValue) ? "-" : fromValue;
            string to = string.IsNullOrWhiteSpace(toValue) ? "Executed" : toValue;
            LogChange(writer, arenaName, lastUnixTime, nowUnixTime, settingName, from, to);
        }

        internal void WriteSection(StreamWriter writer)
        {
            if (writer == null)
            {
                return;
            }

            List<string> batch = TempObjectPools.RentStringList(entries.Count + 1);
            try
            {
                batch.Add("Debug Menu:");
                if (entries.Count == 0)
                {
                    batch.Add("  (none)");
                }
                else
                {
                    foreach (string entry in entries)
                    {
                        batch.Add(entry ?? string.Empty);
                    }
                }

                LogWrite.EncryptedLines(writer, batch);
            }
            finally
            {
                TempObjectPools.ReturnStringList(batch);
            }
        }

        private void LogChange(StreamWriter writer, string arenaName, long lastUnixTime, long nowUnixTime, string name, string fromValue, string toValue)
        {
            long delta = Math.Max(0, nowUnixTime - lastUnixTime);
            entries.Add($"  |{NormalizeArena(arenaName)}|+{delta}|{name}: {fromValue} -> {toValue}");
        }

        private static string NormalizeArena(string arenaName)
        {
            return string.IsNullOrWhiteSpace(arenaName) ? "UnknownArena" : arenaName;
        }

        private static string OnOff(bool value)
        {
            return value ? "On" : "Off";
        }
    }
}
