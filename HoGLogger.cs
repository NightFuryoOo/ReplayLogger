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
using System.Runtime.CompilerServices;
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
        private static readonly Dictionary<int, string> CustomCharmDisplayNames = new()
        {
            { (int)Charm.MarkOfPurity, "Mark of Purity" },
            { (int)Charm.VesselsLament, "Vessel's Lament" },
            { (int)Charm.BoonOfHallownest, "Boon of Hallownest" },
            { (int)Charm.AbyssalBloom, "Abyssal Bloom" }
        };

        private static bool isLogging;
        private static string activeArena;
        private static string lastSceneName = string.Empty;
        private static string currentTempFile;
        private static StreamWriter writer;
        private static CustomCanvas customCanvas;
        private static string masterKeyBlob;
        private static HoGBucketInfo currentBucketInfo = HoGBucketInfo.CreateDefault(null);
        private static string pendingHoGDefaultFolder = HoGLoggerConditions.DefaultBucket;
        private static readonly Dictionary<string, BossHpState> bossHpStates = new(StringComparer.Ordinal);

        private static long lastUnixTime;
        private static long startUnixTime;
        private static int bossCounter;
        private static float lastFps;
        private static SpeedWarnTracker speedWarnTracker = new();
        private static HitWarnTracker hitWarnTracker = new();
        private static FlukenestTracker flukenestTracker = new();
        private static DebugModEventsTracker debugModEventsTracker = new();
        private static DebugHotkeysTracker debugHotkeysTracker = new();
        private static CharmsChangeTracker charmsChangeTracker = new();

        private static List<string> damageAndInv = new();
        private static List<string> invWarnings = new();
        private static List<string> debugModEvents = new();
        private static List<string> debugHotkeyBindings = new();
        private static List<string> debugHotkeyEvents = new();
        private static DamageChangeTracker damageChangeTracker = new();
        private static DebugMenuTracker debugMenuTracker = new();
        private static int currentAttemptIndex = 1;
        private static long lastLoggedDeltaMs = -1;

        private static Dictionary<HKHealthManager, (int maxHP, int lastHP)> infoBoss = new();
        private static Dictionary<KeyCode, List<string>> debugHotkeysByKey = new();
        private static Hook debugKillAllHook;
        private static Hook debugKillSelfHook;

        private static bool isInvincible;
        private static bool isChange;
        private static float invTimer;
        [ModuleInitializer]
        internal static void InitializeModule()
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

            On.SceneLoad.Begin += SceneLoad_Begin;
            On.SceneLoad.RecordEndTime += SceneLoad_RecordEndTime;
            On.GameManager.Update += GameManager_Update;
            On.BossSceneController.Update += BossSceneController_Update;
            On.HeroController.FixedUpdate += HeroController_FixedUpdate;
            On.QuitToMenu.Start += QuitToMenu_Start;
            On.SpellFluke.DoDamage += SpellFluke_DoDamage;

            ModHooks.HitInstanceHook += ModHooks_HitInstanceHook;
            ModHooks.ApplicationQuitHook += ApplicationQuit;
            HoGRoomConditions.Initialize();
            HoGRoomConditions.BossHpDetected += OnBossHpDetected;

            lastSceneName = GameManager.instance?.sceneName ?? string.Empty;
            hooksInitialized = true;
        }

        private static void SceneLoad_Begin(On.SceneLoad.orig_Begin orig, SceneLoad self)
        {
            long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            lastUnixTime = now;

            string targetScene = self.TargetSceneName ?? string.Empty;
            string previousScene = lastSceneName;

            if (!isLogging && HoGLoggerConditions.ShouldStartLogging(previousScene, targetScene))
            {
                StartLogging(targetScene);
            }
            else if (isLogging && HoGLoggerConditions.ShouldStopLogging(activeArena, targetScene))
            {
                StopLogging(targetScene);
            }

            lastSceneName = targetScene;
            orig(self);
        }

        private static void SceneLoad_RecordEndTime(On.SceneLoad.orig_RecordEndTime orig, SceneLoad self, SceneLoad.Phases phase)
        {
            orig(self, phase);
            if (phase == SceneLoad.Phases.UnloadUnusedAssets && isLogging)
            {
                infoBoss.Clear();
            }
        }

        private static void GameManager_Update(On.GameManager.orig_Update orig, GameManager self)
        {
            orig(self);
            if (!isLogging || writer == null)
            {
                return;
            }

            MonitorTimeScale();
            MonitorHeroHealth();
            MonitorDebugModUi();
            debugMenuTracker.Update(writer, activeArena, lastUnixTime);

            DateTimeOffset relative = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.Now.ToUnixTimeMilliseconds() - startUnixTime);
            customCanvas?.UpdateTime(relative.ToString("HH:mm:ss"));

            foreach (KeyCode keyCode in Enum.GetValues(typeof(KeyCode)))
            {
                if (!Input.GetKeyDown(keyCode) && !Input.GetKeyUp(keyCode))
                {
                    continue;
                }

                string keyStatus = Input.GetKeyDown(keyCode) ? "+" : "-";
                long unixTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                float fps = Time.unscaledDeltaTime == 0 ? lastFps : 1f / Time.unscaledDeltaTime;
                lastFps = fps;

                customCanvas?.UpdateWatermark(keyCode);

                int watermarkNumber = customCanvas?.numberInCanvas?.Number ?? 0;
                Color watermarkColorStruct = customCanvas?.numberInCanvas?.Color ?? Color.white;
                string watermarkColor = ColorUtility.ToHtmlStringRGBA(watermarkColorStruct);

                long delta = unixTime - lastUnixTime;

                if (lastLoggedDeltaMs >= 0 && delta < lastLoggedDeltaMs)
                {
                    currentAttemptIndex++;
                    bossCounter++;
                    string timestamp = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss.fff", CultureInfo.InvariantCulture);
                    long playTime = (int)(PlayerData.instance.playTime * 100);
                    string startLine = $"{timestamp}|{unixTime}|{playTime}|{activeArena}| {bossCounter}*";
                    damageAndInv.Add(startLine);

                    try
                    {
                        writer.WriteLine(KeyloggerLogEncryption.EncryptLog(startLine));
                        writer.Flush();
                    }
                    catch (Exception e)
                    {
                        Modding.Logger.LogWarn($"HoGLogger: failed to write attempt separator/start line: {e.Message}");
                    }
                }

                lastLoggedDeltaMs = delta;

                string formattedKey = JoystickKeyMapper.FormatKey(keyCode);
                string logEntry = $"+{delta}|{formattedKey}|{keyStatus}|{watermarkNumber}|#{watermarkColor}|{fps.ToString("F0")}|";
                try
                {
                    writer.WriteLine(KeyloggerLogEncryption.EncryptLog(logEntry));
                    writer.Flush();
                }
                catch (Exception e)
                {
                    Modding.Logger.LogError($"HoGLogger: failed to write key entry: {e.Message}");
                }

                if (keyStatus == "+" && debugHotkeysByKey.Count > 0 && debugHotkeysByKey.TryGetValue(keyCode, out List<string> debugActions))
                {
                    debugHotkeysTracker.TrackActivation(keyCode, activeArena ?? "UnknownArena", lastUnixTime, unixTime);
                    foreach (string action in debugActions)
                    {
                        LogDebugHotkeyActivation(action, keyCode, unixTime);
                    }
                }
            }
        }

        private static void BossSceneController_Update(On.BossSceneController.orig_Update orig, BossSceneController self)
        {
            if (isLogging)
            {
                EnemyUpdate();
            }
            orig(self);
        }

        private static void HeroController_FixedUpdate(On.HeroController.orig_FixedUpdate orig, HeroController self)
        {
            if (isLogging)
            {
                InvCheck();
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
        }

        private static HitInstance ModHooks_HitInstanceHook(HutongGames.PlayMaker.Fsm owner, HitInstance hit)
        {
            if (!isLogging || owner?.GameObject == null)
            {
                return hit;
            }

            long unixTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            string ownerName = owner.GameObject.GetFullPath();

            damageChangeTracker.Track(ownerName, activeArena, unixTime - lastUnixTime, hit.DamageDealt, hit.Multiplier);

            return hit;
        }

        private static void SpellFluke_DoDamage(On.SpellFluke.orig_DoDamage orig, SpellFluke self, GameObject obj, int upwardRecursionAmount, bool burst)
        {
            FlukenestTracker.HandleDoDamage(isLogging, writer, flukenestTracker, activeArena, lastUnixTime, orig, self, obj, upwardRecursionAmount, burst);
        }

        private static void MonitorAttemptSeparator()
        {
            long relativeMs = DateTimeOffset.Now.ToUnixTimeMilliseconds() - startUnixTime;

            if (lastLoggedDeltaMs >= 0 && relativeMs < lastLoggedDeltaMs)
            {
                currentAttemptIndex++;
                string timestamp = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss.fff", CultureInfo.InvariantCulture);
                string separator = $"-----ATTEMPT #{currentAttemptIndex}----- {timestamp}";

                damageAndInv.Add(separator);

                if (writer != null)
                {
                    try
                    {
                        writer.WriteLine(KeyloggerLogEncryption.EncryptLog(separator));
                        writer.Flush();
                    }
                    catch (Exception e)
                    {
                        Modding.Logger.LogWarn($"HoGLogger: failed to write attempt separator: {e.Message}");
                    }
                }
            }

            lastLoggedDeltaMs = relativeMs;
        }

        private static void StartLogging(string arenaName)
        {
            lock (SyncRoot)
            {
                if (isLogging || string.IsNullOrEmpty(arenaName))
                {
                    return;
                }

                try
                {
                    EnsureCanvasSpritesLoaded();

                    startUnixTime = lastUnixTime;
                    bossCounter = 1;
                    masterKeyBlob = KeyloggerLogEncryption.GenerateKeyAndIV();
                    AllHallownestEnhancedToggleSnapshot snapshot = AheSettingsManager.RefreshSnapshot();
                    pendingHoGDefaultFolder = HoGLoggerConditions.DefaultBucket;

                    bool requiresHp = HoGStoragePlanner.RequiresHp(arenaName);
                    if (requiresHp)
                    {
                        ResetBossHpState(arenaName);
                    }

                    int? initialHp = requiresHp ? TryGetBossHp(arenaName) : null;

                    HoGStoragePlan initialPlan = HoGStoragePlanner.GetPlan(arenaName, snapshot, initialHp);
                    ApplyHoGStoragePlan(initialPlan);
                    HoGRoomConditions.MarkPendingScene(initialPlan.NeedsHp ? arenaName : null);
                    string tempDir = Path.GetTempPath();
                    currentTempFile = Path.Combine(tempDir, $"ReplayLoggerHoG_{Guid.NewGuid():N}.log");

                    writer = new StreamWriter(currentTempFile, false);

                    CoreSessionLogger.WriteEncryptedModSnapshot(writer, ModsDirectory, "---------------------------------------------------");

                    string equippedCharms = CoreSessionLogger.BuildEquippedCharmsLine();
                    writer.WriteLine(KeyloggerLogEncryption.EncryptLog(equippedCharms));
                    CoreSessionLogger.WriteEncryptedSkillLines(writer, "---------------------------------------------------");

                    int currentPlayTime = (int)(PlayerData.instance.playTime * 100);
                    int seed = (int)(lastUnixTime ^ currentPlayTime);

                    customCanvas = new CustomCanvas(new NumberInCanvas(seed), new LoadingSprite(masterKeyBlob));
                    customCanvas?.StartUpdateSprite();

                    string timestamp = DateTimeOffset.FromUnixTimeMilliseconds(lastUnixTime).ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss.fff", CultureInfo.InvariantCulture);

                    damageAndInv = new();
                    invWarnings = new();
                    speedWarnTracker.ClearWarnings();
                    hitWarnTracker.Reset();
                    debugModEvents = new();
                    debugHotkeyBindings = new();
                    debugHotkeyEvents = new();
                    damageChangeTracker.Reset();
                    flukenestTracker.Reset();
                    infoBoss = new();
                    debugHotkeysByKey = new();
                    currentAttemptIndex = 1;
                    lastLoggedDeltaMs = -1;
                    charmsChangeTracker.Reset();

                    damageAndInv.Add($"{timestamp}|{lastUnixTime}|{arenaName}| {bossCounter}*");
                    writer.WriteLine(KeyloggerLogEncryption.EncryptLog($"{timestamp}|{lastUnixTime}|{currentPlayTime}|{arenaName}| {bossCounter}*"));
                    writer.Flush();

                    speedWarnTracker.Reset(Mathf.Max(Time.timeScale, 0f));
                    InitializeDebugModHooks();
                    bool initialDebugUiVisible = DebugModIntegration.TryGetUiVisible(out bool visible) && visible;
                    debugModEventsTracker.Reset(initialDebugUiVisible);
                    InitializeDebugHotkeys();
                    debugMenuTracker.Reset(initialDebugUiVisible);
                    speedWarnTracker.LogInitial(writer, lastUnixTime);

                    activeArena = arenaName;
                    isLogging = true;
                }
                catch (Exception e)
                {
                    Modding.Logger.LogError($"HoGLogger: failed to start log for {arenaName}: {e.Message}");
                    StopLogging("InitFailed");
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
                    Modding.Logger.LogError($"HoGLogger: failed to finalize log: {e.Message}");
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

            writer.WriteLine(KeyloggerLogEncryption.EncryptLog($"StartTime: {ReplayLogger.ConvertUnixTimeToDateTimeString(startUnixTime)}, EndTime: {ReplayLogger.ConvertUnixTimeToDateTimeString(endUnixTime)}, TimeInPlay: {ReplayLogger.ConvertUnixTimeToTimeString(endUnixTime - startUnixTime)}"));
            CoreSessionLogger.WriteDamageInvSection(writer, damageAndInv, separatorAfter: null);
            writer.WriteLine(KeyloggerLogEncryption.EncryptLog($"StartTime: {ReplayLogger.ConvertUnixTimeToDateTimeString(startUnixTime)}, EndTime: {ReplayLogger.ConvertUnixTimeToDateTimeString(endUnixTime)}, TimeInPlay: {ReplayLogger.ConvertUnixTimeToTimeString(endUnixTime - startUnixTime)}"));
            CoreSessionLogger.WriteSeparator(writer);
            writer.WriteLine(KeyloggerLogEncryption.EncryptLog("\n\n"));
            CoreSessionLogger.WriteWarningsSection(writer, invWarnings);

            writer.WriteLine(KeyloggerLogEncryption.EncryptLog("\n\n"));
            CoreSessionLogger.WriteSpeedWarningsSection(writer, speedWarnTracker.Warnings);

            writer.WriteLine(KeyloggerLogEncryption.EncryptLog("\n\n"));
            writer.WriteLine(KeyloggerLogEncryption.EncryptLog("HitWarn:"));
            foreach (string warning in hitWarnTracker.Warnings)
            {
                writer.WriteLine(KeyloggerLogEncryption.EncryptLog(warning));
            }
            writer.WriteLine(KeyloggerLogEncryption.EncryptLog("\n\n"));
            writer.WriteLine(KeyloggerLogEncryption.EncryptLog("---------------------------------------------------"));
            DamageChangeTracker.WriteSection(writer, damageChangeTracker);
            FlukenestTracker.WriteSectionWithSeparator(writer, flukenestTracker);
            charmsChangeTracker.Write(writer);

            RefreshBucketInfo(force: true);

            writer.WriteLine(KeyloggerLogEncryption.EncryptLog("-"));
            AheSettingsManager.WriteSettingsWithSeparator(writer);
            writer.WriteLine(KeyloggerLogEncryption.EncryptLog($"HoG Bucket: {currentBucketInfo.BucketLabel ?? HoGLoggerConditions.DefaultBucket}"));
            writer.WriteLine(KeyloggerLogEncryption.EncryptLog(string.Empty));
            writer.WriteLine(KeyloggerLogEncryption.EncryptLog("---------------------------------------------------"));
            ZoteSettingsManager.WriteSettingsWithSeparator(writer);
            CollectorPhasesSettingsManager.WriteSettingsWithSeparator(writer);
            SafeGodseekerQolIntegration.WriteSettingsWithSeparator(writer);

            DebugModEventsWriter.Write(writer, debugModEventsTracker.Events);
            DebugHotKeysWriter.Write(writer, debugHotkeysTracker.Bindings, debugHotkeysTracker.Activations);
            debugMenuTracker.WriteSection(writer);

            CoreSessionLogger.WriteNoBlurSettings(writer);
            CoreSessionLogger.WriteCustomizableAbilitiesSettings(writer);
            CoreSessionLogger.WriteControlSettings(writer);

            CoreSessionLogger.WriteEncryptedModSnapshot(writer, ModsDirectory, "---------------------------------------------------");

            writer.Write(masterKeyBlob);
            writer.Flush();
            writer.Dispose();

            MoveTempFileToFinalLocation();
            customCanvas?.ClearHud();
        }

        private static void CleanupState()
        {
            string arenaToReset = activeArena;
            writer = null;
            customCanvas?.DestroyCanvasDelayed(2.0f);
            customCanvas = null;
            masterKeyBlob = null;
            speedWarnTracker = new SpeedWarnTracker();
            damageAndInv = new();
            invWarnings = new();
            debugModEvents = new();
            debugHotkeyBindings = new();
            debugHotkeyEvents = new();
            debugHotkeysTracker.Reset();
            debugMenuTracker.Reset();
            damageChangeTracker = new();
            flukenestTracker = new();
            currentAttemptIndex = 1;
            lastLoggedDeltaMs = -1;
            hitWarnTracker = new HitWarnTracker();
            infoBoss = new();
            debugHotkeysByKey = new();
            isLogging = false;
            activeArena = null;
            bossCounter = 0;
            isInvincible = false;
            invTimer = 0f;
            currentTempFile = null;
            currentBucketInfo = HoGBucketInfo.CreateDefault(null);
            AheSettingsManager.Reset();
            pendingHoGDefaultFolder = HoGLoggerConditions.DefaultBucket;
            speedWarnTracker = new SpeedWarnTracker();
            debugModEventsTracker.Reset();
            HoGRoomConditions.MarkPendingScene(null);

            if (HoGStoragePlanner.RequiresHp(arenaToReset))
            {
                ResetBossHpState(arenaToReset);
            }
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
                string fileLabel = string.IsNullOrEmpty(currentBucketInfo.FilePrefix) ? displayName : currentBucketInfo.FilePrefix;
                string timeSuffix = DateTimeOffset.FromUnixTimeMilliseconds(lastUnixTime).ToLocalTime().ToString("dd-MM-yyyy HH-mm-ss", CultureInfo.InvariantCulture);
                string rootFolder = string.IsNullOrEmpty(currentBucketInfo.RootFolder) ? HoGLoggerConditions.DefaultBucket : currentBucketInfo.RootFolder;

                if (SafeGodseekerQolIntegration.IsP5HealthEnabled())
                {
                    rootFolder = "P5 HEALTH";
                }

                string finalDir = Path.Combine(DllDirectory, rootFolder, displayName);
                Directory.CreateDirectory(finalDir);

                string finalPath = Path.Combine(finalDir, $"{fileLabel} ({timeSuffix}).log");
                if (File.Exists(finalPath))
                {
                    File.Delete(finalPath);
                }

                if (File.Exists(currentTempFile))
                {
                    File.Move(currentTempFile, finalPath);
                    string toastText = $"{currentBucketInfo.BucketLabel ?? HoGLoggerConditions.DefaultBucket}: {Path.GetFileName(finalPath)}";
                    customCanvas?.ShowSavedFileToast(toastText, 2.0f);
                }
            }
            catch (Exception e)
            {
                Modding.Logger.LogError($"HoGLogger: failed to move log file: {e.Message}");
            }
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
                HoGStoragePlan plan = HoGStoragePlanner.GetPlan(activeArena, AheSettingsManager.CurrentSnapshot, state.Highest);
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
            if (!snapshot.Available)
            {
                return;
            }

            // Use the scene we came from before entering activeArena to disambiguate e.g. Pale Court entry
            string previousScene = lastSceneName;
            HoGStoragePlan plan = HoGStoragePlanner.GetPlan(activeArena, snapshot, GetStoredHp(activeArena), previousScene);
            ApplyHoGStoragePlan(plan);
        }

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
        private static void InvCheck()
        {
            bool shouldBeInvincible =
                HeroController.instance.cState.invulnerable ||
                PlayerData.instance.isInvincible ||
                HeroController.instance.cState.shadowDashing ||
                HeroController.instance.damageMode == DamageMode.HAZARD_ONLY ||
                HeroController.instance.damageMode == DamageMode.NO_DAMAGE;

            var bossList = infoBoss.GetKeysWithUniqueGameObject().Values;

            if (shouldBeInvincible && !isInvincible)
            {
                isInvincible = true;
                invTimer = 0f;

                long unixTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                string hpInfo = "";
                foreach (var boss in bossList)
                {
                    hpInfo += $"|{infoBoss[boss].lastHP}/{infoBoss[boss].maxHP}";
                }
                damageAndInv.Add($"\u00A0+{unixTime - lastUnixTime}{hpInfo}|(INV ON)|");
            }

            if (!shouldBeInvincible && isInvincible)
            {
                isInvincible = false;
                long unixTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                string hpInfo = "";
                foreach (var boss in bossList)
                {
                    hpInfo += $"|{infoBoss[boss].lastHP}/{infoBoss[boss].maxHP}";
                }
                damageAndInv.Add($"\u00A0+{unixTime - lastUnixTime}{hpInfo}|(INV OFF, {invTimer.ToString("F3", CultureInfo.InvariantCulture)})|");
                if (invTimer > 2.6f)
                {
                    invWarnings.Add($"|{activeArena}|+{unixTime - lastUnixTime}{hpInfo}|(INV OFF, {invTimer.ToString("F3", CultureInfo.InvariantCulture)})");
                }
                invTimer = 0f;
            }

            if (isInvincible)
            {
                invTimer += Time.fixedDeltaTime;
            }
        }

        private static void EnemyUpdate()
        {
            List<HKHealthManager> healthManagers = new();
            float searchRadius = 100f;
            int enemyLayer = Physics2D.AllLayers;

            Collider2D[] colliders = Physics2D.OverlapBoxAll(HeroController.instance.transform.position, Vector2.one * searchRadius, 0f, enemyLayer);
            foreach (Collider2D collider in colliders)
            {
                GameObject enemyObject = collider.gameObject;
                if (enemyObject.activeInHierarchy)
                {
                    HKHealthManager enemyHealthManager = enemyObject.GetComponent<HKHealthManager>();
                    if (enemyHealthManager != null)
                    {
                        healthManagers.Add(enemyHealthManager);
                    }
                }
            }

            foreach (HKHealthManager enemyHealthManager in healthManagers.ToList())
            {
                if (enemyHealthManager != null && enemyHealthManager.hp > 0 && !infoBoss.ContainsKey(enemyHealthManager))
                {
                    infoBoss.Add(enemyHealthManager, (enemyHealthManager.hp, 0));
                }

            }

            long unixTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            string hpInfo = "";

            var bossKeys = infoBoss.GetKeysWithUniqueGameObject().Values;
            foreach (var boss in infoBoss.Keys.ToList())
            {
                if (!bossKeys.Contains(boss) && !boss.isDead)
                {
                    continue;
                }

                if (boss.hp != infoBoss[boss].lastHP)
                {
                    infoBoss[boss] = (infoBoss[boss].maxHP, boss.hp);
                    isChange = true;
                }

                hpInfo += $"|{infoBoss[boss].lastHP}/{infoBoss[boss].maxHP}";
            }

            if (isChange)
            {
                damageAndInv.Add($"\u00A0+{unixTime - lastUnixTime}{hpInfo}|");
            }
            isChange = false;

            infoBoss.RemoveAll(kvp => kvp.Key.isDead || kvp.Key.hp <= 0);

            if (!string.IsNullOrEmpty(activeArena) && HoGStoragePlanner.RequiresHp(activeArena))
            {
                BossHpState state = GetBossHpState(activeArena);
                if (state != null)
                {
                    int newHpMetric;
                    if (string.Equals(activeArena, HoGLoggerConditions.PaleCourtWhiteDefenderScene, StringComparison.Ordinal))
                    {
                        newHpMetric = infoBoss.GetKeysWithUniqueGameObject().Values.Sum(hm => infoBoss[hm].maxHP);
                        state.SumMax = Math.Max(state.SumMax, newHpMetric);
                    }
                    else
                    {
                        newHpMetric = infoBoss.GetKeysWithUniqueGameObject().Values.Select(hm => infoBoss[hm].maxHP).DefaultIfEmpty(0).Max();
                    }

                    if (newHpMetric > state.Highest)
                    {
                        state.Highest = newHpMetric;
                        state.Cached = newHpMetric;
                        if (state.Waiting)
                        {
                            HoGStoragePlan plan = HoGStoragePlanner.GetPlan(activeArena, AheSettingsManager.CurrentSnapshot, newHpMetric, lastSceneName);
                            ApplyHoGStoragePlan(plan);
                        }
                    }
                }
            }
        }

        private static void MonitorHeroHealth()
        {
            if (!isLogging || writer == null)
            {
                return;
            }

            string roomName = GameManager.instance?.sceneName ?? activeArena;
            hitWarnTracker.Update(writer, roomName, lastUnixTime);
            charmsChangeTracker.Update(activeArena, lastUnixTime);
        }

        private static void MonitorDebugModUi()
        {
            if (!isLogging || writer == null)
            {
                return;
            }

            debugModEventsTracker.Update(writer, activeArena, lastUnixTime);
        }

        private static void MonitorTimeScale()
        {
            if (!isLogging || writer == null)
            {
                speedWarnTracker.Reset(Mathf.Max(Time.timeScale, 0f));
                speedWarnTracker.ClearWarnings();
                return;
            }

            speedWarnTracker.Update(writer, activeArena, lastUnixTime);
        }

        private static void LogDebugHotkeyActivation(string actionName, KeyCode keyCode, long unixTime)
        {
            if (writer == null)
            {
                return;
            }

            long delta = unixTime - lastUnixTime;
            string entry = $"DebugHotkey|+{delta}|{actionName}|{keyCode}";
            writer.WriteLine(KeyloggerLogEncryption.EncryptLog(entry));
            writer.Flush();

            string arenaName = activeArena ?? "UnknownArena";
            debugHotkeyEvents.Add($"  |{arenaName}|+{delta}|{actionName} ({keyCode})");
        }



        private static void LogDebugModUiEvent()
        {
            // Deprecated: replaced by DebugModEventsTracker.
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
                Modding.Logger.LogWarn($"HoGLogger: failed to hook DebugMod functions: {e.Message}");
            }
        }

        private static void DebugKillAllDetour(Action orig)
        {
            orig();
            if (isLogging && writer != null)
            {
                debugMenuTracker.LogManualChange(writer, activeArena, lastUnixTime, "Cheats/Kill All", null, "Executed");
            }
        }

        private static void DebugKillSelfDetour(Action orig)
        {
            orig();
            if (isLogging && writer != null)
            {
                debugMenuTracker.LogManualChange(writer, activeArena, lastUnixTime, "Cheats/Kill Self", null, "Executed");
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
    }
}

#if NETFRAMEWORK
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    internal sealed class ModuleInitializerAttribute : Attribute { }
}
#endif

