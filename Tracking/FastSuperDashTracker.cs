using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace ReplayLogger
{
    internal sealed class FastSuperDashTracker
    {
        private bool hasInitialState;
        private string initialArenaName;
        private string currentArenaName;
        private long currentBaseUnixTime;
        private Dictionary<string, string> initialState = new(StringComparer.Ordinal);
        private Dictionary<string, string> currentState = new(StringComparer.Ordinal);
        private readonly List<string> changes = new();
        private long lastSpeedLogTime;
        private string lastLoggedSpeedValue;
        private string lastLoggedSpeedInGameValue;
        private const int SpeedMultiplierThrottleMs = 400;

        private Type moduleManagerType;
        private bool moduleManagerResolved;
        private PropertyInfo modulesProperty;
        private FieldInfo modulesField;

        private Type fastSuperDashType;
        private bool fastSuperDashResolved;
        private FieldInfo instantSuperDashField;
        private PropertyInfo instantSuperDashProperty;
        private FieldInfo speedMultiplierField;
        private PropertyInfo speedMultiplierProperty;
        private FieldInfo fastSuperDashEverywhereField;
        private PropertyInfo fastSuperDashEverywhereProperty;

        public bool HasData => hasInitialState || changes.Count > 0;

        public void Reset()
        {
            hasInitialState = false;
            initialArenaName = null;
            currentArenaName = null;
            currentBaseUnixTime = 0;
            initialState.Clear();
            currentState.Clear();
            changes.Clear();
            lastSpeedLogTime = 0;
            lastLoggedSpeedValue = null;
            lastLoggedSpeedInGameValue = null;
        }

        public void StartFight(string arenaName, long baseUnixTime)
        {
            currentArenaName = string.IsNullOrWhiteSpace(arenaName) ? "UnknownArena" : arenaName;
            currentBaseUnixTime = baseUnixTime;

            Dictionary<string, string> snapshot = BuildSnapshot();

            if (!hasInitialState)
            {
                hasInitialState = true;
                initialArenaName = currentArenaName;
                initialState = new Dictionary<string, string>(snapshot, StringComparer.Ordinal);
                currentState = new Dictionary<string, string>(snapshot, StringComparer.Ordinal);
                return;
            }

            if (currentState.Count == 0)
            {
                currentState = new Dictionary<string, string>(snapshot, StringComparer.Ordinal);
                return;
            }

            foreach (var entry in snapshot)
            {
                if (currentState.TryGetValue(entry.Key, out string previous) &&
                    string.Equals(previous, entry.Value, StringComparison.Ordinal))
                {
                    continue;
                }

                string descriptor = previous == null
                    ? $"{entry.Key}: {entry.Value}"
                    : $"{entry.Key}: {previous} -> {entry.Value}";

                long unixTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                long delta = currentBaseUnixTime > 0 ? unixTime - currentBaseUnixTime : 0;
                changes.Add($"|{currentArenaName}|+{delta}|{descriptor}");
            }

            currentState = new Dictionary<string, string>(snapshot, StringComparer.Ordinal);
            UpdateSpeedBaseline(snapshot);
        }

        public void Update(string arenaName)
        {
            if (!hasInitialState)
            {
                return;
            }

            if (!string.IsNullOrEmpty(arenaName) &&
                !string.Equals(currentArenaName, arenaName, StringComparison.Ordinal))
            {
                return;
            }

            Dictionary<string, string> snapshot = BuildSnapshot();
            if (snapshot.Count == 0)
            {
                return;
            }

            if (currentState.Count == 0)
            {
                currentState = new Dictionary<string, string>(snapshot, StringComparer.Ordinal);
                UpdateSpeedBaseline(snapshot);
                return;
            }

            foreach (var entry in snapshot)
            {
                if (IsSpeedMultiplierKey(entry.Key))
                {
                    continue;
                }

                if (currentState.TryGetValue(entry.Key, out string previous) &&
                    string.Equals(previous, entry.Value, StringComparison.Ordinal))
                {
                    continue;
                }

                string descriptor = previous == null
                    ? $"{entry.Key}: {entry.Value}"
                    : $"{entry.Key}: {previous} -> {entry.Value}";

                long unixTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                long delta = currentBaseUnixTime > 0 ? unixTime - currentBaseUnixTime : 0;
                changes.Add($"|{currentArenaName}|+{delta}|{descriptor}");
            }

            LogSpeedMultiplierChanges(snapshot);

            currentState = new Dictionary<string, string>(snapshot, StringComparer.Ordinal);
        }

        public void WriteSection(StreamWriter writer)
        {
            if (writer == null)
            {
                return;
            }

            if (!HasData)
            {
                return;
            }

            LogWrite.EncryptedLine(writer, "  Fast Super Dash:");
            if (!string.IsNullOrEmpty(initialArenaName))
            {
                LogWrite.EncryptedLine(writer, $"    Initial Arena: {initialArenaName}");
            }
            LogWrite.EncryptedLine(writer, "    State:");

            if (initialState.Count == 0)
            {
                LogWrite.EncryptedLine(writer, "      (unavailable)");
            }
            else
            {
                foreach (var entry in initialState)
                {
                    LogWrite.EncryptedLine(writer, $"      {entry.Key}: {entry.Value}");
                }
            }

            LogWrite.EncryptedLine(writer, "    Changes:");
            if (changes.Count == 0)
            {
                LogWrite.EncryptedLine(writer, "      (none)");
            }
            else
            {
                foreach (string change in changes)
                {
                    LogWrite.EncryptedLine(writer, $"      {change}");
                }
            }
        }

        private Dictionary<string, string> BuildSnapshot()
        {
            bool hasEnabled = TryGetFastSuperDashEnabled(out bool enabled);
            bool hasLoaded = TryGetFastSuperDashLoaded(out bool loaded);
            bool hasInstant = TryGetInstantSuperDash(out bool instant);
            bool hasEverywhere = TryGetFastSuperDashEverywhere(out bool everywhere);
            bool hasMultiplier = TryGetSpeedMultiplier(out float multiplier);
            bool moduleActive = hasLoaded ? loaded : (hasEnabled && enabled);
            bool activeInGame = false;
            bool hasSceneState = hasEverywhere && (hasEnabled || hasLoaded) &&
                                 TryComputeActiveState(moduleActive, everywhere, out activeInGame);

            Dictionary<string, string> snapshot = new(StringComparer.Ordinal)
            {
                ["Fast Super Dash"] = hasEnabled ? FormatToggle(enabled) : "N/A",
                ["Instant Super Dash"] = hasInstant ? FormatToggle(instant) : "N/A",
                ["Allow in All Scenes"] = hasEverywhere ? FormatToggle(everywhere) : "N/A",
                ["Speed Multiplier"] = hasMultiplier
                    ? multiplier.ToString("0.##", CultureInfo.InvariantCulture)
                    : "N/A",
                ["Fast Super Dash (In-Game)"] = hasSceneState ? FormatToggle(activeInGame) : "N/A",
                ["Instant Super Dash (In-Game)"] = hasSceneState && hasInstant
                    ? FormatToggle(activeInGame && instant)
                    : "N/A",
                ["Allow in All Scenes (In-Game)"] = hasEverywhere && (hasEnabled || hasLoaded)
                    ? FormatToggle(moduleActive && everywhere)
                    : "N/A",
                ["Speed Multiplier (In-Game)"] = hasSceneState && hasMultiplier
                    ? (activeInGame ? multiplier : 1f).ToString("0.##", CultureInfo.InvariantCulture)
                    : "N/A"
            };

            return snapshot;
        }

        private void LogSpeedMultiplierChanges(Dictionary<string, string> snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            if (!snapshot.TryGetValue("Speed Multiplier", out string speedValue) &&
                !snapshot.TryGetValue("Speed Multiplier (In-Game)", out string speedInGameValue))
            {
                return;
            }

            snapshot.TryGetValue("Speed Multiplier", out speedValue);
            snapshot.TryGetValue("Speed Multiplier (In-Game)", out speedInGameValue);

            bool speedChanged = !string.Equals(lastLoggedSpeedValue, speedValue, StringComparison.Ordinal);
            bool speedInGameChanged = !string.Equals(lastLoggedSpeedInGameValue, speedInGameValue, StringComparison.Ordinal);
            if (!speedChanged && !speedInGameChanged)
            {
                return;
            }

            long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            if (lastSpeedLogTime > 0 && now - lastSpeedLogTime < SpeedMultiplierThrottleMs)
            {
                return;
            }

            if (speedChanged)
            {
                string descriptor = lastLoggedSpeedValue == null
                    ? $"Speed Multiplier: {speedValue}"
                    : $"Speed Multiplier: {lastLoggedSpeedValue} -> {speedValue}";
                long delta = currentBaseUnixTime > 0 ? now - currentBaseUnixTime : 0;
                changes.Add($"|{currentArenaName}|+{delta}|{descriptor}");
                lastLoggedSpeedValue = speedValue;
            }

            if (speedInGameChanged)
            {
                string descriptor = lastLoggedSpeedInGameValue == null
                    ? $"Speed Multiplier (In-Game): {speedInGameValue}"
                    : $"Speed Multiplier (In-Game): {lastLoggedSpeedInGameValue} -> {speedInGameValue}";
                long delta = currentBaseUnixTime > 0 ? now - currentBaseUnixTime : 0;
                changes.Add($"|{currentArenaName}|+{delta}|{descriptor}");
                lastLoggedSpeedInGameValue = speedInGameValue;
            }

            lastSpeedLogTime = now;
        }

        private void UpdateSpeedBaseline(Dictionary<string, string> snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            snapshot.TryGetValue("Speed Multiplier", out lastLoggedSpeedValue);
            snapshot.TryGetValue("Speed Multiplier (In-Game)", out lastLoggedSpeedInGameValue);
            lastSpeedLogTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        }

        private static bool IsSpeedMultiplierKey(string key)
        {
            return string.Equals(key, "Speed Multiplier", StringComparison.Ordinal) ||
                string.Equals(key, "Speed Multiplier (In-Game)", StringComparison.Ordinal);
        }

        private bool TryGetFastSuperDashLoaded(out bool loaded)
        {
            loaded = false;
            if (!TryGetFastSuperDashModule(out object module))
            {
                return false;
            }

            try
            {
                PropertyInfo prop = module.GetType().GetProperty("Loaded", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop?.GetValue(module) is bool flag)
                {
                    loaded = flag;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private bool TryGetFastSuperDashEnabled(out bool enabled)
        {
            enabled = false;
            if (!TryGetFastSuperDashModule(out object module))
            {
                return false;
            }

            try
            {
                PropertyInfo prop = module.GetType().GetProperty("Enabled", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop?.GetValue(module) is bool flag)
                {
                    enabled = flag;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private bool TryComputeActiveState(bool moduleActive, bool allowEverywhere, out bool active)
        {
            active = false;
            if (!TryGetSceneName(out string sceneName))
            {
                return false;
            }

            bool isBossRush = PlayerData.instance != null && PlayerData.instance.bossRushMode;
            bool allowScene =
                allowEverywhere ||
                string.Equals(sceneName, "GG_Workshop", StringComparison.Ordinal) ||
                string.Equals(sceneName, "GG_Atrium", StringComparison.Ordinal) ||
                string.Equals(sceneName, "GG_Atrium_Roof", StringComparison.Ordinal) ||
                (isBossRush && string.Equals(sceneName, "Room_Colosseum_01", StringComparison.Ordinal));

            active = moduleActive && allowScene;
            return true;
        }

        private bool TryGetSceneName(out string sceneName)
        {
            sceneName = GameManager.instance?.sceneName;
            if (string.IsNullOrEmpty(sceneName))
            {
                sceneName = currentArenaName;
            }

            return !string.IsNullOrEmpty(sceneName);
        }

        private bool TryGetInstantSuperDash(out bool enabled)
        {
            return TryGetBoolSetting("instantSuperDash", "InstantSuperDash", ref instantSuperDashField, ref instantSuperDashProperty, out enabled);
        }

        private bool TryGetFastSuperDashEverywhere(out bool enabled)
        {
            return TryGetBoolSetting("fastSuperDashEverywhere", "FastSuperDashEverywhere", ref fastSuperDashEverywhereField, ref fastSuperDashEverywhereProperty, out enabled);
        }

        private bool TryGetSpeedMultiplier(out float multiplier)
        {
            return TryGetFloatSetting("fastSuperDashSpeedMultiplier", "FastSuperDashSpeedMultiplier", ref speedMultiplierField, ref speedMultiplierProperty, out multiplier);
        }

        private bool TryGetFastSuperDashModule(out object module)
        {
            module = null;
            IDictionary modules = GetModuleMap();
            if (modules == null)
            {
                return false;
            }

            if (!modules.Contains("FastSuperDash"))
            {
                return false;
            }

            module = modules["FastSuperDash"];
            return module != null;
        }

        private bool TryGetBoolSetting(string primaryName, string altName, ref FieldInfo field, ref PropertyInfo property, out bool enabled)
        {
            enabled = false;
            Type type = GetFastSuperDashType();
            if (type == null)
            {
                return false;
            }

            if (field == null && property == null)
            {
                const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                field = type.GetField(primaryName, flags) ?? type.GetField(altName, flags);
                if (field == null)
                {
                    property = type.GetProperty(primaryName, flags) ?? type.GetProperty(altName, flags);
                }
            }

            try
            {
                object raw = property != null
                    ? property.GetValue(null)
                    : field?.GetValue(null);

                if (raw is bool flag)
                {
                    enabled = flag;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private bool TryGetFloatSetting(string primaryName, string altName, ref FieldInfo field, ref PropertyInfo property, out float value)
        {
            value = 0f;
            Type type = GetFastSuperDashType();
            if (type == null)
            {
                return false;
            }

            if (field == null && property == null)
            {
                const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                field = type.GetField(primaryName, flags) ?? type.GetField(altName, flags);
                if (field == null)
                {
                    property = type.GetProperty(primaryName, flags) ?? type.GetProperty(altName, flags);
                }
            }

            try
            {
                object raw = property != null
                    ? property.GetValue(null)
                    : field?.GetValue(null);

                if (raw == null)
                {
                    return false;
                }

                value = Convert.ToSingle(raw, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
            }

            return false;
        }

        private IDictionary GetModuleMap()
        {
            Type type = GetModuleManagerType();
            if (type == null)
            {
                return null;
            }

            if (modulesProperty == null && modulesField == null)
            {
                const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                modulesProperty = type.GetProperty("Modules", flags);
                modulesField = type.GetField("Modules", flags) ?? type.GetField("modules", flags);
            }

            try
            {
                object raw = modulesProperty?.GetValue(null) ?? modulesField?.GetValue(null);
                if (raw is IDictionary dict)
                {
                    return dict;
                }

                object value = raw?.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(raw);
                return value as IDictionary;
            }
            catch
            {
            }

            return null;
        }

        private Type GetModuleManagerType()
        {
            if (!moduleManagerResolved)
            {
                moduleManagerType = FindType("GodhomeQoL.ModuleManager");
                moduleManagerResolved = true;
            }

            return moduleManagerType;
        }

        private Type GetFastSuperDashType()
        {
            if (!fastSuperDashResolved)
            {
                fastSuperDashType = FindType("GodhomeQoL.Modules.QoL.FastSuperDash");
                fastSuperDashResolved = true;
            }

            return fastSuperDashType;
        }

        private static Type FindType(string fullName)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = asm.GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static string FormatToggle(bool value) => value ? "On" : "Off";
    }
}
