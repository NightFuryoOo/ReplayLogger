using System;
using System.Reflection;

namespace ReplayLogger
{
    internal static class PaleCourtStatueIntegration
    {
        private const string ModTypeName = "FiveKnights.FiveKnights";
        private const string InstanceFieldName = "Instance";
        private const string SaveSettingsMemberName = "SaveSettings";

        private static readonly string[] AltStatueMawlekNames =
        {
            "AltStatueMawlek",
            "altStatueMawlek"
        };

        private static FieldInfo instanceField;
        private static PropertyInfo saveSettingsProperty;
        private static FieldInfo saveSettingsField;
        private static FieldInfo altStatueMawlekField;
        private static PropertyInfo altStatueMawlekProperty;
        private static bool readFailureLogged;

        internal static bool IsAltStatueMawlekEnabled()
        {
            try
            {
                if (!EnsureHandles())
                {
                    return false;
                }

                object modInstance = instanceField.GetCachedValue(null);
                if (modInstance == null)
                {
                    return false;
                }

                object settings = GetSaveSettings(modInstance);
                if (settings == null)
                {
                    return false;
                }

                object raw = altStatueMawlekProperty != null
                    ? altStatueMawlekProperty.GetCachedValue(settings)
                    : altStatueMawlekField?.GetCachedValue(settings);

                readFailureLogged = false;
                return raw is bool value && value;
            }
            catch (Exception ex)
            {
                if (!readFailureLogged)
                {
                    readFailureLogged = true;
                    global::ReplayLogger.InternalDiagnostics.Warn($"ReplayLogger: failed to read Pale Court statue setting AltStatueMawlek: {ex.Message}");
                }
                return false;
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
                return saveSettingsProperty.GetCachedValue(instance);
            }

            if (saveSettingsField != null)
            {
                return saveSettingsField.GetCachedValue(instance);
            }

            return null;
        }

        private static bool EnsureHandles()
        {
            if (instanceField != null &&
                (saveSettingsProperty != null || saveSettingsField != null) &&
                (altStatueMawlekField != null || altStatueMawlekProperty != null))
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

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic;
            foreach (string name in AltStatueMawlekNames)
            {
                altStatueMawlekField = settingsType.GetField(name, flags);
                if (altStatueMawlekField != null)
                {
                    break;
                }

                altStatueMawlekProperty = settingsType.GetProperty(name, flags);
                if (altStatueMawlekProperty != null)
                {
                    break;
                }
            }

            return altStatueMawlekField != null || altStatueMawlekProperty != null;
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



