using GlobalEnums;
using HutongGames.PlayMaker;
using Modding;
using On;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using MonoMod.RuntimeDetour;
using HKHealthManager = global::HealthManager;

namespace ReplayLogger
{
    internal static class HoGLogger
    {
        private static readonly object SyncRoot = new();
        private static readonly string DllDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        private static readonly string ModsDirectory = new DirectoryInfo(DllDirectory).Parent.FullName;

        private static bool hooksInitialized;
        private static bool runtimeHooksInitialized;
        private static readonly Dictionary<int, string> CustomCharmDisplayNames = new()
        {
            { (int)Charm.MarkOfPurity, "Mark of Purity" },
            { (int)Charm.VesselsLament, "Vessel's Lament" },
            { (int)Charm.BoonOfHallownest, "Boon of Hallownest" },
            { (int)Charm.AbyssalBloom, "Abyssal Bloom" }
        };

        private static bool isLogging;
        private static string activeArena;
        private static int? bossLevelInFight;
        private static int lastVictoryControllerId;
        private static string lastSceneName = string.Empty;
        private static string lastSceneBeforeArena = string.Empty;
        private static string currentTempFile;
        private static StreamWriter writer;
        private static CustomCanvas customCanvas;
        private static string masterKeyBlob;
        private static KeyloggerLogEncryption.Session masterEncryptionSession;
        private static HoGBucketInfo currentBucketInfo = HoGBucketInfo.CreateDefault(null);
        private static string pendingHoGDefaultFolder = HoGLoggerConditions.DefaultBucket;
        private static readonly Dictionary<string, BossHpState> bossHpStates = new(StringComparer.Ordinal);
        private const int BufferedSectionThreshold = 200;
        private const int BlockSizeBytes = 128 * 1024;
        private const int BlockMaxAgeMs = 1500;
        private const int LogQueueCapacity = 49152;

        private static long lastUnixTime;
        private static long startUnixTime;
        private static int bossCounter;
        private static float lastFps;
        private static SpeedWarnTracker speedWarnTracker = new();
        private static HitWarnTracker hitWarnTracker = new();
        private static DebugModEventsTracker debugModEventsTracker = new();
        private static DebugHotkeysTracker debugHotkeysTracker = new();
        private static CharmsChangeTracker charmsChangeTracker = new();
        private static GodhomeQolTracker godhomeQolTracker = new();
        private static GrubsongSoulGainTracker grubsongSoulGainTracker = new();
        private static DreamNailSoulGainTracker dreamNailSoulGainTracker = new();
        private static SoulSpentTracker soulSpentTracker = new();
        private static WeaversongSoulGainTracker weaversongSoulGainTracker = new();
        private static BossPhaseThresholdTracker bossPhaseThresholdTracker = new();
        private static BossSpawnHpTracker bossSpawnHpTracker = new();
        private static BossDifficultyTracker bossDifficultyTracker = new();
        private static PaleCourtStageTracker paleCourtStageTracker = new();
        private static readonly List<HKHealthManager> stageColumnsBuffer = new();

        private static List<ITrackerLifecycle> LifecycleTrackers() =>
        [
            grubsongSoulGainTracker,
            dreamNailSoulGainTracker,
            soulSpentTracker,
            weaversongSoulGainTracker,
            bossPhaseThresholdTracker,
            bossSpawnHpTracker,
            bossDifficultyTracker,
            paleCourtStageTracker
        ];

        private static int speedWarnInlineCursor;
        private static int hitWarnInlineCursor;
        private static int debugModEventsInlineCursor;
        private static int debugHotkeysInlineCursor;
        private static int debugMenuInlineCursor;
        private static int stageInlineCursor;
        private static int charmsInlineCursor;

        private static BufferedLogSection pressedButtonsLog;
        private static BufferedLogSection damageAndInv;
        private static BufferedLogSection invWarnings;
        private static BufferedLogSection speedWarnBuffer;
        private static BufferedLogSection hitWarnBuffer;
        private readonly struct KeyLogEvent
        {
            internal KeyLogEvent(long deltaMs, KeyCode keyCode, bool isDown, int watermarkNumber, Color32 color, int fps)
            {
                DeltaMs = deltaMs;
                KeyCode = keyCode;
                IsDown = isDown;
                WatermarkNumber = watermarkNumber;
                Color = color;
                Fps = fps;
            }

            internal long DeltaMs { get; }
            internal KeyCode KeyCode { get; }
            internal bool IsDown { get; }
            internal int WatermarkNumber { get; }
            internal Color32 Color { get; }
            internal int Fps { get; }
        }

        private static readonly List<KeyLogEvent> keyLogBuffer = new(256);
        private static readonly List<string> keyLogFlushLines = new(256);
        private const int KeyLogFlushIntervalMs = 200;
        private const int KeyLogFlushBatchSize = 50;
        private static long lastKeyLogFlushTime;
        private static int lastHudElapsedSeconds = -1;
        private static readonly AdaptiveKeyScanner keyScanner = new();
        private static readonly Dictionary<Color32, string> keyColorHexCache = new(128);
        private const int KeyColorHexCacheMaxSize = 512;
        private static readonly Dictionary<GameObject, string> ownerPathByGameObject = new(512);
        private static readonly List<GameObject> ownerPathCacheCleanupBuffer = new(128);
        private static float lastOwnerPathCacheCleanupTime;
        private static int ownerPathCacheCleanupCursor;
        private static long cachedFrameUnixTime;
        private const int EnemyColliderBufferInitialSize = 1024;
        private const float EnemyLayerMaskRefreshIntervalSeconds = 1f;
        private const float EnemyColliderOverflowWarnIntervalSeconds = 5f;
        private static Collider2D[] enemyColliderBuffer = new Collider2D[EnemyColliderBufferInitialSize];
        private static bool enemyColliderBufferOverflowLogged;
        private static float lastEnemyColliderOverflowWarningTime;
        private static int enemyScanLayerMask;
        private static float lastEnemyLayerMaskRefreshTime;
        private static readonly Dictionary<GameObject, HKHealthManager> enemyHealthManagerByGameObject = new(512);
        private static readonly List<GameObject> enemyHealthManagerCacheCleanupBuffer = new(64);
        private static float lastEnemyHealthCacheCleanupTime;
        private static int enemyHealthCacheCleanupCursor;
        private static readonly Dictionary<GameObject, HKHealthManager> uniqueBossByGameObject = new(128);
        private static readonly HashSet<HKHealthManager> uniqueBossSet = new();
        private static readonly List<HKHealthManager> infoBossKeysBuffer = new(128);
        private static readonly StringBuilder hpInfoBuilder = new(256);
        private static bool uniqueBossBuffersDirty = true;
        private const float EnemyUpdateIntervalSeconds = 0.1f;
        private const float EnemySeedRetryIntervalSeconds = 0.5f;
        private static float lastEnemyUpdateTime;
        private static float lastEnemySeedTime;

        private static List<string> debugModEvents = new();
        private static List<string> debugHotkeyBindings = new();
        private static List<string> debugHotkeyEvents = new();
        private static DamageChangeTracker damageChangeTracker = new();
        private static DebugMenuTracker debugMenuTracker = new();
        private static int currentAttemptIndex = 1;
        private static long lastLoggedDeltaMs = -1;
        private static readonly string[] HitWarnSectionHeaderLines = { "\n\n", "HitWarn:" };
        private static readonly string[] HitWarnSectionFooterLines = { "\n\n", "---------------------------------------------------" };

        private static Dictionary<HKHealthManager, (int maxHP, int lastHP)> infoBoss = new();
        private static Dictionary<KeyCode, List<string>> debugHotkeysByKey = new();
        private static Hook debugKillAllHook;
        private static Hook debugKillSelfHook;
        private static FieldInfo bossLevelField;
        private static PropertyInfo bossLevelProperty;
        private static Type bossLevelOwnerType;
        private static FieldInfo bossSceneField;
        private static PropertyInfo bossSceneProperty;
        private static Type bossSceneOwnerType;
        private static FieldInfo bossSceneLevelField;
        private static PropertyInfo bossSceneLevelProperty;
        private static Type bossSceneLevelOwnerType;
        private static Func<int?> staticBossLevelGetter;
        private static Type paleCourtLevelOwnerType;
        private static FieldInfo paleCourtLevelField;
        private static PropertyInfo paleCourtLevelProperty;
        private static bool paleCourtLevelReadFailedLogged;
        private static bool bossSceneReadFailedLogged;
        private static bool bossSceneLevelReadFailedLogged;

        private static bool isInvincible;
        private static bool isChange;
        private static float invTimer;
        private static bool hasHeroBoxState;
        private static int lastHeroBoxActive;
        private static float heroBoxOffStartTime = -1f;
        private static Transform cachedHeroTransform;
        private static GameObject cachedHeroBoxObject;
        internal static void EnsureInitialized()
        {
            InitializeHooks();
        }

        private static void InitializeHooks()
        {
            if (hooksInitialized)
            {
                return;
            }

            ModsChecking.PrimeHeavyModCache(ModsDirectory);

            HoGRoomConditions.Initialize();

            lastSceneName = GameManager.instance?.sceneName ?? string.Empty;
            hooksInitialized = true;
        }

        internal static void HandleBootstrapSceneLoadBegin(string targetSceneName)
        {
            long now = KeyLogFormatting.CaptureFrameUnixTime(ref cachedFrameUnixTime);
            lastUnixTime = now;

            string targetScene = targetSceneName ?? string.Empty;
            string previousScene = lastSceneName;
            bool primaryLoggerActive = ReplayLogger.IsPrimaryLoggerActive();

            if (ReplayLogger.IsManualModeEnabled())
            {
                lastSceneName = targetScene;
                return;
            }

            if (primaryLoggerActive)
            {
                if (isLogging)
                {
                    StopLogging(targetScene);
                }

                lastSceneName = targetScene;
                return;
            }

            if (!isLogging && HoGLoggerConditions.ShouldStartLogging(previousScene, targetScene))
            {
                lastSceneBeforeArena = previousScene;
                StartLogging(targetScene);
            }
            else if (isLogging && HoGLoggerConditions.ShouldStopLogging(activeArena, targetScene))
            {
                StopLogging(targetScene);
            }
            else if (isLogging && string.Equals(targetScene, activeArena, StringComparison.Ordinal))
            {
                BeginNextAttempt(now);
            }

            lastSceneName = targetScene;
        }

        private static void BeginNextAttempt(long unixTime)
        {
            try
            {
                lock (SyncRoot)
                {
                    if (!isLogging || writer == null)
                    {
                        return;
                    }

                    FlushKeyLogBufferIfNeeded(unixTime, force: true);
                    currentAttemptIndex++;
                    bossCounter++;
                    lastLoggedDeltaMs = -1;

                    if (IsPaleCourtArena(activeArena) && !IsTisoArena(activeArena))
                    {
                        int? paleCourtLevel = TryReadPaleCourtLevel();
                        if (IsValidBossLevelForArena(paleCourtLevel, activeArena))
                        {
                            bossLevelInFight = paleCourtLevel.Value;
                            ObserveBossLevel(paleCourtLevel.Value);
                        }
                    }

                    string timestamp = FormatUnixTimestamp(unixTime);
                    long playTime = (int)((PlayerData.instance?.playTime ?? 0f) * 100);
                    string startLine = $"{timestamp}|{unixTime}|{playTime}|{activeArena}| {bossCounter}*";
                    bool wroteToSection = false;
                    if (pressedButtonsLog != null)
                    {
                        pressedButtonsLog.Add(startLine);
                        wroteToSection = true;
                    }
                    if (damageAndInv != null)
                    {
                        damageAndInv.Add(startLine);
                        wroteToSection = true;
                    }
                    if (!wroteToSection)
                    {
                        LogWrite.EncryptedLine(writer, startLine);
                    }
                }
            }
            catch (Exception e)
            {
                global::ReplayLogger.InternalDiagnostics.Warn($"HoGLogger: failed to start next attempt: {e.Message}");
            }
        }

        internal static void HandleBootstrapApplicationQuit()
        {
            ApplicationQuit();
        }

        private static void EnsureRuntimeHooks()
        {
            if (runtimeHooksInitialized)
            {
                return;
            }

            On.SceneLoad.RecordEndTime += SceneLoad_RecordEndTime;
            On.GameManager.Update += GameManager_Update;
            On.BossSceneController.Update += BossSceneController_Update;
            On.BossSceneController.EndBossScene += BossSceneController_EndBossScene;
            On.HeroController.Die += HeroController_Die;
            On.HeroController.FixedUpdate += HeroController_FixedUpdate;
            On.HealthManager.Start += HealthManager_Start;
            On.HealthManager.TakeDamage += HealthManager_TakeDamage;
            On.HeroController.SoulGain += HeroController_SoulGain;
            On.HeroController.TakeDamage += HeroController_TakeDamage;
            On.EnemyDreamnailReaction.RecieveDreamImpact += EnemyDreamnailReaction_RecieveDreamImpact;
            On.PlayerData.AddMPCharge += PlayerData_AddMPCharge;
            On.PlayerData.TakeMP += PlayerData_TakeMP;
            On.PlayerData.TakeReserveMP += PlayerData_TakeReserveMP;
            On.HutongGames.PlayMaker.Actions.CallMethod.OnEnter += CallMethod_OnEnter;
            On.HutongGames.PlayMaker.Actions.CallMethodProper.OnEnter += CallMethodProper_OnEnter;
            On.HutongGames.PlayMaker.Actions.SendMessage.OnEnter += SendMessage_OnEnter;
            On.PlayMakerFSM.SendEvent += PlayMakerFSM_SendEvent;
            On.QuitToMenu.Start += QuitToMenu_Start;
            On.SpellFluke.DoDamage += SpellFluke_DoDamage;
            On.DamageEnemies.DoDamage += DamageEnemies_DoDamage;
            On.DamageEffectTicker.Update += DamageEffectTicker_Update;
            On.HitTaker.Hit += HitTaker_Hit;
            On.HutongGames.PlayMaker.Actions.IntOperator.OnEnter += CharmDamageIntOperator_OnEnter;
            On.SendExtraDamage.OnEnter += SendExtraDamage_OnEnter;
            On.ExtraDamageable.RecieveExtraDamage += ExtraDamageable_RecieveExtraDamage;
            ModHooks.HitInstanceHook += ModHooks_HitInstanceHook;
            ModHooks.AfterTakeDamageHook += ModHooks_AfterTakeDamageHook;
            ModHooks.BeforePlayerDeadHook += ModHooks_BeforePlayerDeadHook;
            HoGRoomConditions.BossHpDetected += OnBossHpDetected;

            runtimeHooksInitialized = true;
        }

        private static void ReleaseRuntimeHooks()
        {
            if (!runtimeHooksInitialized)
            {
                return;
            }

            On.SceneLoad.RecordEndTime -= SceneLoad_RecordEndTime;
            On.GameManager.Update -= GameManager_Update;
            On.BossSceneController.Update -= BossSceneController_Update;
            On.BossSceneController.EndBossScene -= BossSceneController_EndBossScene;
            On.HeroController.Die -= HeroController_Die;
            On.HeroController.FixedUpdate -= HeroController_FixedUpdate;
            On.HealthManager.Start -= HealthManager_Start;
            On.HealthManager.TakeDamage -= HealthManager_TakeDamage;
            On.HeroController.SoulGain -= HeroController_SoulGain;
            On.HeroController.TakeDamage -= HeroController_TakeDamage;
            On.EnemyDreamnailReaction.RecieveDreamImpact -= EnemyDreamnailReaction_RecieveDreamImpact;
            On.PlayerData.AddMPCharge -= PlayerData_AddMPCharge;
            On.PlayerData.TakeMP -= PlayerData_TakeMP;
            On.PlayerData.TakeReserveMP -= PlayerData_TakeReserveMP;
            On.HutongGames.PlayMaker.Actions.CallMethod.OnEnter -= CallMethod_OnEnter;
            On.HutongGames.PlayMaker.Actions.CallMethodProper.OnEnter -= CallMethodProper_OnEnter;
            On.HutongGames.PlayMaker.Actions.SendMessage.OnEnter -= SendMessage_OnEnter;
            On.PlayMakerFSM.SendEvent -= PlayMakerFSM_SendEvent;
            On.QuitToMenu.Start -= QuitToMenu_Start;
            On.SpellFluke.DoDamage -= SpellFluke_DoDamage;
            On.DamageEnemies.DoDamage -= DamageEnemies_DoDamage;
            On.DamageEffectTicker.Update -= DamageEffectTicker_Update;
            On.HitTaker.Hit -= HitTaker_Hit;
            On.HutongGames.PlayMaker.Actions.IntOperator.OnEnter -= CharmDamageIntOperator_OnEnter;
            On.SendExtraDamage.OnEnter -= SendExtraDamage_OnEnter;
            On.ExtraDamageable.RecieveExtraDamage -= ExtraDamageable_RecieveExtraDamage;
            ModHooks.HitInstanceHook -= ModHooks_HitInstanceHook;
            ModHooks.AfterTakeDamageHook -= ModHooks_AfterTakeDamageHook;
            ModHooks.BeforePlayerDeadHook -= ModHooks_BeforePlayerDeadHook;
            HoGRoomConditions.BossHpDetected -= OnBossHpDetected;

            runtimeHooksInitialized = false;
        }

        private static void SceneLoad_RecordEndTime(On.SceneLoad.orig_RecordEndTime orig, SceneLoad self, SceneLoad.Phases phase)
        {
            orig(self, phase);
            if (phase == SceneLoad.Phases.UnloadUnusedAssets && isLogging)
            {
                infoBoss.Clear();
                uniqueBossBuffersDirty = true;
                enemyScanLayerMask = 0;
                lastEnemyLayerMaskRefreshTime = 0f;
                enemyColliderBufferOverflowLogged = false;
                lastEnemyColliderOverflowWarningTime = 0f;
                ownerPathByGameObject.Clear();
                ownerPathCacheCleanupBuffer.Clear();
                lastOwnerPathCacheCleanupTime = 0f;
                ownerPathCacheCleanupCursor = 0;
                lastEnemySeedTime = 0f;
            }
        }

        private static void GameManager_Update(On.GameManager.orig_Update orig, GameManager self)
        {
            orig(self);
            if (isLogging && ReplayLogger.IsPrimaryLoggerActive())
            {
                StopLogging("ReplayLoggerPrimaryActive");
                return;
            }

            if (!isLogging || writer == null)
            {
                return;
            }

            long frameUnixTime = KeyLogFormatting.CaptureFrameUnixTime(ref cachedFrameUnixTime);
            MonitorTimeScale(frameUnixTime);
            MonitorHeroHealth(frameUnixTime);
            MonitorDebugModUi(frameUnixTime);

            long relativeMs = Math.Max(0, frameUnixTime - startUnixTime);
            int elapsedSeconds = (int)(relativeMs / 1000);
            if (elapsedSeconds != lastHudElapsedSeconds)
            {
                lastHudElapsedSeconds = elapsedSeconds;
                customCanvas?.UpdateTime(KeyLogFormatting.FormatHudElapsedTime(relativeMs));
            }

            keyScanner.Poll(frameUnixTime, HandleKeyEvent);
            MirrorInlineTimelineEvents();

            FlushKeyLogBufferIfNeeded(frameUnixTime);

        }

        private static void HandleKeyEvent(KeyCode keyCode, bool isDown, long unixTime)
        {
            float fps = Time.unscaledDeltaTime == 0 ? lastFps : 1f / Time.unscaledDeltaTime;
            lastFps = fps;

            customCanvas?.UpdateWatermark(keyCode);

            int watermarkNumber = customCanvas?.numberInCanvas?.Number ?? 0;
            Color watermarkColorStruct = customCanvas?.numberInCanvas?.Color ?? Color.white;

            long delta = unixTime - lastUnixTime;

            if (lastLoggedDeltaMs >= 0 && delta < lastLoggedDeltaMs)
            {
                FlushKeyLogBufferIfNeeded(unixTime, force: true);
                currentAttemptIndex++;
                bossCounter++;
                string timestamp = FormatUnixTimestamp(unixTime);
                long playTime = (int)((PlayerData.instance?.playTime ?? 0f) * 100);
                string startLine = $"{timestamp}|{unixTime}|{playTime}|{activeArena}| {bossCounter}*";
                bool wroteToSection = false;
                if (pressedButtonsLog != null)
                {
                    pressedButtonsLog.Add(startLine);
                    wroteToSection = true;
                }
                if (damageAndInv != null)
                {
                    damageAndInv.Add(startLine);
                    wroteToSection = true;
                }
                if (!wroteToSection)
                {
                    LogWrite.EncryptedLine(writer, startLine);
                }
            }

            lastLoggedDeltaMs = delta;

            int fpsValue = Mathf.RoundToInt(fps);
            keyLogBuffer.Add(new KeyLogEvent(delta, keyCode, isDown, watermarkNumber, (Color32)watermarkColorStruct, fpsValue));

            if (isDown && debugHotkeysByKey.Count > 0 && debugHotkeysByKey.ContainsKey(keyCode))
            {
                debugHotkeysTracker.TrackActivation(keyCode, activeArena ?? "UnknownArena", lastUnixTime, unixTime);
            }
        }

        private static string FormatUnixTimestamp(long unixTimeMilliseconds)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(unixTimeMilliseconds).ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }

        private static string FormatUnixFileSuffix(long unixTimeMilliseconds)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(unixTimeMilliseconds).ToLocalTime().ToString("dd-MM-yyyy HH-mm-ss", CultureInfo.InvariantCulture);
        }

        private static void BossSceneController_Update(On.BossSceneController.orig_Update orig, BossSceneController self)
        {
            if (isLogging)
            {
                CaptureBossLevel(self);
            }
            orig(self);
        }

        private static void BossSceneController_EndBossScene(On.BossSceneController.orig_EndBossScene orig, BossSceneController self)
        {
            RecordAttemptVictory(self);
            orig(self);
        }

        private static void RecordAttemptVictory(BossSceneController controller)
        {
            try
            {
                if (!isLogging || writer == null || controller == null)
                {
                    return;
                }

                int controllerId = controller.GetInstanceID();
                if (controllerId == lastVictoryControllerId)
                {
                    return;
                }

                lastVictoryControllerId = controllerId;
                long nowUnixTime = KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime);
                string arena = GameManager.instance?.sceneName ?? activeArena;
                hitWarnTracker.LogAttemptEnded(writer, arena, lastUnixTime, nowUnixTime, victory: true, attemptNumber: bossCounter);
            }
            catch (Exception)
            {
            }
        }

        private static IEnumerator HeroController_Die(On.HeroController.orig_Die orig, HeroController self)
        {
            RecordAttemptDeath();
            IEnumerator enumerator = orig(self);
            while (enumerator.MoveNext())
            {
                yield return enumerator.Current;
            }
        }

        private static void RecordAttemptDeath()
        {
            try
            {
                if (!isLogging || writer == null)
                {
                    return;
                }

                long nowUnixTime = KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime);
                string arena = GameManager.instance?.sceneName ?? activeArena;
                hitWarnTracker.LogDeathEvent(writer, arena, lastUnixTime, nowUnixTime);
                hitWarnTracker.LogAttemptEnded(writer, arena, lastUnixTime, nowUnixTime, victory: false, attemptNumber: bossCounter);
            }
            catch (Exception)
            {
            }
        }

        private static void CaptureBossLevel(BossSceneController controller)
        {
            if (controller == null || !IsDifficultyArena(activeArena))
            {
                return;
            }

            if (IsPaleCourtArena(activeArena) && !IsTisoArena(activeArena))
            {
                int? paleCourtLevel = TryReadPaleCourtLevel();
                if (IsValidBossLevelForArena(paleCourtLevel, activeArena))
                {
                    if (!bossLevelInFight.HasValue || bossLevelInFight.Value != paleCourtLevel.Value)
                    {
                        bossLevelInFight = paleCourtLevel.Value;
                    }
                    ObserveBossLevel(paleCourtLevel.Value);
                    return;
                }
            }

            int? level = TryReadBossLevel(controller);
            if (!IsValidBossLevelForArena(level, activeArena))
            {
                return;
            }

            if (!bossLevelInFight.HasValue || bossLevelInFight.Value != level.Value)
            {
                bossLevelInFight = level.Value;
            }

            ObserveBossLevel(level.Value);
        }

        private static void ObserveBossLevel(int level)
        {
            try
            {
                bossDifficultyTracker.Observe(activeArena, bossCounter, level, KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime), lastUnixTime);
            }
            catch (Exception e)
            {
                global::ReplayLogger.InternalDiagnostics.Error($"HoGLogger: boss difficulty record failed: {e.Message}");
            }
        }

        private static int? TryReadBossLevel(BossSceneController controller)
        {
            if (controller == null)
            {
                return null;
            }

            int? direct = TryReadLevelFromInstance(
                controller,
                new[] { "bossLevel", "BossLevel", "bossSceneLevel", "BossSceneLevel", "bossSceneTier", "BossSceneTier", "bossDifficulty", "BossDifficulty" },
                ref bossLevelOwnerType,
                ref bossLevelField,
                ref bossLevelProperty);

            if (IsValidBossLevelForArena(direct, activeArena))
            {
                return direct;
            }

            int? fromScene = TryReadBossLevelFromBossScene(controller);
            if (IsValidBossLevelForArena(fromScene, activeArena))
            {
                return fromScene;
            }

            int? fromStatic = TryReadBossLevelFromStatic(activeArena);
            if (IsValidBossLevelForArena(fromStatic, activeArena))
            {
                return fromStatic;
            }

            return null;
        }

        private static void LogReflectionReadFailureOnce(ref bool loggedFlag, string context, Exception ex)
        {
            if (loggedFlag)
            {
                return;
            }

            loggedFlag = true;
            global::ReplayLogger.InternalDiagnostics.Warn($"HoGLogger: failed to read {context}: {ex?.Message ?? "unknown error"}");
        }

        private static int? TryReadPaleCourtLevel()
        {
            if (paleCourtLevelOwnerType == null || (paleCourtLevelField == null && paleCourtLevelProperty == null))
            {
                paleCourtLevelOwnerType = TypeLookup.FindType("BossManagement.CustomWP") ?? FindTypeByName("CustomWP");
                if (paleCourtLevelOwnerType != null)
                {
                    const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                    paleCourtLevelField = paleCourtLevelOwnerType.GetField("lev", flags)
                        ?? paleCourtLevelOwnerType.GetField("Lev", flags)
                        ?? paleCourtLevelOwnerType.GetField("level", flags)
                        ?? paleCourtLevelOwnerType.GetField("Level", flags);

                    if (paleCourtLevelField == null)
                    {
                        paleCourtLevelProperty = paleCourtLevelOwnerType.GetProperty("lev", flags)
                            ?? paleCourtLevelOwnerType.GetProperty("Lev", flags)
                            ?? paleCourtLevelOwnerType.GetProperty("level", flags)
                            ?? paleCourtLevelOwnerType.GetProperty("Level", flags);
                    }
                }
            }

            if (paleCourtLevelOwnerType == null || (paleCourtLevelField == null && paleCourtLevelProperty == null))
            {
                return null;
            }

            try
            {
                object raw = paleCourtLevelProperty != null
                    ? paleCourtLevelProperty.GetCachedValue(null)
                    : paleCourtLevelField.GetCachedValue(null);

                paleCourtLevelReadFailedLogged = false;
                return TryConvertToInt(raw);
            }
            catch (Exception ex)
            {
                LogReflectionReadFailureOnce(ref paleCourtLevelReadFailedLogged, "Pale Court level", ex);
                return null;
            }
        }

        private static int? TryReadBossLevelFromBossScene(BossSceneController controller)
        {
            object bossScene = TryReadBossSceneObject(controller);
            if (bossScene == null)
            {
                return null;
            }

            return TryReadLevelFromInstance(
                bossScene,
                new[] { "bossLevel", "BossLevel", "bossSceneLevel", "BossSceneLevel", "bossSceneTier", "BossSceneTier", "bossDifficulty", "BossDifficulty", "difficulty", "Difficulty" },
                ref bossSceneLevelOwnerType,
                ref bossSceneLevelField,
                ref bossSceneLevelProperty);
        }

        private static object TryReadBossSceneObject(BossSceneController controller)
        {
            if (controller == null)
            {
                return null;
            }

            Type type = controller.GetType();
            if (bossSceneOwnerType != type)
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                bossSceneField = type.GetField("bossScene", flags) ?? type.GetField("BossScene", flags);
                bossSceneProperty = type.GetProperty("bossScene", flags) ?? type.GetProperty("BossScene", flags);
                bossSceneOwnerType = type;
            }

            try
            {
                object raw = bossSceneProperty != null
                    ? bossSceneProperty.GetCachedValue(controller)
                    : bossSceneField?.GetCachedValue(controller);
                bossSceneReadFailedLogged = false;
                return raw;
            }
            catch (Exception ex)
            {
                LogReflectionReadFailureOnce(ref bossSceneReadFailedLogged, "bossScene object", ex);
                return null;
            }
        }

        private static int? TryReadLevelFromInstance(object instance, string[] candidateNames, ref Type cachedOwner, ref FieldInfo cachedField, ref PropertyInfo cachedProperty)
        {
            if (instance == null || candidateNames == null || candidateNames.Length == 0)
            {
                return null;
            }

            Type type = instance.GetType();
            if (cachedOwner != type)
            {
                cachedField = null;
                cachedProperty = null;
                cachedOwner = type;

                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                foreach (string name in candidateNames)
                {
                    cachedField = type.GetField(name, flags);
                    if (cachedField != null)
                    {
                        break;
                    }

                    cachedProperty = type.GetProperty(name, flags);
                    if (cachedProperty != null)
                    {
                        break;
                    }
                }
            }

            if (cachedField == null && cachedProperty == null)
            {
                return null;
            }

            try
            {
                object raw = cachedProperty != null
                    ? cachedProperty.GetCachedValue(instance)
                    : cachedField?.GetCachedValue(instance);

                bossSceneLevelReadFailedLogged = false;
                return TryConvertToInt(raw);
            }
            catch (Exception ex)
            {
                LogReflectionReadFailureOnce(ref bossSceneLevelReadFailedLogged, $"boss level from {instance?.GetType().Name ?? "unknown"}", ex);
                return null;
            }
        }

        private static int? TryReadBossLevelFromStatic(string arenaName)
        {
            if (staticBossLevelGetter != null)
            {
                int? cachedValue = staticBossLevelGetter();
                if (IsValidBossLevelForArena(cachedValue, arenaName))
                {
                    return cachedValue;
                }

                staticBossLevelGetter = null;
            }

            string[] typeNames =
            {
                "BossStatue",
                "BossStatueController",
                "BossChallengeUI",
                "BossSceneController",
                "BossStatueUI",
                "GodhomeManager",
                "GGManager",
                "BossSequenceController"
            };

            string[] memberNames =
            {
                "bossLevel",
                "BossLevel",
                "ggBossLevel",
                "GGBossLevel",
                "currentBossLevel",
                "CurrentBossLevel",
                "bossSceneLevel",
                "BossSceneLevel",
                "bossDifficulty",
                "BossDifficulty",
                "difficulty",
                "Difficulty",
                "statueLevel",
                "bossStatueLevel",
                "bossChallengeLevel"
            };

            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (string typeName in typeNames)
            {
                Type type = FindTypeByName(typeName);
                if (type == null)
                {
                    continue;
                }

                foreach (string memberName in memberNames)
                {
                    FieldInfo field = type.GetField(memberName, flags);
                    if (field != null)
                    {
                        staticBossLevelGetter = () => TryConvertToInt(field.GetCachedValue(null));
                        int? value = staticBossLevelGetter();
                        if (IsValidBossLevelForArena(value, arenaName))
                        {
                            return value;
                        }
                        staticBossLevelGetter = null;
                        continue;
                    }

                    PropertyInfo property = type.GetProperty(memberName, flags);
                    if (property != null)
                    {
                        staticBossLevelGetter = () => TryConvertToInt(property.GetCachedValue(null));
                        int? value = staticBossLevelGetter();
                        if (IsValidBossLevelForArena(value, arenaName))
                        {
                            return value;
                        }
                        staticBossLevelGetter = null;
                    }
                }
            }

            return null;
        }

        private static bool IsValidBossLevel(int? level) =>
            level.HasValue && level.Value >= 0 && level.Value <= 3;

        private static bool IsValidBossLevelForArena(int? level, string arenaName)
        {
            if (!IsValidBossLevel(level))
            {
                return false;
            }

            if (level.Value == 0 && IsVariantArena(arenaName) && !IsAttunedVariantAllowed(arenaName))
            {
                return false;
            }

            return true;
        }

        private static int? TryConvertToInt(object raw)
        {
            if (raw == null)
            {
                return null;
            }

            try
            {
                if (raw is int intValue)
                {
                    return intValue;
                }

                Type rawType = raw.GetType();
                if (rawType.IsEnum)
                {
                    return Convert.ToInt32(raw, CultureInfo.InvariantCulture);
                }

                return Convert.ToInt32(raw, CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        private static void HeroController_FixedUpdate(On.HeroController.orig_FixedUpdate orig, HeroController self)
        {
            if (isLogging)
            {
                long nowUnixTime = KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime);
                cachedFrameUnixTime = nowUnixTime;
                InvCheck(nowUnixTime);

                bossPhaseThresholdTracker.Update(activeArena, uniqueBossByGameObject.Keys, lastUnixTime, nowUnixTime);
                EnemyUpdate(nowUnixTime);
            }
            orig(self);
        }

        private static IEnumerator QuitToMenu_Start(On.QuitToMenu.orig_Start orig, QuitToMenu self)
        {
            StopLogging("QuitToMenu");
            return orig(self);
        }

        private static void ApplicationQuit()
        {
            StopLogging("ApplicationQuit");
            ReleaseRuntimeHooks();
        }

        private static HitInstance ModHooks_HitInstanceHook(HutongGames.PlayMaker.Fsm owner, HitInstance hit)
        {
            if (!isLogging)
            {
                return hit;
            }

            if (owner?.GameObject == null)
            {
                return hit;
            }

            long unixTime = KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime);
            GameObject ownerObject = owner.GameObject;
            string ownerName = OwnerPathCache.GetCachedPath(ownerObject, ownerPathByGameObject);

            CharmDamageTracker.TrackPlayMakerHit(
                isLogging,
                writer,
                damageChangeTracker,
                activeArena,
                lastUnixTime,
                unixTime,
                ownerObject,
                hit);

            if (!string.IsNullOrEmpty(ownerName) &&
                ownerName.StartsWith("Knight/", StringComparison.Ordinal))
            {
                if (infoBoss.Count == 0)
                {
                    SeedTrackedBossesFromActiveScene(Time.unscaledTime);
                }

                if (TryResolveHitTargetHealthManager(hit, out HKHealthManager hitTarget))
                {
                    TrackEnemyHealthManager(hitTarget);
                    if (infoBoss.TryGetValue(hitTarget, out var hpState))
                    {
                        int currentHp = Math.Max(0, hitTarget.hp);
                        if (currentHp != hpState.lastHP)
                        {
                            int maxHp = Math.Max(hpState.maxHP, currentHp);
                            infoBoss[hitTarget] = (maxHp, currentHp);
                            uniqueBossBuffersDirty = true;
                        }
                    }
                }

                TryLogTrackedBossHpDelta(unixTime);
            }

            return hit;
        }

        private static int ModHooks_AfterTakeDamageHook(int hazardType, int damageAmount)
        {
            if (!isLogging || writer == null)
            {
                return damageAmount;
            }

            long nowUnixTime = KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime);
            string arena = GameManager.instance?.sceneName ?? activeArena;
            hitWarnTracker.LogDamageEvent(writer, arena, lastUnixTime, nowUnixTime, hazardType, damageAmount);
            return damageAmount;
        }

        private static void ModHooks_BeforePlayerDeadHook()
        {
            if (!isLogging || writer == null)
            {
                return;
            }

            long nowUnixTime = KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime);
            string arena = GameManager.instance?.sceneName ?? activeArena;
            hitWarnTracker.LogDeathEvent(writer, arena, lastUnixTime, nowUnixTime);
        }

        private static bool insideHeroSoulGain;

        private static void HeroController_SoulGain(On.HeroController.orig_SoulGain orig, HeroController self)
        {
            bool previous = insideHeroSoulGain;
            insideHeroSoulGain = isLogging;
            try
            {
                orig(self);
            }
            finally
            {
                insideHeroSoulGain = previous;
            }
        }

        private static bool insideGrubsongGain;

        private static bool realHitConfirmedThisCall;
        private static bool blockerHitConfirmedThisCall;

        private static void PlayMakerFSM_SendEvent(On.PlayMakerFSM.orig_SendEvent orig, PlayMakerFSM self, string eventName)
        {
            if (isLogging)
            {
                if (string.Equals(eventName, "HeroCtrl-HeroDamaged", StringComparison.Ordinal))
                {
                    realHitConfirmedThisCall = true;
                }
                else if (string.Equals(eventName, "HeroCtrl-TookBlockerHit", StringComparison.Ordinal))
                {
                    blockerHitConfirmedThisCall = true;
                }
            }

            orig(self, eventName);
        }

        private static void HeroController_TakeDamage(On.HeroController.orig_TakeDamage orig, HeroController self, GameObject go, CollisionSide damageSide, int damageAmount, int hazardType)
        {
            bool previous = insideGrubsongGain;
            insideGrubsongGain = isLogging;

            bool checkForRealHit = isLogging && writer != null && damageAmount > 0 && self != null;
            PlayerData dataBefore = checkForRealHit ? PlayerData.instance : null;
            int healthBefore = dataBefore != null ? dataBefore.health : 0;
            int healthBlueBefore = dataBefore != null ? dataBefore.healthBlue : 0;
            int hitsSinceShieldedBefore = 0;
            bool hasHitsSinceShielded = dataBefore != null && HitWarnTracker.TryGetHitsSinceShielded(self, out hitsSinceShieldedBefore);

            bool previousRealHitConfirmed = realHitConfirmedThisCall;
            bool previousBlockerHitConfirmed = blockerHitConfirmedThisCall;
            realHitConfirmedThisCall = false;
            blockerHitConfirmedThisCall = false;

            try
            {
                orig(self, go, damageSide, damageAmount, hazardType);
            }
            finally
            {
                insideGrubsongGain = previous;
            }

            bool realHitConfirmed = realHitConfirmedThisCall;
            bool blockerHitConfirmed = blockerHitConfirmedThisCall;
            realHitConfirmedThisCall = previousRealHitConfirmed;
            blockerHitConfirmedThisCall = previousBlockerHitConfirmed;

            if (!checkForRealHit)
            {
                return;
            }

            if (blockerHitConfirmed)
            {
                hitWarnTracker.LogInvulnerableHitEvent(
                    writer,
                    activeArena,
                    lastUnixTime,
                    KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime),
                    damageAmount,
                    hazardType,
                    "Baldur Shell");
                return;
            }

            if (realHitConfirmed && dataBefore.health == healthBefore && dataBefore.healthBlue == healthBlueBefore)
            {
                string reason;
                if (self.takeNoDamage || HitWarnTracker.IsGodhomeQolInfiniteHpActive())
                {
                    reason = "Infinite HP";
                }
                else if (hasHitsSinceShielded && hitsSinceShieldedBefore != 0 &&
                    HitWarnTracker.TryGetHitsSinceShielded(self, out int hitsSinceShieldedAfter) && hitsSinceShieldedAfter == 0)
                {
                    reason = "Carefree Melody";
                }
                else
                {
                    reason = null;
                }

                hitWarnTracker.LogInvulnerableHitEvent(
                    writer,
                    activeArena,
                    lastUnixTime,
                    KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime),
                    damageAmount,
                    hazardType,
                    reason);
            }
        }

        private static bool insideDreamNailSoulGain;

        private static void EnemyDreamnailReaction_RecieveDreamImpact(On.EnemyDreamnailReaction.orig_RecieveDreamImpact orig, EnemyDreamnailReaction self)
        {
            bool previous = insideDreamNailSoulGain;
            insideDreamNailSoulGain = isLogging;
            try
            {
                orig(self);
            }
            finally
            {
                insideDreamNailSoulGain = previous;
            }
        }

        private static string pendingFsmSoulMethodName;
        private static string pendingFsmSoulSourceLabel;
        private static string pendingFsmSoulRawOwnerPath;

        private static void CallMethod_OnEnter(On.HutongGames.PlayMaker.Actions.CallMethod.orig_OnEnter orig, HutongGames.PlayMaker.Actions.CallMethod self)
        {
            if (!isLogging || self == null)
            {
                orig(self);
                return;
            }

            TrackFsmSoulCall(self.methodName?.Value, self.Owner, self.State?.Name, () => orig(self));
        }

        private static void CallMethodProper_OnEnter(On.HutongGames.PlayMaker.Actions.CallMethodProper.orig_OnEnter orig, HutongGames.PlayMaker.Actions.CallMethodProper self)
        {
            if (!isLogging || self == null)
            {
                orig(self);
                return;
            }

            TrackFsmSoulCall(self.methodName?.Value, self.Owner, self.State?.Name, () => orig(self));
        }

        private static void SendMessage_OnEnter(On.HutongGames.PlayMaker.Actions.SendMessage.orig_OnEnter orig, HutongGames.PlayMaker.Actions.SendMessage self)
        {
            if (!isLogging || self == null)
            {
                orig(self);
                return;
            }

            TrackFsmSoulCall(self.functionCall?.FunctionName, self.Owner, self.State?.Name, () => orig(self));
        }

        private static string ResolveSpellCastName(string stateName)
        {
            if (string.IsNullOrEmpty(stateName))
            {
                return null;
            }

            PlayerData data = PlayerData.instance;

            if (stateName.Contains("Fireball"))
            {
                if (CharmDamageTracker.IsCharmEquipped((int)Charm.Flukenest))
                {
                    return "Flukenest";
                }

                return data != null && data.GetInt("fireballLevel") >= 2 ? "Shade Soul" : "Vengeful Spirit";
            }

            if (stateName.Contains("Scream"))
            {
                return data != null && data.GetInt("screamLevel") >= 2 ? "Abyss Shriek" : "Howling Wraiths";
            }

            if (stateName.Contains("Quake") || stateName.StartsWith("Level Check", StringComparison.Ordinal))
            {
                return data != null && data.GetInt("quakeLevel") >= 2 ? "Descending Dark" : "Desolate Dive";
            }

            return null;
        }

        private static void TrackFsmSoulCall(string methodName, GameObject ownerObject, string stateName, Action callOrig)
        {
            bool isTrackedSoulMethod =
                string.Equals(methodName, "AddMPCharge", StringComparison.Ordinal) ||
                string.Equals(methodName, "TakeMP", StringComparison.Ordinal) ||
                string.Equals(methodName, "TakeMPQuick", StringComparison.Ordinal) ||
                string.Equals(methodName, "TakeReserveMP", StringComparison.Ordinal);

            if (!isTrackedSoulMethod)
            {
                callOrig();
                return;
            }

            string ownerPath = ownerObject?.GetFullPath();
            string sourceLabel =
                CharmDamageTracker.IsGlowingWombSource(ownerObject, ownerPath) ? "Glowing Womb" :
                CharmDamageTracker.IsWeaversongSource(ownerObject, ownerPath) ? "Weaversong" :
                ResolveSpellCastName(stateName);

            string previousMethodName = pendingFsmSoulMethodName;
            string previousSourceLabel = pendingFsmSoulSourceLabel;
            string previousRawOwnerPath = pendingFsmSoulRawOwnerPath;
            pendingFsmSoulMethodName = methodName;
            pendingFsmSoulSourceLabel = sourceLabel;
            pendingFsmSoulRawOwnerPath = ownerPath;
            try
            {
                callOrig();
            }
            finally
            {
                pendingFsmSoulMethodName = previousMethodName;
                pendingFsmSoulSourceLabel = previousSourceLabel;
                pendingFsmSoulRawOwnerPath = previousRawOwnerPath;
            }
        }

        private static bool PlayerData_AddMPCharge(On.PlayerData.orig_AddMPCharge orig, PlayerData self, int amount)
        {
            bool trackAsHit = isLogging && insideHeroSoulGain && self != null && amount > 0;
            bool trackAsGrubsong = isLogging && insideGrubsongGain && self != null && amount > 0;
            bool trackAsDreamNail = isLogging && insideDreamNailSoulGain && self != null && amount > 0;

            bool trackAsFsmWeaversong = isLogging && !trackAsHit && !trackAsGrubsong && !trackAsDreamNail &&
                self != null && amount > 0 &&
                string.Equals(pendingFsmSoulMethodName, "AddMPCharge", StringComparison.Ordinal) &&
                string.Equals(pendingFsmSoulSourceLabel, "Weaversong", StringComparison.Ordinal);
            bool shouldTrack = trackAsHit || trackAsGrubsong || trackAsDreamNail || trackAsFsmWeaversong;
            int mainBefore = shouldTrack ? self.GetInt("MPCharge") : 0;
            int reserveBefore = shouldTrack ? self.GetInt("MPReserve") : 0;

            bool result = orig(self, amount);

            if (!shouldTrack || self == null)
            {
                return result;
            }

            int mainDelta = self.GetInt("MPCharge") - mainBefore;
            int reserveDelta = self.GetInt("MPReserve") - reserveBefore;

            int mainGained = mainDelta == amount ? amount : 0;
            int reserveGained = mainDelta == 0 && reserveDelta == amount ? amount : 0;
            if (mainGained == 0 && reserveGained == 0)
            {
                return result;
            }

            long nowUnixTime = KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime);
            if (trackAsHit)
            {
                godhomeQolTracker.RecordObservedSoulGain(activeArena, lastUnixTime, nowUnixTime, mainGained, reserveGained);
            }
            else if (trackAsGrubsong)
            {
                grubsongSoulGainTracker.RecordObservedGain(activeArena, lastUnixTime, nowUnixTime, mainGained, reserveGained);
            }
            else if (trackAsDreamNail)
            {
                dreamNailSoulGainTracker.RecordObservedGain(activeArena, lastUnixTime, nowUnixTime, mainGained, reserveGained);
            }
            else
            {
                weaversongSoulGainTracker.RecordObservedGain(activeArena, lastUnixTime, nowUnixTime, mainGained, reserveGained);
            }

            return result;
        }

        private const int GlowingWombInferredMainCost = 8;

        private static string ResolveMainSpendSourceLabel(int amount)
        {
            if (pendingFsmSoulSourceLabel != null)
            {
                return pendingFsmSoulSourceLabel;
            }

            if (amount == GlowingWombInferredMainCost && CharmDamageTracker.IsCharmEquipped((int)Charm.GlowingWomb))
            {
                return "Glowing Womb (inferred)";
            }

            return null;
        }

        private static void PlayerData_TakeMP(On.PlayerData.orig_TakeMP orig, PlayerData self, int amount)
        {
            orig(self, amount);

            if (!isLogging || self == null)
            {
                return;
            }

            soulSpentTracker.RecordMainSpend(
                activeArena,
                lastUnixTime,
                KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime),
                ResolveMainSpendSourceLabel(amount),
                pendingFsmSoulRawOwnerPath,
                amount);
        }

        private static void PlayerData_TakeReserveMP(On.PlayerData.orig_TakeReserveMP orig, PlayerData self, int amount)
        {
            orig(self, amount);

            if (!isLogging || self == null)
            {
                return;
            }

            soulSpentTracker.RecordReserveSpend(
                activeArena,
                lastUnixTime,
                KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime),
                pendingFsmSoulSourceLabel,
                pendingFsmSoulRawOwnerPath,
                amount);
        }

        private static void HealthManager_Start(On.HealthManager.orig_Start orig, HKHealthManager self)
        {
            orig(self);

            if (!isLogging || self == null)
            {
                return;
            }

            try
            {
                long nowUnixTime = KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime);
                bossSpawnHpTracker.Record(self, nowUnixTime, lastUnixTime, bossCounter);
                if (IsPaleCourtArena(activeArena))
                {
                    paleCourtStageTracker.Observe(self, activeArena, nowUnixTime, lastUnixTime, bossCounter);
                }
            }
            catch (Exception e)
            {
                global::ReplayLogger.InternalDiagnostics.Error($"HoGLogger: boss spawn HP record failed: {e.Message}");
            }
        }

        private static void HealthManager_TakeDamage(On.HealthManager.orig_TakeDamage orig, HKHealthManager self, HitInstance hitInstance)
        {
            bool shouldTrack = isLogging && self != null && ShouldTrackHealthManager(self);
            int hpBefore = shouldTrack ? Math.Max(0, self.hp) : 0;

            orig(self, hitInstance);

            if (!shouldTrack || self == null)
            {
                return;
            }

            int hpAfter = Math.Max(0, self.hp);
            if (hpAfter == hpBefore)
            {
                return;
            }

            CharmDamageTracker.TrackConfirmedHit(
                isLogging,
                writer,
                damageChangeTracker,
                activeArena,
                lastUnixTime,
                KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime),
                hitInstance,
                self.gameObject);

            TrackEnemyHealthManager(self);
            int maxHp = Math.Max(hpBefore, hpAfter);
            if (infoBoss.TryGetValue(self, out var hpState))
            {
                maxHp = Math.Max(hpState.maxHP, maxHp);
            }

            infoBoss[self] = (maxHp, hpBefore);
            uniqueBossBuffersDirty = true;
            TryLogTrackedBossHpDelta(KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime));
        }

        private static bool TryResolveHitTargetHealthManager(HitInstance hit, out HKHealthManager healthManager)
        {
            healthManager = null;
            object boxedHit = hit;
            if (boxedHit == null)
            {
                return false;
            }

            Type hitType = boxedHit.GetType();
            object rawTarget = BossTrackingHelpers.GetCachedHitTargetRaw(boxedHit, hitType);
            if (rawTarget == null)
            {
                return false;
            }

            if (rawTarget is HKHealthManager manager)
            {
                healthManager = manager;
                return healthManager != null;
            }

            if (!BossTrackingHelpers.TryUnwrapTargetGameObject(rawTarget, out GameObject targetObject))
            {
                return false;
            }

            healthManager = ResolveEnemyHealthManager(targetObject);
            return healthManager != null;
        }

        private static bool TryLogTrackedBossHpDelta(long nowUnixTime)
        {
            if (infoBoss.Count == 0)
            {
                return false;
            }

            BossTrackingHelpers.EnsureUniqueBossBuffers(infoBoss, uniqueBossByGameObject, uniqueBossSet, ref uniqueBossBuffersDirty);
            infoBossKeysBuffer.Clear();
            foreach (HKHealthManager boss in infoBoss.Keys)
            {
                infoBossKeysBuffer.Add(boss);
            }

            bool isChanged = false;
            bool removedAny = false;
            bool stageColumns = IsPaleCourtArena(activeArena);
            bool hideInactiveBosses = IsPaleCourtArena(activeArena);
            stageColumnsBuffer.Clear();
            hpInfoBuilder.Clear();
            foreach (HKHealthManager boss in infoBossKeysBuffer)
            {
                if (boss == null)
                {
                    removedAny |= infoBoss.Remove(boss);
                    continue;
                }

                if (!uniqueBossSet.Contains(boss) && !boss.isDead)
                {
                    continue;
                }

                if (!infoBoss.TryGetValue(boss, out var entry))
                {
                    continue;
                }

                if (IsHiddenFromLedger(boss, entry.maxHP, hideInactiveBosses))
                {
                    continue;
                }

                int currentHp = Math.Max(0, boss.hp);
                int maxHp = Math.Max(entry.maxHP, currentHp);
                if (currentHp != entry.lastHP || maxHp != entry.maxHP)
                {
                    infoBoss[boss] = (maxHp, currentHp);
                    entry = (maxHp, currentHp);
                    isChanged = true;
                }

                hpInfoBuilder.Append('|');
                hpInfoBuilder.Append(entry.lastHP);
                hpInfoBuilder.Append('/');
                hpInfoBuilder.Append(entry.maxHP);
                if (stageColumns)
                {
                    stageColumnsBuffer.Add(boss);
                }

                if (boss.isDead || currentHp <= 0)
                {
                    removedAny |= infoBoss.Remove(boss);
                }
            }

            if (removedAny)
            {
                uniqueBossBuffersDirty = true;
            }

            if (!isChanged)
            {
                return false;
            }

            if (stageColumns)
            {
                paleCourtStageTracker.NoteColumns(activeArena, stageColumnsBuffer, nowUnixTime, lastUnixTime, bossCounter);
            }

            damageAndInv?.Add($"{nowUnixTime - lastUnixTime}{hpInfoBuilder}|");
            return true;
        }

        private static void SpellFluke_DoDamage(On.SpellFluke.orig_DoDamage orig, SpellFluke self, GameObject obj, int upwardRecursionAmount, bool burst)
        {
            long nowUnixTime = KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime);
            FlukenestTracker.HandleDoDamage(isLogging, writer, damageChangeTracker, activeArena, lastUnixTime, nowUnixTime, orig, self, obj, upwardRecursionAmount, burst);
        }

        private static void DamageEnemies_DoDamage(On.DamageEnemies.orig_DoDamage orig, DamageEnemies self, GameObject target)
        {
            long nowUnixTime = KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime);
            CharmDamageTracker.HandleDoDamage(
                isLogging,
                writer,
                damageChangeTracker,
                activeArena,
                lastUnixTime,
                nowUnixTime,
                orig,
                self,
                target);
        }

        private static void HitTaker_Hit(On.HitTaker.orig_Hit orig, GameObject targetGameObject, HitInstance damageInstance, int recursionDepth)
        {
            long nowUnixTime = KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime);
            CharmDamageTracker.HandleHitTakerHit(
                isLogging,
                writer,
                damageChangeTracker,
                activeArena,
                lastUnixTime,
                nowUnixTime,
                orig,
                targetGameObject,
                damageInstance,
                recursionDepth);
        }

        private static void DamageEffectTicker_Update(On.DamageEffectTicker.orig_Update orig, DamageEffectTicker self)
        {
            CharmDamageTracker.HandleDamageEffectTicker(isLogging && writer != null, orig, self);
        }

        private static void CharmDamageIntOperator_OnEnter(
            On.HutongGames.PlayMaker.Actions.IntOperator.orig_OnEnter orig,
            HutongGames.PlayMaker.Actions.IntOperator self)
        {
            long nowUnixTime = KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime);
            CharmDamageTracker.HandleDirectCharmDamage(
                isLogging,
                writer,
                damageChangeTracker,
                soulSpentTracker,
                activeArena,
                lastUnixTime,
                nowUnixTime,
                orig,
                self);
        }

        private static void ExtraDamageable_RecieveExtraDamage(On.ExtraDamageable.orig_RecieveExtraDamage orig, ExtraDamageable self, ExtraDamageTypes extraDamageType)
        {
            long nowUnixTime = KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime);
            CharmDamageTracker.HandleExtraDamage(
                isLogging,
                writer,
                damageChangeTracker,
                activeArena,
                lastUnixTime,
                nowUnixTime,
                orig,
                self,
                extraDamageType);
        }

        private static void SendExtraDamage_OnEnter(On.SendExtraDamage.orig_OnEnter orig, SendExtraDamage self)
        {
            CharmDamageTracker.HandleSendExtraDamage(isLogging && writer != null, orig, self);
        }

        private static void MonitorAttemptSeparator(long nowUnixTime)
        {
            long relativeMs = nowUnixTime - startUnixTime;

            if (lastLoggedDeltaMs >= 0 && relativeMs < lastLoggedDeltaMs)
            {
                currentAttemptIndex++;
                string timestamp = FormatUnixTimestamp(nowUnixTime);
                string separator = $"-----ATTEMPT #{currentAttemptIndex}----- {timestamp}";

                damageAndInv.Add(separator);

                if (writer != null)
                {
                    try
                    {
                        LogWrite.EncryptedLine(writer, separator);
                    }
                    catch (Exception e)
                    {
                        global::ReplayLogger.InternalDiagnostics.Warn($"HoGLogger: failed to write attempt separator: {e.Message}");
                    }
                }
            }

            lastLoggedDeltaMs = relativeMs;
        }

        private static void StartLogging(string arenaName)
        {
            lock (SyncRoot)
            {
                if (isLogging || string.IsNullOrEmpty(arenaName) || ReplayLogger.IsPrimaryLoggerActive())
                {
                    return;
                }

                try
                {
                    EnsureRuntimeHooks();
                    EnsureCanvasSpritesLoaded();

                    startUnixTime = lastUnixTime;
                    bossCounter = 1;
                    masterEncryptionSession = KeyloggerLogEncryption.CreateSession();
                    masterKeyBlob = masterEncryptionSession.SessionKeyBlob;
                    AllHallownestEnhancedToggleSnapshot snapshot = AheSettingsManager.RefreshSnapshot();
                    pendingHoGDefaultFolder = HoGLoggerConditions.DefaultBucket;

                    bool requiresHp = HoGStoragePlanner.RequiresHp(arenaName);
                    if (requiresHp)
                    {
                        ResetBossHpState(arenaName);
                    }

                    int? initialHp = requiresHp ? TryGetBossHp(arenaName) : null;

                    HoGStoragePlan initialPlan = HoGStoragePlanner.GetPlan(arenaName, snapshot, initialHp, lastSceneBeforeArena);
                    ApplyHoGStoragePlan(initialPlan);
                    HoGRoomConditions.MarkPendingScene(initialPlan.NeedsHp ? arenaName : null);
                    string tempDir = ResolveTempLogDirectory();
                    currentTempFile = Path.Combine(tempDir, $"ReplayLoggerHoG_{Guid.NewGuid():N}.log");

                    pressedButtonsLog = new BufferedLogSection(null, BufferedSectionThreshold);
                    damageAndInv = new BufferedLogSection(null, BufferedSectionThreshold);
                    invWarnings = new BufferedLogSection(null, BufferedSectionThreshold);
                    speedWarnBuffer = new BufferedLogSection(null, BufferedSectionThreshold);
                    hitWarnBuffer = new BufferedLogSection(null, BufferedSectionThreshold);

                    writer = new AsyncBlockLogWriter(currentTempFile, masterKeyBlob, masterEncryptionSession, BlockSizeBytes, BlockMaxAgeMs, LogQueueCapacity);
                    CustomKnightSettingsManager.StartTracking(arenaName, lastUnixTime);

                    CoreSessionLogger.WriteEncryptedModSnapshot(writer, ModsDirectory, "---------------------------------------------------");

                    string equippedCharms = CoreSessionLogger.BuildEquippedCharmsLine();
                    LogWrite.EncryptedLine(writer, equippedCharms);
                    CoreSessionLogger.WriteEncryptedSkillLines(writer, "---------------------------------------------------");

                    int currentPlayTime = (int)((PlayerData.instance?.playTime ?? 0f) * 100);
                    int seed = (int)(lastUnixTime ^ currentPlayTime);

                    customCanvas = new CustomCanvas(new NumberInCanvas(seed), new LoadingSprite(masterKeyBlob));
                    customCanvas?.StartUpdateSprite();

                    string timestamp = FormatUnixTimestamp(lastUnixTime);

                    pressedButtonsLog?.Clear();
                    damageAndInv?.Clear();
                    invWarnings?.Clear();
                    speedWarnBuffer?.Clear();
                    hitWarnBuffer?.Clear();
                    speedWarnTracker.ClearWarnings();
                    hitWarnTracker.Reset();
                    debugModEvents = new();
                    debugHotkeyBindings = new();
                    debugHotkeyEvents = new();
                    keyLogBuffer.Clear();
                    lastKeyLogFlushTime = 0;
                    lastHudElapsedSeconds = -1;
                    keyScanner.Clear();
                    keyColorHexCache.Clear();
                    ownerPathByGameObject.Clear();
                    ownerPathCacheCleanupBuffer.Clear();
                    lastOwnerPathCacheCleanupTime = 0f;
                    ownerPathCacheCleanupCursor = 0;
                    enemyColliderBufferOverflowLogged = false;
                    lastEnemyColliderOverflowWarningTime = 0f;
                    enemyScanLayerMask = 0;
                    lastEnemyLayerMaskRefreshTime = 0f;
                    enemyColliderBuffer = new Collider2D[EnemyColliderBufferInitialSize];
                    enemyHealthManagerByGameObject.Clear();
                    enemyHealthManagerCacheCleanupBuffer.Clear();
                    lastEnemyHealthCacheCleanupTime = 0f;
                    lastEnemySeedTime = 0f;
                    enemyHealthCacheCleanupCursor = 0;
                    uniqueBossByGameObject.Clear();
                    uniqueBossSet.Clear();
                    infoBossKeysBuffer.Clear();
                    uniqueBossBuffersDirty = true;
                    hasHeroBoxState = false;
                    lastHeroBoxActive = -1;
                    heroBoxOffStartTime = -1f;
                    cachedHeroTransform = null;
                    cachedHeroBoxObject = null;
                    damageChangeTracker.Reset();
                    CharmDamageTracker.ResetHints();
                    infoBoss = new();
                    uniqueBossBuffersDirty = true;
                    debugHotkeysByKey = new();
                    currentAttemptIndex = 1;
                    lastLoggedDeltaMs = -1;
                    charmsChangeTracker.Reset();
                    foreach (ITrackerLifecycle lifecycleTracker in LifecycleTrackers())
                    {
                        lifecycleTracker.Reset();
                    }
                    ResetInlineTimelineCursors();
                    bossLevelInFight = null;
                    if (IsPaleCourtArena(arenaName) && !IsTisoArena(arenaName))
                    {
                        int? paleCourtLevel = TryReadPaleCourtLevel();
                        if (IsValidBossLevelForArena(paleCourtLevel, arenaName))
                        {
                            bossLevelInFight = paleCourtLevel.Value;
                            bossDifficultyTracker.Observe(arenaName, bossCounter, paleCourtLevel.Value, KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime), lastUnixTime);
                        }
                    }

                    string startLine = $"{timestamp}|{lastUnixTime}|{arenaName}| {bossCounter}*";
                    bool wroteToSection = false;
                    if (pressedButtonsLog != null)
                    {
                        pressedButtonsLog.Add(startLine);
                        wroteToSection = true;
                    }
                    if (damageAndInv != null)
                    {
                        damageAndInv.Add(startLine);
                        wroteToSection = true;
                    }
                    if (!wroteToSection)
                    {
                        LogWrite.EncryptedLine(writer, $"{timestamp}|{lastUnixTime}|{currentPlayTime}|{arenaName}| {bossCounter}*");
                    }

                    speedWarnTracker.Reset(Mathf.Max(Time.timeScale, 0f));
                    InitializeDebugModHooks();
                    bool initialDebugUiVisible = DebugModIntegration.TryGetUiVisible(out bool visible) && visible;
                    debugModEventsTracker.Reset(initialDebugUiVisible);
                    InitializeDebugHotkeys();
                    debugMenuTracker.Reset(initialDebugUiVisible);
                    speedWarnTracker.LogInitial(writer, arenaName, lastUnixTime);
                    godhomeQolTracker.Reset();
                    godhomeQolTracker.StartFight(arenaName, lastUnixTime);
                    cachedFrameUnixTime = lastUnixTime;

                    activeArena = arenaName;
                    isLogging = true;
                }
                catch (Exception e)
                {
                    global::ReplayLogger.InternalDiagnostics.Error($"HoGLogger: failed to start log for {arenaName}: {e.Message}");
                    try
                    {
                        writer?.Dispose();
                    }
                    catch
                    {
                    }

                    try
                    {
                        if (!string.IsNullOrWhiteSpace(currentTempFile) && File.Exists(currentTempFile))
                        {
                            File.Delete(currentTempFile);
                        }
                    }
                    catch
                    {
                    }

                    CleanupState();
                }
            }
        }

        internal static void StopLogging(string exitScene)
        {
            lock (SyncRoot)
            {
                if (!isLogging)
                {
                    return;
                }

                try
                {
                    FinalizeLog(exitScene);
                }
                catch (Exception e)
                {
                    global::ReplayLogger.InternalDiagnostics.Error($"HoGLogger: failed to finalize log: {e.Message}");
                    DisposeWriterSafely(flushBeforeDispose: true, context: "finalize failure");
                    MoveTempFileToFinalLocation();
                }
                finally
                {
                    CleanupState();
                }
            }
        }

        private static void FinalizeLog(string exitScene)
        {
            if (writer == null)
            {
                return;
            }
            _ = exitScene;

            long endUnixTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            string sessionTime = ReplayLogger.ConvertUnixTimeToTimeString((long)(Time.realtimeSinceStartup * 1000f));
            FlushKeyLogBufferIfNeeded(KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime), force: true);
            AppendActiveInvDurationWarning(endUnixTime);
            bool wrotePressedButtons = pressedButtonsLog != null && pressedButtonsLog.HasContent;
            if (wrotePressedButtons)
            {
                LogWrite.EncryptedLine(writer, "\n------------------------PRESSED BUTTONS------------------------\n");
                pressedButtonsLog.WriteEncryptedLines(writer);
                CoreSessionLogger.WriteSeparatorWithSpacing(writer);
            }

            bool wroteDamage = damageAndInv != null && damageAndInv.HasContent;
            if (wroteDamage)
            {
                LogWrite.EncryptedLine(writer, "\n------------------------DAMAGE AND WARNINGS------------------------\n");
                damageAndInv.WriteEncryptedLines(writer);
                CoreSessionLogger.WriteSeparatorWithSpacing(writer);
            }
            LogWrite.EncryptedLine(writer, $"StartTime: {ReplayLogger.ConvertUnixTimeToDateTimeString(startUnixTime)}, EndTime: {ReplayLogger.ConvertUnixTimeToDateTimeString(endUnixTime)}, TimeInPlay: {ReplayLogger.ConvertUnixTimeToTimeString(endUnixTime - startUnixTime)}, SessionTime: {sessionTime}");
            CoreSessionLogger.WriteSeparator(writer);
            LogWrite.EncryptedLine(writer, "\n\n");
            AppendHeroBoxOffDurationWarning(endUnixTime);
            CoreSessionLogger.WriteWarningsSection(writer, invWarnings);

            LogWrite.EncryptedLine(writer, "\n\n");
            if (speedWarnBuffer != null)
            {
                speedWarnBuffer.AddRange(speedWarnTracker.Warnings);
            }
            speedWarnTracker.ClearWarnings();
            CoreSessionLogger.WriteSpeedWarningsSection(writer, speedWarnBuffer);

            LogWrite.EncryptedLines(writer, HitWarnSectionHeaderLines);
            if (hitWarnBuffer != null)
            {
                hitWarnBuffer.AddRange(hitWarnTracker.Warnings);
            }
            hitWarnTracker.ClearWarnings();
            hitWarnBuffer?.WriteEncryptedLines(writer);
            LogWrite.EncryptedLines(writer, HitWarnSectionFooterLines);
            DamageChangeTracker.WriteSection(writer, damageChangeTracker);
            charmsChangeTracker.Write(writer);
            foreach (ITrackerLifecycle lifecycleTracker in LifecycleTrackers())
            {
                lifecycleTracker.Write(writer);
            }

            RefreshBucketInfo(force: true);

            AheSettingsManager.WriteSettingsWithSeparator(writer);
            LogWrite.EncryptedLine(writer, $"HoG Bucket: {currentBucketInfo.BucketLabel ?? HoGLoggerConditions.DefaultBucket}");
            LogWrite.EncryptedLine(writer, string.Empty);
            LogWrite.EncryptedLine(writer, "---------------------------------------------------");
            CustomKnightSettingsManager.WriteSettingsWithSeparator(writer);
            godhomeQolTracker.WriteSection(writer);

            DebugModEventsWriter.Write(writer, debugModEventsTracker.Events);
            DebugHotKeysWriter.Write(writer, debugHotkeysTracker.Bindings, debugHotkeysTracker.Activations);
            debugMenuTracker.WriteSection(writer);
            CoreSessionLogger.WriteSeparator(writer);

            CoreSessionLogger.WriteControlSettings(writer);

            LogWrite.Raw(writer, masterKeyBlob);
            DisposeWriterSafely(flushBeforeDispose: true, context: "finalize");

            MoveTempFileToFinalLocation();
            customCanvas?.ClearHud();
        }

        private static void DisposeWriterSafely(bool flushBeforeDispose, string context)
        {
            StreamWriter writerToDispose = writer;
            if (writerToDispose == null)
            {
                return;
            }

            writer = null;
            if (flushBeforeDispose)
            {
                try
                {
                    writerToDispose.Flush();
                }
                catch (Exception flushEx)
                {
                    global::ReplayLogger.InternalDiagnostics.Warn($"HoGLogger: failed to flush writer during {context}: {flushEx.Message}");
                }
            }

            try
            {
                writerToDispose.Dispose();
            }
            catch (Exception disposeEx)
            {
                global::ReplayLogger.InternalDiagnostics.Warn($"HoGLogger: failed to dispose writer during {context}: {disposeEx.Message}");
            }
        }

        private static void CleanupState()
        {
            string arenaToReset = activeArena;
            DisposeWriterSafely(flushBeforeDispose: true, context: "cleanup");
            customCanvas?.DestroyCanvasDelayed(2.0f);
            customCanvas = null;
            masterKeyBlob = null;
            masterEncryptionSession = null;
            speedWarnTracker = new SpeedWarnTracker();
            pressedButtonsLog?.Clear();
            damageAndInv?.Clear();
            invWarnings?.Clear();
            speedWarnBuffer?.Clear();
            hitWarnBuffer?.Clear();
            pressedButtonsLog = null;
            damageAndInv = null;
            invWarnings = null;
            speedWarnBuffer = null;
            hitWarnBuffer = null;
            debugModEvents = new();
            debugHotkeyBindings = new();
            debugHotkeyEvents = new();
            keyLogBuffer.Clear();
            lastKeyLogFlushTime = 0;
            lastHudElapsedSeconds = -1;
            keyScanner.Clear();
            keyColorHexCache.Clear();
            ownerPathByGameObject.Clear();
            ownerPathCacheCleanupBuffer.Clear();
            lastOwnerPathCacheCleanupTime = 0f;
            ownerPathCacheCleanupCursor = 0;
            enemyColliderBufferOverflowLogged = false;
            lastEnemyColliderOverflowWarningTime = 0f;
            enemyScanLayerMask = 0;
            lastEnemyLayerMaskRefreshTime = 0f;
            enemyColliderBuffer = new Collider2D[EnemyColliderBufferInitialSize];
            enemyHealthManagerByGameObject.Clear();
            enemyHealthManagerCacheCleanupBuffer.Clear();
            lastEnemyHealthCacheCleanupTime = 0f;
            lastEnemySeedTime = 0f;
            enemyHealthCacheCleanupCursor = 0;
            uniqueBossByGameObject.Clear();
            uniqueBossSet.Clear();
            infoBossKeysBuffer.Clear();
            uniqueBossBuffersDirty = true;
            hasHeroBoxState = false;
            lastHeroBoxActive = -1;
            heroBoxOffStartTime = -1f;
            cachedHeroTransform = null;
            cachedHeroBoxObject = null;
            debugHotkeysTracker.Reset();
            debugMenuTracker.Reset();
            godhomeQolTracker.Reset();
            damageChangeTracker = new();
            CharmDamageTracker.ResetHints();
            currentAttemptIndex = 1;
            lastLoggedDeltaMs = -1;
            hitWarnTracker = new HitWarnTracker();
            ResetInlineTimelineCursors();
            infoBoss = new();
            debugHotkeysByKey = new();
            isLogging = false;
            activeArena = null;
            bossLevelInFight = null;
            lastVictoryControllerId = 0;
            lastSceneBeforeArena = string.Empty;
            bossCounter = 0;
            isInvincible = false;
            invTimer = 0f;
            currentTempFile = null;
            cachedFrameUnixTime = 0;
            currentBucketInfo = HoGBucketInfo.CreateDefault(null);
            AheSettingsManager.Reset();
            CustomKnightSettingsManager.Reset();
            pendingHoGDefaultFolder = HoGLoggerConditions.DefaultBucket;
            debugModEventsTracker.Reset();
            HoGRoomConditions.MarkPendingScene(null);

            if (HoGStoragePlanner.RequiresHp(arenaToReset))
            {
                ResetBossHpState(arenaToReset);
            }

            ReleaseRuntimeHooks();
        }

        private static void MoveTempFileToFinalLocation()
        {
            if (string.IsNullOrEmpty(currentTempFile) || activeArena == null)
            {
                return;
            }

            try
            {
                RefreshBucketInfo(force: true);
                string displayName = PathSanitizer.SanitizeSegment(currentBucketInfo.BossFolder ?? HoGLoggerConditions.GetDisplayName(activeArena), "Unknown");
                string fileLabel = string.IsNullOrEmpty(currentBucketInfo.FilePrefix) ? displayName : PathSanitizer.SanitizeSegment(currentBucketInfo.FilePrefix, displayName);
                if (string.Equals(activeArena, "GG_Radiance", StringComparison.Ordinal))
                {
                    string anyRadianceRoot = HoGStoragePlanner.ResolveAnyRadianceRootFolder();
                    if (string.Equals(anyRadianceRoot, "AnyRadiance 3.0", StringComparison.Ordinal))
                    {
                        fileLabel = anyRadianceRoot;
                    }
                }
                string timeSuffix = FormatUnixFileSuffix(lastUnixTime);
                string rootFolder = string.IsNullOrEmpty(currentBucketInfo.RootFolder) ? HoGLoggerConditions.DefaultBucket : PathSanitizer.SanitizeSegment(currentBucketInfo.RootFolder, HoGLoggerConditions.DefaultBucket);

                bool isP5Health = SafeGodseekerQolIntegration.IsP5HealthEnabled();
                if (isP5Health)
                {
                    rootFolder = "P5 HEALTH";
                }

                string finalDir = Path.Combine(DllDirectory, rootFolder, displayName);
                string difficultyFolder = GetDifficultyFolderName();
                if (!string.IsNullOrEmpty(difficultyFolder))
                {
                    finalDir = Path.Combine(finalDir, difficultyFolder);
                }

                string prefix = GetDifficultyPrefix();
                string p5Prefix = isP5Health ? "P5 HP " : string.Empty;

                string runFolderName = $"{p5Prefix}{prefix}{fileLabel} ({timeSuffix})";
                finalDir = Path.Combine(finalDir, runFolderName);
                Directory.CreateDirectory(finalDir);
                string finalPath = Path.Combine(finalDir, $"{runFolderName}.log");
                if (File.Exists(finalPath))
                {
                    File.Delete(finalPath);
                }

                if (File.Exists(currentTempFile))
                {
                    FileMoveHelper.MoveSafely(currentTempFile, finalPath);
                    string toastText = $"{currentBucketInfo.BucketLabel ?? HoGLoggerConditions.DefaultBucket}: {Path.GetFileName(finalPath)}";
                    SavedLogToast.Record(toastText);
                    customCanvas?.ShowSavedFileToast(toastText, ReplayLogger.GetHudToastSeconds());
                }
            }
            catch (Exception e)
            {
                global::ReplayLogger.InternalDiagnostics.Error($"HoGLogger: failed to move log file '{currentTempFile}': {e.Message}");
            }
        }

        private static string ResolveTempLogDirectory()
        {
            return DllDirectory;
        }

        private static string GetDifficultyPrefix()
        {
            if (!IsDifficultyArena(activeArena))
            {
                return string.Empty;
            }

            string label = GetDifficultyLabel();
            return $"[{label}] ";
        }

        private static string GetDifficultyFolderName()
        {
            if (!IsDifficultyArena(activeArena))
            {
                return null;
            }

            return GetDifficultyLabel();
        }

        private static string GetDifficultyLabel()
        {
            if (!bossLevelInFight.HasValue)
            {
                return "None";
            }

            switch (bossLevelInFight.Value)
            {
                case 0:
                    return "Attuned";
                case 1:
                    return "Ascended";
                case 2:
                case 3:
                    return "Radiant";
                default:
                    return "None";
            }
        }

        private static void ApplyDifficultyBucketOverride()
        {
            if (IsPaleCourtArena(activeArena) || !IsDifficultyArena(activeArena))
            {
                return;
            }

            AllHallownestEnhancedToggleSnapshot snapshot = AheSettingsManager.RefreshSnapshot();
            string rootFolder = ResolveHoGRoot(snapshot);
            if (string.Equals(activeArena, "GG_Radiance", StringComparison.Ordinal))
            {
                string anyRadianceRoot = HoGStoragePlanner.ResolveAnyRadianceRootFolder();
                if (!string.IsNullOrEmpty(anyRadianceRoot))
                {
                    rootFolder = anyRadianceRoot;
                }
                else
                {
                    bool coreToggles = snapshot.Available &&
                        snapshot.MainSwitch &&
                        snapshot.StrengthenAllBoss &&
                        snapshot.StrengthenAllMonsters;
                    if (coreToggles && snapshot.MoreRadiance)
                    {
                        rootFolder = "HoG AHE+";
                    }
                }
            }

            string bossFolder = currentBucketInfo.BossFolder ?? HoGLoggerConditions.GetDisplayName(activeArena);
            string filePrefix = currentBucketInfo.FilePrefix;
            currentBucketInfo = new HoGBucketInfo(rootFolder, bossFolder, rootFolder, filePrefix);
            pendingHoGDefaultFolder = rootFolder;
        }

        private static string ResolveHoGRoot(AllHallownestEnhancedToggleSnapshot snapshot)
        {
            if (snapshot.Available && snapshot.MainSwitch && snapshot.StrengthenAllBoss && snapshot.StrengthenAllMonsters)
            {
                return snapshot.OriginalHp ? "HoG AHE" : "HoG AHE+";
            }

            return "HoG";
        }

        private static void ApplyHoGStoragePlan(HoGStoragePlan plan)
        {
            currentBucketInfo = plan.BucketInfo;
            pendingHoGDefaultFolder = plan.BucketInfo.RootFolder ?? HoGLoggerConditions.DefaultBucket;

            if (string.IsNullOrEmpty(plan.HpScene))
            {
                return;
            }

            BossHpState state = GetBossHpState(plan.HpScene);
            if (state == null)
            {
                return;
            }

            state.Waiting = plan.NeedsHp;

            if (plan.HpValue.HasValue)
            {
                state.Cached = plan.HpValue;
                state.Highest = Math.Max(state.Highest, plan.HpValue.Value);
            }

            if (!plan.NeedsHp)
            {
                HoGRoomConditions.MarkPendingScene(null);
            }
        }

        private static void OnBossHpDetected(string sceneName, int hp)
        {
            if (string.IsNullOrEmpty(sceneName) || hp <= 0)
            {
                return;
            }

            BossHpState state = GetBossHpState(sceneName);
            if (state == null)
            {
                return;
            }

            state.Cached = hp;
            state.Highest = Math.Max(state.Highest, hp);
            state.Min = Math.Min(state.Min, hp);
            state.Waiting = false;

            if (isLogging && string.Equals(activeArena, sceneName, StringComparison.Ordinal))
            {
                HoGStoragePlan plan = HoGStoragePlanner.GetPlan(activeArena, AheSettingsManager.CurrentSnapshot, state.Highest, lastSceneBeforeArena);
                ApplyHoGStoragePlan(plan);
            }
        }

        private static int? TryGetBossHp(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                return null;
            }

            bool captureMinimum = string.Equals(sceneName, "GG_Nailmasters", StringComparison.Ordinal);
            int? minHp = null;

            foreach (HKHealthManager manager in UnityEngine.Object.FindObjectsOfType<HKHealthManager>())
            {
                if (manager == null || manager.gameObject == null)
                {
                    continue;
                }

                Scene scene = manager.gameObject.scene;
                if (!scene.IsValid() || !string.Equals(scene.name, sceneName, StringComparison.Ordinal))
                {
                    continue;
                }

                int hp = manager.hp;
                if (hp > 0)
                {
                    BossHpState state = GetBossHpState(sceneName);
                    if (state != null)
                    {
                        state.Cached = hp;
                        state.Highest = Math.Max(state.Highest, hp);
                        state.Min = Math.Min(state.Min, hp);
                    }

                    if (captureMinimum)
                    {
                        if (!minHp.HasValue || hp < minHp.Value)
                        {
                            minHp = hp;
                        }
                        continue;
                    }

                    return hp;
                }
            }

            return minHp;
        }

        private static void RefreshBucketInfo(bool force)
        {
            if (activeArena == null)
            {
                return;
            }

            if (!force && !IsWaitingForHp(activeArena) && currentBucketInfo.RootFolder != HoGLoggerConditions.DefaultBucket)
            {
                return;
            }

            AllHallownestEnhancedToggleSnapshot snapshot = AheSettingsManager.RefreshSnapshot();
            if (snapshot.Available)
            {

                string previousScene = lastSceneBeforeArena;
                HoGStoragePlan plan = HoGStoragePlanner.GetPlan(activeArena, snapshot, GetStoredHp(activeArena), previousScene);
                ApplyHoGStoragePlan(plan);
            }

            ApplyDifficultyBucketOverride();
        }

        private static bool IsDifficultyArena(string arenaName)
        {
            if (string.IsNullOrEmpty(arenaName))
            {
                return false;
            }

            if (arenaName.StartsWith("GG_", StringComparison.Ordinal))
            {
                return true;
            }

            return IsPaleCourtArena(arenaName);
        }

        private static bool IsPaleCourtArena(string arenaName)
        {
            if (string.IsNullOrEmpty(arenaName))
            {
                return false;
            }

            if (IsTisoArena(arenaName))
            {
                return true;
            }

            if (string.Equals(arenaName, HoGLoggerConditions.PaleCourtDryyaScene, StringComparison.Ordinal) ||
                string.Equals(arenaName, HoGLoggerConditions.PaleCourtHegemolScene, StringComparison.Ordinal) ||
                string.Equals(arenaName, HoGLoggerConditions.PaleCourtZemerScene, StringComparison.Ordinal) ||
                string.Equals(arenaName, HoGLoggerConditions.PaleCourtIsmaScene, StringComparison.Ordinal) ||
                string.Equals(arenaName, HoGLoggerConditions.ChampionsCallScene, StringComparison.Ordinal))
            {
                return true;
            }

            if (string.Equals(arenaName, HoGLoggerConditions.PaleCourtWhiteDefenderScene, StringComparison.Ordinal))
            {
                return string.Equals(lastSceneBeforeArena, HoGLoggerConditions.PaleCourtEntryScene, StringComparison.Ordinal);
            }

            return false;
        }

        // Objects that are not part of this fight must not show up as HP columns: anything that lives in another
        // scene (Pale Court preloads an inactive "HK Prime" copy that exists in every Godhome fight), an inactive
        // "HK Prime" copy, and in Pale Court fights also inactive or tiny (under 100 HP) objects.
        private static bool IsHiddenFromLedger(HKHealthManager boss, int maxHp, bool paleCourtArena)
        {
            GameObject host = boss.gameObject;
            if (host == null)
            {
                return true;
            }

            string arenaScene = GameManager.instance?.sceneName ?? activeArena;
            string hostScene = host.scene.name;
            if (!string.IsNullOrEmpty(arenaScene) && !string.IsNullOrEmpty(hostScene)
                && !string.Equals(hostScene, arenaScene, StringComparison.Ordinal))
            {
                return true;
            }

            bool inactive = !host.activeInHierarchy;
            if (inactive && string.Equals(host.name, "HK Prime", StringComparison.Ordinal))
            {
                return true;
            }

            return paleCourtArena && (inactive || maxHp < 100);
        }

        private static bool IsTisoArena(string arenaName) =>
            string.Equals(arenaName, "GG_Brooding_Mawlek_V", StringComparison.Ordinal) &&
            PaleCourtStatueIntegration.IsAltStatueMawlekEnabled();

        private static bool IsVariantArena(string arenaName) =>
            !string.IsNullOrEmpty(arenaName) && arenaName.EndsWith("_V", StringComparison.Ordinal);

        private static bool IsAttunedVariantAllowed(string arenaName) =>
            string.Equals(arenaName, "GG_Mantis_Lords_V", StringComparison.Ordinal) ||
            (string.Equals(arenaName, "GG_Brooding_Mawlek_V", StringComparison.Ordinal) &&
             PaleCourtStatueIntegration.IsAltStatueMawlekEnabled());

        private static void EnsureCanvasSpritesLoaded()
        {
            if (CustomCanvas.flagSpriteTrue == null)
            {
                CustomCanvas.flagSpriteTrue = CustomCanvas.LoadEmbeddedSprite("ElegantKey.png");
            }
            if (CustomCanvas.flagSpriteFalse == null)
            {
                CustomCanvas.flagSpriteFalse = CustomCanvas.LoadEmbeddedSprite("Geo.png");
            }
        }
        private static BossHpState GetBossHpState(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName) || !HoGStoragePlanner.RequiresHp(sceneName))
            {
                return null;
            }

            if (!bossHpStates.TryGetValue(sceneName, out BossHpState state))
            {
                state = new BossHpState();
                bossHpStates[sceneName] = state;
            }

            return state;
        }

        private static void ResetBossHpState(string sceneName)
        {
            var state = GetBossHpState(sceneName);
            if (state == null)
            {
                return;
            }

            state.Waiting = false;
            state.Cached = null;
            state.Highest = 0;
            state.Min = int.MaxValue;
        }

        private static bool IsWaitingForHp(string sceneName)
        {
            var state = GetBossHpState(sceneName);
            return state != null && state.Waiting;
        }

        private static int? GetStoredHp(string sceneName)
        {
            var state = GetBossHpState(sceneName);
            if (state == null)
            {
                return null;
            }

            if (string.Equals(sceneName, "GG_Nailmasters", StringComparison.Ordinal) &&
                state.Min < int.MaxValue)
            {
                return state.Min;
            }

            if (state.Highest > 0)
            {
                return state.Highest;
            }

            return state.Cached;
        }

        private static HKHealthManager ResolveEnemyHealthManager(GameObject enemyObject)
        {
            if (enemyObject == null)
            {
                return null;
            }

            if (!enemyHealthManagerByGameObject.TryGetValue(enemyObject, out HKHealthManager healthManager) || healthManager == null)
            {
                healthManager = enemyObject.GetComponent<HKHealthManager>();
                if (healthManager == null)
                {
                    healthManager = enemyObject.GetComponentInParent<HKHealthManager>();
                }
                if (healthManager == null)
                {
                    healthManager = enemyObject.GetComponentInChildren<HKHealthManager>(includeInactive: true);
                }
                if (healthManager == null)
                {
                    Transform rootTransform = enemyObject.transform?.root;
                    if (rootTransform != null)
                    {
                        healthManager = rootTransform.GetComponentInChildren<HKHealthManager>(includeInactive: true);
                    }
                }
                enemyHealthManagerByGameObject[enemyObject] = healthManager;
            }

            IncludeEnemyLayer(enemyObject.layer);
            if (healthManager?.gameObject != null)
            {
                IncludeEnemyLayer(healthManager.gameObject.layer);
            }
            return healthManager;
        }

        private static void TrackEnemyHealthManager(HKHealthManager healthManager)
        {
            if (healthManager == null || healthManager.hp <= 0)
            {
                return;
            }

            if (infoBoss.ContainsKey(healthManager))
            {
                return;
            }

            infoBoss[healthManager] = (healthManager.hp, healthManager.hp);
            IncludeEnemyLayer(healthManager.gameObject.layer);
            uniqueBossBuffersDirty = true;
        }

        private static void SeedTrackedBossesFromActiveScene(float now)
        {
            if (now - lastEnemySeedTime < EnemySeedRetryIntervalSeconds)
            {
                return;
            }

            lastEnemySeedTime = now;
            string sceneName = GameManager.instance?.sceneName ?? activeArena;
            HKHealthManager[] managers = UnityEngine.Object.FindObjectsOfType<HKHealthManager>();
            if (managers == null || managers.Length == 0)
            {
                return;
            }

            int addedByScene = 0;
            if (!string.IsNullOrEmpty(sceneName))
            {
                for (int i = 0; i < managers.Length; i++)
                {
                    HKHealthManager manager = managers[i];
                    if (!ShouldTrackHealthManager(manager))
                    {
                        continue;
                    }

                    Scene scene = manager.gameObject.scene;
                    if (!scene.IsValid() || !string.Equals(scene.name, sceneName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    enemyHealthManagerByGameObject[manager.gameObject] = manager;
                    TrackEnemyHealthManager(manager);
                    addedByScene++;
                }
            }

            if (addedByScene > 0)
            {
                return;
            }

            for (int i = 0; i < managers.Length; i++)
            {
                HKHealthManager manager = managers[i];
                if (!ShouldTrackHealthManager(manager))
                {
                    continue;
                }

                enemyHealthManagerByGameObject[manager.gameObject] = manager;
                TrackEnemyHealthManager(manager);
            }
        }

        private static bool ShouldTrackHealthManager(HKHealthManager manager) => BossTrackingHelpers.ShouldTrackHealthManager(manager);

        private static void IncludeEnemyLayer(int layer)
        {
            if ((uint)layer >= 32u)
            {
                return;
            }

            enemyScanLayerMask |= 1 << layer;
        }

        private static int ResolveEnemyLayerMask(HeroController hero, float now)
        {
            if (hero == null)
            {
                return enemyScanLayerMask != 0 ? enemyScanLayerMask : Physics2D.AllLayers;
            }

            if (enemyHealthManagerByGameObject.Count == 0 && infoBoss.Count == 0)
            {
                enemyScanLayerMask = Physics2D.AllLayers;
                lastEnemyLayerMaskRefreshTime = now;
                return enemyScanLayerMask;
            }

            if (enemyScanLayerMask != 0 && now - lastEnemyLayerMaskRefreshTime < EnemyLayerMaskRefreshIntervalSeconds)
            {
                return enemyScanLayerMask;
            }

            int mask = BossTrackingHelpers.BuildHeroCollisionLayerMask(hero.gameObject.layer);

            foreach (GameObject enemyObject in enemyHealthManagerByGameObject.Keys)
            {
                if (enemyObject == null)
                {
                    continue;
                }

                mask |= 1 << enemyObject.layer;
            }

            foreach (HKHealthManager boss in infoBoss.Keys)
            {
                if (boss == null || boss.gameObject == null)
                {
                    continue;
                }

                mask |= 1 << boss.gameObject.layer;
            }

            enemyScanLayerMask = mask;
            lastEnemyLayerMaskRefreshTime = now;
            return enemyScanLayerMask;
        }

        private static void InvCheck(long nowUnixTime)
        {
            if (HeroController.instance == null || PlayerData.instance == null)
            {
                return;
            }

            bool shouldBeInvincible =
                HeroController.instance.cState.invulnerable ||
                PlayerData.instance.isInvincible ||
                HeroController.instance.cState.shadowDashing ||
                HeroController.instance.damageMode == DamageMode.HAZARD_ONLY ||
                HeroController.instance.damageMode == DamageMode.NO_DAMAGE;

            BossTrackingHelpers.EnsureUniqueBossBuffers(infoBoss, uniqueBossByGameObject, uniqueBossSet, ref uniqueBossBuffersDirty);
            var bossList = uniqueBossByGameObject.Values;

            if (shouldBeInvincible && !isInvincible)
            {
                isInvincible = true;
                invTimer = 0f;

                string hpInfo = BuildTrackedHpInfo(bossList);
                damageAndInv?.Add($"{nowUnixTime - lastUnixTime}{hpInfo}|(INV ON)|");
            }

            if (!shouldBeInvincible && isInvincible)
            {
                isInvincible = false;
                string hpInfo = BuildTrackedHpInfo(bossList);
                damageAndInv?.Add($"{nowUnixTime - lastUnixTime}{hpInfo}|(INV OFF, {invTimer.ToString("F3", CultureInfo.InvariantCulture)})|");
                if (invTimer > 2.6f)
                {
                    invWarnings?.Add($"|{activeArena}|+{nowUnixTime - lastUnixTime}{hpInfo}|(INV OFF, {invTimer.ToString("F3", CultureInfo.InvariantCulture)})");
                }
                invTimer = 0f;
            }

            if (isInvincible)
            {
                invTimer += Time.fixedDeltaTime;
            }

            TrackHeroBoxActive(bossList, nowUnixTime);
        }

        private static void TrackHeroBoxActive(IEnumerable<HKHealthManager> bossList, long nowUnixTime)
        {
            var hero = HeroController.instance;
            if (hero == null)
            {
                cachedHeroTransform = null;
                cachedHeroBoxObject = null;
                return;
            }

            GameObject heroBoxObject = BossTrackingHelpers.ResolveHeroBoxObject(hero, ref cachedHeroTransform, ref cachedHeroBoxObject);
            int heroBoxActive = heroBoxObject != null ? (heroBoxObject.activeInHierarchy ? 1 : 0) : -1;

            if (!hasHeroBoxState)
            {
                WriteHeroBoxActiveWarning(heroBoxActive, bossList, null, nowUnixTime);
                hasHeroBoxState = true;
                lastHeroBoxActive = heroBoxActive;
                heroBoxOffStartTime = heroBoxActive == 0 ? Time.unscaledTime : -1f;
                return;
            }

            if (heroBoxActive != lastHeroBoxActive)
            {
                WriteHeroBoxActiveWarning(heroBoxActive, bossList, lastHeroBoxActive, nowUnixTime);
                lastHeroBoxActive = heroBoxActive;
                heroBoxOffStartTime = heroBoxActive == 0 ? Time.unscaledTime : -1f;
            }
        }

        private static void WriteHeroBoxActiveWarning(int currentState, IEnumerable<HKHealthManager> bossList, int? previousState, long nowUnixTime)
        {
            string hpInfo = BuildHeroBoxHpInfo(bossList);
            string message = previousState.HasValue
                ? $"{BossTrackingHelpers.FormatState(previousState.Value)} -> {BossTrackingHelpers.FormatState(currentState)}"
                : BossTrackingHelpers.FormatState(currentState);
            string warning = $"|{activeArena}|+{nowUnixTime - lastUnixTime}{hpInfo}|HeroBoxActive: {message}";
            invWarnings?.Add(warning);
            damageAndInv?.Add($"{nowUnixTime - lastUnixTime}{hpInfo}|HeroBoxActive: {message}|");
        }

        private static void AppendHeroBoxOffDurationWarning(long nowUnixTime)
        {
            if (!hasHeroBoxState || lastHeroBoxActive != 0 || heroBoxOffStartTime < 0f)
            {
                return;
            }

            BossTrackingHelpers.EnsureUniqueBossBuffers(infoBoss, uniqueBossByGameObject, uniqueBossSet, ref uniqueBossBuffersDirty);
            string hpInfo = BuildHeroBoxHpInfo(uniqueBossByGameObject.Values);
            float duration = Time.unscaledTime - heroBoxOffStartTime;
            string warning = $"|{activeArena}|+{nowUnixTime - lastUnixTime}{hpInfo}|HeroBoxActive Off Duration: {duration.ToString("F3", CultureInfo.InvariantCulture)}s";
            invWarnings?.Add(warning);
            damageAndInv?.Add($"{nowUnixTime - lastUnixTime}{hpInfo}|HeroBoxActive Off Duration: {duration.ToString("F3", CultureInfo.InvariantCulture)}s|");
        }

        private static void AppendActiveInvDurationWarning(long nowUnixTime)
        {
            if (!isInvincible)
            {
                return;
            }

            BossTrackingHelpers.EnsureUniqueBossBuffers(infoBoss, uniqueBossByGameObject, uniqueBossSet, ref uniqueBossBuffersDirty);
            string hpInfo = BuildTrackedHpInfo(uniqueBossByGameObject.Values);
            string duration = invTimer.ToString("F3", CultureInfo.InvariantCulture);
            string invLine = $"{nowUnixTime - lastUnixTime}{hpInfo}|(INV OFF, {duration})|";
            invWarnings?.Add($"|{activeArena}|+{nowUnixTime - lastUnixTime}{hpInfo}|(INV OFF, {duration})");
            damageAndInv?.Add(invLine);

            isInvincible = false;
            invTimer = 0f;
        }

        private static string BuildHeroBoxHpInfo(IEnumerable<HKHealthManager> bossList)
        {
            if (bossList == null)
            {
                return string.Empty;
            }

            hpInfoBuilder.Clear();
            bool hideInactiveBosses = IsPaleCourtArena(activeArena);
            foreach (var boss in bossList)
            {
                if (infoBoss.TryGetValue(boss, out var hp))
                {
                    if (IsHiddenFromLedger(boss, hp.maxHP, hideInactiveBosses))
                    {
                        continue;
                    }

                    hpInfoBuilder.Append('|');
                    hpInfoBuilder.Append(hp.lastHP);
                    hpInfoBuilder.Append('/');
                    hpInfoBuilder.Append(hp.maxHP);
                }
            }

            return hpInfoBuilder.ToString();
        }

        private static string BuildTrackedHpInfo(IEnumerable<HKHealthManager> bossList)
        {
            if (bossList == null)
            {
                return string.Empty;
            }

            hpInfoBuilder.Clear();
            bool hideInactiveBosses = IsPaleCourtArena(activeArena);
            foreach (HKHealthManager boss in bossList)
            {
                var entry = infoBoss[boss];
                if (IsHiddenFromLedger(boss, entry.maxHP, hideInactiveBosses))
                {
                    continue;
                }

                hpInfoBuilder.Append('|');
                hpInfoBuilder.Append(entry.lastHP);
                hpInfoBuilder.Append('/');
                hpInfoBuilder.Append(entry.maxHP);
            }

            return hpInfoBuilder.ToString();
        }

        private static void EnemyUpdate(long nowUnixTime)
        {
            HeroController hero = HeroController.instance;
            if (hero == null)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now - lastEnemyUpdateTime < EnemyUpdateIntervalSeconds)
            {
                return;
            }
            lastEnemyUpdateTime = now;
            BossTrackingHelpers.CleanupEnemyHealthManagerCacheIfNeeded(enemyHealthManagerByGameObject, enemyHealthManagerCacheCleanupBuffer, ref lastEnemyHealthCacheCleanupTime, ref enemyHealthCacheCleanupCursor, now);
            OwnerPathCache.CleanupIfNeeded(ownerPathByGameObject, ownerPathCacheCleanupBuffer, ref lastOwnerPathCacheCleanupTime, ref ownerPathCacheCleanupCursor, now);
            if (infoBoss.Count == 0)
            {
                SeedTrackedBossesFromActiveScene(now);
            }
            float searchRadius = 100f;
            Vector2 searchSize = Vector2.one * searchRadius;
            int enemyLayer = ResolveEnemyLayerMask(hero, now);

            int colliderCount = BossTrackingHelpers.CollectEnemyCollidersNonAlloc(ref enemyColliderBuffer, hero.transform.position, searchSize, enemyLayer);
            int processedColliderCount = Math.Min(colliderCount, enemyColliderBuffer.Length);
            bool isSaturated = colliderCount >= enemyColliderBuffer.Length;
            if (isSaturated)
            {
                if (!enemyColliderBufferOverflowLogged || now - lastEnemyColliderOverflowWarningTime >= EnemyColliderOverflowWarnIntervalSeconds)
                {
                    global::ReplayLogger.InternalDiagnostics.Warn($"HoGLogger: enemy collider buffer saturated at {enemyColliderBuffer.Length} entries; truncating enemy scan for this tick.");
                    enemyColliderBufferOverflowLogged = true;
                    lastEnemyColliderOverflowWarningTime = now;
                }
            }
            else
            {
                enemyColliderBufferOverflowLogged = false;
            }

            for (int i = 0; i < processedColliderCount; i++)
            {
                Collider2D collider = enemyColliderBuffer[i];
                if (collider == null)
                {
                    continue;
                }

                GameObject enemyObject = collider.gameObject;
                if (!enemyObject.activeInHierarchy)
                {
                    continue;
                }

                HKHealthManager enemyHealthManager = ResolveEnemyHealthManager(enemyObject);
                TrackEnemyHealthManager(enemyHealthManager);
            }

            hpInfoBuilder.Clear();
            bool stageColumns = IsPaleCourtArena(activeArena);
            bool hideInactiveBosses = IsPaleCourtArena(activeArena);
            stageColumnsBuffer.Clear();

            BossTrackingHelpers.EnsureUniqueBossBuffers(infoBoss, uniqueBossByGameObject, uniqueBossSet, ref uniqueBossBuffersDirty);
            infoBossKeysBuffer.Clear();
            foreach (HKHealthManager boss in infoBoss.Keys)
            {
                infoBossKeysBuffer.Add(boss);
            }

            foreach (HKHealthManager boss in infoBossKeysBuffer)
            {
                if (boss == null)
                {
                    continue;
                }

                if (!uniqueBossSet.Contains(boss) && !boss.isDead)
                {
                    continue;
                }

                if (IsHiddenFromLedger(boss, infoBoss[boss].maxHP, hideInactiveBosses))
                {
                    continue;
                }

                if (boss.hp != infoBoss[boss].lastHP)
                {
                    infoBoss[boss] = (infoBoss[boss].maxHP, boss.hp);
                    isChange = true;
                }

                var entry = infoBoss[boss];
                hpInfoBuilder.Append('|');
                hpInfoBuilder.Append(entry.lastHP);
                hpInfoBuilder.Append('/');
                hpInfoBuilder.Append(entry.maxHP);
                if (stageColumns)
                {
                    stageColumnsBuffer.Add(boss);
                }
            }

            if (isChange)
            {
                if (stageColumns)
                {
                    paleCourtStageTracker.NoteColumns(activeArena, stageColumnsBuffer, nowUnixTime, lastUnixTime, bossCounter);
                }

                string hpInfo = hpInfoBuilder.ToString();
                damageAndInv?.Add($"{nowUnixTime - lastUnixTime}{hpInfo}|");
            }
            isChange = false;

            if (stageColumns)
            {
                paleCourtStageTracker.Poll(activeArena, nowUnixTime, lastUnixTime, bossCounter, infoBoss);
            }

            bool removedAnyBoss = false;
            foreach (HKHealthManager boss in infoBossKeysBuffer)
            {
                if (boss != null && !boss.isDead && boss.hp > 0)
                {
                    continue;
                }

                removedAnyBoss |= infoBoss.Remove(boss);
            }

            if (removedAnyBoss)
            {
                uniqueBossBuffersDirty = true;
            }

            if (!string.IsNullOrEmpty(activeArena) && HoGStoragePlanner.RequiresHp(activeArena))
            {
                BossHpState state = GetBossHpState(activeArena);
                if (state != null)
                {
                    BossTrackingHelpers.EnsureUniqueBossBuffers(infoBoss, uniqueBossByGameObject, uniqueBossSet, ref uniqueBossBuffersDirty);

                    int sumMaxHp = 0;
                    int maxMaxHp = 0;
                    foreach (HKHealthManager boss in uniqueBossSet)
                    {
                        if (!infoBoss.TryGetValue(boss, out var hp))
                        {
                            continue;
                        }

                        sumMaxHp += hp.maxHP;
                        if (hp.maxHP > maxMaxHp)
                        {
                            maxMaxHp = hp.maxHP;
                        }
                    }

                    int newHpMetric;
                    if (string.Equals(activeArena, HoGLoggerConditions.PaleCourtWhiteDefenderScene, StringComparison.Ordinal))
                    {
                        newHpMetric = sumMaxHp;
                        state.SumMax = Math.Max(state.SumMax, newHpMetric);
                    }
                    else
                    {
                        newHpMetric = maxMaxHp;
                    }

                    if (newHpMetric > state.Highest)
                    {
                        state.Highest = newHpMetric;
                        state.Cached = newHpMetric;
                        if (state.Waiting)
                        {
                            HoGStoragePlan plan = HoGStoragePlanner.GetPlan(activeArena, AheSettingsManager.CurrentSnapshot, newHpMetric, lastSceneBeforeArena);
                            ApplyHoGStoragePlan(plan);
                        }
                    }
                }
            }
        }

        private static void MonitorHeroHealth(long nowUnixTime)
        {
            if (!isLogging || writer == null)
            {
                return;
            }

            string roomName = GameManager.instance?.sceneName ?? activeArena;
            hitWarnTracker.Update(writer, roomName, lastUnixTime, nowUnixTime);
            charmsChangeTracker.Update(activeArena, lastUnixTime, nowUnixTime);
            MirrorInlineTimelineEvents();
            FlushWarningsIfNeeded(hitWarnBuffer, hitWarnTracker.Warnings, hitWarnTracker.ClearWarnings);
        }

        private static void MonitorDebugModUi(long nowUnixTime)
        {
            if (!isLogging || writer == null)
            {
                return;
            }

            bool debugUiVisible = false;
            if (!DebugModIntegration.TryGetFrameSnapshot(out DebugModFrameSnapshot debugSnapshot))
            {
                godhomeQolTracker.Update(activeArena, nowUnixTime, debugUiVisible);
                MirrorInlineTimelineEvents();
                return;
            }

            debugModEventsTracker.Update(writer, activeArena, lastUnixTime, debugSnapshot, nowUnixTime);
            debugMenuTracker.Update(writer, activeArena, lastUnixTime, debugSnapshot, nowUnixTime);
            debugUiVisible = debugSnapshot.UiVisible;
            godhomeQolTracker.Update(activeArena, nowUnixTime, debugUiVisible);
            MirrorInlineTimelineEvents();
        }

        private static void MonitorTimeScale(long nowUnixTime)
        {
            if (!isLogging || writer == null)
            {
                speedWarnTracker.Reset(Mathf.Max(Time.timeScale, 0f));
                speedWarnTracker.ClearWarnings();
                return;
            }

            speedWarnTracker.Update(writer, activeArena, lastUnixTime, nowUnixTime);
            MirrorInlineTimelineEvents();
            FlushWarningsIfNeeded(speedWarnBuffer, speedWarnTracker.Warnings, speedWarnTracker.ClearWarnings);
        }

        private static void MirrorInlineTimelineEvents()
        {
            if (!isLogging || damageAndInv == null)
            {
                return;
            }

            AppendPrefixedInlineEvents(speedWarnTracker.Warnings, ref speedWarnInlineCursor, "SpeedWarn");
            AppendPrefixedInlineEvents(hitWarnTracker.Warnings, ref hitWarnInlineCursor, "HitWarn");
            AppendPrefixedInlineEvents(debugModEventsTracker.Events, ref debugModEventsInlineCursor, "DebugModUi");
            AppendPrefixedInlineEvents(debugHotkeysTracker.Activations, ref debugHotkeysInlineCursor, "DebugHotkey");
            AppendPrefixedInlineEvents(debugMenuTracker.Entries, ref debugMenuInlineCursor, "DebugMenu");
            AppendPrefixedInlineEvents(paleCourtStageTracker.InlineLines, ref stageInlineCursor, "Stage");
            AppendRawInlineEvents(charmsChangeTracker.InlineEvents, ref charmsInlineCursor);
        }

        private static void ResetInlineTimelineCursors()
        {
            speedWarnInlineCursor = 0;
            hitWarnInlineCursor = 0;
            debugModEventsInlineCursor = 0;
            debugHotkeysInlineCursor = 0;
            debugMenuInlineCursor = 0;
            stageInlineCursor = 0;
            charmsInlineCursor = 0;
        }

        private static void AppendPrefixedInlineEvents(IReadOnlyList<string> source, ref int cursor, string prefix)
        {
            if (source == null || damageAndInv == null)
            {
                cursor = 0;
                return;
            }

            if (cursor > source.Count)
            {
                cursor = 0;
            }

            for (int i = cursor; i < source.Count; i++)
            {
                string raw = source[i];
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                string payload = raw.TrimStart();
                if (payload.StartsWith("|", StringComparison.Ordinal))
                {
                    damageAndInv.Add($"{prefix}{payload}");
                }
                else
                {
                    damageAndInv.Add($"{prefix}|{payload}");
                }
            }

            cursor = source.Count;
        }

        private static void AppendRawInlineEvents(IReadOnlyList<string> source, ref int cursor)
        {
            if (source == null || damageAndInv == null)
            {
                cursor = 0;
                return;
            }

            if (cursor > source.Count)
            {
                cursor = 0;
            }

            for (int i = cursor; i < source.Count; i++)
            {
                string raw = source[i];
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                damageAndInv.Add(raw);
            }

            cursor = source.Count;
        }

        private static void FlushWarningsIfNeeded(BufferedLogSection buffer, IReadOnlyList<string> warnings, Action clearAction)
        {
            if (buffer == null || warnings == null)
            {
                return;
            }

            if (warnings.Count < BufferedSectionThreshold)
            {
                return;
            }

            buffer.AddRange(warnings);
            clearAction?.Invoke();
        }

        private static void FlushKeyLogBufferIfNeeded(long now, bool force = false)
        {
            if (writer == null || keyLogBuffer.Count == 0)
            {
                return;
            }

            if (!force)
            {
                if (now - lastKeyLogFlushTime < KeyLogFlushIntervalMs && keyLogBuffer.Count < KeyLogFlushBatchSize)
                {
                    return;
                }
            }

            keyLogFlushLines.Clear();
            foreach (KeyLogEvent keyEvent in keyLogBuffer)
            {
                string keyStatus = keyEvent.IsDown ? "+" : "-";
                string colorHex = KeyLogFormatting.GetCachedColorHex(keyEvent.Color, keyColorHexCache);
                string entry = $"{keyEvent.DeltaMs}|{keyStatus}{keyEvent.KeyCode}|{keyEvent.WatermarkNumber}|{colorHex}|{keyEvent.Fps}";
                keyLogFlushLines.Add(entry);
            }

            if (isLogging && pressedButtonsLog != null)
            {
                pressedButtonsLog.AddRange(keyLogFlushLines);
            }
            else if (isLogging && damageAndInv != null)
            {
                damageAndInv.AddRange(keyLogFlushLines);
            }
            else
            {
                LogWrite.EncryptedLines(writer, keyLogFlushLines);
            }

            keyLogBuffer.Clear();
            keyLogFlushLines.Clear();
            lastKeyLogFlushTime = now;
        }

        private static void LogDebugModUiEvent()
        {

        }

        private static void InitializeDebugModHooks()
        {
            if (debugKillAllHook != null && debugKillSelfHook != null)
            {
                return;
            }

            try
            {
                Type bindableType = TypeLookup.FindType("DebugMod.BindableFunctions");
                if (bindableType == null)
                {
                    return;
                }

                if (debugKillAllHook == null)
                {
                    MethodInfo killAll = bindableType.GetMethod("KillAll", BindingFlags.Public | BindingFlags.Static);
                    if (killAll != null)
                    {
                        debugKillAllHook = new Hook(killAll, typeof(HoGLogger).GetMethod(nameof(DebugKillAllDetour), BindingFlags.Static | BindingFlags.NonPublic));
                    }
                }

                if (debugKillSelfHook == null)
                {
                    MethodInfo killSelf = bindableType.GetMethod("KillSelf", BindingFlags.Public | BindingFlags.Static);
                    if (killSelf != null)
                    {
                        debugKillSelfHook = new Hook(killSelf, typeof(HoGLogger).GetMethod(nameof(DebugKillSelfDetour), BindingFlags.Static | BindingFlags.NonPublic));
                    }
                }
            }
            catch (Exception e)
            {
                global::ReplayLogger.InternalDiagnostics.Warn($"HoGLogger: failed to hook DebugMod functions: {e.Message}");
            }
        }

        private static void DebugKillAllDetour(Action orig)
        {
            orig();
            if (isLogging && writer != null)
            {
                long nowUnixTime = KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime);
                debugMenuTracker.LogManualChange(writer, activeArena, lastUnixTime, nowUnixTime, "Cheats/Kill All", null, "Executed");
            }
        }

        private static void DebugKillSelfDetour(Action orig)
        {
            orig();
            if (isLogging && writer != null)
            {
                long nowUnixTime = KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime);
                debugMenuTracker.LogManualChange(writer, activeArena, lastUnixTime, nowUnixTime, "Cheats/Kill Self", null, "Executed");
            }
        }

        private static Type FindTypeByName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return null;
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(typeName, false);
                if (type != null)
                {
                    return type;
                }
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (Type type in assembly.GetTypes())
                    {
                        if (type != null && string.Equals(type.Name, typeName, StringComparison.Ordinal))
                        {
                            return type;
                        }
                    }
                }
                catch (ReflectionTypeLoadException ex)
                {
                    foreach (Type type in ex.Types)
                    {
                        if (type != null && string.Equals(type.Name, typeName, StringComparison.Ordinal))
                        {
                            return type;
                        }
                    }
                }
                catch
                {

                }
            }

            return null;
        }

        private static void InitializeDebugHotkeys()
        {
            debugHotkeysTracker.InitializeBindings();
            debugHotkeysByKey = new Dictionary<KeyCode, List<string>>();
            foreach (var pair in debugHotkeysTracker.ActionsByKey)
            {
                debugHotkeysByKey[pair.Key] = new List<string>(pair.Value);
            }
            debugHotkeyBindings = new List<string>(debugHotkeysTracker.Bindings);
            debugHotkeyEvents = new List<string>(debugHotkeysTracker.Activations);
        }

        private sealed class BossHpState
        {
            public bool Waiting;
            public int? Cached;
            public int Highest;
            public int SumMax;
            public int Min = int.MaxValue;
        }

        internal static CustomCanvas GetActiveCanvas() => customCanvas;
    }
}

