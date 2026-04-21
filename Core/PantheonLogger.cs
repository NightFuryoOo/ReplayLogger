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

        private long lastUnixTime;
        private long startUnixTime;

        private bool isPlayChalange = false;
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
        private readonly HashSet<KeyCode> pressedKeys = new();
        private readonly List<KeyCode> pressedKeysBuffer = new(64);
        private const int EnemyColliderBufferInitialSize = 1024;
        private const int EnemyColliderBufferMaxSize = 32768;
        private const float EnemyLayerMaskRefreshIntervalSeconds = 1f;
        private const float EnemyColliderOverflowWarnIntervalSeconds = 5f;
        private Collider2D[] enemyColliderBuffer = new Collider2D[EnemyColliderBufferInitialSize];
        private bool enemyColliderBufferOverflowLogged;
        private float lastEnemyColliderOverflowWarningTime;
        private int enemyScanLayerMask;
        private float lastEnemyLayerMaskRefreshTime;
        private readonly Dictionary<GameObject, HealthManager> enemyHealthManagerByGameObject = new(512);
        private readonly List<GameObject> enemyHealthManagerCacheCleanupBuffer = new(64);
        private const float EnemyHealthCacheCleanupTickSeconds = 0.25f;
        private const int EnemyHealthCacheCleanupMinSize = 128;
        private const int EnemyHealthCacheCleanupBatchSize = 64;
        private float lastEnemyHealthCacheCleanupTime;
        private int enemyHealthCacheCleanupCursor;
        private readonly List<GameObject> ownerPathCacheCleanupBuffer = new(128);
        private const float OwnerPathCacheCleanupTickSeconds = 0.5f;
        private const int OwnerPathCacheCleanupMinSize = 256;
        private const int OwnerPathCacheCleanupBatchSize = 128;
        private float lastOwnerPathCacheCleanupTime;
        private int ownerPathCacheCleanupCursor;
        private const float EnemyUpdateIntervalSeconds = 0.1f;
        private const float EnemySeedRetryIntervalSeconds = 0.5f;
        private float lastEnemyUpdateTime;
        private float lastEnemySeedTime;
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
        private readonly Dictionary<Type, MemberInfo> hitTargetMemberByType = new();
        private readonly HashSet<Type> hitTargetMemberMissTypes = new();

        private readonly SpeedWarnTracker speedWarnTracker = new();
        private readonly HitWarnTracker hitWarnTracker = new();

        private readonly DamageChangeTracker damageChangeTracker = new();
        private readonly List<string> debugModEvents = new();
        private readonly DebugModEventsTracker debugModEventsTracker = new();
        private readonly DebugHotkeysTracker debugHotkeysTracker = new();
        private readonly DebugMenuTracker debugMenuTracker = new();
        private readonly CharmsChangeTracker charmsChangeTracker = new();
        private readonly GodhomeQolTracker godhomeQolTracker = new();
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
        private static Hook debugKillAllHook;
        private static Hook debugKillSelfHook;
        private static readonly string[] HitWarnSectionHeaderLines = { "\n\n", "HitWarn:" };
        private static readonly string[] HitWarnSectionFooterLines = { "\n\n", "---------------------------------------------------" };
        private bool damageSectionStarted;
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
            On.BossSceneController.Update += BossSceneController_Update;
            On.HeroController.FixedUpdate += HeroController_FixedUpdate;
            On.HealthManager.TakeDamage += HealthManager_TakeDamage;
            On.SceneLoad.RecordEndTime += SceneLoad_RecordEndTime;
            On.SpellFluke.DoDamage += SpellFluke_DoDamage;
            On.DamageEnemies.DoDamage += DamageEnemies_DoDamage;
            On.HitTaker.Hit += HitTaker_Hit;
            On.ExtraDamageable.RecieveExtraDamage += ExtraDamageable_RecieveExtraDamage;

            ModHooks.HitInstanceHook += ModHooks_HitInstanceHook;
            ModHooks.AfterTakeDamageHook += ModHooks_AfterTakeDamageHook;
            ModHooks.BeforePlayerDeadHook += ModHooks_BeforePlayerDeadHook;
            ModHooks.AfterSavegameLoadHook += OnAfterSavegameLoad;

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

        private void OnAfterSavegameLoad(SaveGameData _)
        {
            HardwareFingerprint.Prime();
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
            currentPantheonNumber = DeterminePantheonNumber(sequence);
        }

        private static int DeterminePantheonNumber(BossSequence sequence)
        {
            if (sequence == null)
            {
                return 0;
            }

            try
            {
                FieldInfo bossScenesField = typeof(BossSequence).GetField("bossScenes", BindingFlags.Instance | BindingFlags.NonPublic);
                if (bossScenesField?.GetCachedValue(sequence) is not BossScene[] bossScenes || bossScenes.Length == 0)
                {
                    return 0;
                }

                HashSet<string> names = new(bossScenes.Select(scene => scene.sceneName), StringComparer.OrdinalIgnoreCase);
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



        private HitInstance ModHooks_HitInstanceHook(HutongGames.PlayMaker.Fsm owner, HitInstance hit)
        {
            if (!isPlayChalange || writer == null)
            {
                return hit;
            }

            if (owner == null || owner.GameObject == null)
            {
                string fallbackScene = ResolveDamageChangeSceneTag();
                if (!string.IsNullOrEmpty(fallbackScene))
                {
                    CharmDamageTracker.TrackFromHitInstance(
                        isPlayChalange,
                        writer,
                        damageChangeTracker,
                        fallbackScene,
                        lastUnixTime,
                        GetCachedFrameUnixTimeOrNow(),
                        hit);
                }
                return hit;
            }

            long unixTime = GetCachedFrameUnixTimeOrNow();
            string activeSceneName = GetActiveArenaSceneName();
            GameObject ownerObject = owner.GameObject;
            int ownerId = ownerObject.GetInstanceID();
            string ownerName = GetCachedOwnerPath(ownerObject);
            string damageScene = ResolveDamageChangeSceneTag();

            if (!string.IsNullOrEmpty(damageScene))
            {
                damageChangeTracker.Track(ownerId, ownerName, damageScene, unixTime - lastUnixTime, hit.DamageDealt, hit.Multiplier);
            }

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

            long nowUnixTime = GetCachedFrameUnixTimeOrNow();
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

            long nowUnixTime = GetCachedFrameUnixTimeOrNow();
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

            CharmDamageTracker.TrackFromHitWithActualDamage(
                isPlayChalange,
                writer,
                damageChangeTracker,
                activeSceneName,
                lastUnixTime,
                GetCachedFrameUnixTimeOrNow(),
                hitInstance,
                hpBefore - hpAfter,
                self.gameObject);

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

        private bool TryResolveHitTargetHealthManager(HitInstance hit, out HealthManager healthManager)
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

            if (rawTarget is HealthManager manager)
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

        private object GetCachedHitTargetRaw(object boxedHit, Type hitType)
        {
            if (boxedHit == null || hitType == null)
            {
                return null;
            }

            if (hitTargetMemberByType.TryGetValue(hitType, out MemberInfo cachedMember))
            {
                return ReadHitTargetMemberValue(cachedMember, boxedHit);
            }

            if (hitTargetMemberMissTypes.Contains(hitType))
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
                        hitTargetMemberByType[hitType] = field;
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
                    hitTargetMemberByType[hitType] = property;
                    return propertyValue;
                }
            }

            foreach (FieldInfo field in hitType.GetFields(flags))
            {
                object fieldValue = ReadHitTargetMemberValue(field, boxedHit);
                if (IsPotentialHitTargetValue(fieldValue))
                {
                    hitTargetMemberByType[hitType] = field;
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
                    hitTargetMemberByType[hitType] = property;
                    return propertyValue;
                }
            }

            hitTargetMemberMissTypes.Add(hitType);
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
            return value is GameObject or Transform or Component or HealthManager;
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

            EnsureUniqueBossBuffers();
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
            long nowUnixTime = GetCachedFrameUnixTimeOrNow();
            cachedFrameUnixTime = nowUnixTime;
            InvCheck(nowUnixTime);
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
                long nowUnixTime = logger.GetCachedFrameUnixTimeOrNow();
                logger.debugMenuTracker.LogManualChange(logger.writer, logger.lastScene, logger.lastUnixTime, nowUnixTime, "Cheats/Kill All", null, "Executed");
            }
        }

        private static void DebugKillSelfDetour(Action orig)
        {
            orig();
            ReplayLogger logger = Instance;
            if (logger != null && logger.isPlayChalange && logger.writer != null)
            {
                long nowUnixTime = logger.GetCachedFrameUnixTimeOrNow();
                logger.debugMenuTracker.LogManualChange(logger.writer, logger.lastScene, logger.lastUnixTime, nowUnixTime, "Cheats/Kill Self", null, "Executed");
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

        private void RefreshUniqueBossBuffers()
        {
            uniqueBossByGameObject.Clear();
            uniqueBossSet.Clear();

            foreach (HealthManager boss in infoBoss.Keys)
            {
                if (boss == null)
                {
                    continue;
                }

                uniqueBossByGameObject[boss.gameObject] = boss;
            }

            foreach (HealthManager boss in uniqueBossByGameObject.Values)
            {
                uniqueBossSet.Add(boss);
            }

            uniqueBossBuffersDirty = false;
        }

        private void EnsureUniqueBossBuffers()
        {
            if (uniqueBossBuffersDirty)
            {
                RefreshUniqueBossBuffers();
            }
        }

        private string GetCachedOwnerPath(GameObject ownerObject)
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

        private void CleanupOwnerPathCacheIfNeeded(float now)
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

        private static bool ShouldTrackHealthManager(HealthManager manager)
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

        private void IncludeEnemyLayer(int layer)
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

            int mask = BuildHeroCollisionLayerMask(hero.gameObject.layer);

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

        private void CleanupEnemyHealthManagerCacheIfNeeded(float now)
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

        private int CollectEnemyCollidersNonAlloc(Vector2 center, Vector2 size, int layerMask)
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

            EnsureUniqueBossBuffers();
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
                DamageAnfInv?.Add($"{nowUnixTime - lastUnixTime}{hpInfo}|(INV OFF, {invTimer.ToString("F3")})|");
                if (invTimer > 2.6f)
                {
                    string warning = $"|{lastScene}|+{nowUnixTime - lastUnixTime}{hpInfo}|(INV OFF, {invTimer.ToString("F3")})";

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

        private GameObject ResolveHeroBoxObject(HeroController hero)
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

        private void WriteHeroBoxActiveWarning(int currentState, IEnumerable<HealthManager> bossList, int? previousState, long nowUnixTime)
        {
            string hpInfo = BuildHeroBoxHpInfo(bossList);
            string message = previousState.HasValue
                ? $"{FormatState(previousState.Value)} -> {FormatState(currentState)}"
                : FormatState(currentState);
            string warning = $"|{lastScene}|+{nowUnixTime - lastUnixTime}{hpInfo}|HeroBoxActive: {message}";
            InvWarn?.Add(warning);
        }

        private void AppendHeroBoxOffDurationWarning(long nowUnixTime)
        {
            if (!hasHeroBoxState || lastHeroBoxActive != 0 || heroBoxOffStartTime < 0f)
            {
                return;
            }
            EnsureUniqueBossBuffers();
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

            EnsureUniqueBossBuffers();
            string hpInfo = BuildTrackedHpInfo(uniqueBossByGameObject.Values);
            string duration = invTimer.ToString("F3");
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

        private static string FormatState(int state)
        {
            return state switch
            {
                1 => "On",
                0 => "Off",
                _ => "N/A"
            };
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

            EnsureUniqueBossBuffers();
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

        private void BossSceneController_Update(On.BossSceneController.orig_Update orig, BossSceneController self)
        {
            orig(self);
        }


        private IEnumerator QuitToMenu_Start(On.QuitToMenu.orig_Start orig, QuitToMenu self)
        {
            skipPantheonLogging = false;
            if (!isManualLogging)
            {
                Close();
            }
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
                    }

                    lastScene = self.TargetSceneName;
                    orig(self);
                    return;
                }

                bool isEnding = isPlayChalange && self.TargetSceneName.Contains("GG_End_Seq");
                if (isPlayChalange && writer != null)
                {
                    FlushKeyLogBufferIfNeeded(GetCachedFrameUnixTimeOrNow(), force: true);
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

                if (self.TargetSceneName.Contains("GG_Boss_Door") || (self.TargetSceneName.Contains("GG_Vengefly_V") && lastScene == "GG_Atrium_Roof"))
                {
                    int pantheonNumber = currentPantheonNumber;
                    if (pantheonNumber == 0 && self.TargetSceneName.Contains("GG_Vengefly_V") && lastScene == "GG_Atrium_Roof")
                    {
                        pantheonNumber = 5;
                    }

                    if (pantheonNumber > 0 && GodhomeQolRandomPantheonsIntegration.IsPantheonRandomized(pantheonNumber))
                    {
                        skipPantheonLogging = true;
                        lastScene = self.TargetSceneName;
                        orig(self);
                        return;
                    }

                    startUnixTime = lastUnixTime;
                    int curentPlayTime = (int)((PlayerData.instance?.playTime ?? 0f) * 100);
                    isPlayChalange = true;

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
	                          pressedKeys.Clear();
	                          pressedKeysBuffer.Clear();
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
	                          damageChangeTracker.Reset();
                              CharmDamageTracker.ResetHints();
	                          charmsChangeTracker.Reset();
                          ResetInlineTimelineCursors();
	                        CoreSessionLogger.WriteEncryptedModSnapshot(writer, modsDir, "---------------------------------------------------");
                        LogWrite.EncryptedLine(writer, CoreSessionLogger.BuildEquippedCharmsLine());
                        CoreSessionLogger.WriteEncryptedSkillLines(writer, "---------------------------------------------------");
                          speedWarnTracker.LogInitial(writer, self.TargetSceneName, lastUnixTime);
                          InitializeDebugModHooks();
                          godhomeQolTracker.Reset();
                          godhomeQolTracker.StartFight(self.TargetSceneName, lastUnixTime, includeBossManipulate: false, includeBossChallenge: false);
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

                    if (self.TargetSceneName.Contains("GG_Vengefly_V") && lastScene == "GG_Atrium_Roof")
                    {
                        currentPanteon = ("P5", Panteons.P5.ToList());
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
                        if (self.TargetSceneName == Panteons.P1[0])
                            currentPanteon = ("P1", Panteons.P1.ToList());
                        if (self.TargetSceneName == Panteons.P2[0])
                            currentPanteon = ("P2", Panteons.P2.ToList());
                        if (self.TargetSceneName == Panteons.P3[0])
                            currentPanteon = ("P3", Panteons.P3.ToList());
                        if (self.TargetSceneName == Panteons.P4[0])
                            currentPanteon = ("P4", Panteons.P4.ToList());

                        int pantheonNumber = GetPantheonNumber(currentPanteon.name);
                        if (pantheonNumber > 0 && GodhomeQolRandomPantheonsIntegration.IsPantheonRandomized(pantheonNumber))
                        {
                            skipPantheonLogging = true;
                            AbortPantheonLogging();
                            lastScene = self.TargetSceneName;
                            orig(self);
                            return;
                        }
                    }
                    else if (currentPanteon.list != null)
                    {

                        int targetIndex = currentPanteon.list.IndexOf((self.TargetSceneName));
                        int lastSceneIndex = currentPanteon.list.IndexOf(lastScene);
                        godhomeQolTracker.StartFight(self.TargetSceneName, lastUnixTime, includeBossManipulate: false, includeBossChallenge: false);


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

        private static int GetPantheonNumber(string pantheonName)
        {
            if (string.IsNullOrWhiteSpace(pantheonName) || pantheonName.Length < 2 || pantheonName[0] != 'P')
            {
                return 0;
            }

            char digit = pantheonName[1];
            if (digit < '1' || digit > '5')
            {
                return 0;
            }

            return digit - '0';
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
            ZoteSettingsManager.Reset();
            CollectorPhasesSettingsManager.Reset();
            CustomKnightSettingsManager.Reset();
            godhomeQolTracker.Reset();
            debugHotkeysTracker.Reset();
            debugMenuTracker.Reset();
            damageChangeTracker.Reset();
            CharmDamageTracker.ResetHints();
            hitWarnTracker.Reset();
            ResetInlineTimelineCursors();

            keyLogBuffer.Clear();
            lastKeyLogFlushTime = 0;
            lastHudElapsedSeconds = -1;
		            pressedKeys.Clear();
		            pressedKeysBuffer.Clear();
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
            DisposeDebugModHooks();

            isChallengeCompleted = "-";
            bossCounter = 0;
            startUnixTime = 0;
            isPlayChalange = false;
            currentPantheonNumber = 0;
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

            if (!damageSectionStarted)
            {
                LogWrite.EncryptedLine(writer, "\n------------------------DAMAGE INV and PRESSED BUTTONS------------------------\n");
                damageSectionStarted = true;
            }

            if (hasPressedButtons)
            {
                pressedButtonsLog.WriteEncryptedLines(writer);
                pressedButtonsLog.Clear();
                WriteBlockSeparatorWithSpacing(writer);
            }

            if (hasDamageInv)
            {
                DamageAnfInv.WriteEncryptedLines(writer);
                DamageAnfInv.Clear();
                WriteBlockSeparatorWithSpacing(writer);
            }

        }

        private static void WriteBlockSeparatorWithSpacing(StreamWriter writer)
        {
            if (writer == null)
            {
                return;
            }

            LogWrite.EncryptedLine(writer, string.Empty);
            CoreSessionLogger.WriteSeparator(writer);
            LogWrite.EncryptedLine(writer, string.Empty);
        }
        private bool IsValidNextScene(List<string> panteonList, int lastSceneIndex, string targetSceneName)
        {
            int nextIndex = lastSceneIndex + 1;

            if (nextIndex >= panteonList.Count) return false;

            string expectedNextScene = panteonList[nextIndex];

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
                FlushKeyLogBufferIfNeeded(GetCachedFrameUnixTimeOrNow(), force: true);
                AppendActiveInvDurationWarning(endTime);
                bool wrotePressedButtons = pressedButtonsLog != null && pressedButtonsLog.HasContent;
                bool wroteDamageInv = DamageAnfInv != null && DamageAnfInv.HasContent;
                if (wrotePressedButtons || wroteDamageInv)
                {
                    if (!damageSectionStarted)
                    {
                        LogWrite.EncryptedLine(writer, "\n------------------------DAMAGE INV and PRESSED BUTTONS------------------------\n");
                        damageSectionStarted = true;
                    }

                    if (wrotePressedButtons)
                    {
                        pressedButtonsLog.WriteEncryptedLines(writer);
                        WriteBlockSeparatorWithSpacing(writer);
                    }

                    if (wroteDamageInv)
                    {
                        DamageAnfInv.WriteEncryptedLines(writer);
                        WriteBlockSeparatorWithSpacing(writer);
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

                AheSettingsManager.WriteSettingsWithSeparator(writer);
                ZoteSettingsManager.WriteSettingsWithSeparator(writer);
                CollectorPhasesSettingsManager.WriteSettingsWithSeparator(writer);
                SafeGodseekerQolIntegration.WriteSettingsWithSeparator(writer);
                CustomKnightSettingsManager.WriteSettingsWithSeparator(writer);
                godhomeQolTracker.WriteSection(writer);
                DebugModEventsWriter.Write(writer, debugModEventsTracker.Events);
                DebugHotKeysWriter.Write(writer, debugHotkeysTracker.Bindings, debugHotkeysTracker.Activations);
                debugMenuTracker.WriteSection(writer);
                CoreSessionLogger.WriteSeparator(writer);
                CoreSessionLogger.WriteNoBlurSettings(writer);
                CoreSessionLogger.WriteCustomizableAbilitiesSettings(writer);
                CoreSessionLogger.WriteControlSettings(writer);
                LogWrite.EncryptedLine(writer, "\n" + isChallengeCompleted + "\n");

                HardwareFingerprint.WriteEncryptedLine(writer);

                LogWrite.Raw(writer, lastString);
                writer.Flush();
                writer.Close();
                writer = null;

                string pantheonName = string.IsNullOrWhiteSpace(currentPanteon.name) ? "Unknown" : currentPanteon.name;
                string logFolderName = manualClose ? "Manual Logs" : pantheonName;
                string logDir = Path.Combine(dllDir, logFolderName);
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                string dataTimeNow = ConvertUnixTimeToFileSuffix(lastUnixTime);
                string namePrefix = manualClose
                    ? (string.IsNullOrWhiteSpace(manualStartScene) ? "Manual" : manualStartScene)
                    : (string.IsNullOrWhiteSpace(isChallengeCompleted) || isChallengeCompleted == "-" ? pantheonName : $"{isChallengeCompleted}{pantheonName}");
                string newPath = Path.Combine(logDir, $"{namePrefix} ({dataTimeNow}).log");

                string recordName = manualClose ? namePrefix : pantheonName;
                if (File.Exists(currentNameLog))
                {
                    File.Move(currentNameLog, newPath);
                    SavedLogTracker.Record(newPath, manualClose ? "Manual" : "Pantheons", recordName, "None");
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
                global::ReplayLogger.InternalDiagnostics.Info(ex.Message);
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
                ZoteSettingsManager.Reset();
                CollectorPhasesSettingsManager.Reset();
                CustomKnightSettingsManager.Reset();
                godhomeQolTracker.Reset();
                debugHotkeysTracker.Reset();
                debugMenuTracker.Reset();
                ResetInlineTimelineCursors();
                keyLogBuffer.Clear();
                lastKeyLogFlushTime = 0;
                lastHudElapsedSeconds = -1;
		                pressedKeys.Clear();
		                pressedKeysBuffer.Clear();
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
                DisposeDebugModHooks();
                isChallengeCompleted = "-";
                bossCounter = 0;
                startUnixTime = 0;
                isPlayChalange = false;
                isManualLogging = false;
                cachedFrameUnixTime = 0;
                manualStartScene = null;
                manualRoomHeaderWritten = false;
                manualRoomStartUnixTime = 0;
                manualHoldStartTime = 0f;
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
            long nowUnixTime = GetCachedFrameUnixTimeOrNow();
            string damageScene = ResolveDamageChangeSceneTag();
            if (string.IsNullOrEmpty(damageScene))
            {
                damageScene = lastScene;
            }

            FlukenestTracker.HandleDoDamage(isPlayChalange, writer, damageChangeTracker, damageScene, lastUnixTime, nowUnixTime, orig, self, obj, upwardRecursionAmount, burst);
        }

        private void DamageEnemies_DoDamage(On.DamageEnemies.orig_DoDamage orig, DamageEnemies self, GameObject target)
        {
            long nowUnixTime = GetCachedFrameUnixTimeOrNow();
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
            long nowUnixTime = GetCachedFrameUnixTimeOrNow();
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

        private void ExtraDamageable_RecieveExtraDamage(On.ExtraDamageable.orig_RecieveExtraDamage orig, ExtraDamageable self, ExtraDamageTypes extraDamageType)
        {
            long nowUnixTime = GetCachedFrameUnixTimeOrNow();
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

    }
}





