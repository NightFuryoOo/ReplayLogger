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

        private Optional<int> lastObservedMainGain = Optional<int>.None;
        private Optional<int> lastObservedReserveGain = Optional<int>.None;

        private Type settingsType;
        private bool settingsResolved;
        private PropertyInfo globalSettingsProperty;

        private PropertyInfo gearSwitcherProperty;
        private PropertyInfo gearSwitcherPresetsProperty;
        private PropertyInfo gearSwitcherLastPresetProperty;

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
            lastObservedMainGain = Optional<int>.None;
            lastObservedReserveGain = Optional<int>.None;
        }

        public void StartFight(string arenaName, long baseUnixTime)
        {
            currentArenaName = ArenaNormalization.NormalizeLenient(arenaName);
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

            LogFieldChange("Main Vessel Soul Gain (Mod Value)", currentState.MainSoulGainValue, snapshot.MainSoulGainValue, now);
            LogFieldChange("Reserve Vessel Soul Gain (Mod Value)", currentState.ReserveSoulGainValue, snapshot.ReserveSoulGainValue, now);
            LogFieldChange("Nail Damage (Mod Value)", currentState.NailDamageModValue, snapshot.NailDamageModValue, now);
            LogFieldChange("Nail Damage (Game Value)", currentState.NailDamageGameValue, snapshot.NailDamageGameValue, now);

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
            LogFieldChange("Main Vessel Soul Gain (Mod Value)", currentState.MainSoulGainValue, snapshot.MainSoulGainValue, now);
            LogFieldChange("Reserve Vessel Soul Gain (Mod Value)", currentState.ReserveSoulGainValue, snapshot.ReserveSoulGainValue, now);
            LogFieldChange("Nail Damage (Mod Value)", currentState.NailDamageModValue, snapshot.NailDamageModValue, now);
            LogFieldChange("Nail Damage (Game Value)", currentState.NailDamageGameValue, snapshot.NailDamageGameValue, now);

            currentState = snapshot;
        }

        public void RecordObservedSoulGain(string arenaName, long lastUnixTime, long nowUnixTime, int mainGained, int reserveGained)
        {
            if (!hasInitialState)
            {
                return;
            }

            string arena = ArenaNormalization.NormalizeLenient(arenaName);
            long delta = currentBaseUnixTime > 0 ? nowUnixTime - currentBaseUnixTime : 0;

            if (mainGained != 0)
            {
                Optional<int> value = new(mainGained);
                if (value != lastObservedMainGain)
                {
                    if (lastObservedMainGain.HasValue)
                    {
                        string descriptor = $"Main Vessel Soul Gain (Game Value): {OptionalFormatting.FormatOptionalInt(lastObservedMainGain)} -> {OptionalFormatting.FormatOptionalInt(value)}";
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
                        string descriptor = $"Reserve Vessel Soul Gain (Game Value): {OptionalFormatting.FormatOptionalInt(lastObservedReserveGain)} -> {OptionalFormatting.FormatOptionalInt(value)}";
                        changes.Add($"|{arena}|+{delta}|{descriptor}");
                    }

                    lastObservedReserveGain = value;
                }
            }
        }

        public void WriteSection(StreamWriter writer)
        {
            if (writer == null || !HasData)
            {
                return;
            }

            List<string> batch = TempObjectPools.RentStringList(changes.Count + 10);
            try
            {
                batch.Add("  GearSwitcher:");
                if (!string.IsNullOrEmpty(initialArenaName))
                {
                    batch.Add($"    Initial Arena: {initialArenaName}");
                }
                batch.Add("    State:");
                batch.Add($"      Main Vessel Soul Gain (Mod Value): {OptionalFormatting.FormatOptionalInt(initialState.MainSoulGainValue)}");
                batch.Add($"      Reserve Vessel Soul Gain (Mod Value): {OptionalFormatting.FormatOptionalInt(initialState.ReserveSoulGainValue)}");
                batch.Add($"      Main Vessel Soul Gain (Game Value): {OptionalFormatting.FormatOptionalInt(lastObservedMainGain)}");
                batch.Add($"      Reserve Vessel Soul Gain (Game Value): {OptionalFormatting.FormatOptionalInt(lastObservedReserveGain)}");
                batch.Add($"      Nail Damage (Mod Value): {OptionalFormatting.FormatOptionalInt(initialState.NailDamageModValue)}");
                batch.Add($"      Nail Damage (Game Value): {OptionalFormatting.FormatOptionalInt(initialState.NailDamageGameValue)}");
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
                TryGetPresetNailDamage(out int nailDamageMod) ? new Optional<int>(nailDamageMod) : Optional<int>.None,
                TryGetGameNailDamage(out int nailDamageGame) ? new Optional<int>(nailDamageGame) : Optional<int>.None);
        }

        private void LogFieldChange(string key, Optional<int> previous, Optional<int> current, long now)
        {
            if (previous == current)
            {
                return;
            }

            string descriptor = $"{key}: {OptionalFormatting.FormatOptionalInt(previous)} -> {OptionalFormatting.FormatOptionalInt(current)}";
            long delta = currentBaseUnixTime > 0 ? now - currentBaseUnixTime : 0;
            changes.Add($"|{currentArenaName}|+{delta}|{descriptor}");
        }

        private readonly struct GearSwitcherState
        {
            internal GearSwitcherState(
                Optional<int> mainSoulGainValue,
                Optional<int> reserveSoulGainValue,
                Optional<int> nailDamageModValue,
                Optional<int> nailDamageGameValue)
            {
                MainSoulGainValue = mainSoulGainValue;
                ReserveSoulGainValue = reserveSoulGainValue;
                NailDamageModValue = nailDamageModValue;
                NailDamageGameValue = nailDamageGameValue;
            }

            internal Optional<int> MainSoulGainValue { get; }
            internal Optional<int> ReserveSoulGainValue { get; }
            internal Optional<int> NailDamageModValue { get; }
            internal Optional<int> NailDamageGameValue { get; }
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

        private bool TryGetPresetNailDamage(out int value)
        {
            value = 0;

            object preset = GetActivePreset();
            if (preset == null)
            {
                return false;
            }

            try
            {
                if (!ReflectionMemberAccessCache.TryGetCachedRuntimePropertyValue(preset, "NailDamage", out object raw) ||
                    raw == null)
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

        private static bool TryGetGameNailDamage(out int value)
        {
            value = 0;
            if (PlayerData.instance == null)
            {
                return false;
            }

            value = PlayerData.instance.nailDamage;
            return true;
        }

        private object GetActivePreset()
        {
            object settings = GetGearSwitcherSettings();
            if (settings == null)
            {
                return null;
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
                return null;
            }

            if (!string.IsNullOrWhiteSpace(presetName) && presets.Contains(presetName))
            {
                return presets[presetName];
            }

            if (presets.Contains("FullGear"))
            {
                return presets["FullGear"];
            }

            foreach (DictionaryEntry entry in presets)
            {
                return entry.Value;
            }

            return null;
        }

        private bool TryGetPresetSoulGain(out int mainSoulGain, out int reserveSoulGain)
        {
            mainSoulGain = 0;
            reserveSoulGain = 0;

            object preset = GetActivePreset();
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
                settingsType = TypeLookup.FindType("GodhomeQoL.GodhomeQoL") ?? TypeLookup.FindType("GodhomeQoL.Settings.Settings");
                settingsResolved = true;
            }

            return settingsType;
        }
    }
}
