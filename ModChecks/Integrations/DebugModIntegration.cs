using System;
using System.Collections.Generic;
using System.Reflection;
using Modding;
using UnityEngine;

namespace ReplayLogger
{
    internal static class DebugModIntegration
    {
        private const string ModTypeName = "DebugMod.DebugMod";
        private const string SettingsPropertyName = "settings";

        private const string ConsoleFieldName = "ConsoleVisible";
        private const string EnemiesFieldName = "EnemiesPanelVisible";
        private const string HelpFieldName = "HelpPanelVisible";
        private const string InfoFieldName = "InfoPanelVisible";
        private const string TopMenuFieldName = "TopMenuVisible";
        private const string SaveStateFieldName = "SaveStatePanelVisible";
        private const string BindsFieldName = "binds";
        private const string InfiniteSoulFieldName = "infiniteSoul";
        private const string InfiniteHpFieldName = "infiniteHP";
        private const string NoclipFieldName = "noclip";
        private const string KeyBindLockFieldName = "KeyBindLock";

        private static PropertyInfo settingsProperty;
        private static FieldInfo consoleField;
        private static FieldInfo enemiesField;
        private static FieldInfo helpField;
        private static FieldInfo infoField;
        private static FieldInfo topMenuField;
        private static FieldInfo saveStateField;
        private static FieldInfo bindsField;
        private static FieldInfo infiniteSoulField;
        private static FieldInfo infiniteHpField;
        private static FieldInfo noclipField;
        private static FieldInfo keyBindLockField;

        internal static bool TryGetUiVisible(out bool visible)
        {
            visible = false;
            try
            {
                if (!EnsureHandles())
                {
                    return false;
                }

                object settings = settingsProperty.GetValue(null);
                if (settings == null)
                {
                    return false;
                }

                visible =
                    GetBool(consoleField, settings) ||
                    GetBool(enemiesField, settings) ||
                    GetBool(helpField, settings) ||
                    GetBool(infoField, settings) ||
                    GetBool(topMenuField, settings) ||
                    GetBool(saveStateField, settings);

                return true;
            }
            catch (Exception ex)
            {
                Modding.Logger.LogWarn($"ReplayLogger: failed to query DebugMod UI: {ex.Message}");
                return false;
            }
        }

        private static bool EnsureHandles()
        {
            if (settingsProperty != null && consoleField != null)
            {
                return true;
            }

            Type modType = FindType(ModTypeName);
            if (modType == null)
            {
                return false;
            }

            settingsProperty = modType.GetProperty(SettingsPropertyName, BindingFlags.Public | BindingFlags.Static);
            if (settingsProperty == null)
            {
                return false;
            }

            Type settingsType = settingsProperty.PropertyType;
            consoleField = settingsType.GetField(ConsoleFieldName, BindingFlags.Public | BindingFlags.Instance);
            enemiesField = settingsType.GetField(EnemiesFieldName, BindingFlags.Public | BindingFlags.Instance);
            helpField = settingsType.GetField(HelpFieldName, BindingFlags.Public | BindingFlags.Instance);
            infoField = settingsType.GetField(InfoFieldName, BindingFlags.Public | BindingFlags.Instance);
            topMenuField = settingsType.GetField(TopMenuFieldName, BindingFlags.Public | BindingFlags.Instance);
            saveStateField = settingsType.GetField(SaveStateFieldName, BindingFlags.Public | BindingFlags.Instance);
            bindsField = settingsType.GetField(BindsFieldName, BindingFlags.Public | BindingFlags.Instance);

            infiniteSoulField ??= modType.GetField(InfiniteSoulFieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            infiniteHpField ??= modType.GetField(InfiniteHpFieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            noclipField ??= modType.GetField(NoclipFieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            keyBindLockField ??= modType.GetField(KeyBindLockFieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            return consoleField != null &&
                   enemiesField != null &&
                   helpField != null &&
                   infoField != null &&
                   topMenuField != null &&
                   saveStateField != null &&
                   bindsField != null &&
                   infiniteSoulField != null &&
                   infiniteHpField != null &&
                   noclipField != null &&
                   keyBindLockField != null;
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

        private static bool GetBool(FieldInfo field, object instance)
        {
            return field != null && instance != null && field.GetValue(instance) is bool value && value;
        }

        internal static bool TryGetHotkeyBindings(out IReadOnlyDictionary<string, KeyCode> bindings)
        {
            bindings = null;
            try
            {
                if (!EnsureHandles())
                {
                    return false;
                }

                object settings = settingsProperty.GetValue(null);
                if (settings == null)
                {
                    return false;
                }

                if (bindsField?.GetValue(settings) is Dictionary<string, KeyCode> dict && dict.Count > 0)
                {
                    bindings = new Dictionary<string, KeyCode>(dict, StringComparer.Ordinal);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Modding.Logger.LogWarn($"ReplayLogger: failed to read DebugMod hotkeys: {ex.Message}");
            }

            return false;
        }

        internal static bool TryGetCheatToggleSnapshot(out DebugCheatToggleSnapshot snapshot)
        {
            snapshot = DebugCheatToggleSnapshot.Unavailable;
            try
            {
                if (!EnsureHandles())
                {
                    return false;
                }

                snapshot = new DebugCheatToggleSnapshot(
                    available: true,
                    infiniteSoul: GetBool(infiniteSoulField, null),
                    infiniteHp: GetBool(infiniteHpField, null),
                    noclip: GetBool(noclipField, null),
                    keyBindLock: GetBool(keyBindLockField, null));
                return true;
            }
            catch (Exception ex)
            {
                Modding.Logger.LogWarn($"ReplayLogger: failed to read DebugMod cheat toggles: {ex.Message}");
                snapshot = DebugCheatToggleSnapshot.Unavailable;
                return false;
            }
        }
    }

    internal readonly struct DebugCheatToggleSnapshot
    {
        public static DebugCheatToggleSnapshot Unavailable => new(false, false, false, false, false);

        public DebugCheatToggleSnapshot(bool available, bool infiniteSoul, bool infiniteHp, bool noclip, bool keyBindLock)
        {
            Available = available;
            InfiniteSoul = infiniteSoul;
            InfiniteHp = infiniteHp;
            Noclip = noclip;
            KeyBindLock = keyBindLock;
        }

        public bool Available { get; }
        public bool InfiniteSoul { get; }
        public bool InfiniteHp { get; }
        public bool Noclip { get; }
        public bool KeyBindLock { get; }
    }
}
