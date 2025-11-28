using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ReplayLogger
{
    internal sealed class DebugHotkeysTracker
    {
        private readonly Dictionary<KeyCode, List<string>> actionsByKey = new();
        private readonly List<string> bindings = new();
        private readonly List<string> activations = new();

        public IReadOnlyList<string> Bindings => bindings;
        public IReadOnlyList<string> Activations => activations;
        public IReadOnlyDictionary<KeyCode, List<string>> ActionsByKey => actionsByKey;

        public void Reset()
        {
            actionsByKey.Clear();
            bindings.Clear();
            activations.Clear();
        }

        public void InitializeBindings()
        {
            Reset();

            if (!DebugModIntegration.TryGetHotkeyBindings(out IReadOnlyDictionary<string, KeyCode> hotkeyBindings) ||
                hotkeyBindings == null || hotkeyBindings.Count == 0)
            {
                return;
            }

            foreach (var pair in hotkeyBindings.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
            {
                bindings.Add($"  {pair.Key}: {pair.Value}");
                if (!actionsByKey.TryGetValue(pair.Value, out List<string> actions))
                {
                    actions = new List<string>();
                    actionsByKey[pair.Value] = actions;
                }

                actions.Add(pair.Key);
            }
        }

        public void TrackActivation(KeyCode keyCode, string arenaName, long lastUnixTime, long unixTime)
        {
            if (actionsByKey.Count == 0 || !actionsByKey.TryGetValue(keyCode, out List<string> actions) || actions == null)
            {
                return;
            }

            long delta = unixTime - lastUnixTime;
            string arena = string.IsNullOrEmpty(arenaName) ? "UnknownArena" : arenaName;
            foreach (string action in actions)
            {
                activations.Add($"  |{arena}|+{delta}|{action} ({keyCode})");
            }
        }
    }
}
