using System;
using System.Collections.Generic;
using System.IO;

namespace ReplayLogger
{
    internal static class HoGLoggerConditions
    {
        internal const string WorkshopScene = "GG_Workshop";
        internal const string PaleCourtDryyaScene = "gg dryya";
        internal const string PaleCourtHegemolScene = "gg hegemol";
        internal const string PaleCourtZemerScene = "gg zemer";
        internal const string PaleCourtIsmaScene = "gg isma";
        internal const string PaleCourtWhiteDefenderScene = "GG_White_Defender";
        internal const string ChampionsCallScene = "Dream_04_White_Defender";
        internal const string PaleCourtEntryScene = "White_Palace_09";

        private static readonly HashSet<string> TrackedBossRooms = new(StringComparer.Ordinal)
        {
            "GG_Gruz_Mother",
            "GG_Gruz_Mother_V",
            "GG_Vengefly",
            "GG_Vengefly_V",
            "GG_Brooding_Mawlek",
            "GG_Brooding_Mawlek_V",
            "GG_False_Knight",
            "GG_Failed_Champion",
            "GG_Hornet_1",
            "GG_Hornet_2",
            "GG_Mega_Moss_Charger",
            "GG_Flukemarm",
            "GG_Mantis_Lords",
            "GG_Mantis_Lords_V",
            "GG_Oblobbles",
            "GG_Hive_Knight",
            "GG_Broken_Vessel",
            "GG_Lost_Kin",
            "GG_Nosk",
            "GG_Nosk_V",
            "GG_Nosk_Hornet",
            "GG_Collector",
            "GG_Collector_V",
            "GG_God_Tamer",
            "GG_Crystal_Guardian",
            "GG_Crystal_Guardian_2",
            "GG_Uumuu",
            "GG_Uumuu_V",
            "GG_Traitor_Lord",
            "GG_Grey_Prince_Zote",
            "GG_Mage_Knight",
            "GG_Mage_Knight_V",
            "GG_Soul_Master",
            "GG_Soul_Tyrant",
            "GG_Dung_Defender",
            "GG_White_Defender",
            "GG_Watcher_Knights",
            "GG_Ghost_No_Eyes",
            "GG_Ghost_No_Eyes_V",
            "GG_Ghost_Marmu",
            "GG_Ghost_Marmu_V",
            "GG_Ghost_Xero",
            "GG_Ghost_Xero_V",
            "GG_Ghost_Markoth",
            "GG_Ghost_Markoth_V",
            "GG_Ghost_Galien",
            "GG_Ghost_Gorb",
            "GG_Ghost_Gorb_V",
            "GG_Ghost_Hu",
            "GG_Nailmasters",
            "GG_Painter",
            "GG_Sly",
            "GG_Hollow_Knight",
            "GG_Grimm",
            "GG_Grimm_Nightmare",
            "GG_Radiance"
        };

        internal const string DefaultBucket = "HoG";

        internal static bool ShouldStartLogging(string previousScene, string nextScene)
        {
            if (string.IsNullOrEmpty(previousScene) || string.IsNullOrEmpty(nextScene))
            {
                return false;
            }

            if (string.Equals(previousScene, WorkshopScene, StringComparison.Ordinal)
                && TrackedBossRooms.Contains(nextScene))
            {
                return true;
            }

            if (string.Equals(previousScene, PaleCourtEntryScene, StringComparison.Ordinal))
            {
                if (string.Equals(nextScene, PaleCourtDryyaScene, StringComparison.Ordinal) ||
                    string.Equals(nextScene, PaleCourtHegemolScene, StringComparison.Ordinal) ||
                    string.Equals(nextScene, PaleCourtZemerScene, StringComparison.Ordinal) ||
                    string.Equals(nextScene, PaleCourtIsmaScene, StringComparison.Ordinal) ||
                    string.Equals(nextScene, PaleCourtWhiteDefenderScene, StringComparison.Ordinal) ||
                    string.Equals(nextScene, ChampionsCallScene, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool ShouldStopLogging(string activeRoom, string nextScene)
        {
            if (string.IsNullOrEmpty(activeRoom))
            {
                return false;
            }

            if (TrackedBossRooms.Contains(activeRoom))
            {
                return string.IsNullOrEmpty(nextScene)
                    || !string.Equals(nextScene, activeRoom, StringComparison.Ordinal);
            }

            if (string.Equals(activeRoom, PaleCourtDryyaScene, StringComparison.Ordinal) ||
                string.Equals(activeRoom, PaleCourtHegemolScene, StringComparison.Ordinal) ||
                string.Equals(activeRoom, PaleCourtZemerScene, StringComparison.Ordinal) ||
                string.Equals(activeRoom, PaleCourtIsmaScene, StringComparison.Ordinal) ||
                string.Equals(activeRoom, PaleCourtWhiteDefenderScene, StringComparison.Ordinal) ||
                string.Equals(activeRoom, ChampionsCallScene, StringComparison.Ordinal))
            {
                return string.IsNullOrEmpty(nextScene)
                    || !string.Equals(nextScene, activeRoom, StringComparison.Ordinal);
            }

            return false;
        }

        internal static HoGBucketInfo ResolveBucket(string sceneName, AllHallownestEnhancedToggleSnapshot snapshot, string previousScene = null)
        {
            string displayName = GetDisplayName(sceneName);
            HoGBucketInfo fallback = new HoGBucketInfo(DefaultBucket, displayName, DefaultBucket);

            if (string.IsNullOrEmpty(sceneName))
            {
                return fallback;
            }

            bool isPaleCourtEntry = string.Equals(previousScene, PaleCourtEntryScene, StringComparison.Ordinal);

            if (string.Equals(sceneName, PaleCourtDryyaScene, StringComparison.Ordinal))
            {
                return new HoGBucketInfo("Pale Court", "Fierce Dryya", "Pale Court", "Fierce Dryya");
            }

            if (string.Equals(sceneName, PaleCourtHegemolScene, StringComparison.Ordinal))
            {
                return new HoGBucketInfo("Pale Court", "Mighty Hegemol", "Pale Court", "Mighty Hegemol");
            }

            if (string.Equals(sceneName, PaleCourtZemerScene, StringComparison.Ordinal))
            {
                return new HoGBucketInfo("Pale Court", "Mysterious Zemer", "Pale Court", "Mysterious Zemer");
            }

            if (string.Equals(sceneName, PaleCourtIsmaScene, StringComparison.Ordinal))
            {
                return new HoGBucketInfo("Pale Court", "Kindly Isma", "Pale Court", "Kindly Isma");
            }

            if (string.Equals(sceneName, PaleCourtWhiteDefenderScene, StringComparison.Ordinal))
            {
                if (isPaleCourtEntry)
                {
                    return new HoGBucketInfo("Pale Court", "Ogrim & Isma", "Pale Court", "Ogrim & Isma");
                }

                // Treat as standard White Defender when not coming from Pale Court entry
                return new HoGBucketInfo("HoG", "White Defender", "HoG");
            }

            if (string.Equals(sceneName, ChampionsCallScene, StringComparison.Ordinal))
            {
                return new HoGBucketInfo("Pale Court", "Champions Call", "Pale Court", "Champions Call");
            }

            if (string.Equals(sceneName, "GG_Gruz_Mother", StringComparison.Ordinal))
            {
                return new HoGBucketInfo("Other", displayName, "Other");
            }

            if (string.Equals(sceneName, "GG_Vengefly", StringComparison.Ordinal))
            {
                return new HoGBucketInfo("Other", "Vengefly King", "Other");
            }

            if (string.Equals(sceneName, "GG_Brooding_Mawlek", StringComparison.Ordinal))
            {
                return new HoGBucketInfo("Other", "Brooding Mawlek", "Other");
            }

            if (string.Equals(sceneName, "GG_Nosk", StringComparison.Ordinal))
            {
                return new HoGBucketInfo("Other", "Nosk", "Other");
            }

            if (string.Equals(sceneName, "GG_Painter", StringComparison.Ordinal))
            {
                return new HoGBucketInfo("Other", "Paintmaster Sheo", "Other");
            }

            if (string.Equals(sceneName, "GG_Sly", StringComparison.Ordinal))
            {
                return new HoGBucketInfo("Other", "Nailsage Sly", "Other");
            }

            if (string.Equals(sceneName, "GG_Collector", StringComparison.Ordinal))
            {
                return new HoGBucketInfo("Other", "The Collector", "Other");
            }

            if (string.Equals(sceneName, "GG_Uumuu", StringComparison.Ordinal))
            {
                return new HoGBucketInfo("Other", "Uumuu", "Other");
            }

            if (string.Equals(sceneName, "GG_Mage_Knight", StringComparison.Ordinal))
            {
                return new HoGBucketInfo("Other", "Mage Knight", "Other");
            }

            if (string.Equals(sceneName, "GG_Ghost_No_Eyes", StringComparison.Ordinal))
            {
                return new HoGBucketInfo("Other", "No Eyes", "Other");
            }

            if (string.Equals(sceneName, "GG_Ghost_Marmu", StringComparison.Ordinal))
            {
                return new HoGBucketInfo("Other", "Marmu", "Other");
            }

            if (string.Equals(sceneName, "GG_Ghost_Xero", StringComparison.Ordinal))
            {
                return new HoGBucketInfo("Other", "Xero", "Other");
            }

            if (string.Equals(sceneName, "GG_Ghost_Markoth", StringComparison.Ordinal))
            {
                return new HoGBucketInfo("Other", "Markoth", "Other");
            }

            if (string.Equals(sceneName, "GG_Ghost_Gorb", StringComparison.Ordinal))
            {
                return new HoGBucketInfo("Other", "Gorb", "Other");
            }
            if (string.Equals(sceneName, "GG_Hollow_Knight", StringComparison.Ordinal))
            {
                return new HoGBucketInfo("Other", "Pure Vessel", "Other");
            }
            if (string.Equals(sceneName, "GG_Grimm", StringComparison.Ordinal))
            {
                return ResolveAheBucket(sceneName, displayName, snapshot);
            }
            if (string.Equals(sceneName, "GG_Radiance", StringComparison.Ordinal))
            {
                return new HoGBucketInfo("Other", "Absolute Radiance", "Other");
            }
            if (string.Equals(sceneName, "GG_Gruz_Mother_V", StringComparison.Ordinal))
            {
                return ResolveAheBucket("GG_Gruz_Mother_V", displayName, snapshot);
            }

            if (string.Equals(sceneName, "GG_Vengefly_V", StringComparison.Ordinal))
            {
                return ResolveAheBucket("GG_Vengefly_V", "Vengefly King", snapshot);
            }

            if (string.Equals(sceneName, "GG_Brooding_Mawlek_V", StringComparison.Ordinal))
            {
                return ResolveAheBucket("GG_Brooding_Mawlek_V", "Brooding Mawlek", snapshot);
            }

            return fallback;
        }

        private static HoGBucketInfo ResolveAheBucket(string sceneName, string displayName, AllHallownestEnhancedToggleSnapshot snapshot)
        {
            if (!snapshot.Available || !snapshot.MainSwitch)
            {
                return new HoGBucketInfo("HoG", displayName, "HoG");
            }

            if (!snapshot.StrengthenAllBoss || !snapshot.StrengthenAllMonsters)
            {
                return new HoGBucketInfo("Other", displayName, "Other");
            }

            bool coreToggles = snapshot.MainSwitch && snapshot.StrengthenAllBoss && snapshot.StrengthenAllMonsters;

            if (coreToggles && snapshot.OriginalHp)
            {
                return new HoGBucketInfo("HoG AHE", displayName, "HoG AHE");
            }

            if (coreToggles && !snapshot.OriginalHp)
            {
                return new HoGBucketInfo("HoG AHE+", displayName, "HoG AHE+");
            }

            return new HoGBucketInfo(DefaultBucket, displayName, DefaultBucket);
        }

        internal static string GetDisplayName(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                return "HoG";
            }

            string result = sceneName;
            if (result.StartsWith("GG_", StringComparison.Ordinal))
            {
                result = result.Substring(3);
            }

            if (result.EndsWith("_V", StringComparison.Ordinal))
            {
                result = result.Substring(0, result.Length - 2);
            }

            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char invalid in invalidChars)
            {
                result = result.Replace(invalid, '_');
            }

            return string.IsNullOrWhiteSpace(result) ? "HoG" : result;
        }
    }

    internal readonly struct HoGBucketInfo
    {
        public HoGBucketInfo(string rootFolder, string bossFolder, string bucketLabel, string filePrefix = null)
        {
            RootFolder = rootFolder;
            BossFolder = bossFolder;
            BucketLabel = bucketLabel;
            FilePrefix = filePrefix;
        }

        public string RootFolder { get; }
        public string BossFolder { get; }
        public string BucketLabel { get; }
        public string FilePrefix { get; }

        public static HoGBucketInfo CreateDefault(string sceneName) =>
            new HoGBucketInfo(HoGLoggerConditions.DefaultBucket, HoGLoggerConditions.GetDisplayName(sceneName), HoGLoggerConditions.DefaultBucket);
    }
}
