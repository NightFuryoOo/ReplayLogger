using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace ReplayLogger
{
    internal sealed class GodhomeQolBossManipulateTracker
    {
        private const string ModuleManagerTypeName = "GodhomeQoL.ModuleManager";
        private const string BossManipulateNamespace = "GodhomeQoL.Modules.BossChallenge";
        private const string CollectorPhasesNamespace = "GodhomeQoL.Modules.CollectorPhases";
        private const string WorkshopSceneName = "GG_Workshop";
        private static readonly string[] CollectorScenes = { "GG_Collector", "GG_Collector_V" };
        private static readonly string[] GreyPrinceZoteScenes = { "GG_Grey_Prince_Zote" };

        private static readonly string[] ExcludedSettingNameTokens =
        {
            "BeforeP5",
            "HasStoredStateBeforeP5"
        };

        private const string ZoteHelperModuleName = "ZoteHelper";

        private readonly List<ModuleTrackingState> trackedStates = new();

        private readonly HashSet<string> unresolvedArenasThisFight = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<TrackedModule>> sceneModules = new(StringComparer.Ordinal);

        private Type moduleManagerType;
        private bool moduleManagerResolved;
        private PropertyInfo modulesProperty;
        private FieldInfo modulesField;
        private bool sceneModulesBuilt;

        public bool HasData => trackedStates.Count > 0;

        public void Reset()
        {
            trackedStates.Clear();
            unresolvedArenasThisFight.Clear();
        }

        public void StartFight(string arenaName, long baseUnixTime)
        {
            if (IsIgnoredArena(arenaName))
            {
                return;
            }

            CaptureOrUpdateArena(arenaName, baseUnixTime);
        }

        public void Update(string arenaName, long nowUnixTime)
        {
            if (string.IsNullOrWhiteSpace(arenaName) || IsIgnoredArena(arenaName))
            {
                return;
            }

            CaptureOrUpdateArena(arenaName, nowUnixTime);
        }

        public void WriteSection(StreamWriter writer)
        {
            if (writer == null || trackedStates.Count == 0)
            {
                return;
            }

            int hint = 16;
            for (int i = 0; i < trackedStates.Count; i++)
            {
                hint += trackedStates[i].InitialSettings.Count + Math.Max(1, trackedStates[i].Changes.Count) + 4;
            }

            List<string> batch = TempObjectPools.RentStringList(hint);
            try
            {
                batch.Add("  Boss Manipulate:");
                for (int i = 0; i < trackedStates.Count; i++)
                {
                    ModuleTrackingState state = trackedStates[i];
                    batch.Add($"    Initial Arena: {state.ArenaName}");
                    batch.Add($"    Module: {state.ModuleName}");
                    batch.Add("    State:");
                    for (int j = 0; j < state.InitialSettings.Count; j++)
                    {
                        SettingSnapshot setting = state.InitialSettings[j];
                        batch.Add($"      {setting.Name}: {setting.Value}");
                    }

                    batch.Add("    Changes:");
                    if (state.Changes.Count == 0)
                    {
                        batch.Add("      (none)");
                    }
                    else
                    {
                        for (int j = 0; j < state.Changes.Count; j++)
                        {
                            batch.Add($"      {state.Changes[j]}");
                        }
                    }
                }

                LogWrite.EncryptedLines(writer, batch);
            }
            finally
            {
                TempObjectPools.ReturnStringList(batch);
            }
        }

        private void CaptureOrUpdateArena(string arenaName, long unixTime)
        {
            if (string.IsNullOrWhiteSpace(arenaName) || IsIgnoredArena(arenaName))
            {
                return;
            }

            if (!TryGetTrackedModulesForScene(arenaName, out List<TrackedModule> tracked))
            {
                return;
            }

            long timestamp = unixTime > 0
                ? unixTime
                : DateTimeOffset.Now.ToUnixTimeMilliseconds();

            for (int i = 0; i < tracked.Count; i++)
            {
                TrackedModule module = tracked[i];
                if (!TryIsModuleEnabled(module.Instance, out bool enabled) || !enabled)
                {
                    continue;
                }

                List<SettingSnapshot> settings = CaptureSettings(module.SettingsFields);
                if (settings.Count == 0)
                {
                    continue;
                }

                ModuleTrackingState state = FindState(arenaName, module.ModuleName);
                if (state == null)
                {
                    trackedStates.Add(new ModuleTrackingState(arenaName, module.ModuleName, timestamp, settings));
                    continue;
                }

                DiffSettings(state, settings, timestamp);
            }
        }

        private ModuleTrackingState FindState(string arenaName, string moduleName)
        {
            for (int i = 0; i < trackedStates.Count; i++)
            {
                ModuleTrackingState state = trackedStates[i];
                if (string.Equals(state.ArenaName, arenaName, StringComparison.Ordinal) &&
                    string.Equals(state.ModuleName, moduleName, StringComparison.Ordinal))
                {
                    return state;
                }
            }

            return null;
        }

        private static void DiffSettings(ModuleTrackingState state, List<SettingSnapshot> newSettings, long now)
        {
            List<SettingSnapshot> previous = state.CurrentSettings;
            int count = Math.Min(previous.Count, newSettings.Count);
            for (int i = 0; i < count; i++)
            {
                if (string.Equals(previous[i].Value, newSettings[i].Value, StringComparison.Ordinal))
                {
                    continue;
                }

                long delta = state.BaseUnixTime > 0 ? now - state.BaseUnixTime : 0;
                state.Changes.Add($"|{state.ArenaName}|+{delta}|{newSettings[i].Name}: {previous[i].Value} -> {newSettings[i].Value}");
            }

            state.CurrentSettings = newSettings;
        }

        private bool TryGetTrackedModulesForScene(string arenaName, out List<TrackedModule> trackedModules)
        {
            trackedModules = null;
            if (IsIgnoredArena(arenaName) || unresolvedArenasThisFight.Contains(arenaName))
            {
                return false;
            }

            EnsureSceneModules(forceRebuild: false);
            if (sceneModules.TryGetValue(arenaName, out trackedModules) && trackedModules != null && trackedModules.Count > 0)
            {
                return true;
            }

            EnsureSceneModules(forceRebuild: true);
            if (sceneModules.TryGetValue(arenaName, out trackedModules) && trackedModules != null && trackedModules.Count > 0)
            {
                return true;
            }

            if (TryGetExplicitTrackedModulesForScene(arenaName, out trackedModules))
            {
                return true;
            }

            unresolvedArenasThisFight.Add(arenaName);
            return false;
        }

        private void EnsureSceneModules(bool forceRebuild)
        {
            if (sceneModulesBuilt && !forceRebuild)
            {
                return;
            }

            sceneModules.Clear();

            IDictionary modules = GetModuleMap();
            if (modules == null || modules.Count == 0)
            {
                sceneModulesBuilt = true;
                return;
            }

            foreach (DictionaryEntry entry in modules)
            {
                object moduleInstance = entry.Value;
                if (moduleInstance == null)
                {
                    continue;
                }

                Type moduleType = moduleInstance.GetType();
                if (!IsBossManipulateModule(moduleType))
                {
                    continue;
                }

                string moduleName = moduleType.Name ?? string.Empty;
                if (string.Equals(moduleName, ZoteHelperModuleName, StringComparison.Ordinal))
                {
                    continue;
                }

                FieldInfo[] settings = ResolveSettingFields(moduleType);
                if (settings.Length == 0)
                {
                    continue;
                }

                List<string> scenes = ResolveSceneNames(moduleType);
                if (scenes.Count == 0)
                {
                    scenes = ResolveFallbackScenes(moduleName);
                }
                if (scenes.Count == 0)
                {
                    continue;
                }

                TrackedModule tracked = new(moduleName, moduleInstance, settings);
                for (int i = 0; i < scenes.Count; i++)
                {
                    string sceneName = scenes[i];
                    if (string.IsNullOrWhiteSpace(sceneName))
                    {
                        continue;
                    }

                    if (!sceneModules.TryGetValue(sceneName, out List<TrackedModule> list))
                    {
                        list = new List<TrackedModule>(1);
                        sceneModules[sceneName] = list;
                    }

                    list.Add(tracked);
                }
            }

            sceneModulesBuilt = true;
        }

        private bool TryGetExplicitTrackedModulesForScene(string arenaName, out List<TrackedModule> trackedModules)
        {
            trackedModules = null;
            if (string.IsNullOrWhiteSpace(arenaName))
            {
                return false;
            }

            string[] fallbackNameTokens;
            if (IsCollectorArena(arenaName))
            {
                fallbackNameTokens = new[] { "Collector" };
            }
            else if (IsGreyPrinceZoteArena(arenaName))
            {
                fallbackNameTokens = new[] { "Zote", "GreyPrince" };
            }
            else
            {
                return false;
            }

            IDictionary modules = GetModuleMap();
            if (modules == null || modules.Count == 0)
            {
                return false;
            }

            List<TrackedModule> found = new();
            foreach (DictionaryEntry entry in modules)
            {
                object module = entry.Value;
                if (module == null)
                {
                    continue;
                }

                Type foundType = module.GetType();
                if (foundType == null || !IsBossManipulateModule(foundType))
                {
                    continue;
                }

                string moduleName = foundType.Name ?? string.Empty;
                if (string.Equals(moduleName, ZoteHelperModuleName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!ContainsAnyToken(moduleName, fallbackNameTokens))
                {
                    continue;
                }

                FieldInfo[] settings = ResolveSettingFields(foundType);
                if (settings.Length == 0)
                {
                    continue;
                }

                found.Add(new TrackedModule(moduleName, module, settings));
            }

            if (found.Count == 0)
            {
                return false;
            }

            trackedModules = found;

            return true;
        }

        private static bool IsIgnoredArena(string arenaName)
        {
            return string.Equals(arenaName, WorkshopSceneName, StringComparison.Ordinal);
        }

        private static List<SettingSnapshot> CaptureSettings(FieldInfo[] fields)
        {
            List<SettingSnapshot> result = new(fields.Length);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (field == null)
                {
                    continue;
                }

                object raw = null;
                try
                {
                    raw = field.GetCachedValue(null);
                }
                catch
                {
                }

                string settingName = FormatSettingName(field.Name);
                string value = FormatSettingValue(raw);
                result.Add(new SettingSnapshot(settingName, value));
            }

            result.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.Ordinal));
            return result;
        }

        private static FieldInfo[] ResolveSettingFields(Type moduleType)
        {
            if (moduleType == null)
            {
                return Array.Empty<FieldInfo>();
            }

            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
            FieldInfo[] allFields = moduleType.GetFields(flags);
            List<FieldInfo> result = new();

            for (int i = 0; i < allFields.Length; i++)
            {
                FieldInfo field = allFields[i];
                if (field == null || !field.IsStatic || field.IsLiteral || field.IsInitOnly)
                {
                    continue;
                }

                if (!HasLocalSettingAttribute(field))
                {
                    continue;
                }

                if (IsExcludedSettingName(field.Name))
                {
                    continue;
                }

                result.Add(field);
            }

            return result.ToArray();
        }

        private static List<string> ResolveSceneNames(Type moduleType)
        {
            List<string> result = new();
            if (moduleType == null)
            {
                return result;
            }

            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
            FieldInfo[] allFields = moduleType.GetFields(flags);
            for (int i = 0; i < allFields.Length; i++)
            {
                FieldInfo field = allFields[i];
                if (field == null || field.FieldType != typeof(string))
                {
                    continue;
                }

                string sceneName = null;
                try
                {
                    if (field.IsLiteral)
                    {
                        sceneName = field.GetRawConstantValue() as string;
                    }
                    else if (field.IsStatic)
                    {
                        sceneName = field.GetCachedValue(null) as string;
                    }
                }
                catch
                {
                }

                if (string.IsNullOrWhiteSpace(sceneName))
                {
                    continue;
                }

                if (!sceneName.StartsWith("GG_", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!result.Contains(sceneName))
                {
                    result.Add(sceneName);
                }
            }

            return result;
        }

        private static List<string> ResolveFallbackScenes(string moduleName)
        {
            List<string> result = new();
            if (string.IsNullOrWhiteSpace(moduleName))
            {
                return result;
            }

            string[] fallbackScenes = null;
            if (moduleName.IndexOf("Collector", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                fallbackScenes = CollectorScenes;
            }
            else if (moduleName.IndexOf("Zote", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     moduleName.IndexOf("GreyPrince", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                fallbackScenes = GreyPrinceZoteScenes;
            }

            if (fallbackScenes == null || fallbackScenes.Length == 0)
            {
                return result;
            }

            for (int i = 0; i < fallbackScenes.Length; i++)
            {
                string scene = fallbackScenes[i];
                if (!string.IsNullOrWhiteSpace(scene))
                {
                    result.Add(scene);
                }
            }

            return result;
        }

        private static bool HasLocalSettingAttribute(FieldInfo field)
        {
            if (field == null)
            {
                return false;
            }

            try
            {
                object[] attrs = field.GetCustomAttributes(false);
                for (int i = 0; i < attrs.Length; i++)
                {
                    object attr = attrs[i];
                    if (attr == null)
                    {
                        continue;
                    }

                    string attributeName = attr.GetType().Name;
                    if (string.Equals(attributeName, "LocalSettingAttribute", StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool IsExcludedSettingName(string settingName)
        {
            if (string.IsNullOrEmpty(settingName))
            {
                return true;
            }

            for (int i = 0; i < ExcludedSettingNameTokens.Length; i++)
            {
                if (settingName.IndexOf(ExcludedSettingNameTokens[i], StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsBossManipulateModule(Type moduleType)
        {
            if (moduleType == null)
            {
                return false;
            }

            string ns = moduleType.Namespace ?? string.Empty;
            if (ns.StartsWith(BossManipulateNamespace, StringComparison.Ordinal))
            {
                return true;
            }

            return ns.StartsWith(CollectorPhasesNamespace, StringComparison.Ordinal);
        }

        private static bool IsCollectorArena(string arenaName)
        {
            return string.Equals(arenaName, "GG_Collector", StringComparison.Ordinal) ||
                   string.Equals(arenaName, "GG_Collector_V", StringComparison.Ordinal);
        }

        private static bool IsGreyPrinceZoteArena(string arenaName)
        {
            return string.Equals(arenaName, "GG_Grey_Prince_Zote", StringComparison.Ordinal);
        }

        private static bool ContainsAnyToken(string text, string[] tokens)
        {
            if (string.IsNullOrWhiteSpace(text) || tokens == null || tokens.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i];
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                if (text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryIsModuleEnabled(object moduleInstance, out bool enabled)
        {
            enabled = false;
            if (moduleInstance == null)
            {
                return false;
            }

            try
            {
                if (ReflectionMemberAccessCache.TryGetCachedRuntimeBoolProperty(moduleInstance, "Enabled", out bool flag))
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
                moduleManagerType = TypeLookup.FindType(ModuleManagerTypeName);
                moduleManagerResolved = true;
            }

            return moduleManagerType;
        }

        private static string FormatSettingName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return "UnknownSetting";
            }

            string text = rawName.Replace('_', ' ');
            var builder = new System.Text.StringBuilder(text.Length + 12);
            for (int i = 0; i < text.Length; i++)
            {
                char current = text[i];
                if (i > 0 && char.IsUpper(current) && !char.IsWhiteSpace(text[i - 1]) && !char.IsUpper(text[i - 1]))
                {
                    builder.Append(' ');
                }

                builder.Append(i == 0 ? char.ToUpperInvariant(current) : current);
            }

            return builder.ToString();
        }

        private static string FormatSettingValue(object value)
        {
            if (value == null)
            {
                return "null";
            }

            if (value is bool flag)
            {
                return flag ? "On" : "Off";
            }

            if (value is float f)
            {
                return f.ToString("0.###", CultureInfo.InvariantCulture);
            }

            if (value is double d)
            {
                return d.ToString("0.###", CultureInfo.InvariantCulture);
            }

            if (value is decimal dec)
            {
                return dec.ToString("0.###", CultureInfo.InvariantCulture);
            }

            if (value is IFormattable formattable)
            {
                return formattable.ToString(null, CultureInfo.InvariantCulture);
            }

            if (value is IEnumerable enumerable && value is not string)
            {
                List<string> items = new();
                foreach (object item in enumerable)
                {
                    items.Add(item?.ToString() ?? "null");
                }

                return "[" + string.Join(", ", items) + "]";
            }

            return value.ToString();
        }

        private readonly struct TrackedModule
        {
            internal TrackedModule(string moduleName, object instance, FieldInfo[] settingsFields)
            {
                ModuleName = moduleName;
                Instance = instance;
                SettingsFields = settingsFields;
            }

            internal string ModuleName { get; }
            internal object Instance { get; }
            internal FieldInfo[] SettingsFields { get; }
        }

        private readonly struct SettingSnapshot
        {
            internal SettingSnapshot(string name, string value)
            {
                Name = name;
                Value = value;
            }

            internal string Name { get; }
            internal string Value { get; }
        }

        private sealed class ModuleTrackingState
        {
            internal ModuleTrackingState(string arenaName, string moduleName, long baseUnixTime, List<SettingSnapshot> initialSettings)
            {
                ArenaName = arenaName;
                ModuleName = moduleName;
                BaseUnixTime = baseUnixTime;
                InitialSettings = initialSettings;
                CurrentSettings = initialSettings;
            }

            internal string ArenaName { get; }
            internal string ModuleName { get; }
            internal long BaseUnixTime { get; }
            internal List<SettingSnapshot> InitialSettings { get; }
            internal List<SettingSnapshot> CurrentSettings { get; set; }
            internal List<string> Changes { get; } = new();
        }
    }
}
