using System;
using System.Reflection;

namespace ReplayLogger
{
    internal static class GodhomeQolRandomPantheonsIntegration
    {
        private const string RandomPantheonsTypeName = "GodhomeQoL.Modules.BossChallenge.RandomPantheons";
        private const string InstanceFieldName = "Instance";

        private static Type cachedType;
        private static FieldInfo instanceField;
        private static readonly FieldInfo[] pantheonEnabledFields = new FieldInfo[6];

        internal static bool IsPantheonRandomized(int pantheonNumber)
        {
            if (pantheonNumber < 1 || pantheonNumber > 5)
            {
                return false;
            }

            EnsureCache();
            if (cachedType == null)
            {
                return false;
            }

            try
            {
                if (instanceField != null && instanceField.GetValue(null) == null)
                {
                    return false;
                }

                FieldInfo toggleField = pantheonEnabledFields[pantheonNumber];
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
            if (cachedType != null)
            {
                return;
            }

            try
            {
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type t = asm.GetType(RandomPantheonsTypeName, throwOnError: false);
                    if (t != null)
                    {
                        cachedType = t;
                        break;
                    }
                }

                if (cachedType == null)
                {
                    return;
                }

                const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                instanceField = cachedType.GetField(InstanceFieldName, staticFlags);
                for (int i = 1; i <= 5; i++)
                {
                    pantheonEnabledFields[i] = cachedType.GetField($"Pantheon{i}Enabled", staticFlags);
                }
            }
            catch
            {
            }
        }
    }
}

