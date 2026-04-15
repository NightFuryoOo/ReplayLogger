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
        private FastSuperDashState initialState;
        private FastSuperDashState currentState;
        private bool hasCurrentState;
        private readonly List<string> changes = new();
        private Optional<float> lastLoggedSpeedValue;
        private Optional<float> lastLoggedSpeedInGameValue;

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
            initialState = default;
            currentState = default;
            hasCurrentState = false;
            changes.Clear();
            lastLoggedSpeedValue = Optional<float>.None;
            lastLoggedSpeedInGameValue = Optional<float>.None;
        }

        public void StartFight(string arenaName, long baseUnixTime)
        {
            currentArenaName = string.IsNullOrWhiteSpace(arenaName) ? "UnknownArena" : arenaName;
            currentBaseUnixTime = baseUnixTime;
            long now = baseUnixTime;

            FastSuperDashState snapshot = BuildState();

            if (!hasInitialState)
            {
                hasInitialState = true;
                initialArenaName = currentArenaName;
                initialState = snapshot;
                currentState = snapshot;
                hasCurrentState = true;
                UpdateSpeedBaseline(snapshot);
                return;
            }

            if (!hasCurrentState)
            {
                currentState = snapshot;
                hasCurrentState = true;
                UpdateSpeedBaseline(snapshot);
                return;
            }

            LogFieldChange("Fast Super Dash", currentState.FastSuperDash, snapshot.FastSuperDash, now);
            LogFieldChange("Instant Super Dash", currentState.InstantSuperDash, snapshot.InstantSuperDash, now);
            LogFieldChange("Allow in All Scenes", currentState.AllowEverywhere, snapshot.AllowEverywhere, now);
            LogFieldChange("Speed Multiplier", currentState.SpeedMultiplier, snapshot.SpeedMultiplier, now);
            LogFieldChange("Fast Super Dash (In-Game)", currentState.FastSuperDashInGame, snapshot.FastSuperDashInGame, now);
            LogFieldChange("Instant Super Dash (In-Game)", currentState.InstantSuperDashInGame, snapshot.InstantSuperDashInGame, now);
            LogFieldChange("Allow in All Scenes (In-Game)", currentState.AllowEverywhereInGame, snapshot.AllowEverywhereInGame, now);
            LogFieldChange("Speed Multiplier (In-Game)", currentState.SpeedMultiplierInGame, snapshot.SpeedMultiplierInGame, now);

            currentState = snapshot;
            UpdateSpeedBaseline(snapshot);
        }

        public void Update(string arenaName, long nowUnixTime)
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

            FastSuperDashState snapshot = BuildState();

            if (!hasCurrentState)
            {
                currentState = snapshot;
                hasCurrentState = true;
                UpdateSpeedBaseline(snapshot);
                return;
            }

            long now = nowUnixTime;
            LogFieldChange("Fast Super Dash", currentState.FastSuperDash, snapshot.FastSuperDash, now);
            LogFieldChange("Instant Super Dash", currentState.InstantSuperDash, snapshot.InstantSuperDash, now);
            LogFieldChange("Allow in All Scenes", currentState.AllowEverywhere, snapshot.AllowEverywhere, now);
            LogFieldChange("Fast Super Dash (In-Game)", currentState.FastSuperDashInGame, snapshot.FastSuperDashInGame, now);
            LogFieldChange("Instant Super Dash (In-Game)", currentState.InstantSuperDashInGame, snapshot.InstantSuperDashInGame, now);
            LogFieldChange("Allow in All Scenes (In-Game)", currentState.AllowEverywhereInGame, snapshot.AllowEverywhereInGame, now);
            LogSpeedMultiplierChanges(snapshot, now);

            currentState = snapshot;
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

            List<string> batch = TempObjectPools.RentStringList(changes.Count + 12);
            try
            {
                batch.Add("  Fast Super Dash:");
                if (!string.IsNullOrEmpty(initialArenaName))
                {
                    batch.Add($"    Initial Arena: {initialArenaName}");
                }
                batch.Add("    State:");
                batch.Add($"      Fast Super Dash: {FormatOptionalToggle(initialState.FastSuperDash)}");
                batch.Add($"      Instant Super Dash: {FormatOptionalToggle(initialState.InstantSuperDash)}");
                batch.Add($"      Allow in All Scenes: {FormatOptionalToggle(initialState.AllowEverywhere)}");
                batch.Add($"      Speed Multiplier: {FormatOptionalFloat(initialState.SpeedMultiplier)}");
                batch.Add($"      Fast Super Dash (In-Game): {FormatOptionalToggle(initialState.FastSuperDashInGame)}");
                batch.Add($"      Instant Super Dash (In-Game): {FormatOptionalToggle(initialState.InstantSuperDashInGame)}");
                batch.Add($"      Allow in All Scenes (In-Game): {FormatOptionalToggle(initialState.AllowEverywhereInGame)}");
                batch.Add($"      Speed Multiplier (In-Game): {FormatOptionalFloat(initialState.SpeedMultiplierInGame)}");
                batch.Add("    Changes:");
                if (changes.Count == 0)
                {
                    batch.Add("      (none)");
                }
                else
                {
                    foreach (string change in changes)
                    {
                        batch.Add($"      {change}");
                    }
                }

                LogWrite.EncryptedLines(writer, batch);
            }
            finally
            {
                TempObjectPools.ReturnStringList(batch);
            }
        }

        private FastSuperDashState BuildState()
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

            return new FastSuperDashState(
                hasEnabled ? new Optional<bool>(enabled) : Optional<bool>.None,
                hasInstant ? new Optional<bool>(instant) : Optional<bool>.None,
                hasEverywhere ? new Optional<bool>(everywhere) : Optional<bool>.None,
                hasMultiplier ? new Optional<float>(NormalizeFloat(multiplier)) : Optional<float>.None,
                hasSceneState ? new Optional<bool>(activeInGame) : Optional<bool>.None,
                hasSceneState && hasInstant ? new Optional<bool>(activeInGame && instant) : Optional<bool>.None,
                hasEverywhere && (hasEnabled || hasLoaded) ? new Optional<bool>(moduleActive && everywhere) : Optional<bool>.None,
                hasSceneState && hasMultiplier ? new Optional<float>(NormalizeFloat(activeInGame ? multiplier : 1f)) : Optional<float>.None);
        }

        private void LogSpeedMultiplierChanges(FastSuperDashState snapshot, long now)
        {
            Optional<float> speedValue = snapshot.SpeedMultiplier;
            Optional<float> speedInGameValue = snapshot.SpeedMultiplierInGame;
            bool speedChanged = speedValue != lastLoggedSpeedValue;
            bool speedInGameChanged = speedInGameValue != lastLoggedSpeedInGameValue;
            if (!speedChanged && !speedInGameChanged)
            {
                return;
            }

            if (speedChanged)
            {
                string descriptor = !lastLoggedSpeedValue.HasValue
                    ? $"Speed Multiplier: {FormatOptionalFloat(speedValue)}"
                    : $"Speed Multiplier: {FormatOptionalFloat(lastLoggedSpeedValue)} -> {FormatOptionalFloat(speedValue)}";
                long delta = currentBaseUnixTime > 0 ? now - currentBaseUnixTime : 0;
                changes.Add($"|{currentArenaName}|+{delta}|{descriptor}");
                lastLoggedSpeedValue = speedValue;
            }

            if (speedInGameChanged)
            {
                string descriptor = !lastLoggedSpeedInGameValue.HasValue
                    ? $"Speed Multiplier (In-Game): {FormatOptionalFloat(speedInGameValue)}"
                    : $"Speed Multiplier (In-Game): {FormatOptionalFloat(lastLoggedSpeedInGameValue)} -> {FormatOptionalFloat(speedInGameValue)}";
                long delta = currentBaseUnixTime > 0 ? now - currentBaseUnixTime : 0;
                changes.Add($"|{currentArenaName}|+{delta}|{descriptor}");
                lastLoggedSpeedInGameValue = speedInGameValue;
            }
        }

        private void UpdateSpeedBaseline(FastSuperDashState snapshot)
        {
            lastLoggedSpeedValue = snapshot.SpeedMultiplier;
            lastLoggedSpeedInGameValue = snapshot.SpeedMultiplierInGame;
        }

        private void LogFieldChange(string key, Optional<bool> previous, Optional<bool> current, long now)
        {
            if (previous == current)
            {
                return;
            }

            string descriptor = $"{key}: {FormatOptionalToggle(previous)} -> {FormatOptionalToggle(current)}";
            long delta = currentBaseUnixTime > 0 ? now - currentBaseUnixTime : 0;
            changes.Add($"|{currentArenaName}|+{delta}|{descriptor}");
        }

        private void LogFieldChange(string key, Optional<float> previous, Optional<float> current, long now)
        {
            if (previous == current)
            {
                return;
            }

            string descriptor = $"{key}: {FormatOptionalFloat(previous)} -> {FormatOptionalFloat(current)}";
            long delta = currentBaseUnixTime > 0 ? now - currentBaseUnixTime : 0;
            changes.Add($"|{currentArenaName}|+{delta}|{descriptor}");
        }

        private static float NormalizeFloat(float value)
        {
            return (float)Math.Round(value, 2, MidpointRounding.ToEven);
        }

        private static string FormatOptionalToggle(Optional<bool> value)
        {
            return value.HasValue ? FormatToggle(value.Value) : "N/A";
        }

        private static string FormatOptionalFloat(Optional<float> value)
        {
            return value.HasValue
                ? value.Value.ToString("0.##", CultureInfo.InvariantCulture)
                : "N/A";
        }

        private readonly struct FastSuperDashState
        {
            internal FastSuperDashState(
                Optional<bool> fastSuperDash,
                Optional<bool> instantSuperDash,
                Optional<bool> allowEverywhere,
                Optional<float> speedMultiplier,
                Optional<bool> fastSuperDashInGame,
                Optional<bool> instantSuperDashInGame,
                Optional<bool> allowEverywhereInGame,
                Optional<float> speedMultiplierInGame)
            {
                FastSuperDash = fastSuperDash;
                InstantSuperDash = instantSuperDash;
                AllowEverywhere = allowEverywhere;
                SpeedMultiplier = speedMultiplier;
                FastSuperDashInGame = fastSuperDashInGame;
                InstantSuperDashInGame = instantSuperDashInGame;
                AllowEverywhereInGame = allowEverywhereInGame;
                SpeedMultiplierInGame = speedMultiplierInGame;
            }

            internal Optional<bool> FastSuperDash { get; }
            internal Optional<bool> InstantSuperDash { get; }
            internal Optional<bool> AllowEverywhere { get; }
            internal Optional<float> SpeedMultiplier { get; }
            internal Optional<bool> FastSuperDashInGame { get; }
            internal Optional<bool> InstantSuperDashInGame { get; }
            internal Optional<bool> AllowEverywhereInGame { get; }
            internal Optional<float> SpeedMultiplierInGame { get; }
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
                if (ReflectionMemberAccessCache.TryGetCachedRuntimeBoolProperty(module, "Loaded", out bool flag))
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
                if (ReflectionMemberAccessCache.TryGetCachedRuntimeBoolProperty(module, "Enabled", out bool flag))
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
                    ? property.GetCachedValue(null)
                    : field?.GetCachedValue(null);

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
                    ? property.GetCachedValue(null)
                    : field?.GetCachedValue(null);

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
                object raw = modulesProperty?.GetCachedValue(null) ?? modulesField?.GetCachedValue(null);
                if (raw is IDictionary dict)
                {
                    return dict;
                }

                if (ReflectionMemberAccessCache.TryGetCachedRuntimePropertyValue(raw, "Value", out object value))
                {
                    return value as IDictionary;
                }
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
