using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ReplayLogger
{

    internal static class SafeGodseekerQolIntegration
    {
        private const string RootTypeName = "SafeGodseekerQoL.SafeGodseekerQoL";
        private const string ModuleManagerTypeName = "SafeGodseekerQoL.ModuleManager";
        private const string ModuleManagerModulesFieldName = "modules";
        private const string ModuleManagerModulesPropertyName = "Modules";
        private const string GlobalSettingsModulesPropertyName = "Modules";
        private const string GodhomeRootTypeName = "GodhomeQoL.GodhomeQoL";
        private const string GodhomeModuleManagerTypeName = "GodhomeQoL.ModuleManager";

        public static bool IsP5HealthEnabled()
        {
            if (IsGodhomeQolP5HealthEnabled())
            {
                return true;
            }

            try
            {
                Type modType = TypeLookup.FindType(RootTypeName);
                if (modType == null)
                {
                    return false;
                }

                bool active = GetStaticPropertyBool(modType, "Active") ?? false;
                if (!active)
                {
                    return false;
                }

                object globalSettings = GetStaticProperty(modType, "GlobalSettings");
                Dictionary<string, bool> gsModules = GetGlobalModules(globalSettings);
                if (gsModules.TryGetValue("P5Health", out bool gsEnabled) && gsEnabled)
                {
                    return true;
                }

                var modules = GetModules();
                if (modules.TryGetValue("P5Health", out object module) && module != null)
                {
                    bool enabled = GetInstanceBool(module, "Enabled") ?? false;
                    return enabled;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool IsGodhomeQolP5HealthEnabled()
        {
            try
            {
                Type modType = TypeLookup.FindType(GodhomeRootTypeName);
                if (modType == null)
                {
                    return false;
                }

                bool active = GetStaticPropertyBool(modType, "Active") ?? false;
                if (!active)
                {
                    return false;
                }

                object globalSettings = GetStaticProperty(modType, "GlobalSettings");
                Dictionary<string, bool> gsModules = GetGlobalModules(globalSettings);
                if (gsModules.TryGetValue("P5Health", out bool gsEnabled) && gsEnabled)
                {
                    return true;
                }

                var modules = GetModules(GodhomeModuleManagerTypeName);
                if (modules.TryGetValue("P5Health", out object module) && module != null)
                {
                    bool enabled = GetInstanceBool(module, "Enabled") ?? false;
                    return enabled;
                }
            }
            catch
            {
            }

            return false;
        }

        private static IDictionary<string, object> GetModules()
        {
            return GetModules(ModuleManagerTypeName);
        }

        private static IDictionary<string, object> GetModules(string managerTypeName)
        {
            try
            {
                Type mgrType = TypeLookup.FindType(managerTypeName);
                if (mgrType == null)
                {
                    return new Dictionary<string, object>();
                }

                object raw = mgrType.GetProperty(ModuleManagerModulesPropertyName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null);

                IDictionary dict = null;

                if (raw is IDictionary d)
                {
                    dict = d;
                }
                else
                {
                    object fieldRaw = mgrType.GetField(ModuleManagerModulesFieldName, BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
                    object source = raw ?? fieldRaw;
                    object value = source?.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(source);
                    if (value is IDictionary ld)
                    {
                        dict = ld;
                    }
                }

                if (dict == null)
                {
                    return new Dictionary<string, object>();
                }

                return dict.Cast<DictionaryEntry>().ToDictionary(e => e.Key.ToString(), e => e.Value);
            }
            catch
            {
                return new Dictionary<string, object>();
            }
        }

        private static bool? GetInstanceBool(object instance, string propName)
        {
            try
            {
                PropertyInfo prop = instance.GetType().GetProperty(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop?.GetValue(instance) is bool b)
                {
                    return b;
                }
            }
            catch
            {
            }

            return null;
        }

        private static bool? GetStaticPropertyBool(Type type, string propName)
        {
            try
            {
                if (type.GetProperty(propName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null) is bool b)
                {
                    return b;
                }
            }
            catch
            {
            }

            return null;
        }

        private static object GetStaticProperty(Type type, string propName)
        {
            try
            {
                return type.GetProperty(propName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null);
            }
            catch
            {
                return null;
            }
        }

        private static Dictionary<string, bool> GetGlobalModules(object globalSettings)
        {
            Dictionary<string, bool> result = new(StringComparer.Ordinal);
            if (globalSettings == null)
            {
                return result;
            }

            try
            {
                PropertyInfo prop = globalSettings.GetType().GetProperty(GlobalSettingsModulesPropertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop?.GetValue(globalSettings) is IDictionary dict)
                {
                    foreach (DictionaryEntry entry in dict)
                    {
                        if (entry.Key == null || entry.Value == null)
                        {
                            continue;
                        }

                        string name = entry.Key.ToString();
                        if (entry.Value is bool b)
                        {
                            result[name] = b;
                        }
                    }
                }
            }
            catch
            {
            }

            return result;
        }
    }
}
