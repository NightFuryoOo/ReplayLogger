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

        private readonly List<BossManipulateSnapshot> snapshots = new();
        private readonly Dictionary<string, List<TrackedModule>> sceneModules = new(StringComparer.Ordinal);

        private Type moduleManagerType;
        private bool moduleManagerResolved;
        private PropertyInfo modulesProperty;
        private FieldInfo modulesField;
        private bool sceneModulesBuilt;

        public bool HasData => snapshots.Count > 0;

        public void Reset()
        {
            snapshots.Clear();
        }

        public void StartFight(string arenaName, long baseUnixTime)
        {
            if (IsIgnoredArena(arenaName))
            {
                return;
            }

            CaptureSnapshotForArena(arenaName, baseUnixTime);
        }

        public void Update(string arenaName, long nowUnixTime)
        {
            if (string.IsNullOrWhiteSpace(arenaName) || IsIgnoredArena(arenaName))
            {
                return;
            }

            if (HasAnySnapshotForArena(arenaName))
            {
                return;
            }

            CaptureSnapshotForArena(arenaName, nowUnixTime);
        }

        public void WriteSection(StreamWriter writer)
        {
            if (writer == null || snapshots.Count == 0)
            {
                return;
            }

            List<string> batch = TempObjectPools.RentStringList(16 + snapshots.Count * 8);
            try
            {
                batch.Add("  Boss Manipulate:");
                for (int i = 0; i < snapshots.Count; i++)
                {
                    BossManipulateSnapshot snapshot = snapshots[i];
                    string localTime = DateTimeOffset.FromUnixTimeMilliseconds(snapshot.UnixTime)
                        .ToLocalTime()
                        .ToString("dd.MM.yyyy HH:mm:ss.fff", CultureInfo.InvariantCulture);

                    batch.Add($"    Snapshot {i + 1}:");
                    batch.Add($"      Arena: {snapshot.ArenaName}");
                    batch.Add($"      Module: {snapshot.ModuleName}");
                    batch.Add($"      Time: {localTime}");
                    batch.Add("      Settings:");

                    for (int j = 0; j < snapshot.Settings.Length; j++)
                    {
                        SettingSnapshot setting = snapshot.Settings[j];
                        batch.Add($"        {setting.Name}: {setting.Value}");
                    }
                }

                LogWrite.EncryptedLines(writer, batch);
            }
            finally
            {
                TempObjectPools.ReturnStringList(batch);
            }
        }

        private void CaptureSnapshotForArena(string arenaName, long unixTime)
        {
            if (string.IsNullOrWhiteSpace(arenaName) || IsIgnoredArena(arenaName))
            {
                return;
            }

            long timestamp = unixTime > 0
                ? unixTime
                : DateTimeOffset.Now.ToUnixTimeMilliseconds();

            if (!TryGetTrackedModulesForScene(arenaName, out List<TrackedModule> tracked))
            {
                return;
            }

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

                if (snapshots.Count > 0)
                {
                    BossManipulateSnapshot last = snapshots[snapshots.Count - 1];
                    if (string.Equals(last.ArenaName, arenaName, StringComparison.Ordinal) &&
                        string.Equals(last.ModuleName, module.ModuleName, StringComparison.Ordinal) &&
                        last.UnixTime == timestamp)
                    {
                        continue;
                    }
                }

                snapshots.Add(new BossManipulateSnapshot(
                    arenaName,
                    module.ModuleName,
                    timestamp,
                    settings.ToArray()));
            }
        }

        private bool HasAnySnapshotForArena(string arenaName)
        {
            if (string.IsNullOrWhiteSpace(arenaName) || IsIgnoredArena(arenaName))
            {
                return false;
            }

            for (int i = 0; i < snapshots.Count; i++)
            {
                if (string.Equals(snapshots[i].ArenaName, arenaName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryGetTrackedModulesForScene(string arenaName, out List<TrackedModule> trackedModules)
        {
            trackedModules = null;
            if (IsIgnoredArena(arenaName))
            {
                return false;
            }

            EnsureSceneModules(forceRebuild: false);
            if (!sceneModules.TryGetValue(arenaName, out trackedModules) || trackedModules == null || trackedModules.Count == 0)
            {
                EnsureSceneModules(forceRebuild: true);
                if (!sceneModules.TryGetValue(arenaName, out trackedModules) || trackedModules == null || trackedModules.Count == 0)
                {
                    return TryGetExplicitTrackedModulesForScene(arenaName, out trackedModules);
                }
            }

            return true;
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
                moduleManagerType = FindType(ModuleManagerTypeName);
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

                builder.Append(current);
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

        private readonly struct BossManipulateSnapshot
        {
            internal BossManipulateSnapshot(string arenaName, string moduleName, long unixTime, SettingSnapshot[] settings)
            {
                ArenaName = arenaName;
                ModuleName = moduleName;
                UnixTime = unixTime;
                Settings = settings ?? Array.Empty<SettingSnapshot>();
            }

            internal string ArenaName { get; }
            internal string ModuleName { get; }
            internal long UnixTime { get; }
            internal SettingSnapshot[] Settings { get; }
        }
    }
}
