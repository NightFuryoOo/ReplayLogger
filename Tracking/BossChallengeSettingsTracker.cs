using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace ReplayLogger
{
    internal sealed class BossChallengeSettingsTracker
    {
        private bool hasInitialState;
        private string initialArenaName;
        private string currentArenaName;
        private long currentBaseUnixTime;
        private Dictionary<string, string> initialState = new(StringComparer.Ordinal);
        private Dictionary<string, string> currentState = new(StringComparer.Ordinal);
        private readonly List<string> changes = new();

        private Type moduleManagerType;
        private bool moduleManagerResolved;
        private PropertyInfo modulesProperty;
        private FieldInfo modulesField;

        private Type addLifebloodType;
        private bool addLifebloodResolved;
        private FieldInfo lifebloodAmountField;
        private PropertyInfo lifebloodAmountProperty;

        private Type addSoulType;
        private bool addSoulResolved;
        private FieldInfo soulAmountField;
        private PropertyInfo soulAmountProperty;

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

            LogWrite.EncryptedLine(writer, "  Boss Challenge Settings:");
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
            bool hasHalveAscended = TryGetModuleEnabled("HalveDamageHoGAscendedOrAbove", out bool halveAscended);
            bool hasHalveAttuned = TryGetModuleEnabled("HalveDamageHoGAttuned", out bool halveAttuned);
            bool hasAddLifeblood = TryGetModuleEnabled("AddLifeblood", out bool addLifeblood);
            bool hasAddSoul = TryGetModuleEnabled("AddSoul", out bool addSoul);

            Dictionary<string, string> snapshot = new(StringComparer.Ordinal)
            {
                ["Halve Damage [HoG Ascended]"] = hasHalveAscended ? FormatToggle(halveAscended) : "N/A",
                ["Halve Damage [HoG Attuned]"] = hasHalveAttuned ? FormatToggle(halveAttuned) : "N/A",
                ["Add Lifeblood"] = hasAddLifeblood ? FormatToggle(addLifeblood) : "N/A",
                ["Lifeblood Amount"] = TryGetLifebloodAmount(out int lifebloodAmount)
                    ? lifebloodAmount.ToString(CultureInfo.InvariantCulture)
                    : "N/A",
                ["Add Soul"] = hasAddSoul ? FormatToggle(addSoul) : "N/A",
                ["Soul Amount"] = TryGetSoulAmount(out int soulAmount)
                    ? soulAmount.ToString(CultureInfo.InvariantCulture)
                    : "N/A"
            };

            return snapshot;
        }

        private bool TryGetModuleEnabled(string moduleKey, out bool enabled)
        {
            enabled = false;
            if (!TryGetModule(moduleKey, out object module))
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

        private bool TryGetModule(string moduleKey, out object module)
        {
            module = null;
            IDictionary modules = GetModuleMap();
            if (modules == null)
            {
                return false;
            }

            if (!modules.Contains(moduleKey))
            {
                return false;
            }

            module = modules[moduleKey];
            return module != null;
        }

        private bool TryGetLifebloodAmount(out int value)
        {
            value = 0;
            Type type = GetAddLifebloodType();
            if (type == null)
            {
                return false;
            }

            if (lifebloodAmountField == null && lifebloodAmountProperty == null)
            {
                const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                lifebloodAmountField = type.GetField("lifebloodAmount", flags);
                if (lifebloodAmountField == null)
                {
                    lifebloodAmountProperty = type.GetProperty("lifebloodAmount", flags);
                }
            }

            try
            {
                object raw = lifebloodAmountProperty != null
                    ? lifebloodAmountProperty.GetValue(null)
                    : lifebloodAmountField?.GetValue(null);

                if (raw == null)
                {
                    return false;
                }

                if (raw is int intValue)
                {
                    value = intValue;
                    return true;
                }

                value = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
            }

            return false;
        }

        private bool TryGetSoulAmount(out int value)
        {
            value = 0;
            Type type = GetAddSoulType();
            if (type == null)
            {
                return false;
            }

            if (soulAmountField == null && soulAmountProperty == null)
            {
                const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                soulAmountField = type.GetField("soulAmount", flags);
                if (soulAmountField == null)
                {
                    soulAmountProperty = type.GetProperty("soulAmount", flags);
                }
            }

            try
            {
                object raw = soulAmountProperty != null
                    ? soulAmountProperty.GetValue(null)
                    : soulAmountField?.GetValue(null);

                if (raw == null)
                {
                    return false;
                }

                if (raw is int intValue)
                {
                    value = intValue;
                    return true;
                }

                value = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
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

        private Type GetAddLifebloodType()
        {
            if (!addLifebloodResolved)
            {
                addLifebloodType = FindType("GodhomeQoL.Modules.BossChallenge.AddLifeblood");
                addLifebloodResolved = true;
            }

            return addLifebloodType;
        }

        private Type GetAddSoulType()
        {
            if (!addSoulResolved)
            {
                addSoulType = FindType("GodhomeQoL.Modules.BossChallenge.AddSoul");
                addSoulResolved = true;
            }

            return addSoulType;
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
