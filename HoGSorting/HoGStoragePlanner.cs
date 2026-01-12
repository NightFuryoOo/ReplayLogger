using System;
using System.IO;
using System.Reflection;

namespace ReplayLogger
{
    internal static class HoGStoragePlanner
    {
        private const string FalseKnightScene = "GG_False_Knight";
        private const string FalseKnightFolder = "False Knight";

        private const string FailedChampionScene = "GG_Failed_Champion";
        private const string FailedChampionFolder = "Failed Champion";

        private const string Hornet1Scene = "GG_Hornet_1";
        private const string Hornet1Folder = "Hornet 1";

        private const string Hornet2Scene = "GG_Hornet_2";
        private const string Hornet2Folder = "Hornet 2";

        private const string MegaMossScene = "GG_Mega_Moss_Charger";
        private const string MegaMossFolder = "Mega Moss Charger";

        private const string FlukemarmScene = "GG_Flukemarm";
        private const string FlukemarmFolder = "Flukemarm";

        private const string MantisLordsScene = "GG_Mantis_Lords";
        private const string MantisLordsFolder = "Mantis Lords";
        private const string MantisSistersScene = "GG_Mantis_Lords_V";
        private const string MantisSistersFolder = "Sisters of Battle";
        private const string OblobblesScene = "GG_Oblobbles";
        private const string OblobblesFolder = "Oblobbles";
        private const string HiveKnightScene = "GG_Hive_Knight";
        private const string HiveKnightFolder = "Hive Knight";
        private const string BrokenVesselScene = "GG_Broken_Vessel";
        private const string BrokenVesselFolder = "Broken Vessel";
        private const string LostKinScene = "GG_Lost_Kin";
        private const string LostKinFolder = "Lost Kin";
        private const string NoskVScene = "GG_Nosk_V";
        private const string NoskFolder = "Nosk";
        private const string NoskHornetScene = "GG_Nosk_Hornet";
        private const string NoskHornetFolder = "Winged Nosk";
        private const string CollectorVScene = "GG_Collector_V";
        private const string CollectorFolder = "The Collector";
        private const string GodTamerScene = "GG_God_Tamer";
        private const string GodTamerFolder = "God Tamer";
        private const string CrystalGuardianScene = "GG_Crystal_Guardian";
        private const string CrystalGuardianFolder = "Crystal Guardian";
        private const string CrystalGuardian2Scene = "GG_Crystal_Guardian_2";
        private const string CrystalGuardian2Folder = "Crystal Guardian 2";
        private const string UumuuVScene = "GG_Uumuu_V";
        private const string UumuuFolder = "Uumuu";
        private const string TraitorLordScene = "GG_Traitor_Lord";
        private const string TraitorLordFolder = "Traitor Lord";
        private const string GreyPrinceZoteScene = "GG_Grey_Prince_Zote";
        private const string GreyPrinceZoteFolder = "Grey Prince Zote";
        private const string MageKnightVScene = "GG_Mage_Knight_V";
        private const string MageKnightFolder = "Soul Warrior";
        private const string SoulMasterScene = "GG_Soul_Master";
        private const string SoulMasterFolder = "Soul Master";
        private const string SoulTyrantScene = "GG_Soul_Tyrant";
        private const string SoulTyrantFolder = "Soul Tyrant";
        private const string DungDefenderScene = "GG_Dung_Defender";
        private const string DungDefenderFolder = "Dung Defender";
        private const string WhiteDefenderScene = "GG_White_Defender";
        private const string WhiteDefenderFolder = "White Defender";
        private const string WatcherKnightsScene = "GG_Watcher_Knights";
        private const string WatcherKnightsFolder = "Watcher Knights";
        private const string NoEyesVScene = "GG_Ghost_No_Eyes_V";
        private const string NoEyesFolder = "No Eyes";
        private const string MarmuVScene = "GG_Ghost_Marmu_V";
        private const string MarmuFolder = "Marmu";
        private const string XeroVScene = "GG_Ghost_Xero_V";
        private const string XeroFolder = "Xero";
        private const string MarkothVScene = "GG_Ghost_Markoth_V";
        private const string MarkothFolder = "Markoth";
        private const string GalienScene = "GG_Ghost_Galien";
        private const string GalienFolder = "Galien";
        private const string GorbVScene = "GG_Ghost_Gorb_V";
        private const string GorbFolder = "Gorb";
        private const string HuScene = "GG_Ghost_Hu";
        private const string HuFolder = "Elder Hu";
        private const string NailmastersScene = "GG_Nailmasters";
        private const string NailmastersFolder = "Nailmasters Oro & Mato";
        private const string PainterScene = "GG_Painter";
        private const string PainterFolder = "Paintmaster Sheo";
        private const string SlyScene = "GG_Sly";
        private const string SlyFolder = "Nailsage Sly";
        private const string PureVesselScene = "GG_Hollow_Knight";
        private const string PureVesselFolder = "Pure Vessel";
        private const string GrimmScene = "GG_Grimm";
        private const string GrimmFolder = "Grimm";
        private const string GrimmNightmareScene = "GG_Grimm_Nightmare";
        private const string GrimmNightmareFolder = "Nightmare King Grimm";
        private const string RadianceScene = "GG_Radiance";
        private const string RadianceFolder = "Absolute Radiance";
        private const string RadianceMdrSubfolder = "MDR";
        private const string AnyRadianceRootFolder = "Any Radiance";
        private const string AnyRadianceOneFolder = "AnyRadiance 1.0";
        private const string AnyRadianceTwoFolder = "AnyRadiance 2.0";
        private const string AnyRadianceThreeFolder = "AnyRadiance 3.0";
        private const string AnyRadianceOneModFolder = "AnyRadianceFixedDDark";
        private const string AnyRadianceTwoModFolder = "AnyRadiance2-1.5";
        private const string AnyRadianceThreeModFolder = "AnyRadiance 3.0";
        private const string PaleCourtDryyaScene = "gg dryya";
        private const string PaleCourtHegemolScene = "gg hegemol";
        private const string PaleCourtZemerScene = "gg zemer";
        private const string PaleCourtIsmaScene = "gg isma";
        private const string PaleCourtWhiteDefenderScene = "GG_White_Defender";
        private const string ChampionsCallScene = "Dream_04_White_Defender";

        private static readonly string ModsDirectory = ResolveModsDirectory();
        private static Type paleCourtBossOwnerType;
        private static FieldInfo paleCourtBossField;
        private static PropertyInfo paleCourtBossProperty;

        internal static HoGStoragePlan GetPlan(string sceneName, AllHallownestEnhancedToggleSnapshot snapshot, int? bossHp, string previousScene = null)
        {
            if (string.Equals(sceneName, PaleCourtDryyaScene, StringComparison.Ordinal))
            {
                return BuildPaleCourtDryyaPlan(bossHp);
            }

            if (string.Equals(sceneName, PaleCourtHegemolScene, StringComparison.Ordinal))
            {
                return BuildPaleCourtHegemolPlan(bossHp);
            }

            if (string.Equals(sceneName, PaleCourtZemerScene, StringComparison.Ordinal))
            {
                return BuildPaleCourtZemerPlan(bossHp);
            }

            if (string.Equals(sceneName, PaleCourtIsmaScene, StringComparison.Ordinal))
            {
                return BuildPaleCourtIsmaPlan(bossHp);
            }

            if (string.Equals(sceneName, PaleCourtWhiteDefenderScene, StringComparison.Ordinal))
            {
                return BuildPaleCourtWhiteDefenderPlan(bossHp, previousScene);
            }

            if (string.Equals(sceneName, ChampionsCallScene, StringComparison.Ordinal))
            {
                return BuildChampionsCallPlan();
            }

            if (string.Equals(sceneName, RadianceScene, StringComparison.Ordinal))
            {
                return BuildRadiancePlan(snapshot, bossHp);
            }

            HoGBucketInfo bucket = HoGLoggerConditions.ResolveBucket(sceneName, snapshot, previousScene);
            return HoGStoragePlan.Final(bucket, null);
        }
        private static HoGStoragePlan BuildPaleCourtDryyaPlan(int? bossHp)
        {
            HoGBucketInfo bucket = new HoGBucketInfo("Pale Court", "Fierce Dryya", "Pale Court", "Fierce Dryya");
            return HoGStoragePlan.Final(bucket, null, PaleCourtDryyaScene);
        }

        private static HoGStoragePlan BuildPaleCourtHegemolPlan(int? bossHp)
        {
            HoGBucketInfo bucket = new HoGBucketInfo("Pale Court", "Mighty Hegemol", "Pale Court", "Mighty Hegemol");
            return HoGStoragePlan.Final(bucket, null, PaleCourtHegemolScene);
        }

        private static HoGStoragePlan BuildPaleCourtZemerPlan(int? bossHp)
        {
            string bossName = TryReadPaleCourtBossName();
            string bossFolder = string.Equals(bossName, "Mystic", StringComparison.Ordinal)
                ? "Mystic Zemer"
                : "Mysterious Zemer";
            HoGBucketInfo bucket = new HoGBucketInfo("Pale Court", bossFolder, "Pale Court", bossFolder);
            return HoGStoragePlan.Final(bucket, null, PaleCourtZemerScene);
        }
        private static HoGStoragePlan BuildPaleCourtIsmaPlan(int? bossHp)
        {
            HoGBucketInfo bucket = new HoGBucketInfo("Pale Court", "Kindly Isma", "Pale Court", "Kindly Isma");
            return HoGStoragePlan.Final(bucket, null, PaleCourtIsmaScene);
        }

        private static HoGStoragePlan BuildPaleCourtWhiteDefenderPlan(int? bossHp, string previousScene)
        {
            HoGBucketInfo paleCourt = new HoGBucketInfo("Pale Court", "Ogrim & Isma", "Pale Court", "Ogrim & Isma");
            HoGBucketInfo hoGBucket = new HoGBucketInfo("HoG", "White Defender", "HoG");
            HoGBucketInfo hoGAhe = new HoGBucketInfo("HoG AHE", "White Defender", "HoG AHE");
            HoGBucketInfo hoGAhePlus = new HoGBucketInfo("HoG AHE+", "White Defender", "HoG AHE+");

            bool isPaleCourtEntry = string.Equals(previousScene, HoGLoggerConditions.PaleCourtEntryScene, StringComparison.Ordinal);
            if (isPaleCourtEntry)
            {
                return HoGStoragePlan.Final(paleCourt, null, PaleCourtWhiteDefenderScene);
            }

            
            AllHallownestEnhancedToggleSnapshot snapshot = AheSettingsManager.CurrentSnapshot;
            if (snapshot.Available && snapshot.MainSwitch && snapshot.StrengthenAllBoss && snapshot.StrengthenAllMonsters)
            {
                if (snapshot.OriginalHp)
                {
                    return HoGStoragePlan.Final(hoGAhe, null, PaleCourtWhiteDefenderScene);
                }

                return HoGStoragePlan.Final(hoGAhePlus, null, PaleCourtWhiteDefenderScene);
            }

            return HoGStoragePlan.Final(hoGBucket, null, PaleCourtWhiteDefenderScene);
        }

        private static HoGStoragePlan BuildChampionsCallPlan()
        {
            HoGBucketInfo bucket = new HoGBucketInfo("Pale Court", "Champions Call", "Pale Court", "Champions Call");
            return HoGStoragePlan.Final(bucket, null, ChampionsCallScene);
        }

        internal static bool RequiresHp(string sceneName) => false;

        private static HoGStoragePlan BuildRadiancePlan(AllHallownestEnhancedToggleSnapshot snapshot, int? hp)
        {
            string anyRadianceRoot = ResolveAnyRadianceRootFolder();
            if (!string.IsNullOrEmpty(anyRadianceRoot))
            {
                return HoGStoragePlan.Final(new HoGBucketInfo(anyRadianceRoot, string.Empty, anyRadianceRoot, anyRadianceRoot), null, RadianceScene);
            }

            bool coreToggles = snapshot.Available &&
                snapshot.MainSwitch &&
                snapshot.StrengthenAllBoss &&
                snapshot.StrengthenAllMonsters;

            if (coreToggles)
            {
                if (snapshot.MoreRadiance)
                {
                    return HoGStoragePlan.Final(
                        new HoGBucketInfo("HoG AHE+", RadianceMdrSubfolder, "HoG AHE+", RadianceFolder),
                        null,
                        RadianceScene);
                }

                if (!snapshot.OriginalHp)
                {
                    return HoGStoragePlan.Final(CreateBucket("HoG AHE+", RadianceFolder, "HoG AHE+"), null, RadianceScene);
                }

                return HoGStoragePlan.Final(CreateBucket("HoG AHE", RadianceFolder, "HoG AHE"), null, RadianceScene);
            }

            return HoGStoragePlan.Final(CreateBucket("HoG", RadianceFolder, "HoG"), null, RadianceScene);
        }
        private static HoGBucketInfo CreateBucket(string root, string bossFolder, string label, string filePrefix = null) =>
            new HoGBucketInfo(root, bossFolder, label ?? root, filePrefix);

        private static string TryReadPaleCourtBossName()
        {
            if (paleCourtBossOwnerType == null || (paleCourtBossField == null && paleCourtBossProperty == null))
            {
                paleCourtBossOwnerType = FindType("BossManagement.CustomWP") ?? FindTypeByName("CustomWP");
                if (paleCourtBossOwnerType != null)
                {
                    const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                    paleCourtBossField = paleCourtBossOwnerType.GetField("boss", flags)
                        ?? paleCourtBossOwnerType.GetField("Boss", flags);

                    if (paleCourtBossField == null)
                    {
                        paleCourtBossProperty = paleCourtBossOwnerType.GetProperty("boss", flags)
                            ?? paleCourtBossOwnerType.GetProperty("Boss", flags);
                    }
                }
            }

            if (paleCourtBossOwnerType == null || (paleCourtBossField == null && paleCourtBossProperty == null))
            {
                return null;
            }

            try
            {
                object raw = paleCourtBossProperty != null
                    ? paleCourtBossProperty.GetValue(null)
                    : paleCourtBossField.GetValue(null);

                if (raw == null)
                {
                    return null;
                }

                string name = raw.ToString();
                return string.IsNullOrWhiteSpace(name) ? null : name;
            }
            catch
            {
                return null;
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

        internal static string ResolveAnyRadianceRootFolder()
        {
            int? version = GetAnyRadianceVersion();
            return ResolveAnyRadianceRootFolder(version);
        }

        internal static string ResolveAnyRadianceRootFolder(int? version)
        {
            if (!version.HasValue || !IsAnyRadianceActive(version.Value))
            {
                return null;
            }

            return version switch
            {
                3 => AnyRadianceThreeFolder,
                2 => AnyRadianceTwoFolder,
                1 => AnyRadianceOneFolder,
                _ => null
            };
        }

        private static bool IsAnyRadianceActive(int version)
        {
            if (version != 3)
            {
                return true;
            }

            try
            {
                return PlayerData.instance != null && PlayerData.instance.statueStateRadiance.usingAltVersion;
            }
            catch
            {
                return false;
            }
        }

        private static int? GetAnyRadianceVersion()
        {
            if (HasModFolder(AnyRadianceThreeModFolder))
            {
                return 3;
            }

            if (HasModFolder(AnyRadianceTwoModFolder))
            {
                return 2;
            }

            if (HasModFolder(AnyRadianceOneModFolder))
            {
                return 1;
            }

            return null;
        }

        private static bool HasModFolder(string folderName)
        {
            if (string.IsNullOrEmpty(folderName) || string.IsNullOrEmpty(ModsDirectory))
            {
                return false;
            }

            try
            {
                return Directory.Exists(Path.Combine(ModsDirectory, folderName));
            }
            catch
            {
                return false;
            }
        }

        private static string ResolveModsDirectory()
        {
            try
            {
                string dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(dllDir))
                {
                    return null;
                }

                return new DirectoryInfo(dllDir).Parent?.FullName;
            }
            catch
            {
                return null;
            }
        }

    }

    internal readonly struct HoGStoragePlan
    {
        internal HoGStoragePlan(HoGBucketInfo bucketInfo, string hpScene, int? hpValue)
        {
            BucketInfo = bucketInfo;
            HpScene = hpScene;
            HpValue = hpValue;
        }

        internal HoGBucketInfo BucketInfo { get; }
        internal string HpScene { get; }
        internal int? HpValue { get; }
        internal bool NeedsHp => !string.IsNullOrEmpty(HpScene) && !HpValue.HasValue;

        internal static HoGStoragePlan Final(HoGBucketInfo bucketInfo, int? hpValue, string sceneName = null) =>
            new HoGStoragePlan(bucketInfo, sceneName, hpValue);

        internal static HoGStoragePlan WaitForHp(HoGBucketInfo provisionalBucket, string sceneName) =>
            new HoGStoragePlan(provisionalBucket, sceneName, null);
    }
}


