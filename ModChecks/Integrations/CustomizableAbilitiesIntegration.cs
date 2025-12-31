using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace ReplayLogger
{
    internal static class CustomizableAbilitiesIntegration
    {
        private const string ModTypeName = "CustomizableAbilities.CustomizableAbilities";
        private const string GlobalSettingsFieldName = "GS";
        private const string LocalSettingsFieldName = "LS";

        internal static IReadOnlyList<string> GetSettingsLines()
        {
            try
            {
                Type modType = FindType(ModTypeName);
                if (modType == null)
                {
                    return Array.Empty<string>();
                }

                object globalSettings = GetStaticFieldValue(modType, GlobalSettingsFieldName);
                object localSettings = GetStaticFieldValue(modType, LocalSettingsFieldName);

                if (globalSettings == null && localSettings == null)
                {
                    return Array.Empty<string>();
                }

                List<string> lines = new() { "CustomizableAbilities Settings:" };
                bool any = false;

                any |= AppendBool(globalSettings, "EnableMod", "Enable Mod", lines);
                any |= AppendBool(globalSettings, "EnableFloatNailDamage", "Float Nail Damage", lines);
                any |= AppendBool(globalSettings, "EnableDisplay", "Display Overlay", lines);
                any |= AppendBool(globalSettings, "DisplayNailDamage", "Show Nail Damage", lines);
                any |= AppendBool(globalSettings, "DisplayNailSoulGain", "Show Nail Soul Gain", lines);
                any |= AppendBool(globalSettings, "DisplayNailCooldown", "Show Nail Cooldown", lines);
                any |= AppendBool(globalSettings, "DisplayDreamNailDamage", "Show Dream Nail Damage", lines);
                any |= AppendBool(globalSettings, "DisplayDreamNailSoulGain", "Show Dream Nail Soul Gain", lines);
                any |= AppendBool(globalSettings, "DisplaySpellsDamage", "Show Spells Damage", lines);
                any |= AppendBool(globalSettings, "DisplaySuperDashDamage", "Show Crystal Dash Damage", lines);
                any |= AppendBool(globalSettings, "HealEnemiesOverMaxHP", "Heal Enemies Over Max HP", lines);
                any |= AppendBool(localSettings, "IgnoreNailBinding", "Ignore Nail Binding", lines);

                any |= AppendInt(localSettings, "CustomIntNailDamage", "Nail Damage (int)", lines);
                any |= AppendFloat(localSettings, "CustomFloatNailDamage", "Nail Damage (float)", "F3", lines);
                any |= AppendFloat(localSettings, "CustomNailCooldown", "Nail Cooldown", "F3", lines);
                any |= AppendInt(localSettings, "SoulGain", "Soul Gain", lines);
                any |= AppendInt(localSettings, "ReserveSoulGain", "Reserve Soul Gain", lines);

                any |= AppendFloat(localSettings, "CustomDreamNailDamage", "Dream Nail Damage", "F3", lines);
                any |= AppendInt(localSettings, "CustomDreamNailSoulGain", "Dream Nail Soul Gain", lines);

                any |= AppendInt(localSettings, "CustomVengefulSpiritDamage", "Vengeful Spirit Damage", lines);
                any |= AppendInt(localSettings, "CustomShadeSoulDamage", "Shade Soul Damage", lines);
                any |= AppendInt(localSettings, "CustomDesolateDiveDamage", "Desolate Dive Damage", lines);
                any |= AppendInt(localSettings, "CustomDescendingDarkDamage", "Descending Dark Damage", lines);
                any |= AppendInt(localSettings, "CustomHowlingWraithsDamage", "Howling Wraiths Damage", lines);
                any |= AppendInt(localSettings, "CustomAbyssShriekDamage", "Abyss Shriek Damage", lines);

                any |= AppendFloat(localSettings, "CustomSuperDashDamage", "Crystal Dash Damage", "F3", lines);

                return any ? lines : Array.Empty<string>();
            }
            catch (Exception ex)
            {
                Modding.Logger.LogWarn($"ReplayLogger: failed to read CustomizableAbilities settings: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        private static bool AppendBool(object instance, string fieldName, string label, List<string> lines)
        {
            if (TryGetField(instance, fieldName, out bool value))
            {
                lines.Add($"  {label}: {(value ? "On" : "Off")}");
                return true;
            }
            return false;
        }

        private static bool AppendInt(object instance, string fieldName, string label, List<string> lines)
        {
            if (TryGetField(instance, fieldName, out int value))
            {
                lines.Add($"  {label}: {value}");
                return true;
            }
            return false;
        }

        private static bool AppendFloat(object instance, string fieldName, string label, string format, List<string> lines)
        {
            if (TryGetField(instance, fieldName, out float value))
            {
                lines.Add($"  {label}: {value.ToString(format, CultureInfo.InvariantCulture)}");
                return true;
            }
            return false;
        }

        private static bool TryGetField<T>(object instance, string fieldName, out T value)
        {
            value = default;
            if (instance == null || string.IsNullOrEmpty(fieldName))
            {
                return false;
            }

            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
            {
                return false;
            }

            object raw = field.GetValue(instance);
            if (raw is T typed)
            {
                value = typed;
                return true;
            }

            return false;
        }

        private static object GetStaticFieldValue(Type type, string fieldName)
        {
            if (type == null || string.IsNullOrEmpty(fieldName))
            {
                return null;
            }

            FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return field?.GetValue(null);
        }

        private static Type FindType(string fullName)
        {
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
    }
}
