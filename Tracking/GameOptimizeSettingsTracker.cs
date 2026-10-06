using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace ReplayLogger
{
    internal sealed class GameOptimizeSettingsTracker
    {
        private const string GameOptimizeTypeName = "GodhomeQoL.Modules.Performance.FpsBoost";
        private const string GameOptimizeModuleKey = "FpsBoost";
        private const string EnabledLabel = "Enable Game Optimize";

        private static readonly (string Label, string FieldName, bool IsInt)[] Settings =
        {
            ("Remove Blur", "fpsNoBlur", false),
            ("Remove Fog", "fpsNoFog", false),
            ("Remove Haze", "fpsNoHaze", false),
            ("Remove Vignette", "fpsNoVignette", false),
            ("Disable Bloom", "fpsNoBloom", false),
            ("Disable Color Correction", "fpsNoColorCorrection", false),
            ("Disable Film Grain", "fpsNoFilmGrain", false),
            ("Skip Neutral Brightness Pass", "fpsSkipNeutralBrightness", false),
            ("Particle Amount (%)", "fpsParticleAmount", true),
            ("Render Scale (%)", "renderScalePercent", true),
            ("Cache Mod Menu Hotkeys", "fpsCacheMenuHotkeys", false)
        };

        private bool hasInitialState;
        private string initialArenaName;
        private string currentArenaName;
        private long currentBaseUnixTime;
        private string[] initialState;
        private string[] currentState;
        private readonly List<string> changes = new();

        private Type moduleManagerType;
        private bool moduleManagerResolved;
        private PropertyInfo modulesProperty;
        private FieldInfo modulesField;

        private Type gameOptimizeType;
        private bool gameOptimizeResolved;
        private readonly FieldInfo[] settingFields = new FieldInfo[Settings.Length];
        private readonly PropertyInfo[] settingProperties = new PropertyInfo[Settings.Length];

        public bool HasData => hasInitialState
            && (string.Equals(initialState[0], OptionalFormatting.FormatToggle(true), StringComparison.Ordinal) || changes.Count > 0);

        public void Reset()
        {
            hasInitialState = false;
            initialArenaName = null;
            currentArenaName = null;
            currentBaseUnixTime = 0;
            initialState = null;
            currentState = null;
            changes.Clear();
        }

        public void StartFight(string arenaName, long baseUnixTime)
        {
            currentArenaName = ArenaNormalization.NormalizeLenient(arenaName);
            currentBaseUnixTime = baseUnixTime;

            string[] snapshot = BuildState();
            if (!hasInitialState)
            {
                hasInitialState = true;
                initialArenaName = currentArenaName;
                initialState = snapshot;
                currentState = snapshot;
                return;
            }

            LogChanges(snapshot, baseUnixTime);
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

            LogChanges(BuildState(), nowUnixTime);
        }

        public void WriteSection(StreamWriter writer)
        {
            if (writer == null || !HasData)
            {
                return;
            }

            List<string> batch = TempObjectPools.RentStringList(changes.Count + Settings.Length + 6);
            try
            {
                batch.Add("  Game Optimize:");
                if (!string.IsNullOrEmpty(initialArenaName))
                {
                    batch.Add($"    Initial Arena: {initialArenaName}");
                }

                batch.Add("    State:");
                batch.Add($"      {EnabledLabel}: {initialState[0]}");
                for (int i = 0; i < Settings.Length; i++)
                {
                    batch.Add($"      {Settings[i].Label}: {initialState[i + 1]}");
                }

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

        private void LogChanges(string[] snapshot, long now)
        {
            long delta = currentBaseUnixTime > 0 ? now - currentBaseUnixTime : 0;
            for (int i = 0; i < snapshot.Length; i++)
            {
                if (string.Equals(currentState[i], snapshot[i], StringComparison.Ordinal))
                {
                    continue;
                }

                string label = i == 0 ? EnabledLabel : Settings[i - 1].Label;
                changes.Add($"|{currentArenaName}|+{delta}|{label}: {currentState[i]} -> {snapshot[i]}");
            }

            currentState = snapshot;
        }

        private string[] BuildState()
        {
            string[] state = new string[Settings.Length + 1];
            state[0] = TryGetModuleEnabled(out bool enabled) ? OptionalFormatting.FormatToggle(enabled) : "N/A";

            Type type = GetGameOptimizeType();
            for (int i = 0; i < Settings.Length; i++)
            {
                (string _, string fieldName, bool isInt) = Settings[i];
                if (isInt)
                {
                    state[i + 1] = ReflectionSettingLookup.TryGetInt(type, fieldName, ref settingFields[i], ref settingProperties[i], out int number)
                        ? number.ToString(CultureInfo.InvariantCulture)
                        : "N/A";
                }
                else
                {
                    state[i + 1] = ReflectionSettingLookup.TryGetBool(type, fieldName, ref settingFields[i], ref settingProperties[i], out bool flag)
                        ? OptionalFormatting.FormatToggle(flag)
                        : "N/A";
                }
            }

            return state;
        }

        private bool TryGetModuleEnabled(out bool enabled)
        {
            enabled = false;
            IDictionary modules = GetModuleMap();
            if (modules == null || !modules.Contains(GameOptimizeModuleKey))
            {
                return false;
            }

            object module = modules[GameOptimizeModuleKey];
            if (module == null)
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

        private Type GetGameOptimizeType()
        {
            if (!gameOptimizeResolved)
            {
                gameOptimizeType = TypeLookup.FindType(GameOptimizeTypeName);
                gameOptimizeResolved = true;
            }

            return gameOptimizeType;
        }
    }
}
