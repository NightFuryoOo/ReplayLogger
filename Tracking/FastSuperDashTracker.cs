using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

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
        }

        public void StartFight(string arenaName, long baseUnixTime)
        {
            currentArenaName = ArenaNormalization.NormalizeLenient(arenaName);
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
                batch.Add($"      Fast Super Dash: {OptionalFormatting.FormatOptionalToggle(initialState.FastSuperDash)}");
                batch.Add($"      Instant Super Dash: {OptionalFormatting.FormatOptionalToggle(initialState.InstantSuperDash)}");
                batch.Add($"      Allow in All Scenes: {OptionalFormatting.FormatOptionalToggle(initialState.AllowEverywhere)}");
                batch.Add($"      Speed Multiplier: {OptionalFormatting.FormatOptionalFloat(initialState.SpeedMultiplier)}");
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
            bool hasInstant = TryGetInstantSuperDash(out bool instant);
            bool hasEverywhere = TryGetFastSuperDashEverywhere(out bool everywhere);
            bool hasMultiplier = TryGetSpeedMultiplier(out float multiplier);

            return new FastSuperDashState(
                hasEnabled ? new Optional<bool>(enabled) : Optional<bool>.None,
                hasInstant ? new Optional<bool>(instant) : Optional<bool>.None,
                hasEverywhere ? new Optional<bool>(everywhere) : Optional<bool>.None,
                hasMultiplier ? new Optional<float>(OptionalFormatting.NormalizeFloat(multiplier)) : Optional<float>.None);
        }

        private void LogSpeedMultiplierChanges(FastSuperDashState snapshot, long now)
        {
            Optional<float> speedValue = snapshot.SpeedMultiplier;
            if (speedValue == lastLoggedSpeedValue)
            {
                return;
            }

            string descriptor = !lastLoggedSpeedValue.HasValue
                ? $"Speed Multiplier: {OptionalFormatting.FormatOptionalFloat(speedValue)}"
                : $"Speed Multiplier: {OptionalFormatting.FormatOptionalFloat(lastLoggedSpeedValue)} -> {OptionalFormatting.FormatOptionalFloat(speedValue)}";
            long delta = currentBaseUnixTime > 0 ? now - currentBaseUnixTime : 0;
            changes.Add($"|{currentArenaName}|+{delta}|{descriptor}");
            lastLoggedSpeedValue = speedValue;
        }

        private void UpdateSpeedBaseline(FastSuperDashState snapshot)
        {
            lastLoggedSpeedValue = snapshot.SpeedMultiplier;
        }

        private void LogFieldChange(string key, Optional<bool> previous, Optional<bool> current, long now)
        {
            if (previous == current)
            {
                return;
            }

            string descriptor = $"{key}: {OptionalFormatting.FormatOptionalToggle(previous)} -> {OptionalFormatting.FormatOptionalToggle(current)}";
            long delta = currentBaseUnixTime > 0 ? now - currentBaseUnixTime : 0;
            changes.Add($"|{currentArenaName}|+{delta}|{descriptor}");
        }

        private void LogFieldChange(string key, Optional<float> previous, Optional<float> current, long now)
        {
            if (previous == current)
            {
                return;
            }

            string descriptor = $"{key}: {OptionalFormatting.FormatOptionalFloat(previous)} -> {OptionalFormatting.FormatOptionalFloat(current)}";
            long delta = currentBaseUnixTime > 0 ? now - currentBaseUnixTime : 0;
            changes.Add($"|{currentArenaName}|+{delta}|{descriptor}");
        }

        private readonly struct FastSuperDashState
        {
            internal FastSuperDashState(
                Optional<bool> fastSuperDash,
                Optional<bool> instantSuperDash,
                Optional<bool> allowEverywhere,
                Optional<float> speedMultiplier)
            {
                FastSuperDash = fastSuperDash;
                InstantSuperDash = instantSuperDash;
                AllowEverywhere = allowEverywhere;
                SpeedMultiplier = speedMultiplier;
            }

            internal Optional<bool> FastSuperDash { get; }
            internal Optional<bool> InstantSuperDash { get; }
            internal Optional<bool> AllowEverywhere { get; }
            internal Optional<float> SpeedMultiplier { get; }
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

        private bool TryGetInstantSuperDash(out bool enabled)
        {
            return ReflectionSettingLookup.TryGetBool(GetFastSuperDashType(), "instantSuperDash", "InstantSuperDash", ref instantSuperDashField, ref instantSuperDashProperty, out enabled);
        }

        private bool TryGetFastSuperDashEverywhere(out bool enabled)
        {
            return ReflectionSettingLookup.TryGetBool(GetFastSuperDashType(), "fastSuperDashEverywhere", "FastSuperDashEverywhere", ref fastSuperDashEverywhereField, ref fastSuperDashEverywhereProperty, out enabled);
        }

        private bool TryGetSpeedMultiplier(out float multiplier)
        {
            return ReflectionSettingLookup.TryGetFloat(GetFastSuperDashType(), "fastSuperDashSpeedMultiplier", "FastSuperDashSpeedMultiplier", ref speedMultiplierField, ref speedMultiplierProperty, out multiplier);
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
                moduleManagerType = TypeLookup.FindType("GodhomeQoL.ModuleManager");
                moduleManagerResolved = true;
            }

            return moduleManagerType;
        }

        private Type GetFastSuperDashType()
        {
            if (!fastSuperDashResolved)
            {
                fastSuperDashType = TypeLookup.FindType("GodhomeQoL.Modules.QoL.FastSuperDash");
                fastSuperDashResolved = true;
            }

            return fastSuperDashType;
        }
    }
}
