using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
        private const string GlobalSettingsTypeName = "SafeGodseekerQoL.Settings.GlobalSettings";
        private const string GlobalSettingsModulesPropertyName = "Modules";
        private const string GodhomeRootTypeName = "GodhomeQoL.GodhomeQoL";
        private const string GodhomeModuleManagerTypeName = "GodhomeQoL.ModuleManager";
        private static Assembly cachedAssembly;

        public static IReadOnlyList<string> GetSettingsLines()
        {
            Type modType = FindType(RootTypeName);
            if (modType == null)
            {
                return Array.Empty<string>();
            }

            cachedAssembly = modType.Assembly;

            bool? active = GetStaticPropertyBool(modType, "Active");
            object globalSettings = GetStaticProperty(modType, "GlobalSettings");
            Dictionary<string, bool> gsModules = GetGlobalModules(globalSettings);
            var modules = GetModules();

            List<string> lines = new()
            {
                $"SafeGodseekerQoL: {FmtOnOff(active)}",
                "Boss Challenge:"
            };

            
            AddModuleLine(lines, modules, gsModules, "Add LifeBlood", "AddLifeblood");
            AddModuleLine(lines, modules, gsModules, "Add Soul", "AddSoul");
            AddModuleLine(lines, modules, gsModules, "Force Grey Prince Enter Type", "ForceGreyPrinceEnterType");
            AddModuleLine(lines, modules, gsModules, "Halve Damage (HoG Ascended or Above)", "HalveDamageHoGAscendedOrAbove");
            AddModuleLine(lines, modules, gsModules, "Halve Damage (HoG Attuned)", "HalveDamageHoGAttuned");
            AddModuleLine(lines, modules, gsModules, "Halve Damage (Other Place)", "HalveDamageOtherPlace");
            AddModuleLine(lines, modules, gsModules, "Halve Damage (Pantheons)", "HalveDamagePantheons");
            AddModuleLine(lines, modules, gsModules, "Infinite Challenge", "InfiniteChallenge");
            AddModuleLine(lines, modules, gsModules, "P5 Health", "P5Health");
            AddModuleLine(lines, modules, gsModules, "Segmented P5", "SegmentedP5");
            AddBoolSettingLine(lines, "Restart Fight On Success As Well", "SafeGodseekerQoL.Modules.BossChallenge.InfiniteChallenge", "restartFightOnSuccess");
            AddBoolSettingLine(lines, "Restart Fight And Music", "SafeGodseekerQoL.Modules.BossChallenge.InfiniteChallenge", "restartFightAndMusic");
            AddIntSettingLine(lines, "LifeBlood Amount", "SafeGodseekerQoL.Modules.BossChallenge.AddLifeblood", "lifebloodAmount");
            AddIntSettingLine(lines, "Soul Amount", "SafeGodseekerQoL.Modules.BossChallenge.AddSoul", "soulAmount");
            AddValueSettingLine(lines, "GPZ Enter Type", "SafeGodseekerQoL.Modules.BossChallenge.ForceGreyPrinceEnterType", "gpzEnterType");

            lines.Add("BugFix:");
            AddModuleLine(lines, modules, gsModules, "HUD Display Checker", "HUDDisplayChecker");

            lines.Add("Quality Of Life:");
            AddModuleLine(lines, modules, gsModules, "Complete Lower Difficulty", "CompleteLowerDifficulty");
            AddModuleLine(lines, modules, gsModules, "Door Default Begin", "DoorDefaultBegin");
            AddModuleLine(lines, modules, gsModules, "Fast Dream Warp", "FastDreamWarp");
            AddModuleLine(lines, modules, gsModules, "Memorize Bindings", "MemorizeBindings");
            AddModuleLine(lines, modules, gsModules, "Short Death Animation", "ShortDeathAnimation");
            AddModuleLine(lines, modules, gsModules, "Skip Cutscenes", "SkipCutscenes");
            AddModuleLine(lines, modules, gsModules, "Unlock Radiant", "UnlockRadiant");
            AddBoolSettingLine(lines, "Instant Warp", "SafeGodseekerQoL.Modules.QoL.FastDreamWarp", "instantWarp");
            
            const string skipNs = "SafeGodseekerQoL.Modules.QoL.SkipCutscenes";
            AddBoolSettingLine(lines, "Dreamers Get", skipNs, "DreamersGet");
            AddBoolSettingLine(lines, "Absolute Radiance", skipNs, "AbsoluteRadiance");
            AddBoolSettingLine(lines, "Abyss Shriek Get", skipNs, "AbyssShriekGet");
            AddBoolSettingLine(lines, "After Kings Brand Get", skipNs, "AfterKingsBrandGet");
            AddBoolSettingLine(lines, "Black Egg Open", skipNs, "BlackEggOpen");
            AddBoolSettingLine(lines, "Stag Arrive", skipNs, "StagArrive");
            AddBoolSettingLine(lines, "Hall Of Gods Statues", skipNs, "HallOfGodsStatues");
            AddBoolSettingLine(lines, "Godhome Entry", skipNs, "GodhomeEntry");
            AddBoolSettingLine(lines, "Pure Vessel Roar", skipNs, "PureVesselRoar");
            AddBoolSettingLine(lines, "Grimm Nightmare", skipNs, "GrimmNightmare");
            AddBoolSettingLine(lines, "GreyPrinceZote", skipNs, "GreyPrinceZote");
            AddBoolSettingLine(lines, "Collector", skipNs, "Collector");
            AddBoolSettingLine(lines, "First Time Bosses", skipNs, "FirstTimeBosses");
            AddBoolSettingLine(lines, "First Charm", skipNs, "FirstCharm");
            AddBoolSettingLine(lines, "Auto Skip Cinematics", skipNs, "AutoSkipCinematics");
            AddBoolSettingLine(lines, "Allow Skipping Nonskippable", skipNs, "AllowSkippingNonskippable");
            AddBoolSettingLine(lines, "Skip Cutscenes Without Prompt", skipNs, "SkipCutscenesWithoutPrompt");
            AddBoolSettingLine(lines, "Instant Scene Fade Ins", skipNs, "InstantSceneFadeIns");
            AddBoolSettingLine(lines, "Soul Master Phase Transition Skip", skipNs, "SoulMasterPhaseTransitionSkip");

            lines.Add("Miscellaneous:");
            AddModuleLine(lines, modules, gsModules, "Aggressive GS", "AggressiveGC");
            AddModuleLine(lines, modules, gsModules, "Unlock All Modes", "UnlockAllModes");

            return lines;
        }

        public static bool IsP5HealthEnabled()
        {
            if (IsGodhomeQolP5HealthEnabled())
            {
                return true;
            }

            try
            {
                Type modType = FindType(RootTypeName);
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

        public static void WriteSettingsWithSeparator(StreamWriter writer, string separator = "---------------------------------------------------")
        {
            if (writer == null)
            {
                return;
            }

            IReadOnlyList<string> lines = GetSettingsLines();
            if (lines.Count == 0)
            {
                return;
            }

            foreach (string line in lines)
            {
                LogWrite.EncryptedLine(writer, line);
            }

            LogWrite.EncryptedLine(writer, string.Empty);
            if (!string.IsNullOrEmpty(separator))
            {
                LogWrite.EncryptedLine(writer, separator);
            }
        }

        private static void AddModuleLine(List<string> lines, IDictionary<string, object> modules, Dictionary<string, bool> gsModules, string label, string moduleName)
        {
            string state = "N/A";
            if (modules != null && modules.TryGetValue(moduleName, out object module) && module != null)
            {
                bool enabled = GetInstanceBool(module, "Enabled") ?? false;
                state = FmtOnOff(enabled);
            }
            else if (gsModules != null && gsModules.TryGetValue(moduleName, out bool gsEnabled))
            {
                state = FmtOnOff(gsEnabled);
            }

            lines.Add($"  {label}: {state}");
        }

        private static void AddBoolSettingLine(List<string> lines, string label, string typeName, string fieldName)
        {
            bool? val = GetStaticFieldBool(typeName, fieldName);
            lines.Add($"  {label}: {FmtOnOff(val)}");
        }

        private static void AddIntSettingLine(List<string> lines, string label, string typeName, string fieldName)
        {
            object val = GetStaticFieldValue(typeName, fieldName);
            string text = val != null ? Convert.ToString(val, CultureInfo.InvariantCulture) : "N/A";
            lines.Add($"  {label}: {text}");
        }

        private static void AddValueSettingLine(List<string> lines, string label, string typeName, string fieldName)
        {
            object val = GetStaticFieldValue(typeName, fieldName);
            string text = val != null ? val.ToString() : "N/A";
            lines.Add($"  {label}: {text}");
        }

        private static IDictionary<string, object> GetModules()
        {
            return GetModules(ModuleManagerTypeName);
        }

        private static IDictionary<string, object> GetModules(string managerTypeName)
        {
            try
            {
                Type mgrType = FindType(managerTypeName);
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

        private static bool IsGodhomeQolP5HealthEnabled()
        {
            try
            {
                Type modType = FindType(GodhomeRootTypeName);
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

        private static bool? GetStaticFieldBool(string typeName, string fieldName)
        {
            object val = GetStaticFieldValue(typeName, fieldName);
            if (val is bool b)
            {
                return b;
            }
            return null;
        }

        private static object GetStaticFieldValue(string typeName, string fieldName)
        {
            try
            {
                Type t = cachedAssembly?.GetType(typeName) ?? FindType(typeName);
                if (t == null)
                {
                    return null;
                }

                FieldInfo fi = t.GetField(fieldName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                return fi?.GetValue(null);
            }
            catch
            {
                return null;
            }
        }

        private static string FmtOnOff(bool? b) => b.HasValue ? (b.Value ? "On" : "Off") : "N/A";

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

        private static Type FindType(string fullName)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType(fullName, false);
                if (t != null)
                {
                    return t;
                }
            }

            return null;
        }
    }
}
