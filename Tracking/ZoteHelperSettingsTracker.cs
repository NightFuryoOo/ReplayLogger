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

        private const string GreyPrinceZoteArena = "GG_Grey_Prince_Zote";

        private bool hasInitialState;
        private string initialArenaName;
        private string currentArenaName;
        private long currentBaseUnixTime;
        private ZoteHelperState initialState;
        private ZoteHelperState currentState;
        private bool hasCurrentState;
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
        private FieldInfo zoteDoubleSummonsField;
        private PropertyInfo zoteDoubleSummonsProperty;
        private FieldInfo zoteSummonOnlyOnStartField;
        private PropertyInfo zoteSummonOnlyOnStartProperty;

        private Type forceEnterTypeType;
        private bool forceEnterTypeResolved;
        private FieldInfo gpzEnterTypeField;
        private PropertyInfo gpzEnterTypeProperty;

        public bool HasData => hasInitialState
            && ((initialState.ModuleEnabled.HasValue && initialState.ModuleEnabled.Value) || changes.Count > 0);

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
            if (!IsGreyPrinceZoteArena(arenaName))
            {
                return;
            }

            currentArenaName = ArenaNormalization.NormalizeLenient(arenaName);
            currentBaseUnixTime = baseUnixTime;
            long now = baseUnixTime;

            ZoteHelperState snapshot = BuildState();

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

            LogFieldChange("Enable ZoteHelper", currentState.ModuleEnabled, snapshot.ModuleEnabled, now);
            LogFieldChange("Zote Boss HP", currentState.ZoteBossHp, snapshot.ZoteBossHp, now);
            LogFieldChange("Zote Immortal", currentState.ZoteImmortal, snapshot.ZoteImmortal, now);
            LogFieldChange("Spawn Flying Zotelings", currentState.SpawnFlying, snapshot.SpawnFlying, now);
            LogFieldChange("Spawn Hopping Zotelings", currentState.SpawnHopping, snapshot.SpawnHopping, now);
            LogFieldChange("Zote Flying HP", currentState.ZoteFlyingHp, snapshot.ZoteFlyingHp, now);
            LogFieldChange("Zote Hopping HP", currentState.ZoteHoppingHp, snapshot.ZoteHoppingHp, now);
            LogFieldChange("Zote Summon Limit", currentState.ZoteSummonLimit, snapshot.ZoteSummonLimit, now);
            LogFieldChange("Double Summons", currentState.DoubleSummons, snapshot.DoubleSummons, now);
            LogFieldChange("Spawn Zoteling Only On Start", currentState.SummonOnlyOnStart, snapshot.SummonOnlyOnStart, now);
            LogFieldChange("Force GPZ Enter Type", currentState.GpzEnterType, snapshot.GpzEnterType, now);
            currentState = snapshot;
        }

        public void Update(string arenaName, long nowUnixTime)
        {
            if (!IsGreyPrinceZoteArena(arenaName))
            {
                return;
            }

            if (!hasInitialState)
            {
                return;
            }

            if (!string.IsNullOrEmpty(arenaName) &&
                !string.Equals(currentArenaName, arenaName, StringComparison.Ordinal))
            {
                return;
            }

            ZoteHelperState snapshot = BuildState();

            if (!hasCurrentState)
            {
                currentState = snapshot;
                hasCurrentState = true;
                return;
            }

            long now = nowUnixTime;
            LogFieldChange("Enable ZoteHelper", currentState.ModuleEnabled, snapshot.ModuleEnabled, now);
            LogFieldChange("Zote Boss HP", currentState.ZoteBossHp, snapshot.ZoteBossHp, now);
            LogFieldChange("Zote Immortal", currentState.ZoteImmortal, snapshot.ZoteImmortal, now);
            LogFieldChange("Spawn Flying Zotelings", currentState.SpawnFlying, snapshot.SpawnFlying, now);
            LogFieldChange("Spawn Hopping Zotelings", currentState.SpawnHopping, snapshot.SpawnHopping, now);
            LogFieldChange("Zote Flying HP", currentState.ZoteFlyingHp, snapshot.ZoteFlyingHp, now);
            LogFieldChange("Zote Hopping HP", currentState.ZoteHoppingHp, snapshot.ZoteHoppingHp, now);
            LogFieldChange("Zote Summon Limit", currentState.ZoteSummonLimit, snapshot.ZoteSummonLimit, now);
            LogFieldChange("Double Summons", currentState.DoubleSummons, snapshot.DoubleSummons, now);
            LogFieldChange("Spawn Zoteling Only On Start", currentState.SummonOnlyOnStart, snapshot.SummonOnlyOnStart, now);
            LogFieldChange("Force GPZ Enter Type", currentState.GpzEnterType, snapshot.GpzEnterType, now);
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

            List<string> batch = TempObjectPools.RentStringList(changes.Count + 15);
            try
            {
                batch.Add("  Boss Manipulate:");
                if (!string.IsNullOrEmpty(initialArenaName))
                {
                    batch.Add($"    Initial Arena: {initialArenaName}");
                }
                batch.Add("    State:");
                batch.Add($"      Enable ZoteHelper: {OptionalFormatting.FormatOptionalToggle(initialState.ModuleEnabled)}");
                batch.Add($"      Zote Boss HP: {OptionalFormatting.FormatOptionalInt(initialState.ZoteBossHp)}");
                batch.Add($"      Zote Immortal: {OptionalFormatting.FormatOptionalToggle(initialState.ZoteImmortal)}");
                batch.Add($"      Spawn Flying Zotelings: {OptionalFormatting.FormatOptionalToggle(initialState.SpawnFlying)}");
                batch.Add($"      Spawn Hopping Zotelings: {OptionalFormatting.FormatOptionalToggle(initialState.SpawnHopping)}");
                batch.Add($"      Zote Flying HP: {OptionalFormatting.FormatOptionalInt(initialState.ZoteFlyingHp)}");
                batch.Add($"      Zote Hopping HP: {OptionalFormatting.FormatOptionalInt(initialState.ZoteHoppingHp)}");
                batch.Add($"      Zote Summon Limit: {OptionalFormatting.FormatOptionalInt(initialState.ZoteSummonLimit)}");
                batch.Add($"      Double Summons: {OptionalFormatting.FormatOptionalToggle(initialState.DoubleSummons)}");
                batch.Add($"      Spawn Zoteling Only On Start: {OptionalFormatting.FormatOptionalToggle(initialState.SummonOnlyOnStart)}");
                batch.Add($"      Force GPZ Enter Type: {FormatOptionalString(initialState.GpzEnterType)}");
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

        private ZoteHelperState BuildState()
        {
            return new ZoteHelperState(
                TryGetModuleEnabled("ZoteHelper", out bool enabled) ? new Optional<bool>(enabled) : Optional<bool>.None,
                TryGetZoteBossHp(out int bossHp) ? new Optional<int>(bossHp) : Optional<int>.None,
                TryGetZoteImmortal(out bool immortal) ? new Optional<bool>(immortal) : Optional<bool>.None,
                TryGetZoteSpawnFlying(out bool spawnFlying) ? new Optional<bool>(spawnFlying) : Optional<bool>.None,
                TryGetZoteSpawnHopping(out bool spawnHopping) ? new Optional<bool>(spawnHopping) : Optional<bool>.None,
                TryGetZoteFlyingHp(out int flyingHp) ? new Optional<int>(flyingHp) : Optional<int>.None,
                TryGetZoteHoppingHp(out int hoppingHp) ? new Optional<int>(hoppingHp) : Optional<int>.None,
                TryGetZoteSummonLimit(out int summonLimit) ? new Optional<int>(summonLimit) : Optional<int>.None,
                TryGetZoteDoubleSummons(out bool doubleSummons) ? new Optional<bool>(doubleSummons) : Optional<bool>.None,
                TryGetZoteSummonOnlyOnStart(out bool summonOnlyOnStart) ? new Optional<bool>(summonOnlyOnStart) : Optional<bool>.None,
                TryGetGpzEnterType(out string enterType) && !string.IsNullOrWhiteSpace(enterType) ? new Optional<string>(enterType) : Optional<string>.None);
        }

        private void LogFieldChange(string key, Optional<bool> previous, Optional<bool> current, long now)
        {
            if (previous == current)
            {
                return;
            }

            string descriptor = $"{key}: {OptionalFormatting.FormatOptionalToggle(previous)} -> {OptionalFormatting.FormatOptionalToggle(current)}";
            long delta = currentBaseUnixTime > 0 ? now - currentBaseUnixTime : 0;
            changes.Add($"|{currentArenaName}|+{delta}|{descriptor}");
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

        private void LogFieldChange(string key, Optional<string> previous, Optional<string> current, long now)
        {
            if (previous == current)
            {
                return;
            }

            string descriptor = $"{key}: {FormatOptionalString(previous)} -> {FormatOptionalString(current)}";
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

        private bool TryGetZoteBossHp(out int value)
        {
            return ReflectionSettingLookup.TryGetInt(GetZoteHelperType(), "zoteBossHp", ref zoteBossHpField, ref zoteBossHpProperty, out value);
        }

        private bool TryGetZoteImmortal(out bool enabled)
        {
            return ReflectionSettingLookup.TryGetBool(GetZoteHelperType(), "zoteImmortal", ref zoteImmortalField, ref zoteImmortalProperty, out enabled);
        }

        private bool TryGetZoteSpawnFlying(out bool enabled)
        {
            return ReflectionSettingLookup.TryGetBool(GetZoteHelperType(), "zoteSpawnFlying", ref zoteSpawnFlyingField, ref zoteSpawnFlyingProperty, out enabled);
        }

        private bool TryGetZoteSpawnHopping(out bool enabled)
        {
            return ReflectionSettingLookup.TryGetBool(GetZoteHelperType(), "zoteSpawnHopping", ref zoteSpawnHoppingField, ref zoteSpawnHoppingProperty, out enabled);
        }

        private bool TryGetZoteFlyingHp(out int value)
        {
            return ReflectionSettingLookup.TryGetInt(GetZoteHelperType(), "zoteSummonFlyingHp", ref zoteSummonFlyingHpField, ref zoteSummonFlyingHpProperty, out value);
        }

        private bool TryGetZoteHoppingHp(out int value)
        {
            return ReflectionSettingLookup.TryGetInt(GetZoteHelperType(), "zoteSummonHoppingHp", ref zoteSummonHoppingHpField, ref zoteSummonHoppingHpProperty, out value);
        }

        private bool TryGetZoteSummonLimit(out int value)
        {
            return ReflectionSettingLookup.TryGetInt(GetZoteHelperType(), "zoteSummonLimit", ref zoteSummonLimitField, ref zoteSummonLimitProperty, out value);
        }

        private bool TryGetZoteDoubleSummons(out bool enabled)
        {
            return ReflectionSettingLookup.TryGetBool(GetZoteHelperType(), "zoteDoubleSummons", ref zoteDoubleSummonsField, ref zoteDoubleSummonsProperty, out enabled);
        }

        private bool TryGetZoteSummonOnlyOnStart(out bool enabled)
        {
            return ReflectionSettingLookup.TryGetBool(GetZoteHelperType(), "zoteAttackSummonsOnlyOnStart", ref zoteSummonOnlyOnStartField, ref zoteSummonOnlyOnStartProperty, out enabled);
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
                    ? gpzEnterTypeProperty.GetCachedValue(null)
                    : gpzEnterTypeField?.GetCachedValue(null);

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

        private Type GetZoteHelperType()
        {
            if (!zoteHelperResolved)
            {
                zoteHelperType = TypeLookup.FindType("GodhomeQoL.Modules.BossChallenge.ZoteHelper");
                zoteHelperResolved = true;
            }

            return zoteHelperType;
        }

        private Type GetForceEnterTypeType()
        {
            if (!forceEnterTypeResolved)
            {
                forceEnterTypeType = TypeLookup.FindType("GodhomeQoL.Modules.BossChallenge.ForceGreyPrinceEnterType");
                forceEnterTypeResolved = true;
            }

            return forceEnterTypeType;
        }

        private static bool IsGreyPrinceZoteArena(string arenaName)
        {
            return string.Equals(arenaName, GreyPrinceZoteArena, StringComparison.Ordinal);
        }

        private static string FormatOptionalString(Optional<string> value)
        {
            return value.HasValue && !string.IsNullOrWhiteSpace(value.Value)
                ? value.Value
                : "N/A";
        }

        private readonly struct ZoteHelperState
        {
            internal ZoteHelperState(
                Optional<bool> moduleEnabled,
                Optional<int> zoteBossHp,
                Optional<bool> zoteImmortal,
                Optional<bool> spawnFlying,
                Optional<bool> spawnHopping,
                Optional<int> zoteFlyingHp,
                Optional<int> zoteHoppingHp,
                Optional<int> zoteSummonLimit,
                Optional<bool> doubleSummons,
                Optional<bool> summonOnlyOnStart,
                Optional<string> gpzEnterType)
            {
                ModuleEnabled = moduleEnabled;
                ZoteBossHp = zoteBossHp;
                ZoteImmortal = zoteImmortal;
                SpawnFlying = spawnFlying;
                SpawnHopping = spawnHopping;
                ZoteFlyingHp = zoteFlyingHp;
                ZoteHoppingHp = zoteHoppingHp;
                ZoteSummonLimit = zoteSummonLimit;
                DoubleSummons = doubleSummons;
                SummonOnlyOnStart = summonOnlyOnStart;
                GpzEnterType = gpzEnterType;
            }

            internal Optional<bool> ModuleEnabled { get; }
            internal Optional<int> ZoteBossHp { get; }
            internal Optional<bool> ZoteImmortal { get; }
            internal Optional<bool> SpawnFlying { get; }
            internal Optional<bool> SpawnHopping { get; }
            internal Optional<int> ZoteFlyingHp { get; }
            internal Optional<int> ZoteHoppingHp { get; }
            internal Optional<int> ZoteSummonLimit { get; }
            internal Optional<bool> DoubleSummons { get; }
            internal Optional<bool> SummonOnlyOnStart { get; }
            internal Optional<string> GpzEnterType { get; }
        }
    }
}
