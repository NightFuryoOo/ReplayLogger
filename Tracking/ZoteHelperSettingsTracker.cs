using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace ReplayLogger
{
    internal sealed class ZoteHelperSettingsTracker
    {
        private const string ZoteSceneName = "GG_Grey_Prince_Zote";

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

        private Type zoteHelperType;
        private bool zoteHelperResolved;
        private FieldInfo zoteBossHpField;
        private PropertyInfo zoteBossHpProperty;
        private FieldInfo zoteImmortalField;
        private PropertyInfo zoteImmortalProperty;
        private FieldInfo zoteSpawnFlyingField;
        private PropertyInfo zoteSpawnFlyingProperty;
        private FieldInfo zoteSpawnHoppingField;
        private PropertyInfo zoteSpawnHoppingProperty;
        private FieldInfo zoteSummonFlyingHpField;
        private PropertyInfo zoteSummonFlyingHpProperty;
        private FieldInfo zoteSummonHoppingHpField;
        private PropertyInfo zoteSummonHoppingHpProperty;
        private FieldInfo zoteSummonLimitField;
        private PropertyInfo zoteSummonLimitProperty;
        private FieldInfo moduleActiveField;

        private Type forceEnterTypeType;
        private bool forceEnterTypeResolved;
        private FieldInfo gpzEnterTypeField;
        private PropertyInfo gpzEnterTypeProperty;

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

            LogWrite.EncryptedLine(writer, "  ZoteHelper:");
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
            Dictionary<string, string> snapshot = new(StringComparer.Ordinal)
            {
                ["Enable ZoteHelper"] = TryGetModuleEnabled("ZoteHelper", out bool enabled) ? FormatToggle(enabled) : "N/A",
                ["Zote Boss HP"] = TryGetZoteBossHp(out int bossHp) ? bossHp.ToString(CultureInfo.InvariantCulture) : "N/A",
                ["Zote Immortal"] = TryGetZoteImmortal(out bool immortal) ? FormatToggle(immortal) : "N/A",
                ["Spawn Flying Zotelings"] = TryGetZoteSpawnFlying(out bool spawnFlying) ? FormatToggle(spawnFlying) : "N/A",
                ["Spawn Hopping Zotelings"] = TryGetZoteSpawnHopping(out bool spawnHopping) ? FormatToggle(spawnHopping) : "N/A",
                ["Zote Flying HP"] = TryGetZoteFlyingHp(out int flyingHp) ? flyingHp.ToString(CultureInfo.InvariantCulture) : "N/A",
                ["Zote Hopping HP"] = TryGetZoteHoppingHp(out int hoppingHp) ? hoppingHp.ToString(CultureInfo.InvariantCulture) : "N/A",
                ["Zote Summon Limit"] = TryGetZoteSummonLimit(out int summonLimit) ? summonLimit.ToString(CultureInfo.InvariantCulture) : "N/A",
                ["Zote Summon Limit (In-Game)"] = TryGetSummonLimitInGame(out int summonLimitInGame)
                    ? summonLimitInGame.ToString(CultureInfo.InvariantCulture)
                    : "N/A",
                ["Force GPZ Enter Type"] = TryGetGpzEnterType(out string enterType) ? enterType : "N/A"
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

        private bool TryGetZoteBossHp(out int value)
        {
            return TryGetIntSetting("zoteBossHp", ref zoteBossHpField, ref zoteBossHpProperty, out value);
        }

        private bool TryGetZoteImmortal(out bool enabled)
        {
            return TryGetBoolSetting("zoteImmortal", ref zoteImmortalField, ref zoteImmortalProperty, out enabled);
        }

        private bool TryGetZoteSpawnFlying(out bool enabled)
        {
            return TryGetBoolSetting("zoteSpawnFlying", ref zoteSpawnFlyingField, ref zoteSpawnFlyingProperty, out enabled);
        }

        private bool TryGetZoteSpawnHopping(out bool enabled)
        {
            return TryGetBoolSetting("zoteSpawnHopping", ref zoteSpawnHoppingField, ref zoteSpawnHoppingProperty, out enabled);
        }

        private bool TryGetZoteFlyingHp(out int value)
        {
            return TryGetIntSetting("zoteSummonFlyingHp", ref zoteSummonFlyingHpField, ref zoteSummonFlyingHpProperty, out value);
        }

        private bool TryGetZoteHoppingHp(out int value)
        {
            return TryGetIntSetting("zoteSummonHoppingHp", ref zoteSummonHoppingHpField, ref zoteSummonHoppingHpProperty, out value);
        }

        private bool TryGetZoteSummonLimit(out int value)
        {
            return TryGetIntSetting("zoteSummonLimit", ref zoteSummonLimitField, ref zoteSummonLimitProperty, out value);
        }

        private bool TryGetSummonLimitInGame(out int value)
        {
            value = 0;
            if (!TryGetZoteSummonLimit(out value))
            {
                return false;
            }

            if (!IsZoteSceneActive())
            {
                return false;
            }

            if (TryGetModuleActive(out bool active) && !active)
            {
                return false;
            }

            return true;
        }

        private bool TryGetModuleActive(out bool active)
        {
            active = false;
            Type type = GetZoteHelperType();
            if (type == null)
            {
                return false;
            }

            if (moduleActiveField == null)
            {
                const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                moduleActiveField = type.GetField("moduleActive", flags);
            }

            try
            {
                object raw = moduleActiveField?.GetValue(null);
                if (raw is bool flag)
                {
                    active = flag;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private bool TryGetGpzEnterType(out string value)
        {
            value = null;
            Type type = GetForceEnterTypeType();
            if (type == null)
            {
                return false;
            }

            if (gpzEnterTypeField == null && gpzEnterTypeProperty == null)
            {
                const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                gpzEnterTypeField = type.GetField("gpzEnterType", flags);
                if (gpzEnterTypeField == null)
                {
                    gpzEnterTypeProperty = type.GetProperty("gpzEnterType", flags);
                }
            }

            try
            {
                object raw = gpzEnterTypeProperty != null
                    ? gpzEnterTypeProperty.GetValue(null)
                    : gpzEnterTypeField?.GetValue(null);

                if (raw == null)
                {
                    return false;
                }

                value = raw.ToString();
                return !string.IsNullOrWhiteSpace(value);
            }
            catch
            {
            }

            return false;
        }

        private bool TryGetBoolSetting(string fieldName, ref FieldInfo field, ref PropertyInfo property, out bool enabled)
        {
            enabled = false;
            Type type = GetZoteHelperType();
            if (type == null)
            {
                return false;
            }

            if (field == null && property == null)
            {
                const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                field = type.GetField(fieldName, flags);
                if (field == null)
                {
                    property = type.GetProperty(fieldName, flags);
                }
            }

            try
            {
                object raw = property != null
                    ? property.GetValue(null)
                    : field?.GetValue(null);

                if (raw is bool flag)
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

        private bool TryGetIntSetting(string fieldName, ref FieldInfo field, ref PropertyInfo property, out int value)
        {
            value = 0;
            Type type = GetZoteHelperType();
            if (type == null)
            {
                return false;
            }

            if (field == null && property == null)
            {
                const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                field = type.GetField(fieldName, flags);
                if (field == null)
                {
                    property = type.GetProperty(fieldName, flags);
                }
            }

            try
            {
                object raw = property != null
                    ? property.GetValue(null)
                    : field?.GetValue(null);

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

        private bool IsZoteSceneActive()
        {
            string sceneName = GameManager.instance?.sceneName;
            if (string.IsNullOrEmpty(sceneName))
            {
                sceneName = currentArenaName;
            }

            return string.Equals(sceneName, ZoteSceneName, StringComparison.Ordinal);
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

        private Type GetZoteHelperType()
        {
            if (!zoteHelperResolved)
            {
                zoteHelperType = FindType("GodhomeQoL.Modules.BossChallenge.ZoteHelper");
                zoteHelperResolved = true;
            }

            return zoteHelperType;
        }

        private Type GetForceEnterTypeType()
        {
            if (!forceEnterTypeResolved)
            {
                forceEnterTypeType = FindType("GodhomeQoL.Modules.BossChallenge.ForceGreyPrinceEnterType");
                forceEnterTypeResolved = true;
            }

            return forceEnterTypeType;
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
