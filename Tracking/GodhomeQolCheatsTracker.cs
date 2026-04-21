using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MonoMod.RuntimeDetour;

namespace ReplayLogger
{
    internal sealed class GodhomeQolCheatsTracker
    {
        private const string GodhomeQolTypeName = "GodhomeQoL.GodhomeQoL";
        private const string CheatsTypeName = "GodhomeQoL.Modules.Cheats.Cheats";
        private const string ModuleManagerTypeName = "GodhomeQoL.ModuleManager";
        private const string CheatsModuleName = "Cheats";
        private const string UnknownArena = "UnknownArena";

        private static readonly object KillAllHookSync = new();
        private static Hook killAllHook;
        private static WeakReference<GodhomeQolCheatsTracker> activeTrackerRef;

        private bool hasInitialState;
        private string initialArenaName;
        private string currentArenaName;
        private long currentBaseUnixTime;
        private CheatsState initialState;
        private CheatsState currentState;
        private bool hasCurrentState;
        private readonly List<string> changes = new();

        private Type godhomeQolType;
        private bool godhomeQolTypeResolved;
        private PropertyInfo globalSettingsProperty;

        private Type moduleManagerType;
        private bool moduleManagerTypeResolved;
        private PropertyInfo modulesProperty;
        private FieldInfo modulesField;

        private Type cheatsType;
        private bool cheatsTypeResolved;
        private MethodInfo getInfiniteSoulMethod;
        private MethodInfo getInfiniteHpMethod;
        private MethodInfo getInvincibilityMethod;
        private MethodInfo getNoclipMethod;
        private MethodInfo getKillAllHotkeyRawMethod;
        private bool cheatsMethodsResolved;

        public bool HasData => hasInitialState || changes.Count > 0;

        public void Reset()
        {
            ClearActiveTrackerIfOwned();
            hasInitialState = false;
            initialArenaName = null;
            currentArenaName = null;
            currentBaseUnixTime = 0;
            initialState = default;
            currentState = default;
            hasCurrentState = false;
            changes.Clear();
        }

        public void StartFight(string arenaName, long baseUnixTime)
        {
            currentArenaName = NormalizeArenaName(arenaName);
            currentBaseUnixTime = baseUnixTime;
            EnsureKillAllHook();
            SetActiveTracker(this);

            long now = baseUnixTime > 0 ? baseUnixTime : DateTimeOffset.Now.ToUnixTimeMilliseconds();
            if (!TryBuildState(out CheatsState snapshot))
            {
                return;
            }

            if (!hasInitialState)
            {
                hasInitialState = true;
                initialArenaName = currentArenaName;
                initialState = snapshot;
                currentState = snapshot;
                hasCurrentState = true;
                return;
            }

            if (!hasCurrentState)
            {
                currentState = snapshot;
                hasCurrentState = true;
                return;
            }

            LogStateChanges(currentState, snapshot, now);
            currentState = snapshot;
        }

        public void Update(string arenaName, long nowUnixTime)
        {
            if (!string.IsNullOrWhiteSpace(arenaName))
            {
                currentArenaName = arenaName;
            }

            EnsureKillAllHook();
            SetActiveTracker(this);

            long now = nowUnixTime > 0 ? nowUnixTime : DateTimeOffset.Now.ToUnixTimeMilliseconds();
            if (!TryBuildState(out CheatsState snapshot))
            {
                return;
            }

            if (!hasInitialState)
            {
                hasInitialState = true;
                initialArenaName = currentArenaName;
                initialState = snapshot;
                currentState = snapshot;
                hasCurrentState = true;
                return;
            }

            if (!hasCurrentState)
            {
                currentState = snapshot;
                hasCurrentState = true;
                return;
            }

            LogStateChanges(currentState, snapshot, now);
            currentState = snapshot;
        }

        public void WriteSection(StreamWriter writer)
        {
            if (writer == null || !HasData)
            {
                return;
            }

            CheatsState stateToWrite = hasInitialState
                ? initialState
                : (hasCurrentState ? currentState : default);

            List<string> batch = TempObjectPools.RentStringList(changes.Count + 12);
            try
            {
                batch.Add("  Cheats:");
                if (!string.IsNullOrEmpty(initialArenaName))
                {
                    batch.Add($"    Initial Arena: {initialArenaName}");
                }

                batch.Add("    State:");
                batch.Add($"      Enable Cheats (Master): {FormatOptionalBool(stateToWrite.MasterEnabled)}");
                batch.Add($"      Cheats Module: {FormatOptionalBool(stateToWrite.ModuleEnabled)}");
                batch.Add($"      Infinite Soul: {FormatOptionalBool(stateToWrite.InfiniteSoulEnabled)}");
                batch.Add($"      Infinite HP: {FormatOptionalBool(stateToWrite.InfiniteHpEnabled)}");
                batch.Add($"      Invincibility: {FormatOptionalBool(stateToWrite.InvincibilityEnabled)}");
                batch.Add($"      Noclip: {FormatOptionalBool(stateToWrite.NoclipEnabled)}");
                batch.Add($"      Kill All Hotkey: {FormatOptionalHotkey(stateToWrite.KillAllHotkeyRaw)}");
                batch.Add("    Changes:");
                if (changes.Count == 0)
                {
                    batch.Add("      (none)");
                }
                else
                {
                    for (int i = 0; i < changes.Count; i++)
                    {
                        batch.Add($"      {changes[i]}");
                    }
                }

                LogWrite.EncryptedLines(writer, batch);
            }
            finally
            {
                TempObjectPools.ReturnStringList(batch);
            }
        }

        private void LogStateChanges(CheatsState previous, CheatsState current, long now)
        {
            LogBoolChange("Enable Cheats (Master)", previous.MasterEnabled, current.MasterEnabled, now);
            LogBoolChange("Cheats Module", previous.ModuleEnabled, current.ModuleEnabled, now);
            LogBoolChange("Infinite Soul", previous.InfiniteSoulEnabled, current.InfiniteSoulEnabled, now);
            LogBoolChange("Infinite HP", previous.InfiniteHpEnabled, current.InfiniteHpEnabled, now);
            LogBoolChange("Invincibility", previous.InvincibilityEnabled, current.InvincibilityEnabled, now);
            LogBoolChange("Noclip", previous.NoclipEnabled, current.NoclipEnabled, now);
            LogStringChange("Kill All Hotkey", previous.KillAllHotkeyRaw, current.KillAllHotkeyRaw, now, isHotkey: true);
        }

        private void LogBoolChange(string key, Optional<bool> previous, Optional<bool> current, long now)
        {
            if (previous == current)
            {
                return;
            }

            AppendChange(now, $"{key}: {FormatOptionalBool(previous)} -> {FormatOptionalBool(current)}");
        }

        private void LogStringChange(string key, Optional<string> previous, Optional<string> current, long now, bool isHotkey)
        {
            if (previous == current)
            {
                return;
            }

            string left = isHotkey ? FormatOptionalHotkey(previous) : FormatOptionalString(previous);
            string right = isHotkey ? FormatOptionalHotkey(current) : FormatOptionalString(current);
            AppendChange(now, $"{key}: {left} -> {right}");
        }

        private void AppendChange(long now, string descriptor)
        {
            string arena = NormalizeArenaName(currentArenaName);
            long delta = currentBaseUnixTime > 0 ? Math.Max(0, now - currentBaseUnixTime) : 0;
            changes.Add($"|{arena}|+{delta}|{descriptor}");
        }

        private void RecordKillAllActivation(long now, int killedCount)
        {
            if (!hasInitialState && TryBuildState(out CheatsState snapshot))
            {
                hasInitialState = true;
                initialArenaName = NormalizeArenaName(currentArenaName);
                initialState = snapshot;
                currentState = snapshot;
                hasCurrentState = true;
            }

            AppendChange(now, $"Kill All: Executed (killed: {killedCount})");
        }

        private static string NormalizeArenaName(string arenaName)
        {
            return string.IsNullOrWhiteSpace(arenaName) ? UnknownArena : arenaName;
        }

        private bool TryBuildState(out CheatsState state)
        {
            Optional<bool> masterEnabled = TryGetCheatsMasterEnabled(out bool masterFlag)
                ? new Optional<bool>(masterFlag)
                : Optional<bool>.None;

            Optional<bool> moduleEnabled = TryGetCheatsModuleEnabled(out bool moduleFlag)
                ? new Optional<bool>(moduleFlag)
                : Optional<bool>.None;

            Optional<bool> infiniteSoulEnabled = TryGetCheatsFlag(getInfiniteSoulMethod, "GetInfiniteSoulEnabled", out bool infiniteSoulFlag)
                ? new Optional<bool>(infiniteSoulFlag)
                : Optional<bool>.None;

            Optional<bool> infiniteHpEnabled = TryGetCheatsFlag(getInfiniteHpMethod, "GetInfiniteHpEnabled", out bool infiniteHpFlag)
                ? new Optional<bool>(infiniteHpFlag)
                : Optional<bool>.None;

            Optional<bool> invincibilityEnabled = TryGetCheatsFlag(getInvincibilityMethod, "GetInvincibilityEnabled", out bool invincibilityFlag)
                ? new Optional<bool>(invincibilityFlag)
                : Optional<bool>.None;

            Optional<bool> noclipEnabled = TryGetCheatsFlag(getNoclipMethod, "GetNoclipEnabled", out bool noclipFlag)
                ? new Optional<bool>(noclipFlag)
                : Optional<bool>.None;

            Optional<string> killAllHotkeyRaw = TryGetKillAllHotkeyRaw(out string hotkeyRaw)
                ? new Optional<string>(hotkeyRaw ?? string.Empty)
                : Optional<string>.None;

            state = new CheatsState(
                masterEnabled,
                moduleEnabled,
                infiniteSoulEnabled,
                infiniteHpEnabled,
                invincibilityEnabled,
                noclipEnabled,
                killAllHotkeyRaw);

            return state.HasAnyData;
        }

        private bool TryGetCheatsMasterEnabled(out bool value)
        {
            value = false;

            object globalSettings = GetGlobalSettings();
            if (globalSettings == null)
            {
                return false;
            }

            if (!ReflectionMemberAccessCache.TryGetCachedRuntimePropertyValue(globalSettings, "QuickMenuMasters", out object quickMenuMasters) ||
                quickMenuMasters == null)
            {
                return false;
            }

            return ReflectionMemberAccessCache.TryGetCachedRuntimeBoolProperty(quickMenuMasters, "CheatsEnabled", out value);
        }

        private bool TryGetCheatsModuleEnabled(out bool value)
        {
            value = false;

            IDictionary modules = GetModuleMap();
            if (modules == null || modules.Count == 0)
            {
                return false;
            }

            foreach (DictionaryEntry entry in modules)
            {
                object module = entry.Value;
                if (module == null)
                {
                    continue;
                }

                if (!IsCheatsModuleEntry(entry.Key, module))
                {
                    continue;
                }

                if (ReflectionMemberAccessCache.TryGetCachedRuntimeBoolProperty(module, "Enabled", out bool enabled))
                {
                    value = enabled;
                    return true;
                }
            }

            return false;
        }

        private bool IsCheatsModuleEntry(object key, object module)
        {
            string keyText = key?.ToString() ?? string.Empty;
            if (string.Equals(keyText, CheatsModuleName, StringComparison.Ordinal) ||
                string.Equals(keyText, CheatsTypeName, StringComparison.Ordinal))
            {
                return true;
            }

            Type moduleType = module.GetType();
            if (moduleType == null)
            {
                return false;
            }

            if (string.Equals(moduleType.Name, CheatsModuleName, StringComparison.Ordinal))
            {
                return true;
            }

            if (string.Equals(moduleType.FullName, CheatsTypeName, StringComparison.Ordinal))
            {
                return true;
            }

            Type targetCheatsType = GetCheatsType();
            return targetCheatsType != null && targetCheatsType.IsAssignableFrom(moduleType);
        }

        private bool TryGetCheatsFlag(MethodInfo methodCache, string methodName, out bool value)
        {
            value = false;
            EnsureCheatsMethods();

            MethodInfo method = methodCache;
            if (method == null)
            {
                Type type = GetCheatsType();
                if (type == null)
                {
                    return false;
                }

                const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                method = type.GetMethod(methodName, flags);
                CacheCheatsMethod(methodName, method);
            }

            try
            {
                object raw = method?.InvokeCached(null);
                if (raw is bool flag)
                {
                    value = flag;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private bool TryGetKillAllHotkeyRaw(out string value)
        {
            value = string.Empty;
            EnsureCheatsMethods();

            if (getKillAllHotkeyRawMethod == null)
            {
                Type type = GetCheatsType();
                if (type == null)
                {
                    return false;
                }

                const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                getKillAllHotkeyRawMethod = type.GetMethod("GetKillAllHotkeyRaw", flags);
            }

            try
            {
                object raw = getKillAllHotkeyRawMethod?.InvokeCached(null);
                if (raw == null)
                {
                    value = string.Empty;
                    return true;
                }

                value = raw.ToString() ?? string.Empty;
                return true;
            }
            catch
            {
            }

            return false;
        }

        private void EnsureCheatsMethods()
        {
            if (cheatsMethodsResolved)
            {
                return;
            }

            Type type = GetCheatsType();
            if (type != null)
            {
                const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                getInfiniteSoulMethod = type.GetMethod("GetInfiniteSoulEnabled", flags);
                getInfiniteHpMethod = type.GetMethod("GetInfiniteHpEnabled", flags);
                getInvincibilityMethod = type.GetMethod("GetInvincibilityEnabled", flags);
                getNoclipMethod = type.GetMethod("GetNoclipEnabled", flags);
                getKillAllHotkeyRawMethod = type.GetMethod("GetKillAllHotkeyRaw", flags);
            }

            cheatsMethodsResolved = true;
        }

        private void CacheCheatsMethod(string methodName, MethodInfo method)
        {
            if (method == null)
            {
                return;
            }

            switch (methodName)
            {
                case "GetInfiniteSoulEnabled":
                    getInfiniteSoulMethod = method;
                    break;
                case "GetInfiniteHpEnabled":
                    getInfiniteHpMethod = method;
                    break;
                case "GetInvincibilityEnabled":
                    getInvincibilityMethod = method;
                    break;
                case "GetNoclipEnabled":
                    getNoclipMethod = method;
                    break;
            }
        }

        private object GetGlobalSettings()
        {
            Type type = GetGodhomeQolType();
            if (type == null)
            {
                return null;
            }

            if (globalSettingsProperty == null)
            {
                const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                globalSettingsProperty = type.GetProperty("GlobalSettings", flags);
            }

            try
            {
                return globalSettingsProperty?.GetCachedValue(null);
            }
            catch
            {
            }

            return null;
        }

        private IDictionary GetModuleMap()
        {
            Type type = GetModuleManagerType();
            if (type == null)
            {
                return null;
            }

            if (modulesProperty == null && modulesField == null)
            {
                const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                modulesProperty = type.GetProperty("Modules", flags);
                modulesField = type.GetField("Modules", flags) ?? type.GetField("modules", flags);
            }

            try
            {
                object raw = modulesProperty?.GetCachedValue(null) ?? modulesField?.GetCachedValue(null);
                if (raw is IDictionary dict)
                {
                    return dict;
                }

                if (ReflectionMemberAccessCache.TryGetCachedRuntimePropertyValue(raw, "Value", out object value))
                {
                    return value as IDictionary;
                }
            }
            catch
            {
            }

            return null;
        }

        private Type GetGodhomeQolType()
        {
            if (!godhomeQolTypeResolved)
            {
                godhomeQolType = FindType(GodhomeQolTypeName);
                godhomeQolTypeResolved = true;
            }

            return godhomeQolType;
        }

        private Type GetModuleManagerType()
        {
            if (!moduleManagerTypeResolved)
            {
                moduleManagerType = FindType(ModuleManagerTypeName);
                moduleManagerTypeResolved = true;
            }

            return moduleManagerType;
        }

        private Type GetCheatsType()
        {
            if (!cheatsTypeResolved)
            {
                cheatsType = FindType(CheatsTypeName);
                cheatsTypeResolved = true;
            }

            return cheatsType;
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

        private void EnsureKillAllHook()
        {
            lock (KillAllHookSync)
            {
                if (killAllHook != null)
                {
                    return;
                }

                Type type = GetCheatsType();
                if (type == null)
                {
                    return;
                }

                const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                MethodInfo killAllMethod = type.GetMethod("KillAll", flags, null, Type.EmptyTypes, null);
                if (killAllMethod == null)
                {
                    return;
                }

                MethodInfo detour = typeof(GodhomeQolCheatsTracker).GetMethod(
                    nameof(KillAllDetour),
                    BindingFlags.Static | BindingFlags.NonPublic);

                if (detour == null)
                {
                    return;
                }

                try
                {
                    killAllHook = new Hook(killAllMethod, detour);
                }
                catch
                {
                }
            }
        }

        private static void SetActiveTracker(GodhomeQolCheatsTracker tracker)
        {
            lock (KillAllHookSync)
            {
                if (tracker == null)
                {
                    activeTrackerRef = null;
                    return;
                }

                activeTrackerRef = new WeakReference<GodhomeQolCheatsTracker>(tracker);
            }
        }

        private static bool TryGetActiveTracker(out GodhomeQolCheatsTracker tracker)
        {
            tracker = null;
            lock (KillAllHookSync)
            {
                if (activeTrackerRef == null)
                {
                    return false;
                }

                return activeTrackerRef.TryGetTarget(out tracker) && tracker != null;
            }
        }

        private void ClearActiveTrackerIfOwned()
        {
            lock (KillAllHookSync)
            {
                if (activeTrackerRef == null)
                {
                    return;
                }

                if (activeTrackerRef.TryGetTarget(out GodhomeQolCheatsTracker tracker) && ReferenceEquals(tracker, this))
                {
                    activeTrackerRef = null;
                }
            }
        }

        private delegate int OrigKillAll();

        private static int KillAllDetour(OrigKillAll orig)
        {
            int killed = 0;
            bool executed = false;
            try
            {
                if (orig != null)
                {
                    killed = orig();
                    executed = true;
                }

                return killed;
            }
            finally
            {
                if (executed && TryGetActiveTracker(out GodhomeQolCheatsTracker tracker))
                {
                    tracker.RecordKillAllActivation(DateTimeOffset.Now.ToUnixTimeMilliseconds(), killed);
                }
            }
        }

        private static string FormatOptionalBool(Optional<bool> value)
        {
            if (!value.HasValue)
            {
                return "N/A";
            }

            return value.Value ? "On" : "Off";
        }

        private static string FormatOptionalString(Optional<string> value)
        {
            if (!value.HasValue)
            {
                return "N/A";
            }

            string text = value.Value;
            return string.IsNullOrEmpty(text) ? "(empty)" : text;
        }

        private static string FormatOptionalHotkey(Optional<string> value)
        {
            if (!value.HasValue)
            {
                return "N/A";
            }

            string raw = value.Value ?? string.Empty;
            return string.IsNullOrWhiteSpace(raw) ? "Not set" : raw;
        }

        private readonly struct CheatsState
        {
            internal CheatsState(
                Optional<bool> masterEnabled,
                Optional<bool> moduleEnabled,
                Optional<bool> infiniteSoulEnabled,
                Optional<bool> infiniteHpEnabled,
                Optional<bool> invincibilityEnabled,
                Optional<bool> noclipEnabled,
                Optional<string> killAllHotkeyRaw)
            {
                MasterEnabled = masterEnabled;
                ModuleEnabled = moduleEnabled;
                InfiniteSoulEnabled = infiniteSoulEnabled;
                InfiniteHpEnabled = infiniteHpEnabled;
                InvincibilityEnabled = invincibilityEnabled;
                NoclipEnabled = noclipEnabled;
                KillAllHotkeyRaw = killAllHotkeyRaw;
            }

            internal Optional<bool> MasterEnabled { get; }
            internal Optional<bool> ModuleEnabled { get; }
            internal Optional<bool> InfiniteSoulEnabled { get; }
            internal Optional<bool> InfiniteHpEnabled { get; }
            internal Optional<bool> InvincibilityEnabled { get; }
            internal Optional<bool> NoclipEnabled { get; }
            internal Optional<string> KillAllHotkeyRaw { get; }

            internal bool HasAnyData =>
                MasterEnabled.HasValue ||
                ModuleEnabled.HasValue ||
                InfiniteSoulEnabled.HasValue ||
                InfiniteHpEnabled.HasValue ||
                InvincibilityEnabled.HasValue ||
                NoclipEnabled.HasValue ||
                KillAllHotkeyRaw.HasValue;
        }
    }
}
