using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Modding;

namespace ReplayLogger
{
    internal static class CollectorPhasesIntegration
    {
        private const string ModName = "CollectorPhases";
        private const string ModTypeName = "CollectorPhases.CollectorPhasesMod";

        private static readonly string[] LocalSettingsPropertyNames =
        {
            "LS",
            "LocalSettings"
        };

        private static readonly (string Label, string[] Keys)[] FieldMappings =
        {
            ("Infinite Phase", new[] { "infinitePhase", "InfinitePhase" }),
            ("Ignore Initial Jar Limits", new[] { "IgnoreInitialJarLimit", "IgnoreInitialJarLimits" }),
            ("Tower of Love", new[] { "ToL", "TowerOfLove" }),
            ("Hall of Gods Attuned", new[] { "HoG", "HallOfGodsAttuned", "HallOfGods" }),
            ("Hall of Gods Ascended/Radiant", new[] { "HoG2", "HallOfGodsAscended", "HallOfGodsRadiant" }),
            ("Pantheons", new[] { "Pantheons" })
        };

        internal static IReadOnlyList<string> GetSettingsLines()
        {
            try
            {
                object modInstance = GetModInstance();
                if (modInstance == null)
                {
                    return Array.Empty<string>();
                }

                if (!TryGetLocalSettings(modInstance, out object settings) || settings == null)
                {
                    return Array.Empty<string>();
                }

                List<string> lines = new() { "CollectorPhases Settings:" };
                bool any = false;
                foreach ((string label, string[] keys) in FieldMappings)
                {
                    if (TryReadValue(settings, keys, out string formatted))
                    {
                        lines.Add($"  {label}: {formatted}");
                        any = true;
                    }
                }

                return any ? lines : Array.Empty<string>();
            }
            catch (Exception ex)
            {
                Modding.Logger.LogWarn($"ReplayLogger: failed to read CollectorPhases settings: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        private static object GetModInstance()
        {
            try
            {
                object mod = ModHooks.GetMod(ModName);
                if (mod != null)
                {
                    return mod;
                }
            }
            catch
            {
                // ignored
            }

            Type modType = FindType(ModTypeName);
            if (modType == null)
            {
                return null;
            }

            PropertyInfo instanceProperty =
                modType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (instanceProperty?.GetValue(null) is { } propertyInstance)
            {
                return propertyInstance;
            }

            FieldInfo instanceField =
                modType.GetField("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static) ??
                modType.GetField("_instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return instanceField?.GetValue(null);
        }

        private static bool TryGetLocalSettings(object modInstance, out object settings)
        {
            settings = null;
            if (modInstance == null)
            {
                return false;
            }

            Type modType = modInstance.GetType();
            foreach (string name in LocalSettingsPropertyNames)
            {
                PropertyInfo property = modType.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                if (property?.GetValue(modInstance) is { } propertyValue && NameContains(property.PropertyType?.Name, "LocalSettings"))
                {
                    settings = propertyValue;
                    return true;
                }

                FieldInfo field = modType.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                if (field?.GetValue(modInstance) is { } fieldValue && NameContains(field.FieldType?.Name, "LocalSettings"))
                {
                    settings = fieldValue;
                    return true;
                }
            }

            foreach (PropertyInfo property in modType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                if (NameContains(property.PropertyType?.Name, "LocalSettings") &&
                    property.GetValue(modInstance) is { } value)
                {
                    settings = value;
                    return true;
                }
            }

            foreach (FieldInfo field in modType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                if (NameContains(field.FieldType?.Name, "LocalSettings") &&
                    field.GetValue(modInstance) is { } value)
                {
                    settings = value;
                    return true;
                }
            }

            return false;
        }

        private static bool TryReadValue(object settings, IReadOnlyList<string> keys, out string formatted)
        {
            formatted = null;
            if (settings == null)
            {
                return false;
            }

            Type settingsType = settings.GetType();
            foreach (string key in keys)
            {
                FieldInfo field = settingsType.GetField(key, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null && TryFormatValue(field.GetValue(settings), out formatted))
                {
                    return true;
                }

                PropertyInfo property = settingsType.GetProperty(key, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (property != null && TryFormatValue(property.GetValue(settings), out formatted))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryFormatValue(object value, out string formatted)
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
                case sbyte or byte or short or ushort or int or uint or long or ulong:
                    formatted = Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                    return true;
                default:
                    formatted = value.ToString();
                    return true;
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

        private static bool NameContains(string source, string value)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(value))
            {
                return false;
            }

            return source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
