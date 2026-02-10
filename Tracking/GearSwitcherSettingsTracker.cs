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
        private Dictionary<string, string> initialState = new(StringComparer.Ordinal);
        private Dictionary<string, string> currentState = new(StringComparer.Ordinal);
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
            initialState.Clear();
            currentState.Clear();
            changes.Clear();
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

            RecordChanges(snapshot);
            currentState = new Dictionary<string, string>(snapshot, StringComparer.Ordinal);
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
                return;
            }

            RecordChanges(snapshot);
            currentState = new Dictionary<string, string>(snapshot, StringComparer.Ordinal);
        }

        public void WriteSection(StreamWriter writer)
        {
            if (writer == null || !HasData)
            {
                return;
            }

            LogWrite.EncryptedLine(writer, "  GearSwitcher:");
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

        private void RecordChanges(Dictionary<string, string> snapshot)
        {
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
        }

        private Dictionary<string, string> BuildSnapshot()
        {
            Dictionary<string, string> snapshot = new(StringComparer.Ordinal)
            {
                ["Main Vessel Soul Gain (Value)"] = TryGetMainSoulGainValue(out int mainValue)
                    ? mainValue.ToString(CultureInfo.InvariantCulture)
                    : "N/A",
                ["Reserve Vessel Soul Gain (Value)"] = TryGetReserveSoulGainValue(out int reserveValue)
                    ? reserveValue.ToString(CultureInfo.InvariantCulture)
                    : "N/A",
                ["Main Vessel Soul Gain (In-Game)"] = TryGetMainSoulGainOverride(out int mainOverride)
                    ? mainOverride.ToString(CultureInfo.InvariantCulture)
                    : "N/A",
                ["Reserve Vessel Soul Gain (In-Game)"] = TryGetReserveSoulGainOverride(out int reserveOverride)
                    ? reserveOverride.ToString(CultureInfo.InvariantCulture)
                    : "N/A"
            };

            return snapshot;
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
                object rawPreset = gearSwitcherLastPresetProperty?.GetValue(settings);
                presetName = rawPreset?.ToString();
            }
            catch
            {
            }

            IDictionary presets = null;
            try
            {
                presets = gearSwitcherPresetsProperty?.GetValue(settings) as IDictionary;
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

            const BindingFlags presetFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                Type presetType = preset.GetType();
                PropertyInfo mainProp = presetType.GetProperty("MainSoulGain", presetFlags);
                PropertyInfo reserveProp = presetType.GetProperty("ReserveSoulGain", presetFlags);

                object mainRaw = mainProp?.GetValue(preset);
                object reserveRaw = reserveProp?.GetValue(preset);

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
                return gearSwitcherProperty?.GetValue(globalSettings);
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
                return globalSettingsProperty?.GetValue(null);
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
                object raw = field?.GetValue(null);
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
                object raw = method?.Invoke(null, null);
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
