using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace ReplayLogger
{
    internal sealed class GearSwitcherSettingsTracker
    {
        private bool hasInitialState;
        private string initialArenaName;
        private string currentArenaName;
        private long currentBaseUnixTime;
        private GearSwitcherState initialState;
        private GearSwitcherState currentState;
        private bool hasCurrentState;
        private readonly List<string> changes = new();

        private Type settingsType;
        private bool settingsResolved;
        private PropertyInfo globalSettingsProperty;

        private PropertyInfo gearSwitcherProperty;
        private PropertyInfo gearSwitcherPresetsProperty;
        private PropertyInfo gearSwitcherLastPresetProperty;

        private Type gearSwitcherType;
        private bool gearSwitcherResolved;
        private FieldInfo mainSoulGainOverrideField;
        private FieldInfo reserveSoulGainOverrideField;
        private MethodInfo getMainSoulGainMethod;
        private MethodInfo getReserveSoulGainMethod;

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
        }

        public void StartFight(string arenaName, long baseUnixTime)
        {
            currentArenaName = string.IsNullOrWhiteSpace(arenaName) ? "UnknownArena" : arenaName;
            currentBaseUnixTime = baseUnixTime;
            long now = baseUnixTime;

            GearSwitcherState snapshot = BuildState();

            if (!hasInitialState)
            {
                hasInitialState = true;
                initialArenaName = currentArenaName;
                initialState = snapshot;
                currentState = snapshot;
                hasCurrentState = true;
                return;
            }

            if (!hasCurrentState)
            {
                currentState = snapshot;
                hasCurrentState = true;
                return;
            }

            LogFieldChange("Main Vessel Soul Gain (Value)", currentState.MainSoulGainValue, snapshot.MainSoulGainValue, now);
            LogFieldChange("Reserve Vessel Soul Gain (Value)", currentState.ReserveSoulGainValue, snapshot.ReserveSoulGainValue, now);
            LogFieldChange("Main Vessel Soul Gain (In-Game)", currentState.MainSoulGainInGame, snapshot.MainSoulGainInGame, now);
            LogFieldChange("Reserve Vessel Soul Gain (In-Game)", currentState.ReserveSoulGainInGame, snapshot.ReserveSoulGainInGame, now);

            currentState = snapshot;
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

            GearSwitcherState snapshot = BuildState();
            if (!hasCurrentState)
            {
                currentState = snapshot;
                hasCurrentState = true;
                return;
            }

            long now = nowUnixTime;
            LogFieldChange("Main Vessel Soul Gain (Value)", currentState.MainSoulGainValue, snapshot.MainSoulGainValue, now);
            LogFieldChange("Reserve Vessel Soul Gain (Value)", currentState.ReserveSoulGainValue, snapshot.ReserveSoulGainValue, now);
            LogFieldChange("Main Vessel Soul Gain (In-Game)", currentState.MainSoulGainInGame, snapshot.MainSoulGainInGame, now);
            LogFieldChange("Reserve Vessel Soul Gain (In-Game)", currentState.ReserveSoulGainInGame, snapshot.ReserveSoulGainInGame, now);

            currentState = snapshot;
        }

        public void WriteSection(StreamWriter writer)
        {
            if (writer == null || !HasData)
            {
                return;
            }

            List<string> batch = TempObjectPools.RentStringList(changes.Count + 8);
            try
            {
                batch.Add("  GearSwitcher:");
                if (!string.IsNullOrEmpty(initialArenaName))
                {
                    batch.Add($"    Initial Arena: {initialArenaName}");
                }
                batch.Add("    State:");
                batch.Add($"      Main Vessel Soul Gain (Value): {FormatOptionalInt(initialState.MainSoulGainValue)}");
                batch.Add($"      Reserve Vessel Soul Gain (Value): {FormatOptionalInt(initialState.ReserveSoulGainValue)}");
                batch.Add($"      Main Vessel Soul Gain (In-Game): {FormatOptionalInt(initialState.MainSoulGainInGame)}");
                batch.Add($"      Reserve Vessel Soul Gain (In-Game): {FormatOptionalInt(initialState.ReserveSoulGainInGame)}");
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

        private GearSwitcherState BuildState()
        {
            return new GearSwitcherState(
                TryGetMainSoulGainValue(out int mainValue) ? new Optional<int>(mainValue) : Optional<int>.None,
                TryGetReserveSoulGainValue(out int reserveValue) ? new Optional<int>(reserveValue) : Optional<int>.None,
                TryGetMainSoulGainOverride(out int mainOverride) ? new Optional<int>(mainOverride) : Optional<int>.None,
                TryGetReserveSoulGainOverride(out int reserveOverride) ? new Optional<int>(reserveOverride) : Optional<int>.None);
        }

        private void LogFieldChange(string key, Optional<int> previous, Optional<int> current, long now)
        {
            if (previous == current)
            {
                return;
            }

            string descriptor = $"{key}: {FormatOptionalInt(previous)} -> {FormatOptionalInt(current)}";
            long delta = currentBaseUnixTime > 0 ? now - currentBaseUnixTime : 0;
            changes.Add($"|{currentArenaName}|+{delta}|{descriptor}");
        }

        private static string FormatOptionalInt(Optional<int> value)
        {
            return value.HasValue
                ? value.Value.ToString(CultureInfo.InvariantCulture)
                : "N/A";
        }

        private readonly struct GearSwitcherState
        {
            internal GearSwitcherState(
                Optional<int> mainSoulGainValue,
                Optional<int> reserveSoulGainValue,
                Optional<int> mainSoulGainInGame,
                Optional<int> reserveSoulGainInGame)
            {
                MainSoulGainValue = mainSoulGainValue;
                ReserveSoulGainValue = reserveSoulGainValue;
                MainSoulGainInGame = mainSoulGainInGame;
                ReserveSoulGainInGame = reserveSoulGainInGame;
            }

            internal Optional<int> MainSoulGainValue { get; }
            internal Optional<int> ReserveSoulGainValue { get; }
            internal Optional<int> MainSoulGainInGame { get; }
            internal Optional<int> ReserveSoulGainInGame { get; }
        }

        private bool TryGetMainSoulGainValue(out int value)
        {
            value = 0;
            return TryGetPresetSoulGain(out int main, out _)
                ? (value = main) >= 0
                : false;
        }

        private bool TryGetReserveSoulGainValue(out int value)
        {
            value = 0;
            return TryGetPresetSoulGain(out _, out int reserve)
                ? (value = reserve) >= 0
                : false;
        }

        private bool TryGetPresetSoulGain(out int mainSoulGain, out int reserveSoulGain)
        {
            mainSoulGain = 0;
            reserveSoulGain = 0;

            object settings = GetGearSwitcherSettings();
            if (settings == null)
            {
                return false;
            }

            if (gearSwitcherLastPresetProperty == null || gearSwitcherPresetsProperty == null)
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                Type type = settings.GetType();
                gearSwitcherLastPresetProperty ??= type.GetProperty("LastPreset", flags);
                gearSwitcherPresetsProperty ??= type.GetProperty("Presets", flags);
            }

            string presetName = null;
            try
            {
                object rawPreset = gearSwitcherLastPresetProperty?.GetCachedValue(settings);
                presetName = rawPreset?.ToString();
            }
            catch
            {
            }

            IDictionary presets = null;
            try
            {
                presets = gearSwitcherPresetsProperty?.GetCachedValue(settings) as IDictionary;
            }
            catch
            {
            }

            if (presets == null || presets.Count == 0)
            {
                return false;
            }

            object preset = null;
            if (!string.IsNullOrWhiteSpace(presetName) && presets.Contains(presetName))
            {
                preset = presets[presetName];
            }

            if (preset == null)
            {
                if (presets.Contains("FullGear"))
                {
                    preset = presets["FullGear"];
                }
                else
                {
                    foreach (DictionaryEntry entry in presets)
                    {
                        preset = entry.Value;
                        break;
                    }
                }
            }

            if (preset == null)
            {
                return false;
            }

            try
            {
                if (!ReflectionMemberAccessCache.TryGetCachedRuntimePropertyValue(preset, "MainSoulGain", out object mainRaw) ||
                    !ReflectionMemberAccessCache.TryGetCachedRuntimePropertyValue(preset, "ReserveSoulGain", out object reserveRaw))
                {
                    return false;
                }

                if (mainRaw == null || reserveRaw == null)
                {
                    return false;
                }

                mainSoulGain = Convert.ToInt32(mainRaw, CultureInfo.InvariantCulture);
                reserveSoulGain = Convert.ToInt32(reserveRaw, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
            }

            return false;
        }

        private object GetGearSwitcherSettings()
        {
            object globalSettings = GetGlobalSettings();
            if (globalSettings == null)
            {
                return null;
            }

            if (gearSwitcherProperty == null)
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                gearSwitcherProperty = globalSettings.GetType().GetProperty("GearSwitcher", flags);
            }

            try
            {
                return gearSwitcherProperty?.GetCachedValue(globalSettings);
            }
            catch
            {
            }

            return null;
        }

        private object GetGlobalSettings()
        {
            Type type = GetSettingsType();
            if (type == null)
            {
                return null;
            }

            if (globalSettingsProperty == null)
            {
                const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                globalSettingsProperty = type.GetProperty("GlobalSettings", flags);
            }

            try
            {
                return globalSettingsProperty?.GetCachedValue(null);
            }
            catch
            {
            }

            return null;
        }

        private Type GetSettingsType()
        {
            if (!settingsResolved)
            {
                settingsType = FindType("GodhomeQoL.Settings.Settings");
                settingsResolved = true;
            }

            return settingsType;
        }

        private bool TryGetMainSoulGainOverride(out int value)
        {
            value = 0;
            return TryGetSoulGainOverride("mainSoulGainOverride", ref mainSoulGainOverrideField, ref getMainSoulGainMethod, out value);
        }

        private bool TryGetReserveSoulGainOverride(out int value)
        {
            value = 0;
            return TryGetSoulGainOverride("reserveSoulGainOverride", ref reserveSoulGainOverrideField, ref getReserveSoulGainMethod, out value);
        }

        private bool TryGetSoulGainOverride(string fieldName, ref FieldInfo field, ref MethodInfo method, out int value)
        {
            value = 0;
            Type type = GetGearSwitcherType();
            if (type == null)
            {
                return false;
            }

            if (field == null)
            {
                const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                field = type.GetField(fieldName, flags);
            }

            try
            {
                object raw = field?.GetCachedValue(null);
                if (raw != null)
                {
                    value = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
                    return true;
                }
            }
            catch
            {
            }

            if (method == null)
            {
                const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                string methodName = fieldName.IndexOf("main", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "GetMainSoulGain"
                    : "GetReserveSoulGain";
                method = type.GetMethod(methodName, flags);
            }

            try
            {
                object raw = method?.InvokeCached(null);
                if (raw == null)
                {
                    return false;
                }

                value = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
            }

            return false;
        }

        private Type GetGearSwitcherType()
        {
            if (!gearSwitcherResolved)
            {
                gearSwitcherType = FindType("GodhomeQoL.Modules.Tools.GearSwitcher");
                gearSwitcherResolved = true;
            }

            return gearSwitcherType;
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
    }
}
