using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Modding;

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
        private const string CrystalGuardian2Folder = "Enraged Guardian";
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
        private const string WatcherKnightsFolder = "Watcher Knight";
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
        private const string NailmastersFolder = "Oro & Mato";
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
        private const string PaleCourtDryyaScene = "gg dryya";
        private const string PaleCourtHegemolScene = "gg hegemol";
        private const string PaleCourtZemerScene = "gg zemer";
        private const string PaleCourtIsmaScene = "gg isma";
        private const string PaleCourtWhiteDefenderScene = "GG_White_Defender";
        private const string ChampionsCallScene = "Dream_04_White_Defender";

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

            if (string.Equals(sceneName, FalseKnightScene, StringComparison.Ordinal))
            {
                return BuildFalseKnightPlan(snapshot, bossHp);
            }

            if (string.Equals(sceneName, FailedChampionScene, StringComparison.Ordinal))
            {
                return BuildFailedChampionPlan(snapshot, bossHp);
            }

            if (string.Equals(sceneName, Hornet1Scene, StringComparison.Ordinal))
            {
                return BuildHornet1Plan(snapshot, bossHp);
            }

            if (string.Equals(sceneName, Hornet2Scene, StringComparison.Ordinal))
            {
                return BuildHornet2Plan(snapshot, bossHp);
            }

            if (string.Equals(sceneName, MegaMossScene, StringComparison.Ordinal))
            {
                return BuildMegaMossPlan(snapshot, bossHp);
            }

            if (string.Equals(sceneName, FlukemarmScene, StringComparison.Ordinal))
            {
                return BuildFlukemarmPlan(snapshot, bossHp);
            }

            if (string.Equals(sceneName, MantisLordsScene, StringComparison.Ordinal))
            {
                return BuildMantisLordsPlan(snapshot, bossHp);
            }

            if (string.Equals(sceneName, MantisSistersScene, StringComparison.Ordinal))
            {
                return BuildMantisSistersPlan(snapshot, bossHp);
            }

            if (string.Equals(sceneName, OblobblesScene, StringComparison.Ordinal))
            {
                return BuildOblobblesPlan(snapshot, bossHp);
            }

            if (string.Equals(sceneName, HiveKnightScene, StringComparison.Ordinal))
            {
                return BuildHiveKnightPlan(snapshot, bossHp);
            }

            if (string.Equals(sceneName, BrokenVesselScene, StringComparison.Ordinal))
            {
                return BuildBrokenVesselPlan(snapshot, bossHp);
            }

            if (string.Equals(sceneName, LostKinScene, StringComparison.Ordinal))
            {
                return BuildLostKinPlan(snapshot, bossHp);
            }

            if (string.Equals(sceneName, NoskVScene, StringComparison.Ordinal))
            {
                return BuildNoskVPlan(snapshot);
            }

            if (string.Equals(sceneName, NoskHornetScene, StringComparison.Ordinal))
            {
                return BuildNoskHornetPlan(snapshot, bossHp);
            }

            if (string.Equals(sceneName, CollectorVScene, StringComparison.Ordinal))
            {
                return BuildCollectorVPlan(snapshot);
            }

            if (string.Equals(sceneName, GodTamerScene, StringComparison.Ordinal))
            {
                return BuildGodTamerPlan(snapshot, bossHp);
            }

            if (string.Equals(sceneName, CrystalGuardianScene, StringComparison.Ordinal))
            {
                return BuildCrystalGuardianPlan(snapshot, bossHp);
            }

            if (string.Equals(sceneName, CrystalGuardian2Scene, StringComparison.Ordinal))
            {
                return BuildCrystalGuardian2Plan(snapshot, bossHp);
            }

            if (string.Equals(sceneName, UumuuVScene, StringComparison.Ordinal))
            {
                return BuildUumuuVPlan(snapshot);
            }

            if (string.Equals(sceneName, TraitorLordScene, StringComparison.Ordinal))
            {
                return BuildTraitorLordPlan(snapshot, bossHp);
            }

            if (string.Equals(sceneName, GreyPrinceZoteScene, StringComparison.Ordinal))
            {
                return BuildGreyPrinceZotePlan(snapshot);
            }

            if (string.Equals(sceneName, MageKnightVScene, StringComparison.Ordinal))
            {
                return BuildMageKnightVPlan(snapshot, bossHp);
            }

            if (string.Equals(sceneName, SoulMasterScene, StringComparison.Ordinal))
            {
                return BuildSoulMasterPlan(snapshot, bossHp);
            }

            if (string.Equals(sceneName, SoulTyrantScene, StringComparison.Ordinal))
            {
                return BuildSoulTyrantPlan(snapshot, bossHp);
            }

            if (string.Equals(sceneName, DungDefenderScene, StringComparison.Ordinal))
            {
                return BuildDungDefenderPlan(snapshot, bossHp);
            }

            if (string.Equals(sceneName, WhiteDefenderScene, StringComparison.Ordinal))
            {
                return BuildWhiteDefenderPlan(snapshot);
            }

            if (string.Equals(sceneName, WatcherKnightsScene, StringComparison.Ordinal))
            {
                return BuildWatcherKnightsPlan(snapshot, bossHp);
            }

            if (string.Equals(sceneName, NoEyesVScene, StringComparison.Ordinal))
            {
                return BuildNoEyesVPlan(snapshot);
            }

            if (string.Equals(sceneName, MarmuVScene, StringComparison.Ordinal))
            {
                return BuildMarmuVPlan(snapshot);
            }

            if (string.Equals(sceneName, XeroVScene, StringComparison.Ordinal))
            {
                return BuildXeroVPlan(snapshot);
            }

            if (string.Equals(sceneName, MarkothVScene, StringComparison.Ordinal))
            {
                return BuildMarkothVPlan(snapshot);
            }

            if (string.Equals(sceneName, GalienScene, StringComparison.Ordinal))
            {
                return BuildGalienPlan(snapshot, bossHp);
            }

            if (string.Equals(sceneName, GorbVScene, StringComparison.Ordinal))
            {
                return BuildGorbVPlan(snapshot);
            }

            if (string.Equals(sceneName, HuScene, StringComparison.Ordinal))
            {
                return BuildHuPlan(snapshot, bossHp);
            }

            if (string.Equals(sceneName, NailmastersScene, StringComparison.Ordinal))
            {
                return BuildNailmastersPlan(snapshot, bossHp);
            }

            if (string.Equals(sceneName, PainterScene, StringComparison.Ordinal))
            {
                return BuildPainterPlan(snapshot, bossHp);
            }

            if (string.Equals(sceneName, SlyScene, StringComparison.Ordinal))
            {
                return BuildSlyPlan(snapshot, bossHp);
            }

            if (string.Equals(sceneName, PureVesselScene, StringComparison.Ordinal))
            {
                return BuildPureVesselPlan(snapshot, bossHp);
            }

            if (string.Equals(sceneName, GrimmScene, StringComparison.Ordinal))
            {
                return BuildGrimmPlan(snapshot);
            }

            if (string.Equals(sceneName, GrimmNightmareScene, StringComparison.Ordinal))
            {
                return BuildGrimmNightmarePlan(snapshot, bossHp);
            }

            if (string.Equals(sceneName, RadianceScene, StringComparison.Ordinal))
            {
                return BuildRadiancePlan(snapshot, bossHp);
            }

            HoGBucketInfo bucket = HoGLoggerConditions.ResolveBucket(sceneName, snapshot);
            return HoGStoragePlan.Final(bucket, null);
        }

        private static HoGStoragePlan BuildPaleCourtDryyaPlan(int? bossHp)
        {
            const int hpThreshold = 1500;
            string rootFolder = "Pale Court";
            string bossFolder = "Fierce Dryya";
            string fallbackRoot = "Other";
            string filePrefix = "Fierce Dryya";

            if (!bossHp.HasValue)
            {
                return HoGStoragePlan.WaitForHp(new HoGBucketInfo(rootFolder, bossFolder, rootFolder, filePrefix), PaleCourtDryyaScene);
            }

            if (bossHp.Value <= hpThreshold)
            {
                return HoGStoragePlan.Final(new HoGBucketInfo(fallbackRoot, bossFolder, fallbackRoot), bossHp, PaleCourtDryyaScene);
            }

            return HoGStoragePlan.Final(new HoGBucketInfo(rootFolder, bossFolder, rootFolder, filePrefix), bossHp, PaleCourtDryyaScene);
        }

        private static HoGStoragePlan BuildPaleCourtHegemolPlan(int? bossHp)
        {
            const int hpThreshold = 600;
            string rootFolder = "Pale Court";
            string bossFolder = "Mighty Hegemol";
            string fallbackRoot = "Other";
            string filePrefix = "Mighty Hegemol";

            if (!bossHp.HasValue)
            {
                return HoGStoragePlan.WaitForHp(new HoGBucketInfo(rootFolder, bossFolder, rootFolder, filePrefix), PaleCourtHegemolScene);
            }

            if (bossHp.Value <= hpThreshold)
            {
                return HoGStoragePlan.Final(new HoGBucketInfo(fallbackRoot, bossFolder, fallbackRoot), bossHp, PaleCourtHegemolScene);
            }

            return HoGStoragePlan.Final(new HoGBucketInfo(rootFolder, bossFolder, rootFolder, filePrefix), bossHp, PaleCourtHegemolScene);
        }

        private static HoGStoragePlan BuildPaleCourtZemerPlan(int? bossHp)
        {
            const int mysteriousHp = 1400;
            const int mysticHp = 1500;

            HoGBucketInfo mysteriousBucket = new HoGBucketInfo("Pale Court", "Mysterious Zemer", "Pale Court", "Mysterious Zemer");
            HoGBucketInfo mysticBucket = new HoGBucketInfo("Pale Court", "Mystic Zemer", "Pale Court", "Mystic Zemer");
            HoGBucketInfo otherBucket = new HoGBucketInfo("Other", "Zemer", "Other", "Zemer");

            if (!bossHp.HasValue)
            {
                return HoGStoragePlan.WaitForHp(mysteriousBucket, PaleCourtZemerScene);
            }

            if (bossHp.Value < mysteriousHp)
            {
                return HoGStoragePlan.Final(otherBucket, bossHp, PaleCourtZemerScene);
            }

            if (bossHp.Value == mysteriousHp)
            {
                return HoGStoragePlan.Final(mysteriousBucket, bossHp, PaleCourtZemerScene);
            }

            if (bossHp.Value == mysticHp)
            {
                return HoGStoragePlan.Final(mysticBucket, bossHp, PaleCourtZemerScene);
            }

            // Any unexpected HP values fall back to Other.
            return HoGStoragePlan.Final(otherBucket, bossHp, PaleCourtZemerScene);
        }
        private static HoGStoragePlan BuildPaleCourtIsmaPlan(int? bossHp)
        {
            const int threshold = 1450;
            HoGBucketInfo paleCourt = new HoGBucketInfo("Pale Court", "Kindly Isma", "Pale Court", "Kindly Isma");
            HoGBucketInfo other = new HoGBucketInfo("Other", "Kindly Isma", "Other", "Kindly Isma");

            if (!bossHp.HasValue)
            {
                return HoGStoragePlan.WaitForHp(paleCourt, PaleCourtIsmaScene);
            }

            if (bossHp.Value <= threshold)
            {
                return HoGStoragePlan.Final(other, bossHp, PaleCourtIsmaScene);
            }

            return HoGStoragePlan.Final(paleCourt, bossHp, PaleCourtIsmaScene);
        }

        private static HoGStoragePlan BuildPaleCourtWhiteDefenderPlan(int? bossHp, string previousScene)
        {
            HoGBucketInfo paleCourt = new HoGBucketInfo("Pale Court", "Ogrim & Isma", "Pale Court", "Ogrim & Isma");
            HoGBucketInfo hoGBucket = new HoGBucketInfo("HoG", "White Defender", "HoG");
            HoGBucketInfo hoGAhe = new HoGBucketInfo("HoG AHE", "White Defender", "HoG AHE");
            HoGBucketInfo hoGAhePlus = new HoGBucketInfo("HoG AHE+", "White Defender", "HoG AHE+");

            bool fromWorkshop = string.Equals(previousScene, HoGLoggerConditions.WorkshopScene, StringComparison.Ordinal);

            bool hasHp = bossHp.HasValue;
            int hpVal = bossHp.GetValueOrDefault(0);

            if (fromWorkshop)
            {
                return HoGStoragePlan.Final(hoGBucket, bossHp, PaleCourtWhiteDefenderScene);
            }

            // If we haven't observed HP yet, wait with HoG bucket to decide
            if (!hasHp)
            {
                return HoGStoragePlan.WaitForHp(hoGBucket, PaleCourtWhiteDefenderScene);
            }

            // If observed summed max HP > 2000, treat as Pale Court fight
            if (hpVal > 2000)
            {
                return HoGStoragePlan.Final(paleCourt, bossHp, PaleCourtWhiteDefenderScene);
            }

            // Otherwise HoG White Defender with AHE buckets if applicable
            AllHallownestEnhancedToggleSnapshot snapshot = AheSettingsManager.CurrentSnapshot;
            if (snapshot.Available && snapshot.MainSwitch && snapshot.StrengthenAllBoss && snapshot.StrengthenAllMonsters)
            {
                if (snapshot.OriginalHp)
                {
                    return HoGStoragePlan.Final(hoGAhe, bossHp, PaleCourtWhiteDefenderScene);
                }

                return HoGStoragePlan.Final(hoGAhePlus, bossHp, PaleCourtWhiteDefenderScene);
            }

            return HoGStoragePlan.Final(hoGBucket, bossHp, PaleCourtWhiteDefenderScene);
        }

        private static HoGStoragePlan BuildChampionsCallPlan()
        {
            HoGBucketInfo bucket = new HoGBucketInfo("Pale Court", "Champions Call", "Pale Court", "Champions Call");
            return HoGStoragePlan.Final(bucket, null, ChampionsCallScene);
        }

        internal static bool RequiresHp(string sceneName) =>
            string.Equals(sceneName, FalseKnightScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, FailedChampionScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, Hornet1Scene, StringComparison.Ordinal) ||
            string.Equals(sceneName, Hornet2Scene, StringComparison.Ordinal) ||
            string.Equals(sceneName, MegaMossScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, FlukemarmScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, MantisLordsScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, MantisSistersScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, OblobblesScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, HiveKnightScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, BrokenVesselScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, LostKinScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, NoskHornetScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, GodTamerScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, CrystalGuardianScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, CrystalGuardian2Scene, StringComparison.Ordinal) ||
            string.Equals(sceneName, TraitorLordScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, GreyPrinceZoteScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, MageKnightVScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, SoulMasterScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, SoulTyrantScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, DungDefenderScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, WhiteDefenderScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, WatcherKnightsScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, NoEyesVScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, MarmuVScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, XeroVScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, MarkothVScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, GalienScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, GorbVScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, HuScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, NailmastersScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, PainterScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, SlyScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, PureVesselScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, GrimmScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, GrimmNightmareScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, RadianceScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, PaleCourtDryyaScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, PaleCourtHegemolScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, PaleCourtZemerScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, PaleCourtIsmaScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, PaleCourtWhiteDefenderScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, ChampionsCallScene, StringComparison.Ordinal);

        private static HoGStoragePlan BuildFalseKnightPlan(AllHallownestEnhancedToggleSnapshot snapshot, int? hp)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    return HoGStoragePlan.Final(CreateBucket("HoG AHE+", FalseKnightFolder, "HoG AHE+"), hp);
                }

                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE", FalseKnightFolder, "HoG AHE"), FalseKnightScene);
                }

                return hp.Value > 260
                    ? HoGStoragePlan.Final(CreateBucket("HoG AHE", FalseKnightFolder, "HoG AHE"), hp, FalseKnightScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", FalseKnightFolder, "Other"), hp, FalseKnightScene);
            }

            if (!main)
            {
                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG", FalseKnightFolder, "HoG"), FalseKnightScene);
                }

                return hp.Value > 260
                    ? HoGStoragePlan.Final(CreateBucket("HoG", FalseKnightFolder, "HoG"), hp, FalseKnightScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", FalseKnightFolder, "Other"), hp, FalseKnightScene);
            }

            if (!boss || !monsters)
            {
                return HoGStoragePlan.Final(CreateBucket("Other", FalseKnightFolder, "Other"), hp);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", FalseKnightFolder, "Other"), hp);
        }

        private static HoGStoragePlan BuildFailedChampionPlan(AllHallownestEnhancedToggleSnapshot snapshot, int? hp)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    if (!hp.HasValue)
                    {
                        return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE+", FailedChampionFolder, "HoG AHE+"), FailedChampionScene);
                    }

                    return hp.Value > 550
                        ? HoGStoragePlan.Final(CreateBucket("HoG AHE+", FailedChampionFolder, "HoG AHE+"), hp, FailedChampionScene)
                        : HoGStoragePlan.Final(CreateBucket("Other", FailedChampionFolder, "Other"), hp, FailedChampionScene);
                }

                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE", FailedChampionFolder, "HoG AHE"), FailedChampionScene);
                }

                return hp.Value > 360
                    ? HoGStoragePlan.Final(CreateBucket("HoG AHE", FailedChampionFolder, "HoG AHE"), hp, FailedChampionScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", FailedChampionFolder, "Other"), hp, FailedChampionScene);
            }

            if (!main)
            {
                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG", FailedChampionFolder, "HoG"), FailedChampionScene);
                }

                return hp.Value > 360
                    ? HoGStoragePlan.Final(CreateBucket("HoG", FailedChampionFolder, "HoG"), hp, FailedChampionScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", FailedChampionFolder, "Other"), hp, FailedChampionScene);
            }

                return HoGStoragePlan.Final(CreateBucket("Other", FailedChampionFolder, "Other"), hp);
        }

        private static HoGStoragePlan BuildHornet1Plan(AllHallownestEnhancedToggleSnapshot snapshot, int? hp)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    if (!hp.HasValue)
                    {
                        return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE+", Hornet1Folder, "HoG AHE+"), Hornet1Scene);
                    }

                    return hp.Value > 900
                        ? HoGStoragePlan.Final(CreateBucket("HoG AHE+", Hornet1Folder, "HoG AHE+"), hp, Hornet1Scene)
                        : HoGStoragePlan.Final(CreateBucket("Other", Hornet1Folder, "Other"), hp, Hornet1Scene);
                }

                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE", Hornet1Folder, "HoG AHE"), Hornet1Scene);
                }

                return hp.Value > 900
                    ? HoGStoragePlan.Final(CreateBucket("HoG AHE", Hornet1Folder, "HoG AHE"), hp, Hornet1Scene)
                    : HoGStoragePlan.Final(CreateBucket("Other", Hornet1Folder, "Other"), hp, Hornet1Scene);
            }

            if (!main)
            {
                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG", Hornet1Folder, "HoG"), Hornet1Scene);
                }

                return hp.Value > 900
                    ? HoGStoragePlan.Final(CreateBucket("HoG", Hornet1Folder, "HoG"), hp, Hornet1Scene)
                    : HoGStoragePlan.Final(CreateBucket("Other", Hornet1Folder, "Other"), hp, Hornet1Scene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", Hornet1Folder, "Other"), hp, Hornet1Scene);
        }

        private static HoGStoragePlan BuildHornet2Plan(AllHallownestEnhancedToggleSnapshot snapshot, int? hp)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    if (!hp.HasValue)
                    {
                        return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE+", Hornet2Folder, "HoG AHE+"), Hornet2Scene);
                    }

                    return HoGStoragePlan.Final(CreateBucket("HoG AHE+", Hornet2Folder, "HoG AHE+"), hp, Hornet2Scene);
                }

                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE", Hornet2Folder, "HoG AHE"), Hornet2Scene);
                }

                return hp.Value > 800
                    ? HoGStoragePlan.Final(CreateBucket("HoG AHE", Hornet2Folder, "HoG AHE"), hp, Hornet2Scene)
                    : HoGStoragePlan.Final(CreateBucket("Other", Hornet2Folder, "Other"), hp, Hornet2Scene);
            }

            if (!main)
            {
                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG", Hornet2Folder, "HoG"), Hornet2Scene);
                }

                return hp.Value > 800
                    ? HoGStoragePlan.Final(CreateBucket("HoG", Hornet2Folder, "HoG"), hp, Hornet2Scene)
                    : HoGStoragePlan.Final(CreateBucket("Other", Hornet2Folder, "Other"), hp, Hornet2Scene);
            }

            if (!boss || !monsters)
            {
                return HoGStoragePlan.Final(CreateBucket("Other", Hornet2Folder, "Other"), hp, Hornet2Scene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", Hornet2Folder, "Other"), hp, Hornet2Scene);
        }

        private static HoGStoragePlan BuildMegaMossPlan(AllHallownestEnhancedToggleSnapshot snapshot, int? hp)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    if (!hp.HasValue)
                    {
                        return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE+", MegaMossFolder, "HoG AHE+"), MegaMossScene);
                    }

                    return hp.Value > 600
                        ? HoGStoragePlan.Final(CreateBucket("HoG AHE+", MegaMossFolder, "HoG AHE+"), hp, MegaMossScene)
                        : HoGStoragePlan.Final(CreateBucket("Other", MegaMossFolder, "Other"), hp, MegaMossScene);
                }

                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE", MegaMossFolder, "HoG AHE"), MegaMossScene);
                }

                return hp.Value > 480
                    ? HoGStoragePlan.Final(CreateBucket("HoG AHE", MegaMossFolder, "HoG AHE"), hp, MegaMossScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", MegaMossFolder, "Other"), hp, MegaMossScene);
            }

            if (!main)
            {
                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG", MegaMossFolder, "HoG"), MegaMossScene);
                }

                return hp.Value > 480
                    ? HoGStoragePlan.Final(CreateBucket("HoG", MegaMossFolder, "HoG"), hp, MegaMossScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", MegaMossFolder, "Other"), hp, MegaMossScene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", MegaMossFolder, "Other"), hp, MegaMossScene);
        }

        private static HoGStoragePlan BuildFlukemarmPlan(AllHallownestEnhancedToggleSnapshot snapshot, int? hp)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    if (!hp.HasValue)
                    {
                        return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE+", FlukemarmFolder, "HoG AHE+"), FlukemarmScene);
                    }

                    return hp.Value > 800
                        ? HoGStoragePlan.Final(CreateBucket("HoG AHE+", FlukemarmFolder, "HoG AHE+"), hp, FlukemarmScene)
                        : HoGStoragePlan.Final(CreateBucket("Other", FlukemarmFolder, "Other"), hp, FlukemarmScene);
                }

                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE", FlukemarmFolder, "HoG AHE"), FlukemarmScene);
                }

                return hp.Value > 500
                    ? HoGStoragePlan.Final(CreateBucket("HoG AHE", FlukemarmFolder, "HoG AHE"), hp, FlukemarmScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", FlukemarmFolder, "Other"), hp, FlukemarmScene);
            }

            if (!main)
            {
                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG", FlukemarmFolder, "HoG"), FlukemarmScene);
                }

                return hp.Value > 500
                    ? HoGStoragePlan.Final(CreateBucket("HoG", FlukemarmFolder, "HoG"), hp, FlukemarmScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", FlukemarmFolder, "Other"), hp, FlukemarmScene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", FlukemarmFolder, "Other"), hp, FlukemarmScene);
        }

        private static HoGStoragePlan BuildMantisLordsPlan(AllHallownestEnhancedToggleSnapshot snapshot, int? hp)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    if (!hp.HasValue)
                    {
                        return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE+", MantisLordsFolder, "HoG AHE+"), MantisLordsScene);
                    }

                    return hp.Value > 500
                        ? HoGStoragePlan.Final(CreateBucket("HoG AHE+", MantisLordsFolder, "HoG AHE+"), hp, MantisLordsScene)
                        : HoGStoragePlan.Final(CreateBucket("Other", MantisLordsFolder, "Other"), hp, MantisLordsScene);
                }

                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE", MantisLordsFolder, "HoG AHE"), MantisLordsScene);
                }

                return hp.Value > 400
                    ? HoGStoragePlan.Final(CreateBucket("HoG AHE", MantisLordsFolder, "HoG AHE"), hp, MantisLordsScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", MantisLordsFolder, "Other"), hp, MantisLordsScene);
            }

            if (!main)
            {
                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG", MantisLordsFolder, "HoG"), MantisLordsScene);
                }

                return hp.Value > 400
                    ? HoGStoragePlan.Final(CreateBucket("HoG", MantisLordsFolder, "HoG"), hp, MantisLordsScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", MantisLordsFolder, "Other"), hp, MantisLordsScene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", MantisLordsFolder, "Other"), hp, MantisLordsScene);
        }

        private static HoGStoragePlan BuildMantisSistersPlan(AllHallownestEnhancedToggleSnapshot snapshot, int? hp)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    if (!hp.HasValue)
                    {
                        return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE+", MantisSistersFolder, "HoG AHE+"), MantisSistersScene);
                    }

                    return hp.Value > 750
                        ? HoGStoragePlan.Final(CreateBucket("HoG AHE+", MantisSistersFolder, "HoG AHE+"), hp, MantisSistersScene)
                        : HoGStoragePlan.Final(CreateBucket("Other", MantisSistersFolder, "Other"), hp, MantisSistersScene);
                }

                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE", MantisSistersFolder, "HoG AHE"), MantisSistersScene);
                }

                return hp.Value > 750
                    ? HoGStoragePlan.Final(CreateBucket("HoG AHE", MantisSistersFolder, "HoG AHE"), hp, MantisSistersScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", MantisSistersFolder, "Other"), hp, MantisSistersScene);
            }

            if (!main)
            {
                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG", MantisSistersFolder, "HoG"), MantisSistersScene);
                }

                return hp.Value > 750
                    ? HoGStoragePlan.Final(CreateBucket("HoG", MantisSistersFolder, "HoG"), hp, MantisSistersScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", MantisSistersFolder, "Other"), hp, MantisSistersScene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", MantisSistersFolder, "Other"), hp, MantisSistersScene);
        }

        private static HoGStoragePlan BuildOblobblesPlan(AllHallownestEnhancedToggleSnapshot snapshot, int? hp)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    if (!hp.HasValue)
                    {
                        return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE+", OblobblesFolder, "HoG AHE+"), OblobblesScene);
                    }

                    return hp.Value > 700
                        ? HoGStoragePlan.Final(CreateBucket("HoG AHE+", OblobblesFolder, "HoG AHE+"), hp, OblobblesScene)
                        : HoGStoragePlan.Final(CreateBucket("Other", OblobblesFolder, "Other"), hp, OblobblesScene);
                }

                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE", OblobblesFolder, "HoG AHE"), OblobblesScene);
                }

                return hp.Value > 650
                    ? HoGStoragePlan.Final(CreateBucket("HoG AHE", OblobblesFolder, "HoG AHE"), hp, OblobblesScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", OblobblesFolder, "Other"), hp, OblobblesScene);
            }

            if (!main)
            {
                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG", OblobblesFolder, "HoG"), OblobblesScene);
                }

                return hp.Value > 650
                    ? HoGStoragePlan.Final(CreateBucket("HoG", OblobblesFolder, "HoG"), hp, OblobblesScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", OblobblesFolder, "Other"), hp, OblobblesScene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", OblobblesFolder, "Other"), hp, OblobblesScene);
        }

        private static HoGStoragePlan BuildHiveKnightPlan(AllHallownestEnhancedToggleSnapshot snapshot, int? hp)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    return HoGStoragePlan.Final(CreateBucket("HoG AHE+", HiveKnightFolder, "HoG AHE+"), hp, HiveKnightScene);
                }

                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE", HiveKnightFolder, "HoG AHE"), HiveKnightScene);
                }

                return hp.Value > 850
                    ? HoGStoragePlan.Final(CreateBucket("HoG AHE", HiveKnightFolder, "HoG AHE"), hp, HiveKnightScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", HiveKnightFolder, "Other"), hp, HiveKnightScene);
            }

            if (!main)
            {
                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG", HiveKnightFolder, "HoG"), HiveKnightScene);
                }

                return hp.Value > 850
                    ? HoGStoragePlan.Final(CreateBucket("HoG", HiveKnightFolder, "HoG"), hp, HiveKnightScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", HiveKnightFolder, "Other"), hp, HiveKnightScene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", HiveKnightFolder, "Other"), hp, HiveKnightScene);
        }

        private static HoGStoragePlan BuildBrokenVesselPlan(AllHallownestEnhancedToggleSnapshot snapshot, int? hp)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    if (!hp.HasValue)
                    {
                        return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE+", BrokenVesselFolder, "HoG AHE+"), BrokenVesselScene);
                    }

                    return hp.Value > 900
                        ? HoGStoragePlan.Final(CreateBucket("HoG AHE+", BrokenVesselFolder, "HoG AHE+"), hp, BrokenVesselScene)
                        : HoGStoragePlan.Final(CreateBucket("Other", BrokenVesselFolder, "Other"), hp, BrokenVesselScene);
                }

                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE", BrokenVesselFolder, "HoG AHE"), BrokenVesselScene);
                }

                return hp.Value > 700
                    ? HoGStoragePlan.Final(CreateBucket("HoG AHE", BrokenVesselFolder, "HoG AHE"), hp, BrokenVesselScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", BrokenVesselFolder, "Other"), hp, BrokenVesselScene);
            }

            if (!main)
            {
                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG", BrokenVesselFolder, "HoG"), BrokenVesselScene);
                }

                return hp.Value > 700
                    ? HoGStoragePlan.Final(CreateBucket("HoG", BrokenVesselFolder, "HoG"), hp, BrokenVesselScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", BrokenVesselFolder, "Other"), hp, BrokenVesselScene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", BrokenVesselFolder, "Other"), hp, BrokenVesselScene);
        }

        private static HoGStoragePlan BuildLostKinPlan(AllHallownestEnhancedToggleSnapshot snapshot, int? hp)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    if (!hp.HasValue)
                    {
                        return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE+", LostKinFolder, "HoG AHE+"), LostKinScene);
                    }

                    return hp.Value > 1400
                        ? HoGStoragePlan.Final(CreateBucket("HoG AHE+", LostKinFolder, "HoG AHE+"), hp, LostKinScene)
                        : HoGStoragePlan.Final(CreateBucket("Other", LostKinFolder, "Other"), hp, LostKinScene);
                }

                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE", LostKinFolder, "HoG AHE"), LostKinScene);
                }

                return hp.Value > 1200
                    ? HoGStoragePlan.Final(CreateBucket("HoG AHE", LostKinFolder, "HoG AHE"), hp, LostKinScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", LostKinFolder, "Other"), hp, LostKinScene);
            }

            if (!main)
            {
                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG", LostKinFolder, "HoG"), LostKinScene);
                }

                return hp.Value > 1200
                    ? HoGStoragePlan.Final(CreateBucket("HoG", LostKinFolder, "HoG"), hp, LostKinScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", LostKinFolder, "Other"), hp, LostKinScene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", LostKinFolder, "Other"), hp, LostKinScene);
        }

        private static HoGStoragePlan BuildNoskVPlan(AllHallownestEnhancedToggleSnapshot snapshot)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    return HoGStoragePlan.Final(CreateBucket("HoG AHE+", NoskFolder, "HoG AHE+"), null, NoskVScene);
                }

                return HoGStoragePlan.Final(CreateBucket("HoG AHE", NoskFolder, "HoG AHE"), null, NoskVScene);
            }

            if (!main)
            {
                return HoGStoragePlan.Final(CreateBucket("HoG", NoskFolder, "HoG"), null, NoskVScene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", NoskFolder, "Other"), null, NoskVScene);
        }

        private static HoGStoragePlan BuildNoskHornetPlan(AllHallownestEnhancedToggleSnapshot snapshot, int? hp)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    if (!hp.HasValue)
                    {
                        return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE+", NoskHornetFolder, "HoG AHE+"), NoskHornetScene);
                    }

                    return hp.Value > 850
                        ? HoGStoragePlan.Final(CreateBucket("HoG AHE+", NoskHornetFolder, "HoG AHE+"), hp, NoskHornetScene)
                        : HoGStoragePlan.Final(CreateBucket("Other", NoskHornetFolder, "Other"), hp, NoskHornetScene);
                }

                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE", NoskHornetFolder, "HoG AHE"), NoskHornetScene);
                }

                return hp.Value > 750
                    ? HoGStoragePlan.Final(CreateBucket("HoG AHE", NoskHornetFolder, "HoG AHE"), hp, NoskHornetScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", NoskHornetFolder, "Other"), hp, NoskHornetScene);
            }

            if (!main)
            {
                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG", NoskHornetFolder, "HoG"), NoskHornetScene);
                }

                return hp.Value > 750
                    ? HoGStoragePlan.Final(CreateBucket("HoG", NoskHornetFolder, "HoG"), hp, NoskHornetScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", NoskHornetFolder, "Other"), hp, NoskHornetScene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", NoskHornetFolder, "Other"), hp, NoskHornetScene);
        }

        private static HoGStoragePlan BuildCollectorVPlan(AllHallownestEnhancedToggleSnapshot snapshot)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    return HoGStoragePlan.Final(CreateBucket("HoG AHE+", CollectorFolder, "HoG AHE+"), null, CollectorVScene);
                }

                return HoGStoragePlan.Final(CreateBucket("HoG AHE", CollectorFolder, "HoG AHE"), null, CollectorVScene);
            }

            if (!main)
            {
                return HoGStoragePlan.Final(CreateBucket("HoG", CollectorFolder, "HoG"), null, CollectorVScene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", CollectorFolder, "Other"), null, CollectorVScene);
        }

        private static HoGStoragePlan BuildGodTamerPlan(AllHallownestEnhancedToggleSnapshot snapshot, int? hp)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    return HoGStoragePlan.Final(CreateBucket("HoG AHE+", GodTamerFolder, "HoG AHE+"), hp, GodTamerScene);
                }

                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE", GodTamerFolder, "HoG AHE"), GodTamerScene);
                }

                return hp.Value > 750
                    ? HoGStoragePlan.Final(CreateBucket("HoG AHE", GodTamerFolder, "HoG AHE"), hp, GodTamerScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", GodTamerFolder, "Other"), hp, GodTamerScene);
            }

            if (!main)
            {
                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG", GodTamerFolder, "HoG"), GodTamerScene);
                }

                return hp.Value > 750
                    ? HoGStoragePlan.Final(CreateBucket("HoG", GodTamerFolder, "HoG"), hp, GodTamerScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", GodTamerFolder, "Other"), hp, GodTamerScene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", GodTamerFolder, "Other"), hp, GodTamerScene);
        }

        private static HoGStoragePlan BuildCrystalGuardianPlan(AllHallownestEnhancedToggleSnapshot snapshot, int? hp)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    if (!hp.HasValue)
                    {
                        return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE+", CrystalGuardianFolder, "HoG AHE+"), CrystalGuardianScene);
                    }

                    return hp.Value > 650
                        ? HoGStoragePlan.Final(CreateBucket("HoG AHE+", CrystalGuardianFolder, "HoG AHE+"), hp, CrystalGuardianScene)
                        : HoGStoragePlan.Final(CreateBucket("Other", CrystalGuardianFolder, "Other"), hp, CrystalGuardianScene);
                }

                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE", CrystalGuardianFolder, "HoG AHE"), CrystalGuardianScene);
                }

                return hp.Value > 650
                    ? HoGStoragePlan.Final(CreateBucket("HoG AHE", CrystalGuardianFolder, "HoG AHE"), hp, CrystalGuardianScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", CrystalGuardianFolder, "Other"), hp, CrystalGuardianScene);
            }

            if (!main)
            {
                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG", CrystalGuardianFolder, "HoG"), CrystalGuardianScene);
                }

                return hp.Value > 650
                    ? HoGStoragePlan.Final(CreateBucket("HoG", CrystalGuardianFolder, "HoG"), hp, CrystalGuardianScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", CrystalGuardianFolder, "Other"), hp, CrystalGuardianScene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", CrystalGuardianFolder, "Other"), hp, CrystalGuardianScene);
        }

        private static HoGStoragePlan BuildCrystalGuardian2Plan(AllHallownestEnhancedToggleSnapshot snapshot, int? hp)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    if (!hp.HasValue)
                    {
                        return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE+", CrystalGuardian2Folder, "HoG AHE+"), CrystalGuardian2Scene);
                    }

                    return hp.Value > 900
                        ? HoGStoragePlan.Final(CreateBucket("HoG AHE+", CrystalGuardian2Folder, "HoG AHE+"), hp, CrystalGuardian2Scene)
                        : HoGStoragePlan.Final(CreateBucket("Other", CrystalGuardian2Folder, "Other"), hp, CrystalGuardian2Scene);
                }

                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE", CrystalGuardian2Folder, "HoG AHE"), CrystalGuardian2Scene);
                }

                return hp.Value > 650
                    ? HoGStoragePlan.Final(CreateBucket("HoG AHE", CrystalGuardian2Folder, "HoG AHE"), hp, CrystalGuardian2Scene)
                    : HoGStoragePlan.Final(CreateBucket("Other", CrystalGuardian2Folder, "Other"), hp, CrystalGuardian2Scene);
            }

            if (!main)
            {
                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG", CrystalGuardian2Folder, "HoG"), CrystalGuardian2Scene);
                }

                return hp.Value > 650
                    ? HoGStoragePlan.Final(CreateBucket("HoG", CrystalGuardian2Folder, "HoG"), hp, CrystalGuardian2Scene)
                    : HoGStoragePlan.Final(CreateBucket("Other", CrystalGuardian2Folder, "Other"), hp, CrystalGuardian2Scene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", CrystalGuardian2Folder, "Other"), hp, CrystalGuardian2Scene);
        }

        private static HoGStoragePlan BuildUumuuVPlan(AllHallownestEnhancedToggleSnapshot snapshot)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    return HoGStoragePlan.Final(CreateBucket("HoG AHE+", UumuuFolder, "HoG AHE+"), null, UumuuVScene);
                }

                return HoGStoragePlan.Final(CreateBucket("HoG AHE", UumuuFolder, "HoG AHE"), null, UumuuVScene);
            }

            if (!main)
            {
                return HoGStoragePlan.Final(CreateBucket("HoG", UumuuFolder, "HoG"), null, UumuuVScene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", UumuuFolder, "Other"), null, UumuuVScene);
        }

        private static HoGStoragePlan BuildTraitorLordPlan(AllHallownestEnhancedToggleSnapshot snapshot, int? hp)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    return HoGStoragePlan.Final(CreateBucket("HoG AHE+", TraitorLordFolder, "HoG AHE+"), hp, TraitorLordScene);
                }

                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE", TraitorLordFolder, "HoG AHE"), TraitorLordScene);
                }

                return hp.Value > 800
                    ? HoGStoragePlan.Final(CreateBucket("HoG AHE", TraitorLordFolder, "HoG AHE"), hp, TraitorLordScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", TraitorLordFolder, "Other"), hp, TraitorLordScene);
            }

            if (!main)
            {
                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG", TraitorLordFolder, "HoG"), TraitorLordScene);
                }

                return hp.Value > 800
                    ? HoGStoragePlan.Final(CreateBucket("HoG", TraitorLordFolder, "HoG"), hp, TraitorLordScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", TraitorLordFolder, "Other"), hp, TraitorLordScene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", TraitorLordFolder, "Other"), hp, TraitorLordScene);
        }

        private static HoGStoragePlan BuildGreyPrinceZotePlan(AllHallownestEnhancedToggleSnapshot snapshot)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    return HoGStoragePlan.Final(CreateBucket("HoG AHE+", GreyPrinceZoteFolder, "HoG AHE+"), null, GreyPrinceZoteScene);
                }

                return HoGStoragePlan.Final(CreateBucket("HoG AHE", GreyPrinceZoteFolder, "HoG AHE"), null, GreyPrinceZoteScene);
            }

            if (!main)
            {
                return HoGStoragePlan.Final(CreateBucket("HoG", GreyPrinceZoteFolder, "HoG"), null, GreyPrinceZoteScene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", GreyPrinceZoteFolder, "Other"), null, GreyPrinceZoteScene);
        }

        private static HoGStoragePlan BuildMageKnightVPlan(AllHallownestEnhancedToggleSnapshot snapshot, int? hp)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    if (!hp.HasValue)
                    {
                        return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE+", MageKnightFolder, "HoG AHE+"), MageKnightVScene);
                    }

                    return hp.Value > 850
                        ? HoGStoragePlan.Final(CreateBucket("HoG AHE+", MageKnightFolder, "HoG AHE+"), hp, MageKnightVScene)
                        : HoGStoragePlan.Final(CreateBucket("Other", MageKnightFolder, "Other"), hp, MageKnightVScene);
                }

                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE", MageKnightFolder, "HoG AHE"), MageKnightVScene);
                }

                return hp.Value > 750
                    ? HoGStoragePlan.Final(CreateBucket("HoG AHE", MageKnightFolder, "HoG AHE"), hp, MageKnightVScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", MageKnightFolder, "Other"), hp, MageKnightVScene);
            }

            if (!main)
            {
                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG", MageKnightFolder, "HoG"), MageKnightVScene);
                }

                return hp.Value > 750
                    ? HoGStoragePlan.Final(CreateBucket("HoG", MageKnightFolder, "HoG"), hp, MageKnightVScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", MageKnightFolder, "Other"), hp, MageKnightVScene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", MageKnightFolder, "Other"), hp, MageKnightVScene);
        }

        private static HoGStoragePlan BuildSoulMasterPlan(AllHallownestEnhancedToggleSnapshot snapshot, int? hp)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    if (!hp.HasValue)
                    {
                        return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE+", SoulMasterFolder, "HoG AHE+"), SoulMasterScene);
                    }

                    return hp.Value > 800
                        ? HoGStoragePlan.Final(CreateBucket("HoG AHE+", SoulMasterFolder, "HoG AHE+"), hp, SoulMasterScene)
                        : HoGStoragePlan.Final(CreateBucket("Other", SoulMasterFolder, "Other"), hp, SoulMasterScene);
                }

                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE", SoulMasterFolder, "HoG AHE"), SoulMasterScene);
                }

                return hp.Value > 600
                    ? HoGStoragePlan.Final(CreateBucket("HoG AHE", SoulMasterFolder, "HoG AHE"), hp, SoulMasterScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", SoulMasterFolder, "Other"), hp, SoulMasterScene);
            }

            if (!main)
            {
                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG", SoulMasterFolder, "HoG"), SoulMasterScene);
                }

                return hp.Value > 600
                    ? HoGStoragePlan.Final(CreateBucket("HoG", SoulMasterFolder, "HoG"), hp, SoulMasterScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", SoulMasterFolder, "Other"), hp, SoulMasterScene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", SoulMasterFolder, "Other"), hp, SoulMasterScene);
        }

        private static HoGStoragePlan BuildSoulTyrantPlan(AllHallownestEnhancedToggleSnapshot snapshot, int? hp)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    if (!hp.HasValue)
                    {
                        return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE+", SoulTyrantFolder, "HoG AHE+"), SoulTyrantScene);
                    }

                    return hp.Value > 1100
                        ? HoGStoragePlan.Final(CreateBucket("HoG AHE+", SoulTyrantFolder, "HoG AHE+"), hp, SoulTyrantScene)
                        : HoGStoragePlan.Final(CreateBucket("Other", SoulTyrantFolder, "Other"), hp, SoulTyrantScene);
                }

                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE", SoulTyrantFolder, "HoG AHE"), SoulTyrantScene);
                }

                return hp.Value > 900
                    ? HoGStoragePlan.Final(CreateBucket("HoG AHE", SoulTyrantFolder, "HoG AHE"), hp, SoulTyrantScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", SoulTyrantFolder, "Other"), hp, SoulTyrantScene);
            }

            if (!main)
            {
                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG", SoulTyrantFolder, "HoG"), SoulTyrantScene);
                }

                return hp.Value > 900
                    ? HoGStoragePlan.Final(CreateBucket("HoG", SoulTyrantFolder, "HoG"), hp, SoulTyrantScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", SoulTyrantFolder, "Other"), hp, SoulTyrantScene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", SoulTyrantFolder, "Other"), hp, SoulTyrantScene);
        }

        private static HoGStoragePlan BuildDungDefenderPlan(AllHallownestEnhancedToggleSnapshot snapshot, int? hp)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    if (!hp.HasValue)
                    {
                        return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE+", DungDefenderFolder, "HoG AHE+"), DungDefenderScene);
                    }

                    return hp.Value > 1000
                        ? HoGStoragePlan.Final(CreateBucket("HoG AHE+", DungDefenderFolder, "HoG AHE+"), hp, DungDefenderScene)
                        : HoGStoragePlan.Final(CreateBucket("Other", DungDefenderFolder, "Other"), hp, DungDefenderScene);
                }

                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE", DungDefenderFolder, "HoG AHE"), DungDefenderScene);
                }

                return hp.Value > 800
                    ? HoGStoragePlan.Final(CreateBucket("HoG AHE", DungDefenderFolder, "HoG AHE"), hp, DungDefenderScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", DungDefenderFolder, "Other"), hp, DungDefenderScene);
            }

            if (!main)
            {
                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG", DungDefenderFolder, "HoG"), DungDefenderScene);
                }

                return hp.Value > 800
                    ? HoGStoragePlan.Final(CreateBucket("HoG", DungDefenderFolder, "HoG"), hp, DungDefenderScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", DungDefenderFolder, "Other"), hp, DungDefenderScene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", DungDefenderFolder, "Other"), hp, DungDefenderScene);
        }

        private static HoGStoragePlan BuildWhiteDefenderPlan(AllHallownestEnhancedToggleSnapshot snapshot)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    return HoGStoragePlan.Final(CreateBucket("HoG AHE+", WhiteDefenderFolder, "HoG AHE+"), null, WhiteDefenderScene);
                }

                return HoGStoragePlan.Final(CreateBucket("HoG AHE", WhiteDefenderFolder, "HoG AHE"), null, WhiteDefenderScene);
            }

            if (!main)
            {
                return HoGStoragePlan.Final(CreateBucket("HoG", WhiteDefenderFolder, "HoG"), null, WhiteDefenderScene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", WhiteDefenderFolder, "Other"), null, WhiteDefenderScene);
        }

        private static HoGStoragePlan BuildWatcherKnightsPlan(AllHallownestEnhancedToggleSnapshot snapshot, int? hp)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    if (!hp.HasValue)
                    {
                        return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE+", WatcherKnightsFolder, "HoG AHE+"), WatcherKnightsScene);
                    }

                    return hp.Value > 500
                        ? HoGStoragePlan.Final(CreateBucket("HoG AHE+", WatcherKnightsFolder, "HoG AHE+"), hp, WatcherKnightsScene)
                        : HoGStoragePlan.Final(CreateBucket("Other", WatcherKnightsFolder, "Other"), hp, WatcherKnightsScene);
                }

                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE", WatcherKnightsFolder, "HoG AHE"), WatcherKnightsScene);
                }

                return hp.Value > 350
                    ? HoGStoragePlan.Final(CreateBucket("HoG AHE", WatcherKnightsFolder, "HoG AHE"), hp, WatcherKnightsScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", WatcherKnightsFolder, "Other"), hp, WatcherKnightsScene);
            }

            if (!main)
            {
                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG", WatcherKnightsFolder, "HoG"), WatcherKnightsScene);
                }

                return hp.Value > 350
                    ? HoGStoragePlan.Final(CreateBucket("HoG", WatcherKnightsFolder, "HoG"), hp, WatcherKnightsScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", WatcherKnightsFolder, "Other"), hp, WatcherKnightsScene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", WatcherKnightsFolder, "Other"), hp, WatcherKnightsScene);
        }

        private static HoGStoragePlan BuildNoEyesVPlan(AllHallownestEnhancedToggleSnapshot snapshot)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    return HoGStoragePlan.Final(CreateBucket("HoG AHE+", NoEyesFolder, "HoG AHE+"), null, NoEyesVScene);
                }

                return HoGStoragePlan.Final(CreateBucket("HoG AHE", NoEyesFolder, "HoG AHE"), null, NoEyesVScene);
            }

            if (!main)
            {
                return HoGStoragePlan.Final(CreateBucket("HoG", NoEyesFolder, "HoG"), null, NoEyesVScene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", NoEyesFolder, "Other"), null, NoEyesVScene);
        }

        private static HoGStoragePlan BuildMarmuVPlan(AllHallownestEnhancedToggleSnapshot snapshot)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    return HoGStoragePlan.Final(CreateBucket("HoG AHE+", MarmuFolder, "HoG AHE+"), null, MarmuVScene);
                }

                return HoGStoragePlan.Final(CreateBucket("HoG AHE", MarmuFolder, "HoG AHE"), null, MarmuVScene);
            }

            if (!main)
            {
                return HoGStoragePlan.Final(CreateBucket("HoG", MarmuFolder, "HoG"), null, MarmuVScene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", MarmuFolder, "Other"), null, MarmuVScene);
        }

        private static HoGStoragePlan BuildXeroVPlan(AllHallownestEnhancedToggleSnapshot snapshot)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    return HoGStoragePlan.Final(CreateBucket("HoG AHE+", XeroFolder, "HoG AHE+"), null, XeroVScene);
                }

                return HoGStoragePlan.Final(CreateBucket("HoG AHE", XeroFolder, "HoG AHE"), null, XeroVScene);
            }

            if (!main)
            {
                return HoGStoragePlan.Final(CreateBucket("HoG", XeroFolder, "HoG"), null, XeroVScene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", XeroFolder, "Other"), null, XeroVScene);
        }

        private static HoGStoragePlan BuildMarkothVPlan(AllHallownestEnhancedToggleSnapshot snapshot)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    return HoGStoragePlan.Final(CreateBucket("HoG AHE+", MarkothFolder, "HoG AHE+"), null, MarkothVScene);
                }

                return HoGStoragePlan.Final(CreateBucket("HoG AHE", MarkothFolder, "HoG AHE"), null, MarkothVScene);
            }

            if (!main)
            {
                return HoGStoragePlan.Final(CreateBucket("HoG", MarkothFolder, "HoG"), null, MarkothVScene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", MarkothFolder, "Other"), null, MarkothVScene);
        }

        private static HoGStoragePlan BuildGalienPlan(AllHallownestEnhancedToggleSnapshot snapshot, int? hp)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    return HoGStoragePlan.Final(CreateBucket("HoG AHE+", GalienFolder, "HoG AHE+"), hp, GalienScene);
                }

                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE", GalienFolder, "HoG AHE"), GalienScene);
                }

                return hp.Value > 650
                    ? HoGStoragePlan.Final(CreateBucket("HoG AHE", GalienFolder, "HoG AHE"), hp, GalienScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", GalienFolder, "Other"), hp, GalienScene);
            }

            if (!main)
            {
                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG", GalienFolder, "HoG"), GalienScene);
                }

                return hp.Value > 650
                    ? HoGStoragePlan.Final(CreateBucket("HoG", GalienFolder, "HoG"), hp, GalienScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", GalienFolder, "Other"), hp, GalienScene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", GalienFolder, "Other"), hp, GalienScene);
        }

        private static HoGStoragePlan BuildGorbVPlan(AllHallownestEnhancedToggleSnapshot snapshot)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    return HoGStoragePlan.Final(CreateBucket("HoG AHE+", GorbFolder, "HoG AHE+"), null, GorbVScene);
                }

                return HoGStoragePlan.Final(CreateBucket("HoG AHE", GorbFolder, "HoG AHE"), null, GorbVScene);
            }

            if (!main)
            {
                return HoGStoragePlan.Final(CreateBucket("HoG", GorbFolder, "HoG"), null, GorbVScene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", GorbFolder, "Other"), null, GorbVScene);
        }

        private static HoGStoragePlan BuildHuPlan(AllHallownestEnhancedToggleSnapshot snapshot, int? hp)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    return HoGStoragePlan.Final(CreateBucket("HoG AHE+", HuFolder, "HoG AHE+"), hp, HuScene);
                }

                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE", HuFolder, "HoG AHE"), HuScene);
                }

                return hp.Value > 600
                    ? HoGStoragePlan.Final(CreateBucket("HoG AHE", HuFolder, "HoG AHE"), hp, HuScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", HuFolder, "Other"), hp, HuScene);
            }

            if (!main)
            {
                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG", HuFolder, "HoG"), HuScene);
                }

                return hp.Value > 600
                    ? HoGStoragePlan.Final(CreateBucket("HoG", HuFolder, "HoG"), hp, HuScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", HuFolder, "Other"), hp, HuScene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", HuFolder, "Other"), hp, HuScene);
        }

        private static HoGStoragePlan BuildNailmastersPlan(AllHallownestEnhancedToggleSnapshot snapshot, int? hp)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    if (!hp.HasValue)
                    {
                        return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE+", NailmastersFolder, "HoG AHE+"), NailmastersScene);
                    }
                    return hp.Value >= 800
                        ? HoGStoragePlan.Final(CreateBucket("HoG AHE+", NailmastersFolder, "HoG AHE+"), hp, NailmastersScene)
                        : HoGStoragePlan.Final(CreateBucket("Other", NailmastersFolder, "Other"), hp, NailmastersScene);
                }

                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE", NailmastersFolder, "HoG AHE"), NailmastersScene);
                }
                return hp.Value >= 800
                    ? HoGStoragePlan.Final(CreateBucket("HoG AHE", NailmastersFolder, "HoG AHE"), hp, NailmastersScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", NailmastersFolder, "Other"), hp, NailmastersScene);
            }

            if (!main)
            {
                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG", NailmastersFolder, "HoG"), NailmastersScene);
                }
                return hp.Value >= 800
                    ? HoGStoragePlan.Final(CreateBucket("HoG", NailmastersFolder, "HoG"), hp, NailmastersScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", NailmastersFolder, "Other"), hp, NailmastersScene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", NailmastersFolder, "Other"), hp, NailmastersScene);
        }

        private static HoGStoragePlan BuildPainterPlan(AllHallownestEnhancedToggleSnapshot snapshot, int? hp)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    if (!hp.HasValue)
                    {
                        return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE+", PainterFolder, "HoG AHE+"), PainterScene);
                    }
                    return hp.Value > 1200
                        ? HoGStoragePlan.Final(CreateBucket("HoG AHE+", PainterFolder, "HoG AHE+"), hp, PainterScene)
                        : HoGStoragePlan.Final(CreateBucket("Other", PainterFolder, "Other"), hp, PainterScene);
                }

                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE", PainterFolder, "HoG AHE"), PainterScene);
                }
                return hp.Value > 950
                    ? HoGStoragePlan.Final(CreateBucket("HoG AHE", PainterFolder, "HoG AHE"), hp, PainterScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", PainterFolder, "Other"), hp, PainterScene);
            }

            if (!main)
            {
                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG", PainterFolder, "HoG"), PainterScene);
                }
                return hp.Value > 950
                    ? HoGStoragePlan.Final(CreateBucket("HoG", PainterFolder, "HoG"), hp, PainterScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", PainterFolder, "Other"), hp, PainterScene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", PainterFolder, "Other"), hp, PainterScene);
        }

        private static HoGStoragePlan BuildSlyPlan(AllHallownestEnhancedToggleSnapshot snapshot, int? hp)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    if (!hp.HasValue)
                    {
                        return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE+", SlyFolder, "HoG AHE+"), SlyScene);
                    }

                    return hp.Value > 1000
                        ? HoGStoragePlan.Final(CreateBucket("HoG AHE+", SlyFolder, "HoG AHE+"), hp, SlyScene)
                        : HoGStoragePlan.Final(CreateBucket("Other", SlyFolder, "Other"), hp, SlyScene);
                }

                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE", SlyFolder, "HoG AHE"), SlyScene);
                }

                return hp.Value > 800
                    ? HoGStoragePlan.Final(CreateBucket("HoG AHE", SlyFolder, "HoG AHE"), hp, SlyScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", SlyFolder, "Other"), hp, SlyScene);
            }

            if (!main)
            {
                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG", SlyFolder, "HoG"), SlyScene);
                }

                return hp.Value > 800
                    ? HoGStoragePlan.Final(CreateBucket("HoG", SlyFolder, "HoG"), hp, SlyScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", SlyFolder, "Other"), hp, SlyScene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", SlyFolder, "Other"), hp, SlyScene);
        }
        private static HoGStoragePlan BuildPureVesselPlan(AllHallownestEnhancedToggleSnapshot snapshot, int? hp)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    return HoGStoragePlan.Final(CreateBucket("HoG AHE+", PureVesselFolder, "HoG AHE+"), hp, PureVesselScene);
                }

                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE", PureVesselFolder, "HoG AHE"), PureVesselScene);
                }

                return hp.Value > 1600
                    ? HoGStoragePlan.Final(CreateBucket("HoG AHE", PureVesselFolder, "HoG AHE"), hp, PureVesselScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", PureVesselFolder, "Other"), hp, PureVesselScene);
            }

            if (!main)
            {
                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG", PureVesselFolder, "HoG"), PureVesselScene);
                }

                return hp.Value > 1600
                    ? HoGStoragePlan.Final(CreateBucket("HoG", PureVesselFolder, "HoG"), hp, PureVesselScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", PureVesselFolder, "Other"), hp, PureVesselScene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", PureVesselFolder, "Other"), hp, PureVesselScene);
        }

        private static HoGStoragePlan BuildGrimmPlan(AllHallownestEnhancedToggleSnapshot snapshot)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    return HoGStoragePlan.Final(CreateBucket("HoG AHE+", GrimmFolder, "HoG AHE+"), null, GrimmScene);
                }

                return HoGStoragePlan.Final(CreateBucket("HoG AHE", GrimmFolder, "HoG AHE"), null, GrimmScene);
            }

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    return HoGStoragePlan.Final(CreateBucket("HoG AHE+", GrimmFolder, "HoG AHE+"), null, GrimmScene);
                }

                return HoGStoragePlan.Final(CreateBucket("HoG AHE", GrimmFolder, "HoG AHE"), null, GrimmScene);
            }

            if (!main)
            {
                return HoGStoragePlan.Final(CreateBucket("HoG", GrimmFolder, "HoG"), null, GrimmScene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", GrimmFolder, "Other"), null, GrimmScene);
        }

        private static HoGStoragePlan BuildGrimmNightmarePlan(AllHallownestEnhancedToggleSnapshot snapshot, int? hp)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;

            if (main && boss && monsters)
            {
                if (!originalHp)
                {
                    if (!hp.HasValue)
                    {
                        return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE+", GrimmNightmareFolder, "HoG AHE+"), GrimmNightmareScene);
                    }

                    return hp.Value > 1250
                        ? HoGStoragePlan.Final(CreateBucket("HoG AHE+", GrimmNightmareFolder, "HoG AHE+"), hp, GrimmNightmareScene)
                        : HoGStoragePlan.Final(CreateBucket("Other", GrimmNightmareFolder, "Other"), hp, GrimmNightmareScene);
                }

                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG AHE", GrimmNightmareFolder, "HoG AHE"), GrimmNightmareScene);
                }

                return hp.Value > 1250
                    ? HoGStoragePlan.Final(CreateBucket("HoG AHE", GrimmNightmareFolder, "HoG AHE"), hp, GrimmNightmareScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", GrimmNightmareFolder, "Other"), hp, GrimmNightmareScene);
            }

            if (!main)
            {
                if (!hp.HasValue)
                {
                    return HoGStoragePlan.WaitForHp(CreateBucket("HoG", GrimmNightmareFolder, "HoG"), GrimmNightmareScene);
                }

                return hp.Value > 1250
                    ? HoGStoragePlan.Final(CreateBucket("HoG", GrimmNightmareFolder, "HoG"), hp, GrimmNightmareScene)
                    : HoGStoragePlan.Final(CreateBucket("Other", GrimmNightmareFolder, "Other"), hp, GrimmNightmareScene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", GrimmNightmareFolder, "Other"), hp, GrimmNightmareScene);
        }

        private static HoGStoragePlan BuildRadiancePlan(AllHallownestEnhancedToggleSnapshot snapshot, int? hp)
        {
            bool main = snapshot.MainSwitch;
            bool boss = snapshot.StrengthenAllBoss;
            bool monsters = snapshot.StrengthenAllMonsters;
            bool originalHp = snapshot.OriginalHp;
            bool moreRadiance = snapshot.MoreRadiance;

            if (hp.HasValue)
            {
                if (hp.Value <= 2500 && IsAnyRadianceThreeActive())
                {
                    return HoGStoragePlan.Final(
                        CreateBucket(AnyRadianceRootFolder, AnyRadianceThreeFolder, "ANY RADIANCE 3.0", "ANYRAD 3.0"),
                        hp,
                        RadianceScene);
                }

                if (hp.Value > 4500)
                {
                    if (IsAnyRadianceTwoActive())
                    {
                        return HoGStoragePlan.Final(
                            CreateBucket(AnyRadianceRootFolder, AnyRadianceTwoFolder, "ANY RADIANCE 2.0", "ANYRAD 2.0"),
                            hp,
                            RadianceScene);
                    }

                    return HoGStoragePlan.Final(
                        CreateBucket(AnyRadianceRootFolder, AnyRadianceOneFolder, "ANY RADIANCE 1.0", "ANYRAD 1.0"),
                        hp,
                        RadianceScene);
                }
            }

            if (main && boss && monsters)
            {
                if (moreRadiance)
                {
                    return HoGStoragePlan.Final(CreateBucket("HoG AHE+", RadianceFolder, "HoG AHE+", "MDR"), null, RadianceScene);
                }

                if (!originalHp)
                {
                    return HoGStoragePlan.Final(CreateBucket("HoG AHE+", RadianceFolder, "HoG AHE+"), null, RadianceScene);
                }

                return HoGStoragePlan.Final(CreateBucket("HoG AHE", RadianceFolder, "HoG AHE"), null, RadianceScene);
            }

            if (!main)
            {
                return HoGStoragePlan.Final(CreateBucket("HoG", RadianceFolder, "HoG"), null, RadianceScene);
            }

            return HoGStoragePlan.Final(CreateBucket("Other", RadianceFolder, "Other"), null, RadianceScene);
        }
        private static HoGBucketInfo CreateBucket(string root, string bossFolder, string label, string filePrefix = null) =>
            new HoGBucketInfo(root, bossFolder, label ?? root, filePrefix);

        private static bool IsAnyRadianceTwoActive()
        {
            try
            {
                return ModHooks.GetMod("AnyRadiance") != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsAnyRadianceThreeActive()
        {
            try
            {
                if (ModHooks.GetMod("Any Radiance") == null)
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            return ResolveType("AnyRadiance.LocalSettings") != null;
        }

        private static Type ResolveType(string typeFullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic)
                {
                    continue;
                }

                try
                {
                    Type type = assembly.GetType(typeFullName, throwOnError: false, ignoreCase: false);
                    if (type != null)
                    {
                        return type;
                    }
                }
                catch (ReflectionTypeLoadException ex)
                {
                    Type resolved = ex.Types?.FirstOrDefault(t => t != null && string.Equals(t.FullName, typeFullName, StringComparison.Ordinal));
                    if (resolved != null)
                    {
                        return resolved;
                    }
                }
                catch
                {
                }
            }

            return null;
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
