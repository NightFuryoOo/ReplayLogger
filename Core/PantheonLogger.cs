using GlobalEnums;
using IL;
using Modding;
using On;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using MonoMod.RuntimeDetour;
using UObject = UnityEngine.Object;

namespace ReplayLogger
{
    public partial class ReplayLogger : Mod, ICustomMenuMod, IGlobalSettings<ReplayLoggerSettings>
    {
        internal static ReplayLogger Instance;

        internal CustomCanvas customCanvas;
        private string dllDir;
        private string modsDir;

        private StreamWriter writer;
        private string lastString;
        private KeyloggerLogEncryption.Session activeEncryptionSession;
        private string lastScene;
        private (string name, List<string> list) currentPanteon;
        private List<string> capturedPantheonSequence;
        private int capturedPantheonNumber;
        private bool currentPantheonUsesTrueBossRush;
        private bool currentPantheonUsesRandomOrder;

        private long lastUnixTime;
        private long startUnixTime;

        private bool isPlayChalange = false;
        private bool gameplayHooksActive;
        private int currentPantheonNumber;
        private int bossCounter;

        private const int BlockSizeBytes = 96 * 1024;
        private const int BlockMaxAgeMs = 3000;
        private const int LogQueueCapacity = 32768;
        private const int BufferedSectionThreshold = 200;
        private BufferedLogSection pressedButtonsLog;
        private BufferedLogSection DamageAnfInv;
        private BufferedLogSection InvWarn;
        private BufferedLogSection speedWarnBuffer;
        private BufferedLogSection hitWarnBuffer;
        private readonly List<KeyLogEvent> keyLogBuffer = new(256);
        private readonly List<string> keyLogFlushLines = new(256);
        private const int KeyLogFlushIntervalMs = 200;
        private const int KeyLogFlushBatchSize = 50;
        private long lastKeyLogFlushTime;
        private int lastHudElapsedSeconds = -1;
        private readonly AdaptiveKeyScanner keyScanner = new();
        private const int EnemyColliderBufferInitialSize = 1024;
        private const float EnemyLayerMaskRefreshIntervalSeconds = 1f;
        private const float EnemyColliderOverflowWarnIntervalSeconds = 5f;
        private Collider2D[] enemyColliderBuffer = new Collider2D[EnemyColliderBufferInitialSize];
        private bool enemyColliderBufferOverflowLogged;
        private float lastEnemyColliderOverflowWarningTime;
        private int enemyScanLayerMask;
        private float lastEnemyLayerMaskRefreshTime;
        private readonly Dictionary<GameObject, HealthManager> enemyHealthManagerByGameObject = new(512);
        private readonly List<GameObject> enemyHealthManagerCacheCleanupBuffer = new(64);
        private float lastEnemyHealthCacheCleanupTime;
        private int enemyHealthCacheCleanupCursor;
        private readonly List<GameObject> ownerPathCacheCleanupBuffer = new(128);
        private float lastOwnerPathCacheCleanupTime;
        private int ownerPathCacheCleanupCursor;
        private const float EnemyUpdateIntervalSeconds = 0.1f;
        private const float EnemySeedRetryIntervalSeconds = 0.5f;
        private float lastEnemyUpdateTime;
        private float lastEnemySeedTime;

        private readonly SpeedWarnTracker speedWarnTracker = new();
        private readonly HitWarnTracker hitWarnTracker = new();

        private readonly DamageChangeTracker damageChangeTracker = new();
        private readonly List<string> debugModEvents = new();
        private readonly DebugModEventsTracker debugModEventsTracker = new();
        private readonly DebugHotkeysTracker debugHotkeysTracker = new();
        private readonly DebugMenuTracker debugMenuTracker = new();
        private readonly CharmsChangeTracker charmsChangeTracker = new();
        private readonly GodhomeQolTracker godhomeQolTracker = new();
        private readonly GrubsongSoulGainTracker grubsongSoulGainTracker = new();
        private readonly DreamNailSoulGainTracker dreamNailSoulGainTracker = new();
        private readonly SoulSpentTracker soulSpentTracker = new();
        private readonly WeaversongSoulGainTracker weaversongSoulGainTracker = new();

        private readonly BossPhaseThresholdTracker bossPhaseThresholdTracker = new();
        private readonly BossSpawnHpTracker bossSpawnHpTracker = new();

        private List<ITrackerLifecycle> LifecycleTrackers() =>
        [
            grubsongSoulGainTracker,
            dreamNailSoulGainTracker,
            soulSpentTracker,
            weaversongSoulGainTracker,
            bossPhaseThresholdTracker,
            bossSpawnHpTracker
        ];

        private int speedWarnInlineCursor;
        private int hitWarnInlineCursor;
        private int debugModEventsInlineCursor;
        private int debugHotkeysInlineCursor;
        private int debugMenuInlineCursor;
        private int charmsInlineCursor;
        private static readonly HashSet<string> SkipScenes = new(StringComparer.Ordinal)
        {
            "GG_Spa",
            "GG_Engine",
            "GG_Unn",
            "GG_Engine_Root",
            "GG_Wyrm",
            "GG_Engine_Prime",
            "GG_Atrium",
            "GG_Atrium_Roof"
        };
        private static readonly HashSet<string> TrueBossRushRemovedScenes = new(StringComparer.Ordinal)
        {
            "GG_Spa",
            "GG_Engine",
            "GG_Engine_Prime",
            "GG_Engine_Root",
            "GG_Unn",
            "GG_Wyrm"
        };
        private static Hook debugKillAllHook;
        private static Hook debugKillSelfHook;
        private static readonly string[] HitWarnSectionHeaderLines = { "\n\n", "HitWarn:" };
        private static readonly string[] HitWarnSectionFooterLines = { "\n\n", "---------------------------------------------------" };
        private bool damageSectionStarted;
        private bool pressedButtonsSectionStarted;
        private bool skipPantheonLogging;
        private float pantheonToastHideAtUnscaledTime;

        public ReplayLogger() : base(ModInfo.Name) { }
        public override string GetVersion() => ModInfo.Version;

        public override void Initialize()
        {
            Instance = this;
            On.SceneLoad.Begin += OpenFile;
            On.GameManager.Update += CheckPressedKey;
            ModHooks.ApplicationQuitHook += OnApplicationQuit;
            On.QuitToMenu.Start += QuitToMenu_Start;

            On.SceneLoad.RecordEndTime += SceneLoad_RecordEndTime;
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

            On.BossSequenceController.FinishLastBossScene += BossSequenceController_FinishLastBossScene;
            On.BossSequenceController.SetupNewSequence += BossSequenceController_SetupNewSequence;

            dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            modsDir = new DirectoryInfo(dllDir).Parent.FullName;

            ModsChecking.PrimeHeavyModCache(modsDir);
            HoGLogger.EnsureInitialized();

            lastString = string.Empty;
            activeEncryptionSession = null;

            CustomCanvas.flagSpriteFalse = CustomCanvas.LoadEmbeddedSprite("Geo.png");
            CustomCanvas.flagSpriteTrue = CustomCanvas.LoadEmbeddedSprite("ElegantKey.png");

            pressedButtonsLog = null;
            DamageAnfInv = null;
            InvWarn = null;
            debugModEventsTracker.Reset();
            debugHotkeysTracker.InitializeBindings();

            InitializeHotkeys();
            InitializeRebindListener();
        }

        private void EnsureGameplayHooksActive()
        {
            if (gameplayHooksActive)
            {
                return;
            }

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
            gameplayHooksActive = true;
        }

        private void ReleaseGameplayHooksIfIdle()
        {
            if (!gameplayHooksActive || isPlayChalange)
            {
                return;
            }

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
            gameplayHooksActive = false;
        }

        private string isChallengeCompleted = "-";
        private void BossSequenceController_FinishLastBossScene(On.BossSequenceController.orig_FinishLastBossScene orig, BossSceneController self)
        {
            isChallengeCompleted = "+";
            orig(self);
        }

        private void BossSequenceController_SetupNewSequence(
            On.BossSequenceController.orig_SetupNewSequence orig,
            BossSequence sequence,
            BossSequenceController.ChallengeBindings bindings,
            string playerData
        )
        {
            orig(sequence, bindings, playerData);
            capturedPantheonSequence = TryGetPantheonSequence(sequence);
            capturedPantheonNumber = DeterminePantheonNumber(capturedPantheonSequence);
            currentPantheonNumber = capturedPantheonNumber;
        }

        private static List<string> TryGetPantheonSequence(BossSequence sequence)
        {
            if (sequence == null)
            {
                return null;
            }

            try
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                FieldInfo bossScenesField = typeof(BossSequence).GetField("bossScenes", flags);
                if (bossScenesField?.GetCachedValue(sequence) is not BossScene[] bossScenes || bossScenes.Length == 0)
                {
                    return null;
                }

                return bossScenes
                    .Where(scene => scene != null && !string.IsNullOrWhiteSpace(scene.sceneName))
                    .Select(scene => scene.sceneName)
                    .ToList();
            }
            catch
            {
                return null;
            }
        }

        private static int DeterminePantheonNumber(IReadOnlyCollection<string> scenes)
        {
            if (scenes == null || scenes.Count == 0)
            {
                return 0;
            }

            try
            {
                HashSet<string> names = new(scenes, StringComparer.OrdinalIgnoreCase);
                if (names.Contains("GG_Wyrm")
                    || names.Contains("GG_Radiance")
                    || names.Contains("GG_Engine_Root")
                    || names.Contains("GG_Grimm_Nightmare"))
                {
                    return 5;
                }

                if (names.Contains("GG_Engine_Prime") || names.Contains("GG_Hollow_Knight"))
                {
                    return 4;
                }

                if (names.Contains("GG_Sly"))
                {
                    return 3;
                }

                if (names.Contains("GG_Painter"))
                {
                    return 2;
                }

                if (names.Contains("GG_Nailmasters"))
                {
                    return 1;
                }
            }
            catch
            {
            }

            return 0;
        }

        private bool TryResolvePantheonRoute(int pantheonNumber, out List<string> route)
        {
            route = GetStandardPantheonRoute(pantheonNumber);
            currentPantheonUsesTrueBossRush = false;
            currentPantheonUsesRandomOrder = false;
            if (route == null)
            {
                return false;
            }

            if (GodhomeQolRandomPantheonsIntegration.IsPantheonRandomized(pantheonNumber))
            {
                return TryResolveRandomOrderRoute(pantheonNumber, route, out route);
            }

            if (!GodhomeQolRandomPantheonsIntegration.IsTrueBossRushEnabled(pantheonNumber))
            {
                return true;
            }

            List<string> expectedRoute = route
                .Where(scene => !TrueBossRushRemovedScenes.Contains(scene))
                .ToList();
            bool sequenceMatches = capturedPantheonNumber == pantheonNumber
                && capturedPantheonSequence != null
                && capturedPantheonSequence.SequenceEqual(expectedRoute, StringComparer.Ordinal);
            if (!sequenceMatches)
            {
                global::ReplayLogger.InternalDiagnostics.Warn(
                    $"ReplayLogger: rejected unexpected GodhomeQoL True Boss Rush route for P{pantheonNumber}.");
                route = null;
                return false;
            }

            currentPantheonUsesTrueBossRush = true;
            route = new List<string>(capturedPantheonSequence);
            global::ReplayLogger.InternalDiagnostics.Info(
                $"ReplayLogger: using validated GodhomeQoL True Boss Rush route for P{pantheonNumber}.");
            return true;
        }

        private bool TryResolveRandomOrderRoute(int pantheonNumber, List<string> standardRoute, out List<string> route)
        {
            if (!CanLogRandomOrder(pantheonNumber, standardRoute, out string reason))
            {
                global::ReplayLogger.InternalDiagnostics.Warn(
                    $"ReplayLogger: rejected GodhomeQoL Random Order route for P{pantheonNumber}: {reason}");
                route = null;
                return false;
            }

            if (RandomOrderRoute.IsStandardOrder(standardRoute, capturedPantheonSequence))
            {
                route = standardRoute;
                return true;
            }

            currentPantheonUsesRandomOrder = true;
            route = new List<string>(capturedPantheonSequence);
            global::ReplayLogger.InternalDiagnostics.Info(
                $"ReplayLogger: using validated GodhomeQoL Random Order route for P{pantheonNumber}.");
            return true;
        }

        private bool CanLogRandomOrder(int pantheonNumber, List<string> standardRoute, out string reason)
        {
            if (capturedPantheonNumber != pantheonNumber)
            {
                reason = $"the captured sequence belongs to P{capturedPantheonNumber}";
                return false;
            }

            return RandomOrderRoute.TryValidate(standardRoute, capturedPantheonSequence, out reason);
        }

        private bool ShouldSkipRandomizedPantheon(int pantheonNumber)
        {
            if (pantheonNumber < 1
                || pantheonNumber > 5
                || !GodhomeQolRandomPantheonsIntegration.IsPantheonRandomized(pantheonNumber))
            {
                return false;
            }

            if (CanLogRandomOrder(pantheonNumber, GetStandardPantheonRoute(pantheonNumber), out string reason))
            {
                return false;
            }

            global::ReplayLogger.InternalDiagnostics.Warn(
                $"ReplayLogger: Random Order P{pantheonNumber} is not logged: {reason}");
            return true;
        }

        private bool IsRandomOrderFirstScene(int pantheonNumber, string targetScene)
        {
            return currentPantheonNumber == pantheonNumber
                && capturedPantheonNumber == pantheonNumber
                && capturedPantheonSequence != null
                && capturedPantheonSequence.Count > 0
                && string.Equals(capturedPantheonSequence[0], targetScene, StringComparison.Ordinal)
                && GodhomeQolRandomPantheonsIntegration.IsPantheonRandomized(pantheonNumber);
        }

        private bool IsPantheonFiveEntry(string targetScene)
        {
            return lastScene == "GG_Atrium_Roof"
                && !string.IsNullOrEmpty(targetScene)
                && (targetScene.Contains("GG_Vengefly_V") || IsRandomOrderFirstScene(5, targetScene));
        }

        private string GetPantheonLogName(int pantheonNumber)
        {
            return currentPantheonUsesRandomOrder ? $"P{pantheonNumber} Random Order" : $"P{pantheonNumber}";
        }

        private static List<string> GetStandardPantheonRoute(int pantheonNumber)
        {
            return pantheonNumber switch
            {
                1 => Panteons.P1.ToList(),
                2 => Panteons.P2.ToList(),
                3 => Panteons.P3.ToList(),
                4 => Panteons.P4.ToList(),
                5 => Panteons.P5.ToList(),
                _ => null
            };
        }

        private HitInstance ModHooks_HitInstanceHook(HutongGames.PlayMaker.Fsm owner, HitInstance hit)
        {
            if (!isPlayChalange || writer == null)
            {
                return hit;
            }

            if (owner == null || owner.GameObject == null)
            {
                return hit;
            }

            long unixTime = KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime);
            string activeSceneName = GetActiveArenaSceneName();
            GameObject ownerObject = owner.GameObject;
            string ownerName = OwnerPathCache.GetCachedPath(ownerObject, ownerPathByGameObject);

            CharmDamageTracker.TrackPlayMakerHit(
                isPlayChalange,
                writer,
                damageChangeTracker,
                activeSceneName,
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

                if (TryResolveHitTargetHealthManager(hit, out HealthManager hitTarget) &&
                    IsHealthManagerInScene(hitTarget, activeSceneName))
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

        private int ModHooks_AfterTakeDamageHook(int hazardType, int damageAmount)
        {
            if (!isPlayChalange || writer == null)
            {
                return damageAmount;
            }

            long nowUnixTime = KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime);
            string arena = GameManager.instance?.sceneName ?? lastScene;
            hitWarnTracker.LogDamageEvent(writer, arena, lastUnixTime, nowUnixTime, hazardType, damageAmount);
            return damageAmount;
        }

        private void ModHooks_BeforePlayerDeadHook()
        {
            if (!isPlayChalange || writer == null)
            {
                return;
            }

            long nowUnixTime = KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime);
            string arena = GameManager.instance?.sceneName ?? lastScene;
            hitWarnTracker.LogDeathEvent(writer, arena, lastUnixTime, nowUnixTime);
        }

        private string ResolveDamageChangeSceneTag()
        {
            string sceneName = GameManager.instance?.sceneName;
            if (string.IsNullOrEmpty(sceneName))
            {
                sceneName = lastScene;
            }

            if (string.IsNullOrEmpty(sceneName) || SkipScenes.Contains(sceneName))
            {
                return null;
            }

            return sceneName;
        }

        private string GetActiveArenaSceneName()
        {
            string sceneName = GameManager.instance?.sceneName;
            if (string.IsNullOrEmpty(sceneName))
            {
                sceneName = lastScene;
            }

            if (string.IsNullOrEmpty(sceneName) || SkipScenes.Contains(sceneName))
            {
                return null;
            }

            return sceneName;
        }

        private static bool IsGameObjectInScene(GameObject gameObject, string sceneName)
        {
            if (gameObject == null || string.IsNullOrEmpty(sceneName))
            {
                return false;
            }

            Scene scene = gameObject.scene;
            return scene.IsValid() && string.Equals(scene.name, sceneName, StringComparison.Ordinal);
        }

        private static bool IsHealthManagerInScene(HealthManager manager, string sceneName)
        {
            return manager != null && IsGameObjectInScene(manager.gameObject, sceneName);
        }

        private void CleanupTrackedBossesForScene(string sceneName)
        {
            infoBossKeysBuffer.Clear();
            foreach (HealthManager boss in infoBoss.Keys)
            {
                infoBossKeysBuffer.Add(boss);
            }

            bool removedAnyBoss = false;
            foreach (HealthManager boss in infoBossKeysBuffer)
            {
                if (!IsHealthManagerInScene(boss, sceneName))
                {
                    removedAnyBoss |= infoBoss.Remove(boss);
                }
            }

            bool removedAnyEnemyCache = false;
            enemyHealthManagerCacheCleanupBuffer.Clear();
            foreach (var pair in enemyHealthManagerByGameObject)
            {
                GameObject enemyObject = pair.Key;
                if (!IsGameObjectInScene(enemyObject, sceneName) || !IsHealthManagerInScene(pair.Value, sceneName))
                {
                    enemyHealthManagerCacheCleanupBuffer.Add(enemyObject);
                }
            }

            foreach (GameObject enemyObject in enemyHealthManagerCacheCleanupBuffer)
            {
                removedAnyEnemyCache |= enemyHealthManagerByGameObject.Remove(enemyObject);
            }

            bool removedAnyOwnerPath = false;
            ownerPathCacheCleanupBuffer.Clear();
            foreach (var pair in ownerPathByGameObject)
            {
                if (!IsGameObjectInScene(pair.Key, sceneName))
                {
                    ownerPathCacheCleanupBuffer.Add(pair.Key);
                }
            }

            foreach (GameObject ownerObject in ownerPathCacheCleanupBuffer)
            {
                removedAnyOwnerPath |= ownerPathByGameObject.Remove(ownerObject);
            }

            if (removedAnyBoss || removedAnyEnemyCache || removedAnyOwnerPath)
            {
                uniqueBossBuffersDirty = true;
                enemyScanLayerMask = 0;
                lastEnemyLayerMaskRefreshTime = 0f;
            }
        }

        private void HealthManager_Start(On.HealthManager.orig_Start orig, HealthManager self)
        {
            orig(self);

            if (!isPlayChalange || self == null)
            {
                return;
            }

            try
            {
                bossSpawnHpTracker.Record(self, KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime), lastUnixTime);
            }
            catch (Exception e)
            {
                global::ReplayLogger.InternalDiagnostics.Error($"PantheonLogger: boss spawn HP record failed: {e.Message}");
            }
        }

        private void HealthManager_TakeDamage(On.HealthManager.orig_TakeDamage orig, HealthManager self, HitInstance hitInstance)
        {
            string activeSceneName = GetActiveArenaSceneName();
            bool shouldTrack =
                isPlayChalange &&
                self != null &&
                ShouldTrackHealthManager(self) &&
                IsHealthManagerInScene(self, activeSceneName);
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
                isPlayChalange,
                writer,
                damageChangeTracker,
                activeSceneName,
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

        private bool insideHeroSoulGain;

        private void HeroController_SoulGain(On.HeroController.orig_SoulGain orig, HeroController self)
        {
            bool previous = insideHeroSoulGain;
            insideHeroSoulGain = isPlayChalange;
            try
            {
                orig(self);
            }
            finally
            {
                insideHeroSoulGain = previous;
            }
        }

        private bool insideGrubsongGain;

        private bool realHitConfirmedThisCall;
        private bool blockerHitConfirmedThisCall;

        private void PlayMakerFSM_SendEvent(On.PlayMakerFSM.orig_SendEvent orig, PlayMakerFSM self, string eventName)
        {
            if (isPlayChalange)
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

        private void HeroController_TakeDamage(On.HeroController.orig_TakeDamage orig, HeroController self, GameObject go, CollisionSide damageSide, int damageAmount, int hazardType)
        {
            bool previous = insideGrubsongGain;
            insideGrubsongGain = isPlayChalange;

            bool checkForRealHit = isPlayChalange && writer != null && damageAmount > 0 && self != null;
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
                    GetActiveArenaSceneName(),
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
                    GetActiveArenaSceneName(),
                    lastUnixTime,
                    KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime),
                    damageAmount,
                    hazardType,
                    reason);
            }
        }

        private bool insideDreamNailSoulGain;

        private void EnemyDreamnailReaction_RecieveDreamImpact(On.EnemyDreamnailReaction.orig_RecieveDreamImpact orig, EnemyDreamnailReaction self)
        {
            bool previous = insideDreamNailSoulGain;
            insideDreamNailSoulGain = isPlayChalange;
            try
            {
                orig(self);
            }
            finally
            {
                insideDreamNailSoulGain = previous;
            }
        }

        private string pendingFsmSoulMethodName;
        private string pendingFsmSoulSourceLabel;
        private string pendingFsmSoulRawOwnerPath;

        private void CallMethod_OnEnter(On.HutongGames.PlayMaker.Actions.CallMethod.orig_OnEnter orig, HutongGames.PlayMaker.Actions.CallMethod self)
        {
            if (!isPlayChalange || self == null)
            {
                orig(self);
                return;
            }

            TrackFsmSoulCall(self.methodName?.Value, self.Owner, self.State?.Name, () => orig(self));
        }

        private void CallMethodProper_OnEnter(On.HutongGames.PlayMaker.Actions.CallMethodProper.orig_OnEnter orig, HutongGames.PlayMaker.Actions.CallMethodProper self)
        {
            if (!isPlayChalange || self == null)
            {
                orig(self);
                return;
            }

            TrackFsmSoulCall(self.methodName?.Value, self.Owner, self.State?.Name, () => orig(self));
        }

        private void SendMessage_OnEnter(On.HutongGames.PlayMaker.Actions.SendMessage.orig_OnEnter orig, HutongGames.PlayMaker.Actions.SendMessage self)
        {
            if (!isPlayChalange || self == null)
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

        private void TrackFsmSoulCall(string methodName, GameObject ownerObject, string stateName, Action callOrig)
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

        private bool PlayerData_AddMPCharge(On.PlayerData.orig_AddMPCharge orig, PlayerData self, int amount)
        {
            bool trackAsHit = isPlayChalange && insideHeroSoulGain && self != null && amount > 0;
            bool trackAsGrubsong = isPlayChalange && insideGrubsongGain && self != null && amount > 0;
            bool trackAsDreamNail = isPlayChalange && insideDreamNailSoulGain && self != null && amount > 0;

            bool trackAsFsmWeaversong = isPlayChalange && !trackAsHit && !trackAsGrubsong && !trackAsDreamNail &&
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

            string arenaName = GetActiveArenaSceneName();
            long nowUnixTime = KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime);
            if (trackAsHit)
            {
                godhomeQolTracker.RecordObservedSoulGain(arenaName, lastUnixTime, nowUnixTime, mainGained, reserveGained);
            }
            else if (trackAsGrubsong)
            {
                grubsongSoulGainTracker.RecordObservedGain(arenaName, lastUnixTime, nowUnixTime, mainGained, reserveGained);
            }
            else if (trackAsDreamNail)
            {
                dreamNailSoulGainTracker.RecordObservedGain(arenaName, lastUnixTime, nowUnixTime, mainGained, reserveGained);
            }
            else
            {
                weaversongSoulGainTracker.RecordObservedGain(arenaName, lastUnixTime, nowUnixTime, mainGained, reserveGained);
            }

            return result;
        }

        private const int GlowingWombInferredMainCost = 8;

        private string ResolveMainSpendSourceLabel(int amount)
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

        private void PlayerData_TakeMP(On.PlayerData.orig_TakeMP orig, PlayerData self, int amount)
        {
            orig(self, amount);

            if (!isPlayChalange || self == null)
            {
                return;
            }

            soulSpentTracker.RecordMainSpend(
                GetActiveArenaSceneName(),
                lastUnixTime,
                KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime),
                ResolveMainSpendSourceLabel(amount),
                pendingFsmSoulRawOwnerPath,
                amount);
        }

        private void PlayerData_TakeReserveMP(On.PlayerData.orig_TakeReserveMP orig, PlayerData self, int amount)
        {
            orig(self, amount);

            if (!isPlayChalange || self == null)
            {
                return;
            }

            soulSpentTracker.RecordReserveSpend(
                GetActiveArenaSceneName(),
                lastUnixTime,
                KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime),
                pendingFsmSoulSourceLabel,
                pendingFsmSoulRawOwnerPath,
                amount);
        }

        private bool TryResolveHitTargetHealthManager(HitInstance hit, out HealthManager healthManager)
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

            if (rawTarget is HealthManager manager)
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

        private bool TryLogTrackedBossHpDelta(long nowUnixTime)
        {
            if (infoBoss.Count == 0)
            {
                return false;
            }

            string sceneName = GetActiveArenaSceneName();
            if (string.IsNullOrEmpty(sceneName))
            {
                return false;
            }

            BossTrackingHelpers.EnsureUniqueBossBuffers(infoBoss, uniqueBossByGameObject, uniqueBossSet, ref uniqueBossBuffersDirty);
            infoBossKeysBuffer.Clear();
            foreach (HealthManager boss in infoBoss.Keys)
            {
                infoBossKeysBuffer.Add(boss);
            }

            bool isChanged = false;
            bool removedAny = false;
            hpInfoBuilder.Clear();
            foreach (HealthManager boss in infoBossKeysBuffer)
            {
                if (!IsHealthManagerInScene(boss, sceneName))
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

            DamageAnfInv?.Add($"{nowUnixTime - lastUnixTime}{hpInfoBuilder}|");
            return true;
        }

        private void SceneLoad_RecordEndTime(On.SceneLoad.orig_RecordEndTime orig, SceneLoad self, SceneLoad.Phases phase)
        {
            orig(self, phase);
            if (phase == SceneLoad.Phases.UnloadUnusedAssets)
            {
                Self_Finish();
            }
        }

        private void ResetTrackedBossRuntimeState(bool resetHeroBoxState = true)
        {
            infoBoss.Clear();
            uniqueBossByGameObject.Clear();
            uniqueBossSet.Clear();
            infoBossKeysBuffer.Clear();
            uniqueBossBuffersDirty = true;
            enemyHealthManagerByGameObject.Clear();
            enemyHealthManagerCacheCleanupBuffer.Clear();
            ownerPathByGameObject.Clear();
            ownerPathCacheCleanupBuffer.Clear();
            enemyScanLayerMask = 0;
            lastEnemyLayerMaskRefreshTime = 0f;
            enemyColliderBufferOverflowLogged = false;
            lastEnemyColliderOverflowWarningTime = 0f;
            lastEnemyHealthCacheCleanupTime = 0f;
            enemyHealthCacheCleanupCursor = 0;
            lastOwnerPathCacheCleanupTime = 0f;
            ownerPathCacheCleanupCursor = 0;
            lastEnemySeedTime = 0f;
            if (resetHeroBoxState)
            {
                hasHeroBoxState = false;
                lastHeroBoxActive = -1;
                heroBoxOffStartTime = -1f;
                cachedHeroTransform = null;
                cachedHeroBoxObject = null;
            }
        }

        private void Self_Finish()
        {
            if (!isPlayChalange) return;
            ResetTrackedBossRuntimeState(resetHeroBoxState: false);
            cachedFrameUnixTime = 0;
        }

        private void HeroController_FixedUpdate(On.HeroController.orig_FixedUpdate orig, HeroController self)
        {
            long nowUnixTime = KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime);
            cachedFrameUnixTime = nowUnixTime;
            InvCheck(nowUnixTime);

            bossPhaseThresholdTracker.Update(GetActiveArenaSceneName(), uniqueBossByGameObject.Keys, lastUnixTime, nowUnixTime);
            EnemyUpdate(nowUnixTime);
            orig(self);
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
                        debugKillAllHook = new Hook(killAll, typeof(ReplayLogger).GetMethod(nameof(DebugKillAllDetour), BindingFlags.NonPublic | BindingFlags.Static));
                    }
                }

                if (debugKillSelfHook == null)
                {
                    MethodInfo killSelf = bindableType.GetMethod("KillSelf", BindingFlags.Public | BindingFlags.Static);
                    if (killSelf != null)
                    {
                        debugKillSelfHook = new Hook(killSelf, typeof(ReplayLogger).GetMethod(nameof(DebugKillSelfDetour), BindingFlags.NonPublic | BindingFlags.Static));
                    }
                }
            }
            catch (Exception e)
            {
                global::ReplayLogger.InternalDiagnostics.Warn($"ReplayLogger: failed to hook DebugMod functions (pantheon): {e.Message}");
            }
        }

        private static void DisposeDebugModHooks()
        {
            try
            {
                debugKillAllHook?.Dispose();
            }
            catch (Exception e)
            {
                global::ReplayLogger.InternalDiagnostics.Warn($"ReplayLogger: failed to dispose DebugMod KillAll hook: {e.Message}");
            }

            try
            {
                debugKillSelfHook?.Dispose();
            }
            catch (Exception e)
            {
                global::ReplayLogger.InternalDiagnostics.Warn($"ReplayLogger: failed to dispose DebugMod KillSelf hook: {e.Message}");
            }

            debugKillAllHook = null;
            debugKillSelfHook = null;
        }

        internal static bool IsPrimaryLoggerActive()
        {
            ReplayLogger logger = Instance;
            return logger != null && logger.isPlayChalange && logger.writer != null;
        }

        private static void DebugKillAllDetour(Action orig)
        {
            orig();
            ReplayLogger logger = Instance;
            if (logger != null && logger.isPlayChalange && logger.writer != null)
            {
                long nowUnixTime = KeyLogFormatting.GetCachedFrameUnixTimeOrNow(logger.cachedFrameUnixTime);
                logger.debugMenuTracker.LogManualChange(logger.writer, logger.lastScene, logger.lastUnixTime, nowUnixTime, "Cheats/Kill All", null, "Executed");
            }
        }

        private static void DebugKillSelfDetour(Action orig)
        {
            orig();
            ReplayLogger logger = Instance;
            if (logger != null && logger.isPlayChalange && logger.writer != null)
            {
                long nowUnixTime = KeyLogFormatting.GetCachedFrameUnixTimeOrNow(logger.cachedFrameUnixTime);
                logger.debugMenuTracker.LogManualChange(logger.writer, logger.lastScene, logger.lastUnixTime, nowUnixTime, "Cheats/Kill Self", null, "Executed");
            }
        }

        private void MonitorDebugModUi(long nowUnixTime)
        {
            if (!isPlayChalange || writer == null)
            {
                return;
            }

            string arena = isManualLogging
                ? (GameManager.instance?.sceneName ?? lastScene)
                : lastScene;
            if (string.IsNullOrWhiteSpace(arena))
            {
                arena = lastScene;
            }
            bool debugUiVisible = false;
            if (DebugModIntegration.TryGetFrameSnapshot(out DebugModFrameSnapshot debugSnapshot))
            {
                debugModEventsTracker.Update(writer, arena, lastUnixTime, debugSnapshot, nowUnixTime);
                debugMenuTracker.Update(writer, arena, lastUnixTime, debugSnapshot, nowUnixTime);
                debugUiVisible = debugSnapshot.UiVisible;
            }
            charmsChangeTracker.Update(arena, lastUnixTime, nowUnixTime, writer);
            godhomeQolTracker.Update(arena, nowUnixTime, debugUiVisible);
        }

        Dictionary<HealthManager, (int maxHP, int lastHP)> infoBoss = new();
        private readonly Dictionary<GameObject, HealthManager> uniqueBossByGameObject = new(128);
        private readonly HashSet<HealthManager> uniqueBossSet = new();
        private readonly List<HealthManager> infoBossKeysBuffer = new(128);
        private readonly Dictionary<GameObject, string> ownerPathByGameObject = new(512);
        private readonly StringBuilder hpInfoBuilder = new(256);
        private bool uniqueBossBuffersDirty = true;
        bool isInvincible = false;
        float invTimer;
        private bool hasHeroBoxState;
        private int lastHeroBoxActive;
        private float heroBoxOffStartTime = -1f;
        private Transform cachedHeroTransform;
        private GameObject cachedHeroBoxObject;

        private HealthManager ResolveEnemyHealthManager(GameObject enemyObject)
        {
            if (enemyObject == null)
            {
                return null;
            }

            string sceneName = GetActiveArenaSceneName();
            if (!IsGameObjectInScene(enemyObject, sceneName))
            {
                return null;
            }

            if (!enemyHealthManagerByGameObject.TryGetValue(enemyObject, out HealthManager healthManager) ||
                healthManager == null ||
                !IsHealthManagerInScene(healthManager, sceneName))
            {
                healthManager = enemyObject.GetComponent<HealthManager>();
                if (healthManager == null)
                {
                    healthManager = enemyObject.GetComponentInParent<HealthManager>();
                }
                if (healthManager == null)
                {
                    healthManager = enemyObject.GetComponentInChildren<HealthManager>(includeInactive: true);
                }
                if (healthManager == null)
                {
                    Transform rootTransform = enemyObject.transform?.root;
                    if (rootTransform != null)
                    {
                        healthManager = rootTransform.GetComponentInChildren<HealthManager>(includeInactive: true);
                    }
                }

                if (!IsHealthManagerInScene(healthManager, sceneName))
                {
                    healthManager = null;
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

        private void TrackEnemyHealthManager(HealthManager healthManager)
        {
            string sceneName = GetActiveArenaSceneName();
            if (healthManager == null ||
                healthManager.hp <= 0 ||
                !IsHealthManagerInScene(healthManager, sceneName))
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

        private void SeedTrackedBossesFromActiveScene(float now)
        {
            if (now - lastEnemySeedTime < EnemySeedRetryIntervalSeconds)
            {
                return;
            }

            lastEnemySeedTime = now;
            string sceneName = GetActiveArenaSceneName();
            if (string.IsNullOrEmpty(sceneName))
            {
                return;
            }

            HealthManager[] managers = UnityEngine.Object.FindObjectsOfType<HealthManager>();
            if (managers == null || managers.Length == 0)
            {
                return;
            }

            for (int i = 0; i < managers.Length; i++)
            {
                HealthManager manager = managers[i];
                if (!ShouldTrackHealthManager(manager) || !IsHealthManagerInScene(manager, sceneName))
                {
                    continue;
                }

                enemyHealthManagerByGameObject[manager.gameObject] = manager;
                TrackEnemyHealthManager(manager);
            }
        }

        private static bool ShouldTrackHealthManager(HealthManager manager) => BossTrackingHelpers.ShouldTrackHealthManager(manager);

        private void IncludeEnemyLayer(int layer)
        {
            if ((uint)layer >= 32u)
            {
                return;
            }

            enemyScanLayerMask |= 1 << layer;
        }

        private int ResolveEnemyLayerMask(HeroController hero, float now)
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

            foreach (HealthManager boss in infoBoss.Keys)
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

        public void InvCheck(long nowUnixTime)
        {
            if (!isPlayChalange) return;
            if (HeroController.instance == null || PlayerData.instance == null)
            {
                return;
            }

            string sceneName = GetActiveArenaSceneName();
            if (string.IsNullOrEmpty(sceneName))
            {
                ResetTrackedBossRuntimeState(resetHeroBoxState: false);
                return;
            }

            CleanupTrackedBossesForScene(sceneName);

            bool shouldBeInvincible =
                (HeroController.instance.cState.invulnerable ||
                 PlayerData.instance.isInvincible ||
             HeroController.instance.cState.shadowDashing ||
             HeroController.instance.damageMode == DamageMode.HAZARD_ONLY ||
             HeroController.instance.damageMode == DamageMode.NO_DAMAGE);

            BossTrackingHelpers.EnsureUniqueBossBuffers(infoBoss, uniqueBossByGameObject, uniqueBossSet, ref uniqueBossBuffersDirty);
            var bossList = uniqueBossByGameObject.Values;

            if (shouldBeInvincible && !isInvincible)
            {
                isInvincible = true;
                invTimer = 0f;

                string hpInfo = BuildTrackedHpInfo(bossList);
                DamageAnfInv?.Add($"{nowUnixTime - lastUnixTime}{hpInfo}|(INV ON)|");
            }

            if (!shouldBeInvincible && isInvincible)
            {
                isInvincible = false;
                string hpInfo = BuildTrackedHpInfo(bossList);
                DamageAnfInv?.Add($"{nowUnixTime - lastUnixTime}{hpInfo}|(INV OFF, {invTimer.ToString("F3", CultureInfo.InvariantCulture)})|");
                if (invTimer > 2.6f)
                {
                    string warning = $"|{lastScene}|+{nowUnixTime - lastUnixTime}{hpInfo}|(INV OFF, {invTimer.ToString("F3", CultureInfo.InvariantCulture)})";

                    InvWarn?.Add(warning);
                }
                invTimer = 0f;
            }

            if (isInvincible)
                invTimer += Time.fixedDeltaTime;

            TrackHeroBoxActive(bossList, nowUnixTime);
        }

        private void TrackHeroBoxActive(IEnumerable<HealthManager> bossList, long nowUnixTime)
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

        private void WriteHeroBoxActiveWarning(int currentState, IEnumerable<HealthManager> bossList, int? previousState, long nowUnixTime)
        {
            string hpInfo = BuildHeroBoxHpInfo(bossList);
            string message = previousState.HasValue
                ? $"{BossTrackingHelpers.FormatState(previousState.Value)} -> {BossTrackingHelpers.FormatState(currentState)}"
                : BossTrackingHelpers.FormatState(currentState);
            string warning = $"|{lastScene}|+{nowUnixTime - lastUnixTime}{hpInfo}|HeroBoxActive: {message}";
            InvWarn?.Add(warning);
        }

        private void AppendHeroBoxOffDurationWarning(long nowUnixTime)
        {
            if (!hasHeroBoxState || lastHeroBoxActive != 0 || heroBoxOffStartTime < 0f)
            {
                return;
            }
            BossTrackingHelpers.EnsureUniqueBossBuffers(infoBoss, uniqueBossByGameObject, uniqueBossSet, ref uniqueBossBuffersDirty);
            string hpInfo = BuildHeroBoxHpInfo(uniqueBossByGameObject.Values);
            float duration = Time.unscaledTime - heroBoxOffStartTime;
            string warning = $"|{lastScene}|+{nowUnixTime - lastUnixTime}{hpInfo}|HeroBoxActive Off Duration: {duration.ToString("F3", CultureInfo.InvariantCulture)}s";
            InvWarn?.Add(warning);
        }

        private void AppendActiveInvDurationWarning(long nowUnixTime)
        {
            if (!isInvincible)
            {
                return;
            }

            BossTrackingHelpers.EnsureUniqueBossBuffers(infoBoss, uniqueBossByGameObject, uniqueBossSet, ref uniqueBossBuffersDirty);
            string hpInfo = BuildTrackedHpInfo(uniqueBossByGameObject.Values);
            string duration = invTimer.ToString("F3", CultureInfo.InvariantCulture);
            string invLine = $"{nowUnixTime - lastUnixTime}{hpInfo}|(INV OFF, {duration})|";
            DamageAnfInv?.Add(invLine);
            InvWarn?.Add($"|{lastScene}|+{nowUnixTime - lastUnixTime}{hpInfo}|(INV OFF, {duration})");

            isInvincible = false;
            invTimer = 0f;
        }

        private string BuildHeroBoxHpInfo(IEnumerable<HealthManager> bossList)
        {
            if (bossList == null)
            {
                return string.Empty;
            }

            string sceneName = GetActiveArenaSceneName();
            hpInfoBuilder.Clear();
            foreach (var boss in bossList)
            {
                if (!IsHealthManagerInScene(boss, sceneName) || !infoBoss.TryGetValue(boss, out var entry))
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

        private string BuildTrackedHpInfo(IEnumerable<HealthManager> bossList)
        {
            if (bossList == null)
            {
                return string.Empty;
            }

            string sceneName = GetActiveArenaSceneName();
            hpInfoBuilder.Clear();
            foreach (HealthManager boss in bossList)
            {
                if (!IsHealthManagerInScene(boss, sceneName) || !infoBoss.TryGetValue(boss, out var entry))
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

        bool isChange;

        public void EnemyUpdate(long nowUnixTime)
        {
            if (!isPlayChalange) return;
            HeroController hero = HeroController.instance;
            if (hero == null)
            {
                return;
            }
            string sceneName = GetActiveArenaSceneName();
            if (string.IsNullOrEmpty(sceneName))
            {
                ResetTrackedBossRuntimeState(resetHeroBoxState: false);
                return;
            }

            float now = Time.unscaledTime;
            if (now - lastEnemyUpdateTime < EnemyUpdateIntervalSeconds)
            {
                return;
            }
            lastEnemyUpdateTime = now;
            CleanupTrackedBossesForScene(sceneName);
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
                    global::ReplayLogger.InternalDiagnostics.Warn($"ReplayLogger: enemy collider buffer saturated at {enemyColliderBuffer.Length} entries; truncating enemy scan for this tick.");
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

                if (!IsGameObjectInScene(enemyObject, sceneName))
                {
                    continue;
                }

                HealthManager healthManager = ResolveEnemyHealthManager(enemyObject);
                TrackEnemyHealthManager(healthManager);
            }

            hpInfoBuilder.Clear();

            BossTrackingHelpers.EnsureUniqueBossBuffers(infoBoss, uniqueBossByGameObject, uniqueBossSet, ref uniqueBossBuffersDirty);
            infoBossKeysBuffer.Clear();
            foreach (HealthManager boss in infoBoss.Keys)
            {
                infoBossKeysBuffer.Add(boss);
            }

            bool removedAnyBoss = false;
            foreach (HealthManager boss in infoBossKeysBuffer)
            {
                if (!IsHealthManagerInScene(boss, sceneName))
                {
                    removedAnyBoss |= infoBoss.Remove(boss);
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

                if (boss.hp != entry.lastHP)
                {
                    entry = (entry.maxHP, boss.hp);
                    infoBoss[boss] = entry;
                    isChange = true;
                }

                hpInfoBuilder.Append('|');
                hpInfoBuilder.Append(entry.lastHP);
                hpInfoBuilder.Append('/');
                hpInfoBuilder.Append(entry.maxHP);
            }

            if (isChange)
            {
                string hpInfo = hpInfoBuilder.ToString();
                DamageAnfInv?.Add($"{nowUnixTime - lastUnixTime}{hpInfo}|");
            }
            isChange = false;

            foreach (HealthManager boss in infoBossKeysBuffer)
            {
                if (boss != null &&
                    IsHealthManagerInScene(boss, sceneName) &&
                    !boss.isDead &&
                    boss.hp > 0)
                {
                    continue;
                }

                removedAnyBoss |= infoBoss.Remove(boss);
            }

            if (removedAnyBoss)
            {
                uniqueBossBuffersDirty = true;
            }

        }

        private IEnumerator QuitToMenu_Start(On.QuitToMenu.orig_Start orig, QuitToMenu self)
        {
            skipPantheonLogging = false;

            Close();
            return orig(self);
        }

        private void StartLoad()
        {
            customCanvas?.StartUpdateSprite();
        }

        private string currentNameLog;
        private void OpenFile(On.SceneLoad.orig_Begin orig, SceneLoad self)
        {
            try
            {
                try
                {
                    HoGLogger.HandleBootstrapSceneLoadBegin(self?.TargetSceneName);
                }
                catch (Exception hogEx)
                {
                    global::ReplayLogger.InternalDiagnostics.Warn($"ReplayLogger: HoG bootstrap scene handler failed: {hogEx.Message}");
                }

                if (IsManualModeEnabled())
                {
                    lastScene = self.TargetSceneName;
                    orig(self);
                    return;
                }

                if (skipPantheonLogging)
                {
                    if (self.TargetSceneName.Contains("GG_End_Seq")
                        || self.TargetSceneName == "GG_Atrium"
                        || self.TargetSceneName == "GG_Workshop")
                    {
                        skipPantheonLogging = false;
                        currentPantheonNumber = 0;
                        capturedPantheonNumber = 0;
                        capturedPantheonSequence = null;
                        currentPantheonUsesTrueBossRush = false;
                        currentPantheonUsesRandomOrder = false;
                    }

                    lastScene = self.TargetSceneName;
                    orig(self);
                    return;
                }

                bool isEnding = isPlayChalange && self.TargetSceneName.Contains("GG_End_Seq");
                if (isPlayChalange && writer != null)
                {
                    FlushKeyLogBufferIfNeeded(KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime), force: true);
                    FlushBufferedSectionsForTransition();
                    writer.Flush();
                    if (!isEnding)
                    {
                        ResetTrackedBossRuntimeState(resetHeroBoxState: false);
                    }
                }

                var dataTimeNow = DateTimeOffset.Now;
                lastUnixTime = dataTimeNow.ToUnixTimeMilliseconds();
                cachedFrameUnixTime = lastUnixTime;
                var dataTime = dataTimeNow.ToString("dd.MM.yyyy HH:mm:ss.fff");
                if (isPlayChalange && self.TargetSceneName.Contains("GG_End_Seq"))
                {
                    Close();
                }

                if (self.TargetSceneName.Contains("GG_Boss_Door") || IsPantheonFiveEntry(self.TargetSceneName))
                {
                    int pantheonNumber = currentPantheonNumber;
                    if (pantheonNumber == 0 && IsPantheonFiveEntry(self.TargetSceneName))
                    {
                        pantheonNumber = 5;
                    }

                    if (pantheonNumber > 0 && ShouldSkipRandomizedPantheon(pantheonNumber))
                    {
                        skipPantheonLogging = true;
                        lastScene = self.TargetSceneName;
                        orig(self);
                        return;
                    }

                    startUnixTime = lastUnixTime;
                    int curentPlayTime = (int)((PlayerData.instance?.playTime ?? 0f) * 100);
                    isPlayChalange = true;
                    EnsureGameplayHooksActive();

                    try
                    {

                        activeEncryptionSession = KeyloggerLogEncryption.CreateSession();
                        lastString = activeEncryptionSession.SessionKeyBlob;
                        currentNameLog = Path.Combine(dllDir, $"KeyLog{DateTime.UtcNow.Ticks}.log");
                        pressedButtonsLog?.Clear();
                        DamageAnfInv?.Clear();
                        InvWarn?.Clear();
                        speedWarnBuffer?.Clear();
                        hitWarnBuffer?.Clear();
                        pressedButtonsLog = new BufferedLogSection($"{currentNameLog}.keys.tmp", BufferedSectionThreshold);
                        DamageAnfInv = new BufferedLogSection($"{currentNameLog}.damage.tmp", BufferedSectionThreshold);
                        InvWarn = new BufferedLogSection($"{currentNameLog}.warn.tmp", BufferedSectionThreshold);
                        speedWarnBuffer = new BufferedLogSection($"{currentNameLog}.speed.tmp", BufferedSectionThreshold);
                        hitWarnBuffer = new BufferedLogSection($"{currentNameLog}.hit.tmp", BufferedSectionThreshold);
                        writer = new AsyncBlockLogWriter(currentNameLog, lastString, activeEncryptionSession, BlockSizeBytes, BlockMaxAgeMs, LogQueueCapacity);
                        CustomKnightSettingsManager.StartTracking(self.TargetSceneName, lastUnixTime);
                        AheSettingsManager.RefreshSnapshot();
                        speedWarnTracker.Reset(Mathf.Max(Time.timeScale, 0f));
                        hitWarnTracker.Reset();
                        bool initialDebugUiVisible = DebugModIntegration.TryGetUiVisible(out bool visible) && visible;
                        debugModEventsTracker.Reset(initialDebugUiVisible);
                        debugMenuTracker.Reset(initialDebugUiVisible);
                        debugHotkeysTracker.InitializeBindings();
                        keyLogBuffer.Clear();
                        lastKeyLogFlushTime = 0;
                        lastHudElapsedSeconds = -1;
                        keyScanner.Clear();
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
                        infoBoss.Clear();
                        ownerPathByGameObject.Clear();
                        ownerPathCacheCleanupBuffer.Clear();
                        lastOwnerPathCacheCleanupTime = 0f;
                        ownerPathCacheCleanupCursor = 0;
                        uniqueBossByGameObject.Clear();
                        uniqueBossSet.Clear();
                        infoBossKeysBuffer.Clear();
                        uniqueBossBuffersDirty = true;
                        hasHeroBoxState = false;
                        lastHeroBoxActive = -1;
                        heroBoxOffStartTime = -1f;
                        cachedHeroTransform = null;
                        cachedHeroBoxObject = null;
                        isInvincible = false;
                        invTimer = 0f;
                        damageSectionStarted = false;
                        pressedButtonsSectionStarted = false;
                        damageChangeTracker.Reset();
                        CharmDamageTracker.ResetHints();
                        charmsChangeTracker.Reset();
                        foreach (ITrackerLifecycle lifecycleTracker in LifecycleTrackers())
                        {
                            lifecycleTracker.Reset();
                        }
                        ResetInlineTimelineCursors();
                        CoreSessionLogger.WriteEncryptedModSnapshot(writer, modsDir, "---------------------------------------------------");
                        LogWrite.EncryptedLine(writer, CoreSessionLogger.BuildEquippedCharmsLine());
                        CoreSessionLogger.WriteEncryptedSkillLines(writer, "---------------------------------------------------");
                        speedWarnTracker.LogInitial(writer, self.TargetSceneName, lastUnixTime);
                        InitializeDebugModHooks();
                        godhomeQolTracker.Reset();
                        godhomeQolTracker.StartFight(self.TargetSceneName, lastUnixTime, includeBossManipulate: false, includeBossChallenge: true);
                        cachedFrameUnixTime = lastUnixTime;

                    }
                    catch (Exception e)
                    {
                        global::ReplayLogger.InternalDiagnostics.Error("ReplayLogger: failed to start pantheon logging: " + e.Message);
                        AbortPantheonLogging();
                        lastScene = self.TargetSceneName;
                        orig(self);
                        return;
                    }

                    int seed = (int)(lastUnixTime ^ curentPlayTime);

                    customCanvas = new CustomCanvas(new NumberInCanvas(seed), new LoadingSprite(lastString));
                    pantheonToastHideAtUnscaledTime = 0f;

                    if (IsPantheonFiveEntry(self.TargetSceneName))
                    {
                        if (!TryResolvePantheonRoute(5, out List<string> route))
                        {
                            skipPantheonLogging = true;
                            AbortPantheonLogging();
                            lastScene = self.TargetSceneName;
                            orig(self);
                            return;
                        }

                        currentPanteon = (GetPantheonLogName(5), route);
                        bossCounter++;

                        string startLine = $"{dataTime}|{lastUnixTime}|{self.TargetSceneName}| {bossCounter}*";
                        bool wroteToSection = false;
                        if (pressedButtonsLog != null)
                        {
                            pressedButtonsLog.Add(startLine);
                            wroteToSection = true;
                        }
                        if (DamageAnfInv != null)
                        {
                            DamageAnfInv.Add(startLine);
                            wroteToSection = true;
                        }

                        if (!wroteToSection)
                        {
                            LogWrite.EncryptedLine(writer, $"{dataTime}|{lastUnixTime}|{curentPlayTime}|{self.TargetSceneName}| {bossCounter}*");
                        }
                    }
                    else
                    {
                        string startLine = $"{dataTime}|{lastUnixTime}|{self.TargetSceneName}|";
                        bool wroteToSection = false;
                        if (pressedButtonsLog != null)
                        {
                            pressedButtonsLog.Add(startLine);
                            wroteToSection = true;
                        }
                        if (DamageAnfInv != null)
                        {
                            DamageAnfInv.Add(startLine);
                            wroteToSection = true;
                        }

                        if (!wroteToSection)
                        {
                            LogWrite.EncryptedLine(writer, $"{dataTime}|{lastUnixTime}|{curentPlayTime}|{self.TargetSceneName}|");
                        }
                    }

                }
                else if (isPlayChalange)
                {
                    if (currentPanteon.list == null && lastScene.Contains("GG_Boss_Door"))
                    {
                        int pantheonNumber = 0;
                        if (self.TargetSceneName == Panteons.P1[0])
                            pantheonNumber = 1;
                        if (self.TargetSceneName == Panteons.P2[0])
                            pantheonNumber = 2;
                        if (self.TargetSceneName == Panteons.P3[0])
                            pantheonNumber = 3;
                        if (self.TargetSceneName == Panteons.P4[0])
                            pantheonNumber = 4;
                        if (pantheonNumber == 0
                            && currentPantheonNumber >= 1
                            && currentPantheonNumber <= 4
                            && IsRandomOrderFirstScene(currentPantheonNumber, self.TargetSceneName))
                            pantheonNumber = currentPantheonNumber;

                        if (pantheonNumber > 0 && ShouldSkipRandomizedPantheon(pantheonNumber))
                        {
                            skipPantheonLogging = true;
                            AbortPantheonLogging();
                            lastScene = self.TargetSceneName;
                            orig(self);
                            return;
                        }

                        if (pantheonNumber == 0 || !TryResolvePantheonRoute(pantheonNumber, out List<string> route))
                        {
                            skipPantheonLogging = true;
                            AbortPantheonLogging();
                            lastScene = self.TargetSceneName;
                            orig(self);
                            return;
                        }

                        currentPanteon = (GetPantheonLogName(pantheonNumber), route);
                    }
                    else if (currentPanteon.list != null)
                    {

                        int targetIndex = currentPanteon.list.IndexOf((self.TargetSceneName));
                        int lastSceneIndex = currentPanteon.list.IndexOf(lastScene);
                        godhomeQolTracker.StartFight(self.TargetSceneName, lastUnixTime, includeBossManipulate: false, includeBossChallenge: true);

                        if (targetIndex == -1 || (lastSceneIndex != -1 && !(IsValidNextScene(currentPanteon.list, lastSceneIndex, self.TargetSceneName))))
                        {
                            Close();
                            lastScene = self.TargetSceneName;
                            orig(self);
                            return;
                        }
                        if (lastScene == "GG_Spa")
                        {
                            currentPanteon.list?.Remove(lastScene);
                        }

                    }
                    bool isSkippedScene = SkipScenes.Contains(self.TargetSceneName);
                    if (!isSkippedScene)
                        bossCounter++;

                    StartLoad();
                    string startLine = $"{dataTime}|{lastUnixTime}|{self.TargetSceneName}{(!isSkippedScene ? $"| {bossCounter}*" : "")}";
                    bool wroteToSection = false;
                    if (pressedButtonsLog != null)
                    {
                        pressedButtonsLog.Add(startLine);
                        wroteToSection = true;
                    }
                    if (DamageAnfInv != null)
                    {
                        DamageAnfInv.Add(startLine);
                        wroteToSection = true;
                    }

                    if (!wroteToSection)
                    {
                        LogWrite.EncryptedLine(writer, $"{dataTime}|{lastUnixTime}|{self.TargetSceneName}|{{sprite}}{self.TargetSceneName}{(!isSkippedScene ? $"| {bossCounter}*" : "")}");
                    }

                }
            }
            catch (Exception e)
            {
                global::ReplayLogger.InternalDiagnostics.Error("ReplayLogger: OpenFile failed: " + e.Message);
                bool hasPantheonState =
                    writer != null ||
                    isPlayChalange ||
                    pressedButtonsLog != null ||
                    DamageAnfInv != null ||
                    InvWarn != null ||
                    speedWarnBuffer != null ||
                    hitWarnBuffer != null ||
                    customCanvas != null ||
                    !string.IsNullOrWhiteSpace(currentPanteon.name) ||
                    currentPanteon.list != null;

                if (!isManualLogging && hasPantheonState)
                {
                    AbortPantheonLogging();
                }
            }
            lastScene = self.TargetSceneName;
            orig(self);
        }

        private void OnApplicationQuit()
        {
            try
            {
                HoGLogger.HandleBootstrapApplicationQuit();
            }
            catch (Exception hogEx)
            {
                global::ReplayLogger.InternalDiagnostics.Warn($"ReplayLogger: HoG bootstrap quit handler failed: {hogEx.Message}");
            }

            Close();
        }

        private void AbortPantheonLogging()
        {
            try
            {
                writer?.Flush();
            }
            catch
            {
            }

            try
            {
                writer?.Close();
            }
            catch
            {
            }

            writer = null;
            activeEncryptionSession = null;

            try
            {
                if (!string.IsNullOrWhiteSpace(currentNameLog) && File.Exists(currentNameLog))
                {
                    File.Delete(currentNameLog);
                }
            }
            catch
            {
            }

            pressedButtonsLog?.Clear();
            DamageAnfInv?.Clear();
            InvWarn?.Clear();
            speedWarnBuffer?.Clear();
            hitWarnBuffer?.Clear();
            pressedButtonsLog = null;
            DamageAnfInv = null;
            InvWarn = null;
            speedWarnBuffer = null;
            hitWarnBuffer = null;

            AheSettingsManager.Reset();
            CustomKnightSettingsManager.Reset();
            godhomeQolTracker.Reset();
            debugHotkeysTracker.Reset();
            debugMenuTracker.Reset();
            damageChangeTracker.Reset();
            CharmDamageTracker.ResetHints();
            hitWarnTracker.Reset();
            bossPhaseThresholdTracker.Reset();
            ResetInlineTimelineCursors();

            keyLogBuffer.Clear();
            lastKeyLogFlushTime = 0;
            lastHudElapsedSeconds = -1;
            keyScanner.Clear();
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
            infoBoss.Clear();
            ownerPathByGameObject.Clear();
            ownerPathCacheCleanupBuffer.Clear();
            lastOwnerPathCacheCleanupTime = 0f;
            ownerPathCacheCleanupCursor = 0;
            uniqueBossByGameObject.Clear();
            uniqueBossSet.Clear();
            infoBossKeysBuffer.Clear();
            uniqueBossBuffersDirty = true;
            hasHeroBoxState = false;
            lastHeroBoxActive = -1;
            heroBoxOffStartTime = -1f;
            cachedHeroTransform = null;
            cachedHeroBoxObject = null;
            isInvincible = false;
            invTimer = 0f;
            damageSectionStarted = false;
            pressedButtonsSectionStarted = false;
            DisposeDebugModHooks();

            isChallengeCompleted = "-";
            bossCounter = 0;
            startUnixTime = 0;
            isPlayChalange = false;
            ReleaseGameplayHooksIfIdle();
            currentPantheonNumber = 0;
            capturedPantheonNumber = 0;
            capturedPantheonSequence = null;
            currentPantheonUsesTrueBossRush = false;
            currentPantheonUsesRandomOrder = false;
            cachedFrameUnixTime = 0;

            customCanvas?.DestroyCanvas();
            pantheonToastHideAtUnscaledTime = 0f;
            currentPanteon = (null, null);
        }

        private void FlushBufferedSectionsForTransition()
        {
            if (writer == null)
            {
                return;
            }

            bool hasPressedButtons = pressedButtonsLog != null && pressedButtonsLog.HasContent;
            bool hasDamageInv = DamageAnfInv != null && DamageAnfInv.HasContent;
            if (!hasPressedButtons && !hasDamageInv)
            {
                return;
            }

            if (hasPressedButtons)
            {
                if (!pressedButtonsSectionStarted)
                {
                    LogWrite.EncryptedLine(writer, "\n------------------------PRESSED BUTTONS------------------------\n");
                    pressedButtonsSectionStarted = true;
                }

                pressedButtonsLog.WriteEncryptedLines(writer);
                pressedButtonsLog.Clear();
                CoreSessionLogger.WriteSeparatorWithSpacing(writer);
            }

            if (hasDamageInv)
            {
                if (!damageSectionStarted)
                {
                    LogWrite.EncryptedLine(writer, "\n------------------------DAMAGE AND WARNINGS------------------------\n");
                    damageSectionStarted = true;
                }

                DamageAnfInv.WriteEncryptedLines(writer);
                DamageAnfInv.Clear();
                CoreSessionLogger.WriteSeparatorWithSpacing(writer);
            }

        }

        private bool IsValidNextScene(List<string> panteonList, int lastSceneIndex, string targetSceneName)
        {
            int nextIndex = lastSceneIndex + 1;

            if (nextIndex >= panteonList.Count) return false;

            string expectedNextScene = panteonList[nextIndex];

            if (currentPantheonUsesTrueBossRush)
            {
                return expectedNextScene == targetSceneName;
            }

            if (expectedNextScene != targetSceneName)
            {
                nextIndex++;
                if (nextIndex >= panteonList.Count)
                {
                    return false;
                }
                expectedNextScene = panteonList[nextIndex];

            }

            return expectedNextScene == targetSceneName;
        }

        public static string ConvertUnixTimeToDateTimeString(long unixTimeMilliseconds)
        {
            DateTimeOffset dateTimeOffset = DateTimeOffset.FromUnixTimeMilliseconds(unixTimeMilliseconds);

            string dateTimeString = dateTimeOffset.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss.fff");

            return dateTimeString;
        }

        private static string ConvertUnixTimeToFileSuffix(long unixTimeMilliseconds)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(unixTimeMilliseconds).ToLocalTime().ToString("dd-MM-yyyy HH-mm-ss");
        }
        public static string ConvertUnixTimeToTimeString(long unixTimeMilliseconds)
        {
            TimeSpan span = TimeSpan.FromMilliseconds(Math.Max(0, unixTimeMilliseconds));
            if (span.Days > 0)
            {
                return $"{span.Days:D2}.{span:hh\\:mm\\:ss\\.fff}";
            }
            return span.ToString(@"hh\:mm\:ss\.fff");
        }

        private void Close()
        {
            Close(isManualLogging);
        }

        private void Close(bool manualClose)
        {
            bool toastShown = false;
            float hudToastSeconds = ReplayLogger.GetHudToastSeconds();
            try
            {
                if (writer == null)
                {
                    return;
                }

                long endTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                string sessionTime = ConvertUnixTimeToTimeString((long)(Time.realtimeSinceStartup * 1000f));
                FlushKeyLogBufferIfNeeded(KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime), force: true);
                AppendActiveInvDurationWarning(endTime);
                bool wrotePressedButtons = pressedButtonsLog != null && pressedButtonsLog.HasContent;
                bool wroteDamageInv = DamageAnfInv != null && DamageAnfInv.HasContent;
                if (wrotePressedButtons || wroteDamageInv)
                {
                    if (wrotePressedButtons)
                    {
                        if (!pressedButtonsSectionStarted)
                        {
                            LogWrite.EncryptedLine(writer, "\n------------------------PRESSED BUTTONS------------------------\n");
                            pressedButtonsSectionStarted = true;
                        }

                        pressedButtonsLog.WriteEncryptedLines(writer);
                        CoreSessionLogger.WriteSeparatorWithSpacing(writer);
                    }

                    if (wroteDamageInv)
                    {
                        if (!damageSectionStarted)
                        {
                            LogWrite.EncryptedLine(writer, "\n------------------------DAMAGE AND WARNINGS------------------------\n");
                            damageSectionStarted = true;
                        }

                        DamageAnfInv.WriteEncryptedLines(writer);
                        CoreSessionLogger.WriteSeparatorWithSpacing(writer);
                    }
                }
                LogWrite.EncryptedLine(writer, $"StartTime: {ConvertUnixTimeToDateTimeString(startUnixTime)}, EndTime: {ConvertUnixTimeToDateTimeString(endTime)}, TimeInPlay: {ConvertUnixTimeToTimeString(endTime - startUnixTime)}, SessionTime: {sessionTime}");
                CoreSessionLogger.WriteSeparator(writer);
                LogWrite.EncryptedLine(writer, "\n\n");

                AppendHeroBoxOffDurationWarning(endTime);
                CoreSessionLogger.WriteWarningsSection(writer, InvWarn);

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
                hitWarnTracker.Reset();

                DamageChangeTracker.WriteSection(writer, damageChangeTracker);
                charmsChangeTracker.Write(writer);
                foreach (ITrackerLifecycle lifecycleTracker in LifecycleTrackers())
                {
                    lifecycleTracker.Write(writer);
                }

                AheSettingsManager.WriteSettingsWithSeparator(writer);
                CustomKnightSettingsManager.WriteSettingsWithSeparator(writer);
                godhomeQolTracker.WriteSection(writer);
                DebugModEventsWriter.Write(writer, debugModEventsTracker.Events);
                DebugHotKeysWriter.Write(writer, debugHotkeysTracker.Bindings, debugHotkeysTracker.Activations);
                debugMenuTracker.WriteSection(writer);
                CoreSessionLogger.WriteSeparator(writer);
                CoreSessionLogger.WriteControlSettings(writer);
                if (!manualClose)
                {

                    LogWrite.EncryptedLine(writer, "\n" + isChallengeCompleted + "\n");
                }

                LogWrite.Raw(writer, lastString);
                writer.Flush();
                writer.Close();
                writer = null;

                string pantheonName = PathSanitizer.SanitizeSegment(currentPanteon.name, "Unknown");
                string logFolderName = manualClose ? "Manual Logs" : pantheonName;
                string logDir = Path.Combine(dllDir, logFolderName);
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                string dataTimeNow = ConvertUnixTimeToFileSuffix(lastUnixTime);

                string namePrefix = manualClose
                    ? PathSanitizer.SanitizeSegment(manualStartScene, "Manual")
                    : PathSanitizer.SanitizeSegment(
                        string.IsNullOrWhiteSpace(isChallengeCompleted) || isChallengeCompleted == "-" ? pantheonName : $"{isChallengeCompleted}{pantheonName}",
                        pantheonName);

                string runFolderName = $"{namePrefix} ({dataTimeNow})";
                string runDir = Path.Combine(logDir, runFolderName);
                Directory.CreateDirectory(runDir);
                string newPath = Path.Combine(runDir, $"{runFolderName}.log");

                if (File.Exists(currentNameLog))
                {
                    FileMoveHelper.MoveSafely(currentNameLog, newPath);
                    string toastText = Path.GetFileName(newPath);
                    SavedLogToast.Record(toastText);
                    pantheonToastHideAtUnscaledTime = Time.unscaledTime + hudToastSeconds;
                    if (customCanvas != null)
                    {
                        customCanvas.ShowSavedFileToast(toastText, hudToastSeconds);
                    }
                    else
                    {
                        SavedLogToast.Show(hudToastSeconds);
                    }
                    toastShown = true;
                }
            }
            catch (Exception ex)
            {

                global::ReplayLogger.InternalDiagnostics.Error($"ReplayLogger: failed to finalize/save log '{currentNameLog}': {ex.Message}");
            }
            finally
            {
                StreamWriter writerToDispose = writer;
                writer = null;
                activeEncryptionSession = null;
                if (writerToDispose != null)
                {
                    try
                    {
                        writerToDispose.Dispose();
                    }
                    catch (Exception disposeEx)
                    {
                        global::ReplayLogger.InternalDiagnostics.Warn("ReplayLogger: failed to dispose pantheon writer: " + disposeEx.Message);
                    }
                }

                pressedButtonsLog?.Clear();
                DamageAnfInv?.Clear();
                InvWarn?.Clear();
                speedWarnBuffer?.Clear();
                hitWarnBuffer?.Clear();
                pressedButtonsLog = null;
                DamageAnfInv = null;
                InvWarn = null;
                speedWarnBuffer = null;
                hitWarnBuffer = null;
                AheSettingsManager.Reset();
                CustomKnightSettingsManager.Reset();
                godhomeQolTracker.Reset();
                debugHotkeysTracker.Reset();
                debugMenuTracker.Reset();
                bossPhaseThresholdTracker.Reset();
                ResetInlineTimelineCursors();
                keyLogBuffer.Clear();
                lastKeyLogFlushTime = 0;
                lastHudElapsedSeconds = -1;
                keyScanner.Clear();
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
                infoBoss.Clear();
                ownerPathByGameObject.Clear();
                ownerPathCacheCleanupBuffer.Clear();
                lastOwnerPathCacheCleanupTime = 0f;
                ownerPathCacheCleanupCursor = 0;
                uniqueBossByGameObject.Clear();
                uniqueBossSet.Clear();
                infoBossKeysBuffer.Clear();
                uniqueBossBuffersDirty = true;
                hasHeroBoxState = false;
                lastHeroBoxActive = -1;
                heroBoxOffStartTime = -1f;
                cachedHeroTransform = null;
                cachedHeroBoxObject = null;
                isInvincible = false;
                invTimer = 0f;
                damageSectionStarted = false;
                pressedButtonsSectionStarted = false;
                DisposeDebugModHooks();
                isChallengeCompleted = "-";
                bossCounter = 0;
                startUnixTime = 0;
                isPlayChalange = false;
                isManualLogging = false;
                ReleaseGameplayHooksIfIdle();
                currentPantheonNumber = 0;
                capturedPantheonNumber = 0;
                capturedPantheonSequence = null;
                currentPantheonUsesTrueBossRush = false;
                currentPantheonUsesRandomOrder = false;
                cachedFrameUnixTime = 0;
                manualStartScene = null;
                manualRoomHeaderWritten = false;
                manualRoomStartUnixTime = 0;
                manualHoldStartTime = 0f;
                manualBossManipulateLatched = false;
                if (toastShown || manualClose)
                {
                    customCanvas?.DestroyCanvasDelayed(hudToastSeconds);
                }
                else
                {
                    if (customCanvas != null && pantheonToastHideAtUnscaledTime > Time.unscaledTime)
                    {
                        float remaining = pantheonToastHideAtUnscaledTime - Time.unscaledTime;
                        customCanvas.DestroyCanvasDelayed(Mathf.Max(remaining, 0.1f));
                    }
                    else
                    {
                        customCanvas?.DestroyCanvas();
                        pantheonToastHideAtUnscaledTime = 0f;
                    }
                }
                currentPanteon = (null, null);
            }
        }

        private static string ExtractObjectName(string log)
        {
            if (string.IsNullOrEmpty(log))
            {
                return null;
            }

            const string damagePrefix = "Add NEW unique damage: ";
            const string multiplierPrefix = "Add NEW unique multiplier: ";
            int ownerStart;
            if (log.StartsWith(damagePrefix, StringComparison.Ordinal))
            {
                ownerStart = damagePrefix.Length;
            }
            else if (log.StartsWith(multiplierPrefix, StringComparison.Ordinal))
            {
                ownerStart = multiplierPrefix.Length;
            }
            else
            {
                return null;
            }

            int ownerEnd = log.IndexOf('-', ownerStart);
            if (ownerEnd <= ownerStart)
            {
                return null;
            }

            string owner = log.Substring(ownerStart, ownerEnd - ownerStart).Trim();
            return owner.Length == 0 ? null : owner;
        }

        public static Dictionary<string, List<string>> SortLogsByObjectName(List<string> logs)
        {
            Dictionary<string, List<string>> sortedLogs = new(StringComparer.Ordinal);

            foreach (string log in logs)
            {
                string objectName = ExtractObjectName(log);

                if (objectName != null)
                {
                    if (!sortedLogs.TryGetValue(objectName, out List<string> groupedLogs))
                    {
                        groupedLogs = new List<string>();
                        sortedLogs[objectName] = groupedLogs;
                    }

                    groupedLogs.Add(log);
                }
            }

            return sortedLogs;
        }

        float lastFps = 0f;

        private void SpellFluke_DoDamage(On.SpellFluke.orig_DoDamage orig, SpellFluke self, GameObject obj, int upwardRecursionAmount, bool burst)
        {
            long nowUnixTime = KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime);
            string damageScene = ResolveDamageChangeSceneTag();
            if (string.IsNullOrEmpty(damageScene))
            {
                damageScene = lastScene;
            }

            FlukenestTracker.HandleDoDamage(isPlayChalange, writer, damageChangeTracker, damageScene, lastUnixTime, nowUnixTime, orig, self, obj, upwardRecursionAmount, burst);
        }

        private void DamageEnemies_DoDamage(On.DamageEnemies.orig_DoDamage orig, DamageEnemies self, GameObject target)
        {
            long nowUnixTime = KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime);
            string damageScene = ResolveDamageChangeSceneTag();
            if (string.IsNullOrEmpty(damageScene))
            {
                damageScene = lastScene;
            }

            CharmDamageTracker.HandleDoDamage(
                isPlayChalange,
                writer,
                damageChangeTracker,
                damageScene,
                lastUnixTime,
                nowUnixTime,
                orig,
                self,
                target);
        }

        private void HitTaker_Hit(On.HitTaker.orig_Hit orig, GameObject targetGameObject, HitInstance damageInstance, int recursionDepth)
        {
            long nowUnixTime = KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime);
            string damageScene = ResolveDamageChangeSceneTag();
            if (string.IsNullOrEmpty(damageScene))
            {
                damageScene = lastScene;
            }

            CharmDamageTracker.HandleHitTakerHit(
                isPlayChalange,
                writer,
                damageChangeTracker,
                damageScene,
                lastUnixTime,
                nowUnixTime,
                orig,
                targetGameObject,
                damageInstance,
                recursionDepth);
        }

        private void DamageEffectTicker_Update(On.DamageEffectTicker.orig_Update orig, DamageEffectTicker self)
        {
            CharmDamageTracker.HandleDamageEffectTicker(isPlayChalange && writer != null, orig, self);
        }

        private void CharmDamageIntOperator_OnEnter(
            On.HutongGames.PlayMaker.Actions.IntOperator.orig_OnEnter orig,
            HutongGames.PlayMaker.Actions.IntOperator self)
        {
            long nowUnixTime = KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime);
            string damageScene = ResolveDamageChangeSceneTag();
            if (string.IsNullOrEmpty(damageScene))
            {
                damageScene = lastScene;
            }

            CharmDamageTracker.HandleDirectCharmDamage(
                isPlayChalange,
                writer,
                damageChangeTracker,
                soulSpentTracker,
                damageScene,
                lastUnixTime,
                nowUnixTime,
                orig,
                self);
        }

        private void ExtraDamageable_RecieveExtraDamage(On.ExtraDamageable.orig_RecieveExtraDamage orig, ExtraDamageable self, ExtraDamageTypes extraDamageType)
        {
            long nowUnixTime = KeyLogFormatting.GetCachedFrameUnixTimeOrNow(cachedFrameUnixTime);
            string damageScene = ResolveDamageChangeSceneTag();
            if (string.IsNullOrEmpty(damageScene))
            {
                damageScene = lastScene;
            }

            CharmDamageTracker.HandleExtraDamage(
                isPlayChalange,
                writer,
                damageChangeTracker,
                damageScene,
                lastUnixTime,
                nowUnixTime,
                orig,
                self,
                extraDamageType);
        }

        private void SendExtraDamage_OnEnter(On.SendExtraDamage.orig_OnEnter orig, SendExtraDamage self)
        {
            CharmDamageTracker.HandleSendExtraDamage(isPlayChalange && writer != null, orig, self);
        }

    }
}

