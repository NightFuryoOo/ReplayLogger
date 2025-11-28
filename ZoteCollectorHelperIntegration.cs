using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Modding;

namespace ReplayLogger
{
    internal static class ZoteCollectorHelperIntegration
    {
        private const string ModName = "ZoteCollectorHelper";
        private const string OnSaveGlobalMethodName = "OnSaveGlobal";

        private const string EnabledFieldName = "on";
        private const string BossSkinFieldName = "zoteBossSkin";
        private const string SummonHpFieldName = "zoteSummonHP";
        private const string SummonLimitFieldName = "zoteSummonLimit";
        private const string ScaleFieldName = "zoteScale";
        private const string CollectorHpFieldName = "colSummonHP";

        private static readonly string[] ScaleDisplayValues =
        {
            "0.25",
            "0.5",
            "0.75",
            "1",
            "1.25",
            "1.5",
            "1.75",
            "2"
        };

        private static MethodInfo onSaveGlobalMethod;
        private static FieldInfo enabledField;
        private static FieldInfo bossSkinField;
        private static FieldInfo summonHpField;
        private static FieldInfo summonLimitField;
        private static FieldInfo scaleField;
        private static FieldInfo collectorHpField;

        internal static IReadOnlyList<string> GetSettingsLines()
        {
            try
            {
                object modInstance = GetModInstance();
                if (modInstance == null)
                {
                    return Array.Empty<string>();
                }

                if (!EnsureHandles(modInstance.GetType()))
                {
                    return Array.Empty<string>();
                }

                object settings = onSaveGlobalMethod?.Invoke(modInstance, null);
                if (settings == null)
                {
                    return Array.Empty<string>();
                }

                List<string> lines = new()
                {
                    "ZoteCollectorHelper Settings:",
                    $"  Mod Enabled: {FormatBool(enabledField, settings)}",
                    $"  Zote Boss Skin: {FormatBool(bossSkinField, settings)}",
                    $"  Zote Summon HP: {FormatInt(summonHpField, settings)}",
                    $"  Zote Summon Limit: {FormatInt(summonLimitField, settings)}",
                    $"  Zote Scale: {FormatScale(settings)}",
                    $"  Collector Summon HP: {FormatInt(collectorHpField, settings)}"
                };

                return lines;
            }
            catch (Exception ex)
            {
                Modding.Logger.LogWarn($"ReplayLogger: failed to read ZoteCollectorHelper settings: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        private static object GetModInstance()
        {
            try
            {
                return ModHooks.GetMod(ModName);
            }
            catch
            {
                return null;
            }
        }

        private static bool EnsureHandles(Type modType)
        {
            if (modType == null)
            {
                return false;
            }

            if (onSaveGlobalMethod != null &&
                enabledField != null &&
                bossSkinField != null &&
                summonHpField != null &&
                summonLimitField != null &&
                scaleField != null &&
                collectorHpField != null)
            {
                return true;
            }

            onSaveGlobalMethod = modType.GetMethod(OnSaveGlobalMethodName, BindingFlags.Public | BindingFlags.Instance);
            if (onSaveGlobalMethod == null)
            {
                return false;
            }

            Type settingsType = onSaveGlobalMethod.ReturnType;
            if (settingsType == null)
            {
                return false;
            }

            enabledField = settingsType.GetField(EnabledFieldName, BindingFlags.Public | BindingFlags.Instance);
            bossSkinField = settingsType.GetField(BossSkinFieldName, BindingFlags.Public | BindingFlags.Instance);
            summonHpField = settingsType.GetField(SummonHpFieldName, BindingFlags.Public | BindingFlags.Instance);
            summonLimitField = settingsType.GetField(SummonLimitFieldName, BindingFlags.Public | BindingFlags.Instance);
            scaleField = settingsType.GetField(ScaleFieldName, BindingFlags.Public | BindingFlags.Instance);
            collectorHpField = settingsType.GetField(CollectorHpFieldName, BindingFlags.Public | BindingFlags.Instance);

            return enabledField != null &&
                   bossSkinField != null &&
                   summonHpField != null &&
                   summonLimitField != null &&
                   scaleField != null &&
                   collectorHpField != null;
        }

        private static string FormatBool(FieldInfo field, object instance)
        {
            if (field?.GetValue(instance) is bool value)
            {
                return value ? "On" : "Off";
            }

            return "N/A";
        }

        private static string FormatInt(FieldInfo field, object instance)
        {
            if (field?.GetValue(instance) is int value)
            {
                return value.ToString(CultureInfo.InvariantCulture);
            }

            return "N/A";
        }

        private static string FormatScale(object settings)
        {
            if (scaleField?.GetValue(settings) is int rawValue)
            {
                int index = rawValue - 1;
                if (index < 0)
                {
                    index = 0;
                }
                else if (index >= ScaleDisplayValues.Length)
                {
                    index = ScaleDisplayValues.Length - 1;
                }

                return ScaleDisplayValues[index];
            }

            return "N/A";
        }
    }
}
