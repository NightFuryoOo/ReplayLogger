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
        private static int speedWarnInlineCursor;
        private static int hitWarnInlineCursor;
        private static int debugModEventsInlineCursor;
        private static int debugHotkeysInlineCursor;
        private static int debugMenuInlineCursor;
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
        private static readonly KeyCode[] AllKeyCodes = Enum.GetValues(typeof(KeyCode)).Cast<KeyCode>().Distinct().ToArray();
        private static readonly HashSet<KeyCode> pressedKeys = new();
        private static readonly List<KeyCode> pressedKeysBuffer = new(64);
        private static readonly List<KeyCode> adaptiveHotKeyCodes = new();
        private static readonly HashSet<KeyCode> adaptiveHotKeySet = new();
        private static readonly List<KeyCode> adaptiveColdKeyCodes = new();
        private static readonly List<KeyCode> adaptivePromoteKeyBuffer = new();
        private static bool adaptiveKeyScanInitialized;
        private const int AdaptiveHotKeyLimit = 48;
        private static readonly Dictionary<Color32, string> keyColorHexCache = new(128);
        private const int KeyColorHexCacheMaxSize = 512;
        private static readonly Dictionary<GameObject, string> ownerPathByGameObject = new(512);
        private static readonly List<GameObject> ownerPathCacheCleanupBuffer = new(128);
        private const float OwnerPathCacheCleanupTickSeconds = 0.5f;
        private const int OwnerPathCacheCleanupMinSize = 256;
        private const int OwnerPathCacheCleanupBatchSize = 128;
        private static float lastOwnerPathCacheCleanupTime;
        private static int ownerPathCacheCleanupCursor;
        private static long cachedFrameUnixTime;
        private const int EnemyColliderBufferInitialSize = 1024;
        private const int EnemyColliderBufferMaxSize = 32768;
        private const float EnemyLayerMaskRefreshIntervalSeconds = 1f;
        private const float EnemyColliderOverflowWarnIntervalSeconds = 5f;
        private static Collider2D[] enemyColliderBuffer = new Collider2D[EnemyColliderBufferInitialSize];
        private static bool enemyColliderBufferOverflowLogged;
        private static float lastEnemyColliderOverflowWarningTime;
        private static int enemyScanLayerMask;
        private static float lastEnemyLayerMaskRefreshTime;
        private static readonly Dictionary<GameObject, HKHealthManager> enemyHealthManagerByGameObject = new(512);
        private static readonly List<GameObject> enemyHealthManagerCacheCleanupBuffer = new(64);
        private const float EnemyHealthCacheCleanupTickSeconds = 0.25f;
        private const int EnemyHealthCacheCleanupMinSize = 128;
        private const int EnemyHealthCacheCleanupBatchSize = 64;
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
        private static readonly string[] HitTargetMemberNames =
        {
            "Target",
            "target",
            "TargetObject",
            "targetObject",
            "TargetCollider",
            "targetCollider",
            "Other",
            "other",
            "GameObject",
            "gameObject"
        };
        private static readonly Dictionary<Type, MemberInfo> HitTargetMemberByType = new();
        private static readonly HashSet<Type> HitTargetMemberMissTypes = new();
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
            long now = CaptureFrameUnixTime();
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

            lastSceneName = targetScene;
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
            On.HeroController.FixedUpdate += HeroController_FixedUpdate;
            On.HealthManager.TakeDamage += HealthManager_TakeDamage;
            On.QuitToMenu.Start += QuitToMenu_Start;
            On.SpellFluke.DoDamage += SpellFluke_DoDamage;
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
            On.HeroController.FixedUpdate -= HeroController_FixedUpdate;
            On.HealthManager.TakeDamage -= HealthManager_TakeDamage;
            On.QuitToMenu.Start -= QuitToMenu_Start;
            On.SpellFluke.DoDamage -= SpellFluke_DoDamage;
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

            long frameUnixTime = CaptureFrameUnixTime();
            MonitorTimeScale(frameUnixTime);
            MonitorHeroHealth(frameUnixTime);
            MonitorDebugModUi(frameUnixTime);

            long relativeMs = Math.Max(0, frameUnixTime - startUnixTime);
            int elapsedSeconds = (int)(relativeMs / 1000);
            if (elapsedSeconds != lastHudElapsedSeconds)
            {
                lastHudElapsedSeconds = elapsedSeconds;
                customCanvas?.UpdateTime(FormatHudElapsedTime(relativeMs));
            }

            PollKeyEvents(frameUnixTime);
            MirrorInlineTimelineEvents();

            FlushKeyLogBufferIfNeeded(frameUnixTime);

        }

        private static void PollKeyEvents(long pollUnixTime)
        {
            bool hasNewKeyDown = Input.anyKeyDown;
            if (!hasNewKeyDown && pressedKeys.Count == 0)
            {
                return;
            }

            if (hasNewKeyDown)
            {
                EnsureAdaptiveKeyScanInitialized();
                adaptivePromoteKeyBuffer.Clear();
                ScanKeyDownCandidates(adaptiveHotKeyCodes, pollUnixTime);
                ScanKeyDownCandidates(adaptiveColdKeyCodes, pollUnixTime);
                PromoteAdaptiveKeys();
            }

            if (pressedKeys.Count == 0)
            {
                return;
            }

            pressedKeysBuffer.Clear();
            pressedKeysBuffer.AddRange(pressedKeys);
            foreach (KeyCode keyCode in pressedKeysBuffer)
            {
                if (Input.GetKey(keyCode))
                {
                    continue;
                }

                HandleKeyEvent(keyCode, isDown: false, pollUnixTime);
                pressedKeys.Remove(keyCode);
            }
        }

        private static void EnsureAdaptiveKeyScanInitialized()
        {
            if (adaptiveKeyScanInitialized)
            {
                return;
            }

            adaptiveHotKeyCodes.Clear();
            adaptiveHotKeySet.Clear();
            adaptiveColdKeyCodes.Clear();
            adaptivePromoteKeyBuffer.Clear();
            foreach (KeyCode keyCode in AllKeyCodes)
            {
                adaptiveColdKeyCodes.Add(keyCode);
            }

            adaptiveKeyScanInitialized = true;
        }

        private static void ScanKeyDownCandidates(IReadOnlyList<KeyCode> candidates, long pollUnixTime)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                KeyCode keyCode = candidates[i];
                if (pressedKeys.Contains(keyCode))
                {
                    continue;
                }

                if (!Input.GetKeyDown(keyCode))
                {
                    continue;
                }

                if (pressedKeys.Add(keyCode))
                {
                    HandleKeyEvent(keyCode, isDown: true, pollUnixTime);
                }

                if (!adaptiveHotKeySet.Contains(keyCode))
                {
                    adaptivePromoteKeyBuffer.Add(keyCode);
                }
            }
        }

        private static void PromoteAdaptiveKeys()
        {
            if (adaptivePromoteKeyBuffer.Count == 0)
            {
                return;
            }

            for (int i = 0; i < adaptivePromoteKeyBuffer.Count; i++)
            {
                KeyCode keyCode = adaptivePromoteKeyBuffer[i];
                if (!adaptiveHotKeySet.Add(keyCode))
                {
                    continue;
                }

                adaptiveHotKeyCodes.Add(keyCode);
                RemoveKeyCode(adaptiveColdKeyCodes, keyCode);
            }

            while (adaptiveHotKeyCodes.Count > AdaptiveHotKeyLimit)
            {
                KeyCode demoted = adaptiveHotKeyCodes[0];
                adaptiveHotKeyCodes.RemoveAt(0);
                adaptiveHotKeySet.Remove(demoted);
                if (!adaptiveColdKeyCodes.Contains(demoted))
                {
                    adaptiveColdKeyCodes.Add(demoted);
                }
            }

            adaptivePromoteKeyBuffer.Clear();
        }

        private static void RemoveKeyCode(List<KeyCode> source, KeyCode keyCode)
        {
            if (source == null || source.Count == 0)
            {
                return;
            }

            for (int i = 0; i < source.Count; i++)
            {
                if (source[i] == keyCode)
                {
                    source.RemoveAt(i);
                    return;
                }
            }
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

        private static string GetCachedColorHex(Color32 color)
        {
            if (keyColorHexCache.TryGetValue(color, out string colorHex))
            {
                return colorHex;
            }

            if (keyColorHexCache.Count >= KeyColorHexCacheMaxSize)
            {
                keyColorHexCache.Clear();
            }

            colorHex = ColorUtility.ToHtmlStringRGBA(color);
            keyColorHexCache[color] = colorHex;
            return colorHex;
        }

        private static string FormatHudElapsedTime(long relativeMs)
        {
            long totalSeconds = Math.Max(0L, relativeMs) / 1000L;
            int hours = (int)((totalSeconds / 3600L) % 24L);
            int minutes = (int)((totalSeconds / 60L) % 60L);
            int seconds = (int)(totalSeconds % 60L);
            return $"{hours:D2}:{minutes:D2}:{seconds:D2}";
        }

        private static string FormatUnixTimestamp(long unixTimeMilliseconds)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(unixTimeMilliseconds).ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }

        private static string FormatUnixFileSuffix(long unixTimeMilliseconds)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(unixTimeMilliseconds).ToLocalTime().ToString("dd-MM-yyyy HH-mm-ss", CultureInfo.InvariantCulture);
        }

        private static long GetCachedFrameUnixTimeOrNow()
        {
            long cached = cachedFrameUnixTime;
            return cached > 0 ? cached : DateTimeOffset.Now.ToUnixTimeMilliseconds();
        }

        private static long CaptureFrameUnixTime()
        {
            long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            cachedFrameUnixTime = now;
            return now;
        }

        private static void BossSceneController_Update(On.BossSceneController.orig_Update orig, BossSceneController self)
        {
            if (isLogging)
            {
                CaptureBossLevel(self);
            }
            orig(self);
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
                paleCourtLevelOwnerType = FindType("BossManagement.CustomWP") ?? FindTypeByName("CustomWP");
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
                long nowUnixTime = GetCachedFrameUnixTimeOrNow();
                cachedFrameUnixTime = nowUnixTime;
                InvCheck(nowUnixTime);
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
            if (!isLogging || owner?.GameObject == null)
            {
                return hit;
            }

            long unixTime = GetCachedFrameUnixTimeOrNow();
            GameObject ownerObject = owner.GameObject;
            int ownerId = ownerObject.GetInstanceID();
            string ownerName = GetCachedOwnerPath(ownerObject);

            damageChangeTracker.Track(ownerId, ownerName, activeArena, unixTime - lastUnixTime, hit.DamageDealt, hit.Multiplier);

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

            long nowUnixTime = GetCachedFrameUnixTimeOrNow();
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

            long nowUnixTime = GetCachedFrameUnixTimeOrNow();
            string arena = GameManager.instance?.sceneName ?? activeArena;
            hitWarnTracker.LogDeathEvent(writer, arena, lastUnixTime, nowUnixTime);
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

            TrackEnemyHealthManager(self);
            int maxHp = Math.Max(hpBefore, hpAfter);
            if (infoBoss.TryGetValue(self, out var hpState))
            {
                maxHp = Math.Max(hpState.maxHP, maxHp);
            }

            // Force one delta write with the exact pre-hit value.
            infoBoss[self] = (maxHp, hpBefore);
            uniqueBossBuffersDirty = true;
            TryLogTrackedBossHpDelta(GetCachedFrameUnixTimeOrNow());
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
            object rawTarget = GetCachedHitTargetRaw(boxedHit, hitType);
            if (rawTarget == null)
            {
                return false;
            }

            if (rawTarget is HKHealthManager manager)
            {
                healthManager = manager;
                return healthManager != null;
            }

            if (!TryUnwrapTargetGameObject(rawTarget, out GameObject targetObject))
            {
                return false;
            }

            healthManager = ResolveEnemyHealthManager(targetObject);
            return healthManager != null;
        }

        private static object GetCachedHitTargetRaw(object boxedHit, Type hitType)
        {
            if (boxedHit == null || hitType == null)
            {
                return null;
            }

            if (HitTargetMemberByType.TryGetValue(hitType, out MemberInfo cachedMember))
            {
                return ReadHitTargetMemberValue(cachedMember, boxedHit);
            }

            if (HitTargetMemberMissTypes.Contains(hitType))
            {
                return null;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (string memberName in HitTargetMemberNames)
            {
                FieldInfo field = hitType.GetField(memberName, flags);
                if (field != null)
                {
                    object fieldValue = ReadHitTargetMemberValue(field, boxedHit);
                    if (fieldValue != null)
                    {
                        HitTargetMemberByType[hitType] = field;
                        return fieldValue;
                    }
                }

                PropertyInfo property = hitType.GetProperty(memberName, flags);
                if (property == null || property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                object propertyValue = ReadHitTargetMemberValue(property, boxedHit);
                if (propertyValue != null)
                {
                    HitTargetMemberByType[hitType] = property;
                    return propertyValue;
                }
            }

            foreach (FieldInfo field in hitType.GetFields(flags))
            {
                object fieldValue = ReadHitTargetMemberValue(field, boxedHit);
                if (IsPotentialHitTargetValue(fieldValue))
                {
                    HitTargetMemberByType[hitType] = field;
                    return fieldValue;
                }
            }

            foreach (PropertyInfo property in hitType.GetProperties(flags))
            {
                if (property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                object propertyValue = ReadHitTargetMemberValue(property, boxedHit);
                if (IsPotentialHitTargetValue(propertyValue))
                {
                    HitTargetMemberByType[hitType] = property;
                    return propertyValue;
                }
            }

            HitTargetMemberMissTypes.Add(hitType);
            return null;
        }

        private static object ReadHitTargetMemberValue(MemberInfo memberInfo, object boxedHit)
        {
            if (memberInfo == null || boxedHit == null)
            {
                return null;
            }

            try
            {
                return memberInfo switch
                {
                    FieldInfo field => field.GetCachedValue(boxedHit),
                    PropertyInfo property => property.GetCachedValue(boxedHit),
                    _ => null
                };
            }
            catch
            {
                return null;
            }
        }

        private static bool TryUnwrapTargetGameObject(object rawTarget, out GameObject targetObject)
        {
            return TryExtractGameObject(rawTarget, depth: 2, out targetObject);
        }

        private static bool IsPotentialHitTargetValue(object value)
        {
            return value is GameObject or Transform or Component or HKHealthManager;
        }

        private static bool TryExtractGameObject(object value, int depth, out GameObject gameObject)
        {
            if (value == null || depth < 0)
            {
                gameObject = null;
                return false;
            }

            switch (value)
            {
                case GameObject directGameObject:
                    gameObject = directGameObject;
                    return gameObject != null;
                case Transform directTransform:
                    gameObject = directTransform.gameObject;
                    return gameObject != null;
                case Component directComponent:
                    gameObject = directComponent.gameObject;
                    return gameObject != null;
            }

            if (depth == 0)
            {
                gameObject = null;
                return false;
            }

            Type valueType = value.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            string[] nestedNames =
            {
                "gameObject",
                "GameObject",
                "transform",
                "Transform",
                "target",
                "Target",
                "other",
                "Other"
            };

            foreach (string nestedName in nestedNames)
            {
                FieldInfo field = valueType.GetField(nestedName, flags);
                if (field != null && TryExtractGameObject(ReadHitTargetMemberValue(field, value), depth - 1, out gameObject))
                {
                    return true;
                }

                PropertyInfo property = valueType.GetProperty(nestedName, flags);
                if (property != null &&
                    property.GetIndexParameters().Length == 0 &&
                    TryExtractGameObject(ReadHitTargetMemberValue(property, value), depth - 1, out gameObject))
                {
                    return true;
                }
            }

            gameObject = null;
            return false;
        }

        private static bool TryLogTrackedBossHpDelta(long nowUnixTime)
        {
            if (infoBoss.Count == 0)
            {
                return false;
            }

            EnsureUniqueBossBuffers();
            infoBossKeysBuffer.Clear();
            foreach (HKHealthManager boss in infoBoss.Keys)
            {
                infoBossKeysBuffer.Add(boss);
            }

            bool isChanged = false;
            bool removedAny = false;
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

            damageAndInv?.Add($"{nowUnixTime - lastUnixTime}{hpInfoBuilder}|");
            return true;
        }

        private static string GetCachedOwnerPath(GameObject ownerObject)
        {
            if (ownerObject == null)
            {
                return string.Empty;
            }

            if (ownerPathByGameObject.TryGetValue(ownerObject, out string cachedPath))
            {
                return cachedPath ?? string.Empty;
            }

            string path = ownerObject.GetFullPath();
            ownerPathByGameObject[ownerObject] = path;
            return path;
        }

        private static void CleanupOwnerPathCacheIfNeeded(float now)
        {
            if (ownerPathByGameObject.Count < OwnerPathCacheCleanupMinSize)
            {
                ownerPathCacheCleanupCursor = 0;
                return;
            }

            if (now - lastOwnerPathCacheCleanupTime < OwnerPathCacheCleanupTickSeconds)
            {
                return;
            }

            lastOwnerPathCacheCleanupTime = now;
            ownerPathCacheCleanupBuffer.Clear();
            int startIndex = ownerPathCacheCleanupCursor;
            int endIndexExclusive = startIndex + OwnerPathCacheCleanupBatchSize;
            int index = 0;
            foreach (var pair in ownerPathByGameObject)
            {
                if (index < startIndex)
                {
                    index++;
                    continue;
                }

                if (index >= endIndexExclusive)
                {
                    break;
                }

                if (pair.Key == null)
                {
                    ownerPathCacheCleanupBuffer.Add(pair.Key);
                }

                index++;
            }

            foreach (GameObject key in ownerPathCacheCleanupBuffer)
            {
                ownerPathByGameObject.Remove(key);
            }

            if (index < endIndexExclusive)
            {
                ownerPathCacheCleanupCursor = 0;
                return;
            }

            int remainingCount = ownerPathByGameObject.Count;
            ownerPathCacheCleanupCursor = endIndexExclusive >= remainingCount ? 0 : endIndexExclusive;
        }

        private static void SpellFluke_DoDamage(On.SpellFluke.orig_DoDamage orig, SpellFluke self, GameObject obj, int upwardRecursionAmount, bool burst)
        {
            long nowUnixTime = GetCachedFrameUnixTimeOrNow();
            FlukenestTracker.HandleDoDamage(isLogging, writer, damageChangeTracker, activeArena, lastUnixTime, nowUnixTime, orig, self, obj, upwardRecursionAmount, burst);
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
                    // Keep section buffers in memory to avoid plaintext spill files near temporary HoG logs.
                    pressedButtonsLog = new BufferedLogSection(null, BufferedSectionThreshold);
                    damageAndInv = new BufferedLogSection(null, BufferedSectionThreshold);
                    invWarnings = new BufferedLogSection(null, BufferedSectionThreshold);
                    speedWarnBuffer = new BufferedLogSection(null, BufferedSectionThreshold);
                    hitWarnBuffer = new BufferedLogSection(null, BufferedSectionThreshold);

                    writer = new AsyncBlockLogWriter(currentTempFile, masterKeyBlob, masterEncryptionSession, BlockSizeBytes, BlockMaxAgeMs, LogQueueCapacity);

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
                    pressedKeys.Clear();
                    pressedKeysBuffer.Clear();
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
                    infoBoss = new();
                    uniqueBossBuffersDirty = true;
                    debugHotkeysByKey = new();
                    currentAttemptIndex = 1;
                    lastLoggedDeltaMs = -1;
                    charmsChangeTracker.Reset();
                    ResetInlineTimelineCursors();
                    bossLevelInFight = null;
                    if (IsPaleCourtArena(arenaName) && !IsTisoArena(arenaName))
                    {
                        int? paleCourtLevel = TryReadPaleCourtLevel();
                        if (IsValidBossLevelForArena(paleCourtLevel, arenaName))
                        {
                            bossLevelInFight = paleCourtLevel.Value;
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
                    speedWarnTracker.LogInitial(writer, lastUnixTime);
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

        private static void StopLogging(string exitScene)
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
            FlushKeyLogBufferIfNeeded(GetCachedFrameUnixTimeOrNow(), force: true);
            LogWrite.EncryptedLine(writer, "\n------------------------DAMAGE INV and PRESSED BUTTONS------------------------\n");
            bool wrotePressedButtons = pressedButtonsLog != null && pressedButtonsLog.HasContent;
            if (wrotePressedButtons)
            {
                pressedButtonsLog.WriteEncryptedLines(writer);
                WriteBlockSeparatorWithSpacing();
            }

            bool wroteDamage = damageAndInv != null && damageAndInv.HasContent;
            if (wroteDamage)
            {
                damageAndInv.WriteEncryptedLines(writer);
                WriteBlockSeparatorWithSpacing();
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

            RefreshBucketInfo(force: true);

            LogWrite.EncryptedLine(writer, "-");
            AheSettingsManager.WriteSettingsWithSeparator(writer);
            LogWrite.EncryptedLine(writer, $"HoG Bucket: {currentBucketInfo.BucketLabel ?? HoGLoggerConditions.DefaultBucket}");
            LogWrite.EncryptedLine(writer, string.Empty);
            LogWrite.EncryptedLine(writer, "---------------------------------------------------");
            ZoteSettingsManager.WriteSettingsWithSeparator(writer);
            CollectorPhasesSettingsManager.WriteSettingsWithSeparator(writer);
            SafeGodseekerQolIntegration.WriteSettingsWithSeparator(writer);
            godhomeQolTracker.WriteSection(writer);

            DebugModEventsWriter.Write(writer, debugModEventsTracker.Events);
            DebugHotKeysWriter.Write(writer, debugHotkeysTracker.Bindings, debugHotkeysTracker.Activations);
            debugMenuTracker.WriteSection(writer);
            CoreSessionLogger.WriteSeparator(writer);

            CoreSessionLogger.WriteNoBlurSettings(writer);
            CoreSessionLogger.WriteCustomizableAbilitiesSettings(writer);
            CoreSessionLogger.WriteControlSettings(writer);

            HardwareFingerprint.WriteEncryptedLine(writer);
            CoreSessionLogger.WriteEncryptedModSnapshot(writer, ModsDirectory, "---------------------------------------------------");

            LogWrite.Raw(writer, masterKeyBlob);
            DisposeWriterSafely(flushBeforeDispose: true, context: "finalize");

            MoveTempFileToFinalLocation();
            customCanvas?.ClearHud();
        }

        private static void WriteBlockSeparatorWithSpacing()
        {
            if (writer == null)
            {
                return;
            }

            LogWrite.EncryptedLine(writer, string.Empty);
            CoreSessionLogger.WriteSeparator(writer);
            LogWrite.EncryptedLine(writer, string.Empty);
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
            pressedKeys.Clear();
            pressedKeysBuffer.Clear();
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
            currentAttemptIndex = 1;
            lastLoggedDeltaMs = -1;
            hitWarnTracker = new HitWarnTracker();
            ResetInlineTimelineCursors();
            infoBoss = new();
            debugHotkeysByKey = new();
            isLogging = false;
            activeArena = null;
            bossLevelInFight = null;
            lastSceneBeforeArena = string.Empty;
            bossCounter = 0;
            isInvincible = false;
            invTimer = 0f;
            currentTempFile = null;
            cachedFrameUnixTime = 0;
            currentBucketInfo = HoGBucketInfo.CreateDefault(null);
            AheSettingsManager.Reset();
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
                string displayName = currentBucketInfo.BossFolder ?? HoGLoggerConditions.GetDisplayName(activeArena);
                string bossFolderForSave = string.IsNullOrWhiteSpace(currentBucketInfo.BossFolder)
                    ? HoGLoggerConditions.GetDisplayName(activeArena)
                    : currentBucketInfo.BossFolder;
                string fileLabel = string.IsNullOrEmpty(currentBucketInfo.FilePrefix) ? displayName : currentBucketInfo.FilePrefix;
                if (string.Equals(activeArena, "GG_Radiance", StringComparison.Ordinal))
                {
                    string anyRadianceRoot = HoGStoragePlanner.ResolveAnyRadianceRootFolder();
                    if (string.Equals(anyRadianceRoot, "AnyRadiance 3.0", StringComparison.Ordinal))
                    {
                        fileLabel = anyRadianceRoot;
                    }
                }
                string timeSuffix = FormatUnixFileSuffix(lastUnixTime);
                string rootFolder = string.IsNullOrEmpty(currentBucketInfo.RootFolder) ? HoGLoggerConditions.DefaultBucket : currentBucketInfo.RootFolder;

                bool isP5Health = SafeGodseekerQolIntegration.IsP5HealthEnabled();
                if (isP5Health)
                {
                    rootFolder = "P5 HEALTH";
                }

                string finalDir = Path.Combine(DllDirectory, rootFolder, displayName);
                string difficultyFolder = GetDifficultyFolderName();
                string difficultyFolderForSave = string.IsNullOrWhiteSpace(difficultyFolder) ? "None" : difficultyFolder;
                if (!string.IsNullOrEmpty(difficultyFolder))
                {
                    finalDir = Path.Combine(finalDir, difficultyFolder);
                }
                Directory.CreateDirectory(finalDir);

                string prefix = GetDifficultyPrefix();
                string p5Prefix = isP5Health ? "P5 HP " : string.Empty;
                string finalPath = Path.Combine(finalDir, $"{p5Prefix}{prefix}{fileLabel} ({timeSuffix}).log");
                if (File.Exists(finalPath))
                {
                    File.Delete(finalPath);
                }

                if (File.Exists(currentTempFile))
                {
                    MoveFileSafely(currentTempFile, finalPath);
                    SavedLogTracker.Record(finalPath, rootFolder, bossFolderForSave, difficultyFolderForSave);
                    string toastText = $"{currentBucketInfo.BucketLabel ?? HoGLoggerConditions.DefaultBucket}: {Path.GetFileName(finalPath)}";
                    SavedLogToast.Record(toastText);
                    customCanvas?.ShowSavedFileToast(toastText, ReplayLogger.GetHudToastSeconds());
                }
            }
            catch (Exception e)
            {
                global::ReplayLogger.InternalDiagnostics.Error($"HoGLogger: failed to move log file: {e.Message}");
            }
        }

        private static string ResolveTempLogDirectory()
        {
            return DllDirectory;
        }

        private static void MoveFileSafely(string sourcePath, string destinationPath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(destinationPath))
            {
                return;
            }

            try
            {
                File.Move(sourcePath, destinationPath);
                return;
            }
            catch (IOException moveIoEx)
            {
                global::ReplayLogger.InternalDiagnostics.Warn($"HoGLogger: File.Move failed for '{Path.GetFileName(sourcePath)}', fallback to copy+delete: {moveIoEx.Message}");
            }

            File.Copy(sourcePath, destinationPath, overwrite: true);
            File.Delete(sourcePath);
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

        private static void RefreshUniqueBossBuffers()
        {
            uniqueBossByGameObject.Clear();
            uniqueBossSet.Clear();

            foreach (HKHealthManager boss in infoBoss.Keys)
            {
                if (boss == null)
                {
                    continue;
                }

                uniqueBossByGameObject[boss.gameObject] = boss;
            }

            foreach (HKHealthManager boss in uniqueBossByGameObject.Values)
            {
                uniqueBossSet.Add(boss);
            }

            uniqueBossBuffersDirty = false;
        }

        private static void EnsureUniqueBossBuffers()
        {
            if (uniqueBossBuffersDirty)
            {
                RefreshUniqueBossBuffers();
            }
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

        private static bool ShouldTrackHealthManager(HKHealthManager manager)
        {
            if (manager == null || manager.gameObject == null || manager.hp <= 0 || manager.isDead)
            {
                return false;
            }

            HeroController hero = HeroController.instance;
            GameObject heroObject = hero?.gameObject;
            if (heroObject == null)
            {
                return true;
            }

            if (ReferenceEquals(manager.gameObject, heroObject))
            {
                return false;
            }

            Transform managerRoot = manager.gameObject.transform?.root;
            Transform heroRoot = heroObject.transform?.root;
            return managerRoot == null || heroRoot == null || !ReferenceEquals(managerRoot, heroRoot);
        }

        private static void IncludeEnemyLayer(int layer)
        {
            if ((uint)layer >= 32u)
            {
                return;
            }

            enemyScanLayerMask |= 1 << layer;
        }

        private static int BuildHeroCollisionLayerMask(int heroLayer)
        {
            if ((uint)heroLayer >= 32u)
            {
                return Physics2D.AllLayers;
            }

            int mask = 0;
            for (int layer = 0; layer < 32; layer++)
            {
                if (!Physics2D.GetIgnoreLayerCollision(heroLayer, layer))
                {
                    mask |= 1 << layer;
                }
            }

            return mask != 0 ? mask : Physics2D.AllLayers;
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

            int mask = BuildHeroCollisionLayerMask(hero.gameObject.layer);

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

        private static void CleanupEnemyHealthManagerCacheIfNeeded(float now)
        {
            if (enemyHealthManagerByGameObject.Count < EnemyHealthCacheCleanupMinSize)
            {
                enemyHealthCacheCleanupCursor = 0;
                return;
            }

            if (now - lastEnemyHealthCacheCleanupTime < EnemyHealthCacheCleanupTickSeconds)
            {
                return;
            }

            lastEnemyHealthCacheCleanupTime = now;
            enemyHealthManagerCacheCleanupBuffer.Clear();
            int startIndex = enemyHealthCacheCleanupCursor;
            int endIndexExclusive = startIndex + EnemyHealthCacheCleanupBatchSize;
            int index = 0;
            foreach (var pair in enemyHealthManagerByGameObject)
            {
                if (index < startIndex)
                {
                    index++;
                    continue;
                }

                if (index >= endIndexExclusive)
                {
                    break;
                }

                if (pair.Key == null || pair.Value == null)
                {
                    enemyHealthManagerCacheCleanupBuffer.Add(pair.Key);
                }

                index++;
            }

            foreach (GameObject key in enemyHealthManagerCacheCleanupBuffer)
            {
                enemyHealthManagerByGameObject.Remove(key);
            }

            if (index < endIndexExclusive)
            {
                enemyHealthCacheCleanupCursor = 0;
                return;
            }

            int remainingCount = enemyHealthManagerByGameObject.Count;
            enemyHealthCacheCleanupCursor = endIndexExclusive >= remainingCount ? 0 : endIndexExclusive;
        }

        private static int CollectEnemyCollidersNonAlloc(Vector2 center, Vector2 size, int layerMask)
        {
            int colliderCount = Physics2D.OverlapBoxNonAlloc(center, size, 0f, enemyColliderBuffer, layerMask);
            while (colliderCount >= enemyColliderBuffer.Length && enemyColliderBuffer.Length < EnemyColliderBufferMaxSize)
            {
                int nextSize = Math.Min(enemyColliderBuffer.Length * 2, EnemyColliderBufferMaxSize);
                enemyColliderBuffer = new Collider2D[nextSize];
                colliderCount = Physics2D.OverlapBoxNonAlloc(center, size, 0f, enemyColliderBuffer, layerMask);
            }

            return colliderCount;
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

            EnsureUniqueBossBuffers();
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

            GameObject heroBoxObject = ResolveHeroBoxObject(hero);
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

        private static GameObject ResolveHeroBoxObject(HeroController hero)
        {
            if (hero == null)
            {
                cachedHeroTransform = null;
                cachedHeroBoxObject = null;
                return null;
            }

            Transform heroTransform = hero.transform;
            if (cachedHeroTransform != heroTransform)
            {
                cachedHeroTransform = heroTransform;
                cachedHeroBoxObject = null;
            }

            if (cachedHeroBoxObject == null)
            {
                Transform heroBoxTransform = heroTransform.Find("HeroBox");
                cachedHeroBoxObject = heroBoxTransform != null ? heroBoxTransform.gameObject : null;
            }

            return cachedHeroBoxObject;
        }

        private static void WriteHeroBoxActiveWarning(int currentState, IEnumerable<HKHealthManager> bossList, int? previousState, long nowUnixTime)
        {
            string hpInfo = BuildHeroBoxHpInfo(bossList);
            string message = previousState.HasValue
                ? $"{FormatState(previousState.Value)} -> {FormatState(currentState)}"
                : FormatState(currentState);
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

            EnsureUniqueBossBuffers();
            string hpInfo = BuildHeroBoxHpInfo(uniqueBossByGameObject.Values);
            float duration = Time.unscaledTime - heroBoxOffStartTime;
            string warning = $"|{activeArena}|+{nowUnixTime - lastUnixTime}{hpInfo}|HeroBoxActive Off Duration: {duration.ToString("F3", CultureInfo.InvariantCulture)}s";
            invWarnings?.Add(warning);
            damageAndInv?.Add($"{nowUnixTime - lastUnixTime}{hpInfo}|HeroBoxActive Off Duration: {duration.ToString("F3", CultureInfo.InvariantCulture)}s|");
        }

        private static string BuildHeroBoxHpInfo(IEnumerable<HKHealthManager> bossList)
        {
            if (bossList == null)
            {
                return string.Empty;
            }

            hpInfoBuilder.Clear();
            foreach (var boss in bossList)
            {
                if (infoBoss.TryGetValue(boss, out var hp))
                {
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
            foreach (HKHealthManager boss in bossList)
            {
                var entry = infoBoss[boss];
                hpInfoBuilder.Append('|');
                hpInfoBuilder.Append(entry.lastHP);
                hpInfoBuilder.Append('/');
                hpInfoBuilder.Append(entry.maxHP);
            }

            return hpInfoBuilder.ToString();
        }

        private static string FormatState(int state) =>
            state switch
            {
                1 => "On",
                0 => "Off",
                _ => "N/A"
            };

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
            CleanupEnemyHealthManagerCacheIfNeeded(now);
            CleanupOwnerPathCacheIfNeeded(now);
            if (infoBoss.Count == 0)
            {
                SeedTrackedBossesFromActiveScene(now);
            }
            float searchRadius = 100f;
            Vector2 searchSize = Vector2.one * searchRadius;
            int enemyLayer = ResolveEnemyLayerMask(hero, now);

            int colliderCount = CollectEnemyCollidersNonAlloc(hero.transform.position, searchSize, enemyLayer);
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

            EnsureUniqueBossBuffers();
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
            }

            if (isChange)
            {
                string hpInfo = hpInfoBuilder.ToString();
                damageAndInv?.Add($"{nowUnixTime - lastUnixTime}{hpInfo}|");
            }
            isChange = false;

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
                    EnsureUniqueBossBuffers();

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
            AppendRawInlineEvents(charmsChangeTracker.InlineEvents, ref charmsInlineCursor);
        }

        private static void ResetInlineTimelineCursors()
        {
            speedWarnInlineCursor = 0;
            hitWarnInlineCursor = 0;
            debugModEventsInlineCursor = 0;
            debugHotkeysInlineCursor = 0;
            debugMenuInlineCursor = 0;
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
                string colorHex = GetCachedColorHex(keyEvent.Color);
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
                Type bindableType = FindType("DebugMod.BindableFunctions");
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
                long nowUnixTime = GetCachedFrameUnixTimeOrNow();
                debugMenuTracker.LogManualChange(writer, activeArena, lastUnixTime, nowUnixTime, "Cheats/Kill All", null, "Executed");
            }
        }

        private static void DebugKillSelfDetour(Action orig)
        {
            orig();
            if (isLogging && writer != null)
            {
                long nowUnixTime = GetCachedFrameUnixTimeOrNow();
                debugMenuTracker.LogManualChange(writer, activeArena, lastUnixTime, nowUnixTime, "Cheats/Kill Self", null, "Executed");
            }
        }

        private static Type FindType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
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



