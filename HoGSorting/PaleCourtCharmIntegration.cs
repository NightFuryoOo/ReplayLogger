using System;
using System.Collections.Generic;
using System.Reflection;

namespace ReplayLogger
{
    internal static class PaleCourtCharmIntegration
    {
        private const string ModTypeName = "FiveKnights.FiveKnights";
        private const string InstanceFieldName = "Instance";
        private const string SaveSettingsMemberName = "SaveSettings";
        private const string EquippedCharmsFieldName = "equippedCharms";

        private static readonly string[] CharmNames =
        {
            "Mark of Purity",
            "Vessel's Lament",
            "Boon of Hallownest",
            "Abyssal Bloom"
        };

        private static FieldInfo instanceField;
        private static PropertyInfo saveSettingsProperty;
        private static FieldInfo saveSettingsField;
        private static FieldInfo equippedCharmsField;

        internal static IReadOnlyList<string> GetEquippedCharmNames()
        {
            try
            {
                if (!EnsureHandles())
                {
                    return Array.Empty<string>();
                }

                object modInstance = instanceField.GetValue(null);
                if (modInstance == null)
                {
                    return Array.Empty<string>();
                }

                object settings = GetSaveSettings(modInstance);
                if (settings == null)
                {
                    return Array.Empty<string>();
                }

                if (equippedCharmsField?.GetValue(settings) is not bool[] flags || flags.Length == 0)
                {
                    return Array.Empty<string>();
                }

                List<string> equipped = new();
                int limit = Math.Min(flags.Length, CharmNames.Length);
                for (int i = 0; i < limit; i++)
                {
                    if (flags[i])
                    {
                        equipped.Add(CharmNames[i]);
                    }
                }

                return equipped;
            }
            catch (Exception ex)
            {
                Modding.Logger.LogWarn($"ReplayLogger: failed to read Pale Court charms: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        private static object GetSaveSettings(object instance)
        {
            if (instance == null)
            {
                return null;
            }

            if (saveSettingsProperty != null)
            {
                return saveSettingsProperty.GetValue(instance);
            }

            if (saveSettingsField != null)
            {
                return saveSettingsField.GetValue(instance);
            }

            return null;
        }

        private static bool EnsureHandles()
        {
            if (instanceField != null && (saveSettingsProperty != null || saveSettingsField != null) && equippedCharmsField != null)
            {
                return true;
            }

            Type modType = FindType(ModTypeName);
            if (modType == null)
            {
                return false;
            }

            instanceField = modType.GetField(InstanceFieldName, BindingFlags.Public | BindingFlags.Static);
            if (instanceField == null)
            {
                return false;
            }

            saveSettingsProperty = modType.GetProperty(SaveSettingsMemberName, BindingFlags.Public | BindingFlags.Instance);
            saveSettingsField = modType.GetField(SaveSettingsMemberName, BindingFlags.Public | BindingFlags.Instance);

            Type settingsType = saveSettingsProperty?.PropertyType ?? saveSettingsField?.FieldType;
            if (settingsType == null)
            {
                return false;
            }

            equippedCharmsField = settingsType.GetField(EquippedCharmsFieldName, BindingFlags.Public | BindingFlags.Instance);
            return equippedCharmsField != null;
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
    }
}
