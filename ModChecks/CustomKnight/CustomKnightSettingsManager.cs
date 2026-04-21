using System;
using System.IO;
using System.Reflection;

namespace ReplayLogger
{
    internal static class CustomKnightSettingsManager
    {
        private const string CustomKnightTypeName = "CustomKnight.CustomKnight";
        private const string SkinManagerTypeName = "CustomKnight.SkinManager";

        private static bool typeResolutionAttempted;
        private static Type customKnightType;
        private static Type skinManagerType;
        private static PropertyInfo globalSettingsProperty;
        private static PropertyInfo saveSettingsProperty;
        private static MethodInfo getCurrentSkinMethod;

        public static void Reset()
        {
            typeResolutionAttempted = false;
            customKnightType = null;
            skinManagerType = null;
            globalSettingsProperty = null;
            saveSettingsProperty = null;
            getCurrentSkinMethod = null;
        }

        public static string BuildSettingsLine()
        {
            if (!TryResolveTypes())
            {
                return "CustomKnight Skin: unavailable";
            }

            string currentSkinName = null;
            string currentSkinId = null;
            string saveDefaultSkin = null;
            string globalDefaultSkin = null;

            try
            {
                object globalSettings = globalSettingsProperty?.GetCachedValue(null);
                globalDefaultSkin = TryGetRuntimeStringProperty(globalSettings, "DefaultSkin");
            }
            catch
            {
            }

            try
            {
                object saveSettings = saveSettingsProperty?.GetCachedValue(null);
                saveDefaultSkin = TryGetRuntimeStringProperty(saveSettings, "DefaultSkin");
            }
            catch
            {
            }

            try
            {
                object currentSkin = getCurrentSkinMethod?.InvokeCached(null);
                if (currentSkin != null)
                {
                    currentSkinName = TryGetRuntimeStringMethod(currentSkin, "GetName");
                    currentSkinId = TryGetRuntimeStringMethod(currentSkin, "GetId");
                }
            }
            catch
            {
            }

            string displaySkin = ResolveDisplaySkin(currentSkinName, currentSkinId, saveDefaultSkin, globalDefaultSkin);
            return $"CustomKnight Skin: {displaySkin}";
        }

        public static void WriteSettingsWithSeparator(StreamWriter writer, string separator = "---------------------------------------------------")
        {
            if (writer == null)
            {
                return;
            }

            string line = BuildSettingsLine();
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            LogWrite.EncryptedLine(writer, line);
            if (!string.IsNullOrEmpty(separator))
            {
                LogWrite.EncryptedLine(writer, separator);
            }
        }

        private static bool TryResolveTypes()
        {
            if (typeResolutionAttempted)
            {
                return customKnightType != null && skinManagerType != null;
            }

            typeResolutionAttempted = true;

            customKnightType = FindType(CustomKnightTypeName);
            skinManagerType = FindType(SkinManagerTypeName);
            if (customKnightType == null || skinManagerType == null)
            {
                return false;
            }

            const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            globalSettingsProperty = customKnightType.GetProperty("GlobalSettings", staticFlags);
            saveSettingsProperty = customKnightType.GetProperty("SaveSettings", staticFlags);
            getCurrentSkinMethod = skinManagerType.GetMethod("GetCurrentSkin", staticFlags);
            return true;
        }

        private static Type FindType(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
            {
                return null;
            }

            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = asm.GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static string TryGetRuntimeStringProperty(object instance, string propertyName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return null;
            }

            if (!ReflectionMemberAccessCache.TryGetCachedRuntimePropertyValue(instance, propertyName, out object raw))
            {
                return null;
            }

            return raw as string ?? raw?.ToString();
        }

        private static string TryGetRuntimeStringMethod(object instance, string methodName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(methodName))
            {
                return null;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            MethodInfo method = instance.GetType().GetMethod(methodName, flags, null, Type.EmptyTypes, null);
            if (method == null)
            {
                return null;
            }

            object raw = method.InvokeCached(instance);
            return raw as string ?? raw?.ToString();
        }

        private static string ResolveDisplaySkin(string currentSkinName, string currentSkinId, string saveDefaultSkin, string globalDefaultSkin)
        {
            if (!string.IsNullOrWhiteSpace(currentSkinName) && !string.IsNullOrWhiteSpace(currentSkinId))
            {
                if (string.Equals(currentSkinName, currentSkinId, StringComparison.Ordinal))
                {
                    return currentSkinName;
                }

                return $"{currentSkinName} ({currentSkinId})";
            }

            if (!string.IsNullOrWhiteSpace(currentSkinName))
            {
                return currentSkinName;
            }

            if (!string.IsNullOrWhiteSpace(currentSkinId))
            {
                return currentSkinId;
            }

            if (!string.IsNullOrWhiteSpace(saveDefaultSkin))
            {
                return saveDefaultSkin;
            }

            if (!string.IsNullOrWhiteSpace(globalDefaultSkin))
            {
                return globalDefaultSkin;
            }

            return "N/A";
        }
    }
}
