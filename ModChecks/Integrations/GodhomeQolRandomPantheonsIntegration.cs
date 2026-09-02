using System;
using System.Reflection;

namespace ReplayLogger
{
    internal static class GodhomeQolRandomPantheonsIntegration
    {
        private const string RandomPantheonsTypeName = "GodhomeQoL.Modules.BossChallenge.RandomPantheons";
        private const string TrueBossRushTypeName = "GodhomeQoL.Modules.BossChallenge.TrueBossRush";
        private const string InstanceFieldName = "Instance";

        private static Type randomPantheonsType;
        private static FieldInfo randomPantheonsInstanceField;
        private static readonly FieldInfo[] randomPantheonEnabledFields = new FieldInfo[6];
        private static Type trueBossRushType;
        private static FieldInfo trueBossRushInstanceField;
        private static readonly FieldInfo[] trueBossRushEnabledFields = new FieldInfo[6];

        internal static bool IsPantheonRandomized(int pantheonNumber)
        {
            if (pantheonNumber < 1 || pantheonNumber > 5)
            {
                return false;
            }

            EnsureCache();
            return ReadPantheonToggle(
                pantheonNumber,
                randomPantheonsType,
                randomPantheonsInstanceField,
                randomPantheonEnabledFields);
        }

        internal static bool IsTrueBossRushEnabled(int pantheonNumber)
        {
            if (pantheonNumber < 1 || pantheonNumber > 5)
            {
                return false;
            }

            EnsureCache();
            return ReadPantheonToggle(
                pantheonNumber,
                trueBossRushType,
                trueBossRushInstanceField,
                trueBossRushEnabledFields);
        }

        private static bool ReadPantheonToggle(
            int pantheonNumber,
            Type moduleType,
            FieldInfo moduleInstanceField,
            FieldInfo[] toggleFields)
        {
            if (moduleType == null)
            {
                return false;
            }

            try
            {
                if (moduleInstanceField != null && moduleInstanceField.GetValue(null) == null)
                {
                    return false;
                }

                FieldInfo toggleField = toggleFields[pantheonNumber];
                if (toggleField?.GetValue(null) is bool enabled)
                {
                    return enabled;
                }
            }
            catch
            {
            }

            return false;
        }

        private static void EnsureCache()
        {
            if (randomPantheonsType != null && trueBossRushType != null)
            {
                return;
            }

            try
            {
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    randomPantheonsType ??= asm.GetType(RandomPantheonsTypeName, throwOnError: false);
                    trueBossRushType ??= asm.GetType(TrueBossRushTypeName, throwOnError: false);
                    if (randomPantheonsType != null && trueBossRushType != null)
                    {
                        break;
                    }
                }

                const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                if (randomPantheonsType != null)
                {
                    randomPantheonsInstanceField = randomPantheonsType.GetField(InstanceFieldName, staticFlags);
                    for (int i = 1; i <= 5; i++)
                    {
                        randomPantheonEnabledFields[i] = randomPantheonsType.GetField($"Pantheon{i}Enabled", staticFlags);
                    }
                }

                if (trueBossRushType != null)
                {
                    trueBossRushInstanceField = trueBossRushType.GetField(InstanceFieldName, staticFlags);
                    for (int i = 1; i <= 5; i++)
                    {
                        trueBossRushEnabledFields[i] = trueBossRushType.GetField($"TrueBossRushPantheon{i}Enabled", staticFlags);
                    }
                }
            }
            catch
            {
            }
        }
    }
}
