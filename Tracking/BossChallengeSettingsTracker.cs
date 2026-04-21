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
        private BossChallengeState initialState;
        private BossChallengeState currentState;
        private bool hasCurrentState;
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
            initialState = default;
            currentState = default;
            hasCurrentState = false;
            changes.Clear();
        }

        public void StartFight(string arenaName, long baseUnixTime)
        {
            currentArenaName = string.IsNullOrWhiteSpace(arenaName) ? "UnknownArena" : arenaName;
            currentBaseUnixTime = baseUnixTime;

            BossChallengeState snapshot = BuildState();

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

            long now = baseUnixTime;
            LogFieldChange("Add Lifeblood", currentState.AddLifeblood, snapshot.AddLifeblood, now);
            LogFieldChange("Lifeblood Amount", currentState.LifebloodAmount, snapshot.LifebloodAmount, now);
            LogFieldChange("Add Soul", currentState.AddSoul, snapshot.AddSoul, now);
            LogFieldChange("Soul Amount", currentState.SoulAmount, snapshot.SoulAmount, now);
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

            BossChallengeState snapshot = BuildState();

            if (!hasCurrentState)
            {
                currentState = snapshot;
                hasCurrentState = true;
                return;
            }

            long now = nowUnixTime;
            LogFieldChange("Add Lifeblood", currentState.AddLifeblood, snapshot.AddLifeblood, now);
            LogFieldChange("Lifeblood Amount", currentState.LifebloodAmount, snapshot.LifebloodAmount, now);
            LogFieldChange("Add Soul", currentState.AddSoul, snapshot.AddSoul, now);
            LogFieldChange("Soul Amount", currentState.SoulAmount, snapshot.SoulAmount, now);
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

            List<string> batch = TempObjectPools.RentStringList(changes.Count + 10);
            try
            {
                batch.Add("  Boss Challenge Settings:");
                if (!string.IsNullOrEmpty(initialArenaName))
                {
                    batch.Add($"    Initial Arena: {initialArenaName}");
                }
                batch.Add("    State:");
                batch.Add($"      Add Lifeblood: {FormatOptionalToggle(initialState.AddLifeblood)}");
                batch.Add($"      Lifeblood Amount: {FormatOptionalInt(initialState.LifebloodAmount)}");
                batch.Add($"      Add Soul: {FormatOptionalToggle(initialState.AddSoul)}");
                batch.Add($"      Soul Amount: {FormatOptionalInt(initialState.SoulAmount)}");
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

        private BossChallengeState BuildState()
        {
            bool hasAddLifeblood = TryGetModuleEnabled("AddLifeblood", out bool addLifeblood);
            bool hasAddSoul = TryGetModuleEnabled("AddSoul", out bool addSoul);

            return new BossChallengeState(
                hasAddLifeblood ? new Optional<bool>(addLifeblood) : Optional<bool>.None,
                TryGetLifebloodAmount(out int lifebloodAmount) ? new Optional<int>(lifebloodAmount) : Optional<int>.None,
                hasAddSoul ? new Optional<bool>(addSoul) : Optional<bool>.None,
                TryGetSoulAmount(out int soulAmount) ? new Optional<int>(soulAmount) : Optional<int>.None);
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

        private bool TryGetModuleEnabled(string moduleKey, out bool enabled)
        {
            enabled = false;
            if (!TryGetModule(moduleKey, out object module))
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
                    ? lifebloodAmountProperty.GetCachedValue(null)
                    : lifebloodAmountField?.GetCachedValue(null);

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
                    ? soulAmountProperty.GetCachedValue(null)
                    : soulAmountField?.GetCachedValue(null);

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

        private static string FormatOptionalToggle(Optional<bool> value)
        {
            return value.HasValue ? FormatToggle(value.Value) : "N/A";
        }

        private static string FormatOptionalInt(Optional<int> value)
        {
            return value.HasValue
                ? value.Value.ToString(CultureInfo.InvariantCulture)
                : "N/A";
        }

        private static string FormatToggle(bool value) => value ? "On" : "Off";

        private readonly struct BossChallengeState
        {
            internal BossChallengeState(
                Optional<bool> addLifeblood,
                Optional<int> lifebloodAmount,
                Optional<bool> addSoul,
                Optional<int> soulAmount)
            {
                AddLifeblood = addLifeblood;
                LifebloodAmount = lifebloodAmount;
                AddSoul = addSoul;
                SoulAmount = soulAmount;
            }

            internal Optional<bool> AddLifeblood { get; }
            internal Optional<int> LifebloodAmount { get; }
            internal Optional<bool> AddSoul { get; }
            internal Optional<int> SoulAmount { get; }
        }
    }
}
