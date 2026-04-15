using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace ReplayLogger
{
    internal sealed class CarefreeMelodyResetTracker
    {
        private bool hasInitialState;
        private string initialArenaName;
        private string currentArenaName;
        private long currentBaseUnixTime;
        private CarefreeMelodyState initialState;
        private CarefreeMelodyState currentState;
        private bool hasCurrentState;
        private readonly List<string> changes = new();

        private Type moduleManagerType;
        private bool moduleManagerResolved;
        private PropertyInfo modulesProperty;
        private FieldInfo modulesField;

        private FieldInfo hitsSinceShieldedField;
        private PropertyInfo hitsSinceShieldedProperty;

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

            CarefreeMelodyState snapshot = BuildState();

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

            LogFieldChange("Carefree Melody Reset", currentState.ModuleEnabled, snapshot.ModuleEnabled, now);
            LogFieldChange("Hits Since Shielded (In-Game)", currentState.HitsSinceShielded, snapshot.HitsSinceShielded, now);
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

            CarefreeMelodyState snapshot = BuildState();

            if (!hasCurrentState)
            {
                currentState = snapshot;
                hasCurrentState = true;
                return;
            }

            long now = nowUnixTime;
            LogFieldChange("Carefree Melody Reset", currentState.ModuleEnabled, snapshot.ModuleEnabled, now);
            LogFieldChange("Hits Since Shielded (In-Game)", currentState.HitsSinceShielded, snapshot.HitsSinceShielded, now);
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

            LogWrite.EncryptedLine(writer, "  Carefree Melody Reset:");
            if (!string.IsNullOrEmpty(initialArenaName))
            {
                LogWrite.EncryptedLine(writer, $"    Initial Arena: {initialArenaName}");
            }
            LogWrite.EncryptedLine(writer, "    State:");

            LogWrite.EncryptedLine(writer, $"      Carefree Melody Reset: {FormatOptionalToggle(initialState.ModuleEnabled)}");
            LogWrite.EncryptedLine(writer, $"      Hits Since Shielded (In-Game): {FormatOptionalInt(initialState.HitsSinceShielded)}");

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

        private CarefreeMelodyState BuildState()
        {
            bool hasEnabled = TryGetModuleEnabled(out bool enabled);
            bool hasHits = TryGetHitsSinceShielded(out int hits);

            return new CarefreeMelodyState(
                hasEnabled ? new Optional<bool>(enabled) : Optional<bool>.None,
                hasHits ? new Optional<int>(hits) : Optional<int>.None);
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

        private bool TryGetHitsSinceShielded(out int hits)
        {
            hits = 0;
            HeroController hero = HeroController.instance;
            if (hero == null)
            {
                return false;
            }

            if (hitsSinceShieldedField == null && hitsSinceShieldedProperty == null)
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                hitsSinceShieldedField = typeof(HeroController).GetField("hitsSinceShielded", flags);
                if (hitsSinceShieldedField == null)
                {
                    hitsSinceShieldedProperty = typeof(HeroController).GetProperty("hitsSinceShielded", flags);
                }
            }

            try
            {
                object raw = hitsSinceShieldedProperty != null
                    ? hitsSinceShieldedProperty.GetCachedValue(hero)
                    : hitsSinceShieldedField?.GetCachedValue(hero);

                if (raw == null)
                {
                    return false;
                }

                if (raw is int value)
                {
                    hits = value;
                    return true;
                }

                hits = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
            }

            return false;
        }

        private bool TryGetModuleEnabled(out bool enabled)
        {
            enabled = false;
            if (!TryGetModule(out object module))
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

        private bool TryGetModule(out object module)
        {
            module = null;
            IDictionary modules = GetModuleMap();
            if (modules == null)
            {
                return false;
            }

            if (!modules.Contains("CarefreeMelodyReset"))
            {
                return false;
            }

            module = modules["CarefreeMelodyReset"];
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

        private Type GetModuleType()
        {
            return FindType("GodhomeQoL.Modules.QoL.CarefreeMelodyReset");
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

        private readonly struct CarefreeMelodyState
        {
            internal CarefreeMelodyState(Optional<bool> moduleEnabled, Optional<int> hitsSinceShielded)
            {
                ModuleEnabled = moduleEnabled;
                HitsSinceShielded = hitsSinceShielded;
            }

            internal Optional<bool> ModuleEnabled { get; }
            internal Optional<int> HitsSinceShielded { get; }
        }
    }
}
