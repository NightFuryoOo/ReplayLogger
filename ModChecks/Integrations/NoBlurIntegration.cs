using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Modding;

namespace ReplayLogger
{
    internal static class NoBlurIntegration
    {
        private const string ModName = "No Blur";
        private const string ModTypeName = "NoBlur.NoBlur";
        private const string SettingsPropertyName = "GS";

        private static readonly (string Label, string FieldName)[] FieldMappings =
        {
            ("No Blur", "noBlur"),
            ("No Fog", "noFog"),
            ("No Haze", "noHaze"),
            ("Blur Hotkey", "blurHotkey"),
            ("Fog Hotkey", "fogHotkey"),
            ("Haze Hotkey", "hazeHotkey")
        };

        internal static IReadOnlyList<string> GetSettingsLines()
        {
            try
            {
                object settings = GetSettingsInstance();
                if (settings == null)
                {
                    return Array.Empty<string>();
                }

                List<string> lines = new() { "NoBlur Settings:" };
                bool any = false;
                foreach ((string label, string fieldName) in FieldMappings)
                {
                    if (TryReadField(settings, fieldName, out string formatted))
                    {
                        lines.Add($"  {label}: {formatted}");
                        any = true;
                    }
                }

                return any ? lines : Array.Empty<string>();
            }
            catch (Exception ex)
            {
                Modding.Logger.LogWarn($"ReplayLogger: failed to read NoBlur settings: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        private static object GetSettingsInstance()
        {
            try
            {
                object mod = ModHooks.GetMod(ModName);
                if (mod != null)
                {
                    return GetSettingsFromType(mod.GetType());
                }
            }
            catch
            {
                
            }

            Type modType = FindType(ModTypeName);
            return GetSettingsFromType(modType);
        }

        private static object GetSettingsFromType(Type modType)
        {
            if (modType == null)
            {
                return null;
            }

            PropertyInfo property = modType.GetProperty(SettingsPropertyName, BindingFlags.Public | BindingFlags.Static);
            if (property != null && property.GetValue(null) is { } value)
            {
                return value;
            }

            FieldInfo field = modType.GetField(SettingsPropertyName, BindingFlags.Public | BindingFlags.Static);
            return field?.GetValue(null);
        }

        private static bool TryReadField(object settings, string fieldName, out string formatted)
        {
            formatted = null;
            if (settings == null || string.IsNullOrEmpty(fieldName))
            {
                return false;
            }

            Type settingsType = settings.GetType();
            FieldInfo field = settingsType.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null && TryFormat(field.GetValue(settings), out formatted))
            {
                return true;
            }

            PropertyInfo property = settingsType.GetProperty(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property != null && TryFormat(property.GetValue(settings), out formatted))
            {
                return true;
            }

            return false;
        }

        private static bool TryFormat(object value, out string formatted)
        {
            formatted = null;
            if (value is null)
            {
                return false;
            }

            switch (value)
            {
                case bool b:
                    formatted = b ? "On" : "Off";
                    return true;
                case IFormattable f:
                    formatted = f.ToString(null, CultureInfo.InvariantCulture);
                    return true;
                default:
                    formatted = value.ToString();
                    return true;
            }
        }

        private static Type FindType(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
            {
                return null;
            }

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
