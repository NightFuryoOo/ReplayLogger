using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace ReplayLogger
{
        internal sealed class GodhomeQolCollectorPhasesTracker
        {
            private bool hasInitialState;
            private string initialArenaName;
            private string currentArenaName;
            private long currentBaseUnixTime;
            private long initialFillDeadline;
            private Dictionary<string, string> initialState = new(StringComparer.Ordinal);
            private Dictionary<string, string> currentState = new(StringComparer.Ordinal);
            private readonly List<string> changes = new();
            private readonly HashSet<string> pendingInitialKeys = new(StringComparer.Ordinal);
            private const int InitialFillWindowMs = 2000;
            private long lastUpdateTime;
            private const int UpdateThrottleMs = 1000;

            private Type moduleManagerType;
            private bool moduleManagerResolved;
            private PropertyInfo modulesProperty;
            private FieldInfo modulesField;

            private Type collectorPhasesType;
            private bool collectorPhasesResolved;
            private FieldInfo collectorPhaseField;
            private PropertyInfo collectorPhaseProperty;
            private FieldInfo collectorImmortalField;
            private PropertyInfo collectorImmortalProperty;
            private FieldInfo ignoreInitialJarLimitField;
            private PropertyInfo ignoreInitialJarLimitProperty;
            private FieldInfo useCustomPhase2ThresholdField;
            private PropertyInfo useCustomPhase2ThresholdProperty;
            private FieldInfo customPhase2ThresholdField;
            private PropertyInfo customPhase2ThresholdProperty;
            private FieldInfo useMaxHpField;
            private PropertyInfo useMaxHpProperty;
            private FieldInfo collectorMaxHpField;
            private PropertyInfo collectorMaxHpProperty;
            private FieldInfo buzzerHpField;
            private PropertyInfo buzzerHpProperty;
            private FieldInfo rollerHpField;
            private PropertyInfo rollerHpProperty;
            private FieldInfo spitterHpField;
            private PropertyInfo spitterHpProperty;
            private FieldInfo spawnBuzzerField;
            private PropertyInfo spawnBuzzerProperty;
            private FieldInfo spawnRollerField;
            private PropertyInfo spawnRollerProperty;
            private FieldInfo spawnSpitterField;
            private PropertyInfo spawnSpitterProperty;
            private FieldInfo disableSummonLimitField;
            private PropertyInfo disableSummonLimitProperty;
            private FieldInfo customSummonLimitField;
            private PropertyInfo customSummonLimitProperty;

            private PlayMakerFSM collectorControlFsm;
            private int collectorControlFsmId;
            private IntCompare collectorResummonCompare;
            private IntCompare collectorSummonLimitCompare;
            private IntCompare collectorEnemyCountLimitCompare;
            private PlayMakerFSM collectorPhaseControlFsm;
            private int collectorPhaseControlFsmId;
            private IntCompare collectorPhase2ThresholdCompare;
            private long lastControlFsmSearchTime;
            private long lastPhaseFsmSearchTime;
            private const int FsmSearchThrottleMs = 1000;

            public bool HasData => hasInitialState || changes.Count > 0;

            public void Reset()
            {
                hasInitialState = false;
                initialArenaName = null;
                currentArenaName = null;
                currentBaseUnixTime = 0;
                initialFillDeadline = 0;
                initialState.Clear();
                currentState.Clear();
                changes.Clear();
                pendingInitialKeys.Clear();
                lastUpdateTime = 0;
                lastControlFsmSearchTime = 0;
                lastPhaseFsmSearchTime = 0;
            }

            public void StartFight(string arenaName, long baseUnixTime)
            {
                currentArenaName = string.IsNullOrWhiteSpace(arenaName) ? "UnknownArena" : arenaName;
                currentBaseUnixTime = baseUnixTime;
                collectorControlFsm = null;
                collectorControlFsmId = 0;
                collectorResummonCompare = null;
                collectorSummonLimitCompare = null;
                collectorEnemyCountLimitCompare = null;
                collectorPhaseControlFsm = null;
                collectorPhaseControlFsmId = 0;
                collectorPhase2ThresholdCompare = null;
                lastControlFsmSearchTime = 0;
                lastPhaseFsmSearchTime = 0;
                lastUpdateTime = 0;

                Dictionary<string, string> snapshot = BuildSnapshot();

                if (!hasInitialState)
                {
                    hasInitialState = true;
                    initialArenaName = currentArenaName;
                    initialState = new Dictionary<string, string>(snapshot, StringComparer.Ordinal);
                    currentState = new Dictionary<string, string>(snapshot, StringComparer.Ordinal);
                    pendingInitialKeys.Clear();
                    foreach (var entry in initialState)
                    {
                        if (string.Equals(entry.Value, "N/A", StringComparison.Ordinal))
                        {
                            pendingInitialKeys.Add(entry.Key);
                        }
                    }
                    initialFillDeadline = DateTimeOffset.Now.ToUnixTimeMilliseconds() + InitialFillWindowMs;
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

                long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                if (lastUpdateTime > 0 && now - lastUpdateTime < UpdateThrottleMs)
                {
                    return;
                }

                lastUpdateTime = now;

                Dictionary<string, string> snapshot = BuildSnapshot();
                if (snapshot.Count == 0)
                {
                    return;
                }

                if (pendingInitialKeys.Count > 0 && now <= initialFillDeadline)
                {
                    foreach (var entry in snapshot)
                    {
                        if (!pendingInitialKeys.Contains(entry.Key))
                        {
                            continue;
                        }

                        if (string.Equals(entry.Value, "N/A", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        initialState[entry.Key] = entry.Value;
                        currentState[entry.Key] = entry.Value;
                        pendingInitialKeys.Remove(entry.Key);
                    }
                }
                else if (pendingInitialKeys.Count > 0 && now > initialFillDeadline)
                {
                    pendingInitialKeys.Clear();
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

                LogWrite.EncryptedLine(writer, "  Collector Phases:");
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
                    ["Enabled"] = TryGetCollectorPhasesEnabled(out bool enabled) ? FormatToggle(enabled) : "N/A",
                    ["Phase"] = TryGetCollectorPhase(out int phase) ? phase.ToString(CultureInfo.InvariantCulture) : "N/A",
                    ["Collector Immortal"] = TryGetCollectorImmortal(out bool immortal) ? FormatToggle(immortal) : "N/A",
                    ["Ignore Initial Jar Limits (Toggle)"] = TryGetIgnoreInitialJarLimitToggle(out bool ignoreInit) ? FormatToggle(ignoreInit) : "N/A",
                    ["Initial Jar Limit (In-Game)"] = TryGetInitialJarLimitInGame(out int limit) ? limit.ToString(CultureInfo.InvariantCulture) : "N/A",
                    ["Use Custom Phase 2 HP (Toggle)"] = TryGetUseCustomPhase2Threshold(out bool useCustomPhase2) ? FormatToggle(useCustomPhase2) : "N/A",
                    ["Phase 2 HP Threshold (Value)"] = TryGetCustomPhase2Threshold(out int phase2Value) ? phase2Value.ToString(CultureInfo.InvariantCulture) : "N/A",
                    ["Phase 2 HP Threshold (In-Game)"] = TryGetPhase2ThresholdInGame(out int phase2InGame) ? phase2InGame.ToString(CultureInfo.InvariantCulture) : "N/A",
                    ["Use Max HP (Toggle)"] = TryGetUseMaxHp(out bool useMax) ? FormatToggle(useMax) : "N/A",
                    ["Collector Max HP (Value)"] = TryGetCollectorMaxHp(out int maxHp) ? maxHp.ToString(CultureInfo.InvariantCulture) : "N/A",
                    ["Squit HP (Value)"] = TryGetBuzzerHp(out int buzzerHp) ? buzzerHp.ToString(CultureInfo.InvariantCulture) : "N/A",
                    ["Spawn Squit (Toggle)"] = TryGetSpawnBuzzer(out bool spawnBuzzer) ? FormatToggle(spawnBuzzer) : "N/A",
                    ["Baldur HP (Value)"] = TryGetRollerHp(out int rollerHp) ? rollerHp.ToString(CultureInfo.InvariantCulture) : "N/A",
                    ["Spawn Baldur (Toggle)"] = TryGetSpawnRoller(out bool spawnRoller) ? FormatToggle(spawnRoller) : "N/A",
                    ["Aspid HP (Value)"] = TryGetSpitterHp(out int spitterHp) ? spitterHp.ToString(CultureInfo.InvariantCulture) : "N/A",
                    ["Spawn Aspid (Toggle)"] = TryGetSpawnSpitter(out bool spawnSpitter) ? FormatToggle(spawnSpitter) : "N/A"
                    ,
                    ["Use Custom Summon Limit (Toggle)"] = TryGetDisableSummonLimit(out bool disableSummonLimit) ? FormatToggle(disableSummonLimit) : "N/A",
                    ["Custom Summon Limit (Value)"] = TryGetCustomSummonLimit(out int customSummonLimit) ? customSummonLimit.ToString(CultureInfo.InvariantCulture) : "N/A",
                    ["Summon Limit (In-Game / Summon?)"] = TryGetSummonLimitInGame(out int summonLimit) ? summonLimit.ToString(CultureInfo.InvariantCulture) : "N/A",
                    ["Summon Limit (In-Game / Enemy Count)"] = TryGetEnemyCountLimitInGame(out int enemyCountLimit) ? enemyCountLimit.ToString(CultureInfo.InvariantCulture) : "N/A"
                };

                return snapshot;
            }

            private bool TryGetCollectorPhasesEnabled(out bool enabled)
            {
                enabled = false;
                IDictionary modules = GetModuleMap();
                if (modules == null)
                {
                    return false;
                }

                if (!modules.Contains("CollectorPhases"))
                {
                    return false;
                }

                object module = modules["CollectorPhases"];
                if (module == null)
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

            private bool TryGetCollectorPhase(out int phase)
            {
                phase = 0;
                Type type = GetCollectorPhasesType();
                if (type == null)
                {
                    return false;
                }

                if (collectorPhaseField == null && collectorPhaseProperty == null)
                {
                    const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                    collectorPhaseField = type.GetField("collectorPhase", flags)
                        ?? type.GetField("CollectorPhase", flags);
                    if (collectorPhaseField == null)
                    {
                        collectorPhaseProperty = type.GetProperty("collectorPhase", flags)
                            ?? type.GetProperty("CollectorPhase", flags);
                    }
                }

                try
                {
                    object raw = collectorPhaseProperty != null
                        ? collectorPhaseProperty.GetValue(null)
                        : collectorPhaseField?.GetValue(null);

                    if (raw == null)
                    {
                        return false;
                    }

                    if (raw is int value)
                    {
                        phase = value;
                        return true;
                    }

                    phase = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
                    return true;
                }
                catch
                {
                }

                return false;
            }

            private bool TryGetCollectorImmortal(out bool immortal)
            {
                immortal = false;
                Type type = GetCollectorPhasesType();
                if (type == null)
                {
                    return false;
                }

                if (collectorImmortalField == null && collectorImmortalProperty == null)
                {
                    const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                    collectorImmortalField = type.GetField("CollectorImmortal", flags)
                        ?? type.GetField("collectorImmortal", flags);
                    if (collectorImmortalField == null)
                    {
                        collectorImmortalProperty = type.GetProperty("CollectorImmortal", flags)
                            ?? type.GetProperty("collectorImmortal", flags);
                    }
                }

                try
                {
                    object raw = collectorImmortalProperty != null
                        ? collectorImmortalProperty.GetValue(null)
                        : collectorImmortalField?.GetValue(null);

                    if (raw is bool flag)
                    {
                        immortal = flag;
                        return true;
                    }
                }
                catch
                {
                }

                return false;
            }

            private bool TryGetIgnoreInitialJarLimitToggle(out bool enabled)
            {
                enabled = false;
                Type type = GetCollectorPhasesType();
                if (type == null)
                {
                    return false;
                }

                if (ignoreInitialJarLimitField == null && ignoreInitialJarLimitProperty == null)
                {
                    const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                    ignoreInitialJarLimitField = type.GetField("IgnoreInitialJarLimit", flags)
                        ?? type.GetField("ignoreInitialJarLimit", flags);
                    if (ignoreInitialJarLimitField == null)
                    {
                        ignoreInitialJarLimitProperty = type.GetProperty("IgnoreInitialJarLimit", flags)
                            ?? type.GetProperty("ignoreInitialJarLimit", flags);
                    }
                }

                try
                {
                    object raw = ignoreInitialJarLimitProperty != null
                        ? ignoreInitialJarLimitProperty.GetValue(null)
                        : ignoreInitialJarLimitField?.GetValue(null);

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

            private bool TryGetUseCustomPhase2Threshold(out bool enabled)
            {
                enabled = false;
                Type type = GetCollectorPhasesType();
                if (type == null)
                {
                    return false;
                }

                if (useCustomPhase2ThresholdField == null && useCustomPhase2ThresholdProperty == null)
                {
                    const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                    useCustomPhase2ThresholdField = type.GetField("UseCustomPhase2Threshold", flags)
                        ?? type.GetField("useCustomPhase2Threshold", flags);
                    if (useCustomPhase2ThresholdField == null)
                    {
                        useCustomPhase2ThresholdProperty = type.GetProperty("UseCustomPhase2Threshold", flags)
                            ?? type.GetProperty("useCustomPhase2Threshold", flags);
                    }
                }

                try
                {
                    object raw = useCustomPhase2ThresholdProperty != null
                        ? useCustomPhase2ThresholdProperty.GetValue(null)
                        : useCustomPhase2ThresholdField?.GetValue(null);

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

            private bool TryGetCustomPhase2Threshold(out int threshold)
            {
                threshold = 0;
                Type type = GetCollectorPhasesType();
                if (type == null)
                {
                    return false;
                }

                if (customPhase2ThresholdField == null && customPhase2ThresholdProperty == null)
                {
                    const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                    customPhase2ThresholdField = type.GetField("CustomPhase2Threshold", flags)
                        ?? type.GetField("customPhase2Threshold", flags);
                    if (customPhase2ThresholdField == null)
                    {
                        customPhase2ThresholdProperty = type.GetProperty("CustomPhase2Threshold", flags)
                            ?? type.GetProperty("customPhase2Threshold", flags);
                    }
                }

                try
                {
                    object raw = customPhase2ThresholdProperty != null
                        ? customPhase2ThresholdProperty.GetValue(null)
                        : customPhase2ThresholdField?.GetValue(null);

                    if (raw == null)
                    {
                        return false;
                    }

                    threshold = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
                    return true;
                }
                catch
                {
                }

                return false;
            }

            private bool TryGetUseMaxHp(out bool enabled)
            {
                enabled = false;
                Type type = GetCollectorPhasesType();
                if (type == null)
                {
                    return false;
                }

                if (useMaxHpField == null && useMaxHpProperty == null)
                {
                    const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                    useMaxHpField = type.GetField("UseMaxHP", flags)
                        ?? type.GetField("UseMaxHp", flags)
                        ?? type.GetField("useMaxHP", flags)
                        ?? type.GetField("useMaxHp", flags);
                    if (useMaxHpField == null)
                    {
                        useMaxHpProperty = type.GetProperty("UseMaxHP", flags)
                            ?? type.GetProperty("UseMaxHp", flags)
                            ?? type.GetProperty("useMaxHP", flags)
                            ?? type.GetProperty("useMaxHp", flags);
                    }
                }

                try
                {
                    object raw = useMaxHpProperty != null
                        ? useMaxHpProperty.GetValue(null)
                        : useMaxHpField?.GetValue(null);

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

            private bool TryGetCollectorMaxHp(out int maxHp)
            {
                maxHp = 0;
                Type type = GetCollectorPhasesType();
                if (type == null)
                {
                    return false;
                }

                if (collectorMaxHpField == null && collectorMaxHpProperty == null)
                {
                    const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                    collectorMaxHpField = type.GetField("collectorMaxHP", flags)
                        ?? type.GetField("CollectorMaxHP", flags)
                        ?? type.GetField("collectorMaxHp", flags)
                        ?? type.GetField("CollectorMaxHp", flags);
                    if (collectorMaxHpField == null)
                    {
                        collectorMaxHpProperty = type.GetProperty("collectorMaxHP", flags)
                            ?? type.GetProperty("CollectorMaxHP", flags)
                            ?? type.GetProperty("collectorMaxHp", flags)
                            ?? type.GetProperty("CollectorMaxHp", flags);
                    }
                }

                try
                {
                    object raw = collectorMaxHpProperty != null
                        ? collectorMaxHpProperty.GetValue(null)
                        : collectorMaxHpField?.GetValue(null);

                    if (raw == null)
                    {
                        return false;
                    }

                    maxHp = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
                    return true;
                }
                catch
                {
                }

                return false;
            }

            private bool TryGetBuzzerHp(out int hp)
            {
                hp = 0;
                return TryGetIntSetting("buzzerHP", "BuzzerHP", ref buzzerHpField, ref buzzerHpProperty, out hp);
            }

            private bool TryGetRollerHp(out int hp)
            {
                hp = 0;
                return TryGetIntSetting("rollerHP", "RollerHP", ref rollerHpField, ref rollerHpProperty, out hp);
            }

            private bool TryGetSpitterHp(out int hp)
            {
                hp = 0;
                return TryGetIntSetting("spitterHP", "SpitterHP", ref spitterHpField, ref spitterHpProperty, out hp);
            }

            private bool TryGetSpawnBuzzer(out bool enabled)
            {
                enabled = false;
                return TryGetBoolSetting("spawnBuzzer", "SpawnBuzzer", ref spawnBuzzerField, ref spawnBuzzerProperty, out enabled);
            }

            private bool TryGetSpawnRoller(out bool enabled)
            {
                enabled = false;
                return TryGetBoolSetting("spawnRoller", "SpawnRoller", ref spawnRollerField, ref spawnRollerProperty, out enabled);
            }

            private bool TryGetSpawnSpitter(out bool enabled)
            {
                enabled = false;
                return TryGetBoolSetting("spawnSpitter", "SpawnSpitter", ref spawnSpitterField, ref spawnSpitterProperty, out enabled);
            }

            private bool TryGetDisableSummonLimit(out bool enabled)
            {
                enabled = false;
                Type type = GetCollectorPhasesType();
                if (type == null)
                {
                    return false;
                }

                if (disableSummonLimitField == null && disableSummonLimitProperty == null)
                {
                    const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                    disableSummonLimitField = type.GetField("DisableSummonLimit", flags)
                        ?? type.GetField("disableSummonLimit", flags);
                    if (disableSummonLimitField == null)
                    {
                        disableSummonLimitProperty = type.GetProperty("DisableSummonLimit", flags)
                            ?? type.GetProperty("disableSummonLimit", flags);
                    }
                }

                try
                {
                    object raw = disableSummonLimitProperty != null
                        ? disableSummonLimitProperty.GetValue(null)
                        : disableSummonLimitField?.GetValue(null);

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

            private bool TryGetCustomSummonLimit(out int value)
            {
                value = 0;
                Type type = GetCollectorPhasesType();
                if (type == null)
                {
                    return false;
                }

                if (customSummonLimitField == null && customSummonLimitProperty == null)
                {
                    const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                    customSummonLimitField = type.GetField("CustomSummonLimit", flags)
                        ?? type.GetField("customSummonLimit", flags);
                    if (customSummonLimitField == null)
                    {
                        customSummonLimitProperty = type.GetProperty("CustomSummonLimit", flags)
                            ?? type.GetProperty("customSummonLimit", flags);
                    }
                }

                try
                {
                    object raw = customSummonLimitProperty != null
                        ? customSummonLimitProperty.GetValue(null)
                        : customSummonLimitField?.GetValue(null);

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

            private bool TryGetBoolSetting(string primaryName, string altName, ref FieldInfo field, ref PropertyInfo property, out bool enabled)
            {
                enabled = false;
                Type type = GetCollectorPhasesType();
                if (type == null)
                {
                    return false;
                }

                if (field == null && property == null)
                {
                    const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                    field = type.GetField(primaryName, flags) ?? type.GetField(altName, flags);
                    if (field == null)
                    {
                        property = type.GetProperty(primaryName, flags) ?? type.GetProperty(altName, flags);
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

            private bool TryGetIntSetting(string primaryName, string altName, ref FieldInfo field, ref PropertyInfo property, out int value)
            {
                value = 0;
                Type type = GetCollectorPhasesType();
                if (type == null)
                {
                    return false;
                }

                if (field == null && property == null)
                {
                    const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                    field = type.GetField(primaryName, flags) ?? type.GetField(altName, flags);
                    if (field == null)
                    {
                        property = type.GetProperty(primaryName, flags) ?? type.GetProperty(altName, flags);
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

            private bool TryGetInitialJarLimitInGame(out int limit)
            {
                limit = 0;
                if (!TryResolveResummonCompare(out IntCompare compare))
                {
                    return false;
                }

                try
                {
                    if (compare.integer2 == null)
                    {
                        return false;
                    }

                    limit = compare.integer2.Value;
                    return true;
                }
                catch
                {
                }

                return false;
            }

            private bool TryGetPhase2ThresholdInGame(out int threshold)
            {
                threshold = 0;
                if (!TryResolvePhase2ThresholdCompare(out IntCompare compare))
                {
                    return false;
                }

                try
                {
                    if (compare.integer2 == null)
                    {
                        return false;
                    }

                    threshold = compare.integer2.Value;
                    return true;
                }
                catch
                {
                }

                return false;
            }

            private bool TryGetSummonLimitInGame(out int limit)
            {
                limit = 0;
                if (!TryResolveSummonLimitCompare(out IntCompare compare))
                {
                    return false;
                }

                try
                {
                    if (compare.integer2 == null)
                    {
                        return false;
                    }

                    limit = compare.integer2.Value;
                    return true;
                }
                catch
                {
                }

                return false;
            }

            private bool TryGetEnemyCountLimitInGame(out int limit)
            {
                limit = 0;
                if (!TryResolveEnemyCountLimitCompare(out IntCompare compare))
                {
                    return false;
                }

                try
                {
                    if (compare.integer2 == null)
                    {
                        return false;
                    }

                    limit = compare.integer2.Value;
                    return true;
                }
                catch
                {
                }

                return false;
            }

            private bool TryResolveResummonCompare(out IntCompare compare)
            {
                compare = collectorResummonCompare;
                if (compare != null && IsCollectorControlFsmValid(collectorControlFsm))
                {
                    return true;
                }

                collectorResummonCompare = null;
                if (!TryGetCollectorControlFsm(out PlayMakerFSM fsm))
                {
                    return false;
                }

                return TryResolveControlStateCompare(fsm, "Resummon?", ref collectorResummonCompare, out compare);
            }

            private bool TryResolveSummonLimitCompare(out IntCompare compare)
            {
                compare = collectorSummonLimitCompare;
                if (compare != null && IsCollectorControlFsmValid(collectorControlFsm))
                {
                    return true;
                }

                collectorSummonLimitCompare = null;
                if (!TryGetCollectorControlFsm(out PlayMakerFSM fsm))
                {
                    return false;
                }

                if (!TryResolveControlStateCompare(fsm, "Summon?", ref collectorSummonLimitCompare, out compare))
                {
                    return false;
                }

                return true;
            }

            private bool TryResolveEnemyCountLimitCompare(out IntCompare compare)
            {
                compare = collectorEnemyCountLimitCompare;
                if (compare != null && IsCollectorControlFsmValid(collectorControlFsm))
                {
                    return true;
                }

                collectorEnemyCountLimitCompare = null;
                if (!TryGetCollectorControlFsm(out PlayMakerFSM fsm))
                {
                    return false;
                }

                if (!TryResolveControlStateCompare(fsm, "Enemy Count", ref collectorEnemyCountLimitCompare, out compare))
                {
                    return false;
                }

                return true;
            }

            private bool TryResolveControlStateCompare(PlayMakerFSM fsm, string stateName, ref IntCompare cachedCompare, out IntCompare compare)
            {
                compare = null;

                try
                {
                    if (fsm == null)
                    {
                        return false;
                    }

                    FsmState state = fsm.Fsm?.GetState(stateName);
                    if (state?.Actions == null)
                    {
                        return false;
                    }

                    foreach (FsmStateAction action in state.Actions)
                    {
                        if (action is IntCompare intCompare && intCompare.integer2 != null)
                        {
                            cachedCompare = intCompare;
                            compare = intCompare;
                            return true;
                        }
                    }
                }
                catch
                {
                }

                return false;
            }

            private bool TryResolvePhase2ThresholdCompare(out IntCompare compare)
            {
                compare = collectorPhase2ThresholdCompare;
                if (compare != null && IsCollectorPhaseControlValid(collectorPhaseControlFsm))
                {
                    return true;
                }

                collectorPhase2ThresholdCompare = null;
                collectorPhaseControlFsm = null;

                try
                {
                    if (!TryGetCollectorPhaseControlFsm(out PlayMakerFSM fsm))
                    {
                        return false;
                    }

                    FsmState state = fsm.Fsm?.GetState("Check");
                    if (state?.Actions == null)
                    {
                        return false;
                    }

                    foreach (FsmStateAction action in state.Actions)
                    {
                        if (action is IntCompare intCompare && intCompare.integer2 != null)
                        {
                            collectorPhase2ThresholdCompare = intCompare;
                            compare = intCompare;
                            return true;
                        }
                    }
                }
                catch
                {
                }

                return false;
            }

            private bool TryGetCollectorControlFsm(out PlayMakerFSM fsm)
            {
                fsm = collectorControlFsm;
                if (IsCollectorControlFsmValid(fsm))
                {
                    return true;
                }

                long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                if (lastControlFsmSearchTime > 0 && now - lastControlFsmSearchTime < FsmSearchThrottleMs)
                {
                    return false;
                }

                lastControlFsmSearchTime = now;

                PlayMakerFSM best = FindCollectorFsm("Control", currentArenaName);
                if (best == null && !string.IsNullOrEmpty(currentArenaName))
                {
                    best = FindCollectorFsm("Control", null);
                }

                if (best == null)
                {
                    return false;
                }

                collectorControlFsm = best;
                int instanceId = best.GetInstanceID();
                if (collectorControlFsmId != instanceId)
                {
                    collectorControlFsmId = instanceId;
                    collectorResummonCompare = null;
                    collectorSummonLimitCompare = null;
                    collectorEnemyCountLimitCompare = null;
                }

                fsm = best;
                return true;
            }

            private bool TryGetCollectorPhaseControlFsm(out PlayMakerFSM fsm)
            {
                fsm = collectorPhaseControlFsm;
                if (IsCollectorPhaseControlValid(fsm))
                {
                    return true;
                }

                long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                if (lastPhaseFsmSearchTime > 0 && now - lastPhaseFsmSearchTime < FsmSearchThrottleMs)
                {
                    return false;
                }

                lastPhaseFsmSearchTime = now;

                PlayMakerFSM best = FindCollectorFsm("Phase Control", currentArenaName);
                if (best == null && !string.IsNullOrEmpty(currentArenaName))
                {
                    best = FindCollectorFsm("Phase Control", null);
                }

                if (best == null)
                {
                    return false;
                }

                collectorPhaseControlFsm = best;
                int instanceId = best.GetInstanceID();
                if (collectorPhaseControlFsmId != instanceId)
                {
                    collectorPhaseControlFsmId = instanceId;
                    collectorPhase2ThresholdCompare = null;
                }

                fsm = best;
                return true;
            }

            private static bool IsCollectorControlFsmValid(PlayMakerFSM fsm)
            {
                if (fsm == null)
                {
                    return false;
                }

                GameObject go = fsm.gameObject;
                if (go == null || !go.activeInHierarchy)
                {
                    return false;
                }

                if (!fsm.enabled)
                {
                    return false;
                }

                if (!string.Equals(go.name, "Jar Collector", StringComparison.Ordinal))
                {
                    return false;
                }

                if (!string.Equals(fsm.FsmName, "Control", StringComparison.Ordinal))
                {
                    return false;
                }

                return true;
            }

            private static bool IsCollectorPhaseControlValid(PlayMakerFSM fsm)
            {
                if (fsm == null)
                {
                    return false;
                }

                GameObject go = fsm.gameObject;
                if (go == null || !go.activeInHierarchy)
                {
                    return false;
                }

                if (!fsm.enabled)
                {
                    return false;
                }

                if (!string.Equals(go.name, "Jar Collector", StringComparison.Ordinal))
                {
                    return false;
                }

                if (!string.Equals(fsm.FsmName, "Phase Control", StringComparison.Ordinal))
                {
                    return false;
                }

                return true;
            }

            private static PlayMakerFSM FindCollectorFsm(string fsmName, string sceneName)
            {
                PlayMakerFSM best = null;
                int bestScore = int.MinValue;

                PlayMakerFSM[] fsms = UnityEngine.Object.FindObjectsOfType<PlayMakerFSM>();
                foreach (PlayMakerFSM fsm in fsms)
                {
                    if (fsm == null || fsm.gameObject == null)
                    {
                        continue;
                    }

                    if (!string.Equals(fsm.gameObject.name, "Jar Collector", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!string.Equals(fsm.FsmName, fsmName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!fsm.enabled || !fsm.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(sceneName) && !string.Equals(fsm.gameObject.scene.name, sceneName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    int score = 0;
                    try
                    {
                        HealthManager hm = fsm.gameObject.GetComponent<HealthManager>();
                        if (hm != null)
                        {
                            if (!hm.isDead)
                            {
                                score += 100;
                            }

                            if (hm.hp > 0)
                            {
                                score += 10;
                            }

                            score += hm.hp;
                        }
                    }
                    catch
                    {
                    }

                    score += fsm.gameObject.GetInstanceID() & 0xFF;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = fsm;
                    }
                }

                return best;
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

            private Type GetCollectorPhasesType()
            {
                if (!collectorPhasesResolved)
                {
                    collectorPhasesType = FindType("GodhomeQoL.Modules.CollectorPhases.CollectorPhases");
                    collectorPhasesResolved = true;
                }

                return collectorPhasesType;
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
