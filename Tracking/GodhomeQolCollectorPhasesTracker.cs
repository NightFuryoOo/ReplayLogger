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
            private CollectorPhasesState initialState;
            private CollectorPhasesState currentState;
            private bool hasCurrentState;
            private readonly List<string> changes = new();
            private CollectorPhasesPendingState pendingInitialState;
            private const int InitialFillWindowMs = 2000;

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
            private string fsmCacheSceneName;
            private const int FsmSearchThrottleMs = 1000;

            public bool HasData => hasInitialState || changes.Count > 0;

            public void Reset()
            {
                hasInitialState = false;
                initialArenaName = null;
                currentArenaName = null;
                currentBaseUnixTime = 0;
                initialFillDeadline = 0;
                initialState = default;
                currentState = default;
                hasCurrentState = false;
                changes.Clear();
                pendingInitialState = default;
                lastControlFsmSearchTime = 0;
                lastPhaseFsmSearchTime = 0;
                fsmCacheSceneName = null;
                PlayMakerFsmSceneCache.Invalidate();
            }

            public void StartFight(string arenaName, long baseUnixTime)
            {
                currentArenaName = string.IsNullOrWhiteSpace(arenaName) ? "UnknownArena" : arenaName;
                currentBaseUnixTime = baseUnixTime;
                long now = baseUnixTime;
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
                fsmCacheSceneName = null;
                PlayMakerFsmSceneCache.Invalidate();

                CollectorPhasesState snapshot = BuildState(now);

                if (!hasInitialState)
                {
                    hasInitialState = true;
                    initialArenaName = currentArenaName;
                    initialState = snapshot;
                    currentState = snapshot;
                    hasCurrentState = true;
                    pendingInitialState = CollectorPhasesPendingState.FromState(initialState);
                    initialFillDeadline = pendingInitialState.HasAny
                        ? now + InitialFillWindowMs
                        : 0;
                    return;
                }

                if (!hasCurrentState)
                {
                    currentState = snapshot;
                    hasCurrentState = true;
                    return;
                }

                LogStateChanges(snapshot, now);
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

                long now = nowUnixTime;

                CollectorPhasesState snapshot = BuildState(now);
                if (pendingInitialState.HasAny && now <= initialFillDeadline)
                {
                    FillPendingInitialState(snapshot);
                }
                else if (pendingInitialState.HasAny && now > initialFillDeadline)
                {
                    pendingInitialState = default;
                }

                if (!hasCurrentState)
                {
                    currentState = snapshot;
                    hasCurrentState = true;
                    return;
                }

                LogStateChanges(snapshot, now);
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

                List<string> batch = TempObjectPools.RentStringList(changes.Count + 24);
                try
                {
                    batch.Add("  Collector Phases:");
                    if (!string.IsNullOrEmpty(initialArenaName))
                    {
                        batch.Add($"    Initial Arena: {initialArenaName}");
                    }
                    batch.Add("    State:");
                    batch.Add($"      Enabled: {FormatOptionalToggle(initialState.Enabled)}");
                    batch.Add($"      Phase: {FormatOptionalInt(initialState.Phase)}");
                    batch.Add($"      Collector Immortal: {FormatOptionalToggle(initialState.CollectorImmortal)}");
                    batch.Add($"      Ignore Initial Jar Limits (Toggle): {FormatOptionalToggle(initialState.IgnoreInitialJarLimit)}");
                    batch.Add($"      Initial Jar Limit (In-Game): {FormatOptionalInt(initialState.InitialJarLimitInGame)}");
                    batch.Add($"      Use Custom Phase 2 HP (Toggle): {FormatOptionalToggle(initialState.UseCustomPhase2Threshold)}");
                    batch.Add($"      Phase 2 HP Threshold (Value): {FormatOptionalInt(initialState.Phase2ThresholdValue)}");
                    batch.Add($"      Phase 2 HP Threshold (In-Game): {FormatOptionalInt(initialState.Phase2ThresholdInGame)}");
                    batch.Add($"      Use Max HP (Toggle): {FormatOptionalToggle(initialState.UseMaxHp)}");
                    batch.Add($"      Collector Max HP (Value): {FormatOptionalInt(initialState.CollectorMaxHp)}");
                    batch.Add($"      Squit HP (Value): {FormatOptionalInt(initialState.BuzzerHp)}");
                    batch.Add($"      Spawn Squit (Toggle): {FormatOptionalToggle(initialState.SpawnBuzzer)}");
                    batch.Add($"      Baldur HP (Value): {FormatOptionalInt(initialState.RollerHp)}");
                    batch.Add($"      Spawn Baldur (Toggle): {FormatOptionalToggle(initialState.SpawnRoller)}");
                    batch.Add($"      Aspid HP (Value): {FormatOptionalInt(initialState.SpitterHp)}");
                    batch.Add($"      Spawn Aspid (Toggle): {FormatOptionalToggle(initialState.SpawnSpitter)}");
                    batch.Add($"      Use Custom Summon Limit (Toggle): {FormatOptionalToggle(initialState.DisableSummonLimit)}");
                    batch.Add($"      Custom Summon Limit (Value): {FormatOptionalInt(initialState.CustomSummonLimit)}");
                    batch.Add($"      Summon Limit (In-Game / Summon?): {FormatOptionalInt(initialState.SummonLimitInGame)}");
                    batch.Add($"      Summon Limit (In-Game / Enemy Count): {FormatOptionalInt(initialState.EnemyCountLimitInGame)}");
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

            private CollectorPhasesState BuildState(long nowUnixTime)
            {
                return new CollectorPhasesState(
                    TryGetCollectorPhasesEnabled(out bool enabled) ? new Optional<bool>(enabled) : Optional<bool>.None,
                    TryGetCollectorPhase(out int phase) ? new Optional<int>(phase) : Optional<int>.None,
                    TryGetCollectorImmortal(out bool immortal) ? new Optional<bool>(immortal) : Optional<bool>.None,
                    TryGetIgnoreInitialJarLimitToggle(out bool ignoreInit) ? new Optional<bool>(ignoreInit) : Optional<bool>.None,
                    TryGetInitialJarLimitInGame(out int initialLimit, nowUnixTime) ? new Optional<int>(initialLimit) : Optional<int>.None,
                    TryGetUseCustomPhase2Threshold(out bool useCustomPhase2) ? new Optional<bool>(useCustomPhase2) : Optional<bool>.None,
                    TryGetCustomPhase2Threshold(out int phase2Value) ? new Optional<int>(phase2Value) : Optional<int>.None,
                    TryGetPhase2ThresholdInGame(out int phase2InGame, nowUnixTime) ? new Optional<int>(phase2InGame) : Optional<int>.None,
                    TryGetUseMaxHp(out bool useMaxHp) ? new Optional<bool>(useMaxHp) : Optional<bool>.None,
                    TryGetCollectorMaxHp(out int maxHp) ? new Optional<int>(maxHp) : Optional<int>.None,
                    TryGetBuzzerHp(out int buzzerHp) ? new Optional<int>(buzzerHp) : Optional<int>.None,
                    TryGetSpawnBuzzer(out bool spawnBuzzer) ? new Optional<bool>(spawnBuzzer) : Optional<bool>.None,
                    TryGetRollerHp(out int rollerHp) ? new Optional<int>(rollerHp) : Optional<int>.None,
                    TryGetSpawnRoller(out bool spawnRoller) ? new Optional<bool>(spawnRoller) : Optional<bool>.None,
                    TryGetSpitterHp(out int spitterHp) ? new Optional<int>(spitterHp) : Optional<int>.None,
                    TryGetSpawnSpitter(out bool spawnSpitter) ? new Optional<bool>(spawnSpitter) : Optional<bool>.None,
                    TryGetDisableSummonLimit(out bool disableSummonLimit) ? new Optional<bool>(disableSummonLimit) : Optional<bool>.None,
                    TryGetCustomSummonLimit(out int customSummonLimit) ? new Optional<int>(customSummonLimit) : Optional<int>.None,
                    TryGetSummonLimitInGame(out int summonLimit, nowUnixTime) ? new Optional<int>(summonLimit) : Optional<int>.None,
                    TryGetEnemyCountLimitInGame(out int enemyCountLimit, nowUnixTime) ? new Optional<int>(enemyCountLimit) : Optional<int>.None);
            }

            private void LogStateChanges(CollectorPhasesState snapshot, long now)
            {
                LogFieldChange("Enabled", currentState.Enabled, snapshot.Enabled, now);
                LogFieldChange("Phase", currentState.Phase, snapshot.Phase, now);
                LogFieldChange("Collector Immortal", currentState.CollectorImmortal, snapshot.CollectorImmortal, now);
                LogFieldChange("Ignore Initial Jar Limits (Toggle)", currentState.IgnoreInitialJarLimit, snapshot.IgnoreInitialJarLimit, now);
                LogFieldChange("Initial Jar Limit (In-Game)", currentState.InitialJarLimitInGame, snapshot.InitialJarLimitInGame, now);
                LogFieldChange("Use Custom Phase 2 HP (Toggle)", currentState.UseCustomPhase2Threshold, snapshot.UseCustomPhase2Threshold, now);
                LogFieldChange("Phase 2 HP Threshold (Value)", currentState.Phase2ThresholdValue, snapshot.Phase2ThresholdValue, now);
                LogFieldChange("Phase 2 HP Threshold (In-Game)", currentState.Phase2ThresholdInGame, snapshot.Phase2ThresholdInGame, now);
                LogFieldChange("Use Max HP (Toggle)", currentState.UseMaxHp, snapshot.UseMaxHp, now);
                LogFieldChange("Collector Max HP (Value)", currentState.CollectorMaxHp, snapshot.CollectorMaxHp, now);
                LogFieldChange("Squit HP (Value)", currentState.BuzzerHp, snapshot.BuzzerHp, now);
                LogFieldChange("Spawn Squit (Toggle)", currentState.SpawnBuzzer, snapshot.SpawnBuzzer, now);
                LogFieldChange("Baldur HP (Value)", currentState.RollerHp, snapshot.RollerHp, now);
                LogFieldChange("Spawn Baldur (Toggle)", currentState.SpawnRoller, snapshot.SpawnRoller, now);
                LogFieldChange("Aspid HP (Value)", currentState.SpitterHp, snapshot.SpitterHp, now);
                LogFieldChange("Spawn Aspid (Toggle)", currentState.SpawnSpitter, snapshot.SpawnSpitter, now);
                LogFieldChange("Use Custom Summon Limit (Toggle)", currentState.DisableSummonLimit, snapshot.DisableSummonLimit, now);
                LogFieldChange("Custom Summon Limit (Value)", currentState.CustomSummonLimit, snapshot.CustomSummonLimit, now);
                LogFieldChange("Summon Limit (In-Game / Summon?)", currentState.SummonLimitInGame, snapshot.SummonLimitInGame, now);
                LogFieldChange("Summon Limit (In-Game / Enemy Count)", currentState.EnemyCountLimitInGame, snapshot.EnemyCountLimitInGame, now);
            }

            private void FillPendingInitialState(CollectorPhasesState snapshot)
            {
                UpdatePendingField(ref initialState.Enabled, ref currentState.Enabled, snapshot.Enabled, ref pendingInitialState.Enabled);
                UpdatePendingField(ref initialState.Phase, ref currentState.Phase, snapshot.Phase, ref pendingInitialState.Phase);
                UpdatePendingField(ref initialState.CollectorImmortal, ref currentState.CollectorImmortal, snapshot.CollectorImmortal, ref pendingInitialState.CollectorImmortal);
                UpdatePendingField(ref initialState.IgnoreInitialJarLimit, ref currentState.IgnoreInitialJarLimit, snapshot.IgnoreInitialJarLimit, ref pendingInitialState.IgnoreInitialJarLimit);
                UpdatePendingField(ref initialState.InitialJarLimitInGame, ref currentState.InitialJarLimitInGame, snapshot.InitialJarLimitInGame, ref pendingInitialState.InitialJarLimitInGame);
                UpdatePendingField(ref initialState.UseCustomPhase2Threshold, ref currentState.UseCustomPhase2Threshold, snapshot.UseCustomPhase2Threshold, ref pendingInitialState.UseCustomPhase2Threshold);
                UpdatePendingField(ref initialState.Phase2ThresholdValue, ref currentState.Phase2ThresholdValue, snapshot.Phase2ThresholdValue, ref pendingInitialState.Phase2ThresholdValue);
                UpdatePendingField(ref initialState.Phase2ThresholdInGame, ref currentState.Phase2ThresholdInGame, snapshot.Phase2ThresholdInGame, ref pendingInitialState.Phase2ThresholdInGame);
                UpdatePendingField(ref initialState.UseMaxHp, ref currentState.UseMaxHp, snapshot.UseMaxHp, ref pendingInitialState.UseMaxHp);
                UpdatePendingField(ref initialState.CollectorMaxHp, ref currentState.CollectorMaxHp, snapshot.CollectorMaxHp, ref pendingInitialState.CollectorMaxHp);
                UpdatePendingField(ref initialState.BuzzerHp, ref currentState.BuzzerHp, snapshot.BuzzerHp, ref pendingInitialState.BuzzerHp);
                UpdatePendingField(ref initialState.SpawnBuzzer, ref currentState.SpawnBuzzer, snapshot.SpawnBuzzer, ref pendingInitialState.SpawnBuzzer);
                UpdatePendingField(ref initialState.RollerHp, ref currentState.RollerHp, snapshot.RollerHp, ref pendingInitialState.RollerHp);
                UpdatePendingField(ref initialState.SpawnRoller, ref currentState.SpawnRoller, snapshot.SpawnRoller, ref pendingInitialState.SpawnRoller);
                UpdatePendingField(ref initialState.SpitterHp, ref currentState.SpitterHp, snapshot.SpitterHp, ref pendingInitialState.SpitterHp);
                UpdatePendingField(ref initialState.SpawnSpitter, ref currentState.SpawnSpitter, snapshot.SpawnSpitter, ref pendingInitialState.SpawnSpitter);
                UpdatePendingField(ref initialState.DisableSummonLimit, ref currentState.DisableSummonLimit, snapshot.DisableSummonLimit, ref pendingInitialState.DisableSummonLimit);
                UpdatePendingField(ref initialState.CustomSummonLimit, ref currentState.CustomSummonLimit, snapshot.CustomSummonLimit, ref pendingInitialState.CustomSummonLimit);
                UpdatePendingField(ref initialState.SummonLimitInGame, ref currentState.SummonLimitInGame, snapshot.SummonLimitInGame, ref pendingInitialState.SummonLimitInGame);
                UpdatePendingField(ref initialState.EnemyCountLimitInGame, ref currentState.EnemyCountLimitInGame, snapshot.EnemyCountLimitInGame, ref pendingInitialState.EnemyCountLimitInGame);
            }

            private static void UpdatePendingField<T>(
                ref Optional<T> initialField,
                ref Optional<T> currentField,
                Optional<T> snapshotField,
                ref bool pending)
            {
                if (!pending || !snapshotField.HasValue)
                {
                    return;
                }

                initialField = snapshotField;
                currentField = snapshotField;
                pending = false;
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

            private struct CollectorPhasesPendingState
            {
                internal bool Enabled;
                internal bool Phase;
                internal bool CollectorImmortal;
                internal bool IgnoreInitialJarLimit;
                internal bool InitialJarLimitInGame;
                internal bool UseCustomPhase2Threshold;
                internal bool Phase2ThresholdValue;
                internal bool Phase2ThresholdInGame;
                internal bool UseMaxHp;
                internal bool CollectorMaxHp;
                internal bool BuzzerHp;
                internal bool SpawnBuzzer;
                internal bool RollerHp;
                internal bool SpawnRoller;
                internal bool SpitterHp;
                internal bool SpawnSpitter;
                internal bool DisableSummonLimit;
                internal bool CustomSummonLimit;
                internal bool SummonLimitInGame;
                internal bool EnemyCountLimitInGame;

                internal bool HasAny =>
                    Enabled ||
                    Phase ||
                    CollectorImmortal ||
                    IgnoreInitialJarLimit ||
                    InitialJarLimitInGame ||
                    UseCustomPhase2Threshold ||
                    Phase2ThresholdValue ||
                    Phase2ThresholdInGame ||
                    UseMaxHp ||
                    CollectorMaxHp ||
                    BuzzerHp ||
                    SpawnBuzzer ||
                    RollerHp ||
                    SpawnRoller ||
                    SpitterHp ||
                    SpawnSpitter ||
                    DisableSummonLimit ||
                    CustomSummonLimit ||
                    SummonLimitInGame ||
                    EnemyCountLimitInGame;

                internal static CollectorPhasesPendingState FromState(CollectorPhasesState state)
                {
                    return new CollectorPhasesPendingState
                    {
                        Enabled = !state.Enabled.HasValue,
                        Phase = !state.Phase.HasValue,
                        CollectorImmortal = !state.CollectorImmortal.HasValue,
                        IgnoreInitialJarLimit = !state.IgnoreInitialJarLimit.HasValue,
                        InitialJarLimitInGame = !state.InitialJarLimitInGame.HasValue,
                        UseCustomPhase2Threshold = !state.UseCustomPhase2Threshold.HasValue,
                        Phase2ThresholdValue = !state.Phase2ThresholdValue.HasValue,
                        Phase2ThresholdInGame = !state.Phase2ThresholdInGame.HasValue,
                        UseMaxHp = !state.UseMaxHp.HasValue,
                        CollectorMaxHp = !state.CollectorMaxHp.HasValue,
                        BuzzerHp = !state.BuzzerHp.HasValue,
                        SpawnBuzzer = !state.SpawnBuzzer.HasValue,
                        RollerHp = !state.RollerHp.HasValue,
                        SpawnRoller = !state.SpawnRoller.HasValue,
                        SpitterHp = !state.SpitterHp.HasValue,
                        SpawnSpitter = !state.SpawnSpitter.HasValue,
                        DisableSummonLimit = !state.DisableSummonLimit.HasValue,
                        CustomSummonLimit = !state.CustomSummonLimit.HasValue,
                        SummonLimitInGame = !state.SummonLimitInGame.HasValue,
                        EnemyCountLimitInGame = !state.EnemyCountLimitInGame.HasValue
                    };
                }
            }

            private struct CollectorPhasesState
            {
                internal CollectorPhasesState(
                    Optional<bool> enabled,
                    Optional<int> phase,
                    Optional<bool> collectorImmortal,
                    Optional<bool> ignoreInitialJarLimit,
                    Optional<int> initialJarLimitInGame,
                    Optional<bool> useCustomPhase2Threshold,
                    Optional<int> phase2ThresholdValue,
                    Optional<int> phase2ThresholdInGame,
                    Optional<bool> useMaxHp,
                    Optional<int> collectorMaxHp,
                    Optional<int> buzzerHp,
                    Optional<bool> spawnBuzzer,
                    Optional<int> rollerHp,
                    Optional<bool> spawnRoller,
                    Optional<int> spitterHp,
                    Optional<bool> spawnSpitter,
                    Optional<bool> disableSummonLimit,
                    Optional<int> customSummonLimit,
                    Optional<int> summonLimitInGame,
                    Optional<int> enemyCountLimitInGame)
                {
                    Enabled = enabled;
                    Phase = phase;
                    CollectorImmortal = collectorImmortal;
                    IgnoreInitialJarLimit = ignoreInitialJarLimit;
                    InitialJarLimitInGame = initialJarLimitInGame;
                    UseCustomPhase2Threshold = useCustomPhase2Threshold;
                    Phase2ThresholdValue = phase2ThresholdValue;
                    Phase2ThresholdInGame = phase2ThresholdInGame;
                    UseMaxHp = useMaxHp;
                    CollectorMaxHp = collectorMaxHp;
                    BuzzerHp = buzzerHp;
                    SpawnBuzzer = spawnBuzzer;
                    RollerHp = rollerHp;
                    SpawnRoller = spawnRoller;
                    SpitterHp = spitterHp;
                    SpawnSpitter = spawnSpitter;
                    DisableSummonLimit = disableSummonLimit;
                    CustomSummonLimit = customSummonLimit;
                    SummonLimitInGame = summonLimitInGame;
                    EnemyCountLimitInGame = enemyCountLimitInGame;
                }

                internal Optional<bool> Enabled;
                internal Optional<int> Phase;
                internal Optional<bool> CollectorImmortal;
                internal Optional<bool> IgnoreInitialJarLimit;
                internal Optional<int> InitialJarLimitInGame;
                internal Optional<bool> UseCustomPhase2Threshold;
                internal Optional<int> Phase2ThresholdValue;
                internal Optional<int> Phase2ThresholdInGame;
                internal Optional<bool> UseMaxHp;
                internal Optional<int> CollectorMaxHp;
                internal Optional<int> BuzzerHp;
                internal Optional<bool> SpawnBuzzer;
                internal Optional<int> RollerHp;
                internal Optional<bool> SpawnRoller;
                internal Optional<int> SpitterHp;
                internal Optional<bool> SpawnSpitter;
                internal Optional<bool> DisableSummonLimit;
                internal Optional<int> CustomSummonLimit;
                internal Optional<int> SummonLimitInGame;
                internal Optional<int> EnemyCountLimitInGame;
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
                        ? collectorPhaseProperty.GetCachedValue(null)
                        : collectorPhaseField?.GetCachedValue(null);

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
                        ? collectorImmortalProperty.GetCachedValue(null)
                        : collectorImmortalField?.GetCachedValue(null);

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
                        ? ignoreInitialJarLimitProperty.GetCachedValue(null)
                        : ignoreInitialJarLimitField?.GetCachedValue(null);

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
                        ? useCustomPhase2ThresholdProperty.GetCachedValue(null)
                        : useCustomPhase2ThresholdField?.GetCachedValue(null);

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
                        ? customPhase2ThresholdProperty.GetCachedValue(null)
                        : customPhase2ThresholdField?.GetCachedValue(null);

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
                        ? useMaxHpProperty.GetCachedValue(null)
                        : useMaxHpField?.GetCachedValue(null);

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
                        ? collectorMaxHpProperty.GetCachedValue(null)
                        : collectorMaxHpField?.GetCachedValue(null);

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
                        ? disableSummonLimitProperty.GetCachedValue(null)
                        : disableSummonLimitField?.GetCachedValue(null);

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
                        ? customSummonLimitProperty.GetCachedValue(null)
                        : customSummonLimitField?.GetCachedValue(null);

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
                        ? property.GetCachedValue(null)
                        : field?.GetCachedValue(null);

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
                        ? property.GetCachedValue(null)
                        : field?.GetCachedValue(null);

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

            private bool TryGetInitialJarLimitInGame(out int limit, long nowUnixTime)
            {
                limit = 0;
                if (!TryResolveResummonCompare(out IntCompare compare, nowUnixTime))
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

            private bool TryGetPhase2ThresholdInGame(out int threshold, long nowUnixTime)
            {
                threshold = 0;
                if (!TryResolvePhase2ThresholdCompare(out IntCompare compare, nowUnixTime))
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

            private bool TryGetSummonLimitInGame(out int limit, long nowUnixTime)
            {
                limit = 0;
                if (!TryResolveSummonLimitCompare(out IntCompare compare, nowUnixTime))
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

            private bool TryGetEnemyCountLimitInGame(out int limit, long nowUnixTime)
            {
                limit = 0;
                if (!TryResolveEnemyCountLimitCompare(out IntCompare compare, nowUnixTime))
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

            private bool TryResolveResummonCompare(out IntCompare compare, long nowUnixTime)
            {
                compare = collectorResummonCompare;
                if (compare != null && IsCollectorControlFsmValid(collectorControlFsm))
                {
                    return true;
                }

                collectorResummonCompare = null;
                if (!TryGetCollectorControlFsm(out PlayMakerFSM fsm, nowUnixTime))
                {
                    return false;
                }

                return TryResolveControlStateCompare(fsm, "Resummon?", ref collectorResummonCompare, out compare);
            }

            private bool TryResolveSummonLimitCompare(out IntCompare compare, long nowUnixTime)
            {
                compare = collectorSummonLimitCompare;
                if (compare != null && IsCollectorControlFsmValid(collectorControlFsm))
                {
                    return true;
                }

                collectorSummonLimitCompare = null;
                if (!TryGetCollectorControlFsm(out PlayMakerFSM fsm, nowUnixTime))
                {
                    return false;
                }

                if (!TryResolveControlStateCompare(fsm, "Summon?", ref collectorSummonLimitCompare, out compare))
                {
                    return false;
                }

                return true;
            }

            private bool TryResolveEnemyCountLimitCompare(out IntCompare compare, long nowUnixTime)
            {
                compare = collectorEnemyCountLimitCompare;
                if (compare != null && IsCollectorControlFsmValid(collectorControlFsm))
                {
                    return true;
                }

                collectorEnemyCountLimitCompare = null;
                if (!TryGetCollectorControlFsm(out PlayMakerFSM fsm, nowUnixTime))
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

            private bool TryResolvePhase2ThresholdCompare(out IntCompare compare, long nowUnixTime)
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
                    if (!TryGetCollectorPhaseControlFsm(out PlayMakerFSM fsm, nowUnixTime))
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

            private bool TryGetCollectorControlFsm(out PlayMakerFSM fsm, long nowUnixTime)
            {
                EnsureCollectorSceneCacheCurrent();

                fsm = collectorControlFsm;
                if (IsCollectorControlFsmValid(fsm))
                {
                    return true;
                }

                long now = nowUnixTime;
                if (lastControlFsmSearchTime > 0 && now - lastControlFsmSearchTime < FsmSearchThrottleMs)
                {
                    return false;
                }

                lastControlFsmSearchTime = now;

                PlayMakerFSM best = FindCollectorFsm("Control", currentArenaName, forceRefresh: false);
                if (best == null && !string.IsNullOrEmpty(currentArenaName))
                {
                    best = FindCollectorFsm("Control", null, forceRefresh: false);
                }

                if (best == null)
                {
                    best = FindCollectorFsm("Control", currentArenaName, forceRefresh: true);
                    if (best == null && !string.IsNullOrEmpty(currentArenaName))
                    {
                        best = FindCollectorFsm("Control", null, forceRefresh: true);
                    }
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

            private bool TryGetCollectorPhaseControlFsm(out PlayMakerFSM fsm, long nowUnixTime)
            {
                EnsureCollectorSceneCacheCurrent();

                fsm = collectorPhaseControlFsm;
                if (IsCollectorPhaseControlValid(fsm))
                {
                    return true;
                }

                long now = nowUnixTime;
                if (lastPhaseFsmSearchTime > 0 && now - lastPhaseFsmSearchTime < FsmSearchThrottleMs)
                {
                    return false;
                }

                lastPhaseFsmSearchTime = now;

                PlayMakerFSM best = FindCollectorFsm("Phase Control", currentArenaName, forceRefresh: false);
                if (best == null && !string.IsNullOrEmpty(currentArenaName))
                {
                    best = FindCollectorFsm("Phase Control", null, forceRefresh: false);
                }

                if (best == null)
                {
                    best = FindCollectorFsm("Phase Control", currentArenaName, forceRefresh: true);
                    if (best == null && !string.IsNullOrEmpty(currentArenaName))
                    {
                        best = FindCollectorFsm("Phase Control", null, forceRefresh: true);
                    }
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

            private PlayMakerFSM FindCollectorFsm(string fsmName, string sceneName, bool forceRefresh)
            {
                PlayMakerFSM best = null;
                int bestScore = int.MinValue;

                PlayMakerFSM[] fsms = GetSceneFsmCache(forceRefresh);
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

            private void EnsureCollectorSceneCacheCurrent()
            {
                string activeSceneName = GameManager.instance?.sceneName;
                if (string.Equals(fsmCacheSceneName, activeSceneName, StringComparison.Ordinal))
                {
                    return;
                }

                fsmCacheSceneName = activeSceneName;
                PlayMakerFsmSceneCache.Invalidate();
                collectorControlFsm = null;
                collectorControlFsmId = 0;
                collectorResummonCompare = null;
                collectorSummonLimitCompare = null;
                collectorEnemyCountLimitCompare = null;
                collectorPhaseControlFsm = null;
                collectorPhaseControlFsmId = 0;
                collectorPhase2ThresholdCompare = null;
            }

            private PlayMakerFSM[] GetSceneFsmCache(bool forceRefresh)
            {
                return PlayMakerFsmSceneCache.Get(forceRefresh);
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
