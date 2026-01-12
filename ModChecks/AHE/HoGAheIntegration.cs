using System;
using System.Reflection;

namespace ReplayLogger
{
    internal static class AllHallownestEnhancedIntegration
    {
        private const string ModTypeName = "AllHallownestEnhanced.AllHallownestEnhanced";
        private const string SettingsFieldName = "settings_";
        private const string MainSwitchFieldName = "on";
        private const string BossToggleFieldName = "EnhanceBOSS";
        private const string MonsterToggleFieldName = "EnhanceEnemy";
        private const string OriginalHpFieldName = "originalHp";
        private const string EnhancedRadianceFieldName = "enhanced_2Radiant";

        private static FieldInfo settingsField;
        private static FieldInfo mainSwitchField;
        private static FieldInfo bossField;
        private static FieldInfo monsterField;
        private static FieldInfo originalHpField;
        private static FieldInfo enhancedRadianceField;

        internal static bool AreAllRequiredTogglesEnabled()
        {
            return GetToggleSnapshot().CoreTogglesEnabled;
        }

        internal static AllHallownestEnhancedToggleSnapshot GetToggleSnapshot()
        {
            try
            {
                if (!EnsureFieldHandles())
                {
                return AllHallownestEnhancedToggleSnapshot.Unavailable;
            }

            object settings = settingsField.GetValue(null);
            if (settings == null)
                {
                    return AllHallownestEnhancedToggleSnapshot.Unavailable;
                }

            return new AllHallownestEnhancedToggleSnapshot(
                available: true,
                mainSwitch: GetBool(mainSwitchField, settings),
                strengthenBoss: GetBool(bossField, settings),
                strengthenMonsters: GetBool(monsterField, settings),
                originalHp: GetBool(originalHpField, settings),
                moreRadiance: GetBool(enhancedRadianceField, settings));
            }
            catch (Exception e)
            {
                Modding.Logger.LogWarn($"ReplayLogger: failed to read AHE settings: {e.Message}");
                return AllHallownestEnhancedToggleSnapshot.Unavailable;
            }
        }

        private static bool EnsureFieldHandles()
        {
            if (settingsField != null && mainSwitchField != null && bossField != null && monsterField != null && originalHpField != null)
            {
                return true;
            }

            Type modType = FindType(ModTypeName);
            if (modType == null)
            {
                return false;
            }

            settingsField = modType.GetField(SettingsFieldName, BindingFlags.Public | BindingFlags.Static);
            if (settingsField == null)
            {
                return false;
            }

            Type settingsType = settingsField.FieldType;
            mainSwitchField = settingsType.GetField(MainSwitchFieldName, BindingFlags.Public | BindingFlags.Instance);
            bossField = settingsType.GetField(BossToggleFieldName, BindingFlags.Public | BindingFlags.Instance);
            monsterField = settingsType.GetField(MonsterToggleFieldName, BindingFlags.Public | BindingFlags.Instance);
            originalHpField = settingsType.GetField(OriginalHpFieldName, BindingFlags.Public | BindingFlags.Instance);
            enhancedRadianceField = settingsType.GetField(EnhancedRadianceFieldName, BindingFlags.Public | BindingFlags.Instance);

            return mainSwitchField != null && bossField != null && monsterField != null && originalHpField != null && enhancedRadianceField != null;
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

        private static bool GetBool(FieldInfo field, object instance) =>
            field != null && instance != null && field.GetValue(instance) is bool value && value;
    }

    internal readonly struct AllHallownestEnhancedToggleSnapshot
    {
        public static AllHallownestEnhancedToggleSnapshot Unavailable => new(false, false, false, false, false, false);

        public AllHallownestEnhancedToggleSnapshot(bool available, bool mainSwitch, bool strengthenBoss, bool strengthenMonsters, bool originalHp, bool moreRadiance)
        {
            Available = available;
            MainSwitch = mainSwitch;
            StrengthenAllBoss = strengthenBoss;
            StrengthenAllMonsters = strengthenMonsters;
            OriginalHp = originalHp;
            MoreRadiance = moreRadiance;
        }

        public bool Available { get; }
        public bool MainSwitch { get; }
        public bool StrengthenAllBoss { get; }
        public bool StrengthenAllMonsters { get; }
        public bool OriginalHp { get; }
        public bool MoreRadiance { get; }

        public bool CoreTogglesEnabled =>
            Available && MainSwitch && StrengthenAllBoss && StrengthenAllMonsters;
    }
}
