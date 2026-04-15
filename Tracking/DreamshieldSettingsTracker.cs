using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace ReplayLogger
{
    internal sealed class DreamshieldSettingsTracker
    {
        private bool hasInitialState;
        private string initialArenaName;
        private string currentArenaName;
        private long currentBaseUnixTime;
        private DreamshieldState initialState;
        private DreamshieldState currentState;
        private bool hasCurrentState;
        private readonly List<string> changes = new();
        private Optional<float> lastLoggedRotationDelay;
        private Optional<float> lastLoggedRotationDelayInGame;
        private Optional<float> lastLoggedRotationSpeed;
        private Optional<string> lastLoggedRotationAnglesInGame;

        private Type dreamshieldType;
        private bool dreamshieldResolved;
        private FieldInfo startAngleEnabledField;
        private PropertyInfo startAngleEnabledProperty;
        private FieldInfo rotationDelayField;
        private PropertyInfo rotationDelayProperty;
        private FieldInfo rotationSpeedField;
        private PropertyInfo rotationSpeedProperty;

        private PlayMakerFSM dreamshieldControlFsm;
        private int dreamshieldControlFsmId;
        private Rotate cachedRotateAction;
        private int cachedRotateActionFsmId;
        private long lastFsmSearchTime;
        private string fsmCacheSceneName;
        private const int FsmSearchThrottleMs = 1000;

        public bool HasData => hasInitialState || changes.Count > 0;

        public void Reset()
        {
            hasInitialState = false;
            initialArenaName = null;
            currentArenaName = null;
            currentBaseUnixTime = 0;
            initialState = default;
            currentState = default;
            hasCurrentState = false;
            changes.Clear();
            dreamshieldControlFsm = null;
            dreamshieldControlFsmId = 0;
            cachedRotateAction = null;
            cachedRotateActionFsmId = 0;
            lastFsmSearchTime = 0;
            fsmCacheSceneName = null;
            PlayMakerFsmSceneCache.Invalidate();
            lastLoggedRotationDelay = Optional<float>.None;
            lastLoggedRotationDelayInGame = Optional<float>.None;
            lastLoggedRotationSpeed = Optional<float>.None;
            lastLoggedRotationAnglesInGame = Optional<string>.None;
        }

        public void StartFight(string arenaName, long baseUnixTime)
        {
            currentArenaName = string.IsNullOrWhiteSpace(arenaName) ? "UnknownArena" : arenaName;
            currentBaseUnixTime = baseUnixTime;
            long now = baseUnixTime;
            dreamshieldControlFsm = null;
            dreamshieldControlFsmId = 0;
            cachedRotateAction = null;
            cachedRotateActionFsmId = 0;
            lastFsmSearchTime = 0;
            fsmCacheSceneName = null;
            PlayMakerFsmSceneCache.Invalidate();

            DreamshieldState snapshot = BuildState(now);

            if (!hasInitialState)
            {
                hasInitialState = true;
                initialArenaName = currentArenaName;
                initialState = snapshot;
                currentState = snapshot;
                hasCurrentState = true;
                UpdateSliderBaseline(snapshot);
                return;
            }

            if (!hasCurrentState)
            {
                currentState = snapshot;
                hasCurrentState = true;
                UpdateSliderBaseline(snapshot);
                return;
            }

            LogFieldChange("Dreamshield Start Angle", currentState.StartAngle, snapshot.StartAngle, now);
            LogFieldChange("Rotation Delay (sec)", currentState.RotationDelay, snapshot.RotationDelay, now);
            LogFieldChange("Rotation Speed Multiplier", currentState.RotationSpeed, snapshot.RotationSpeed, now);
            LogFieldChange("Dreamshield Start Angle (In-Game)", currentState.StartAngleInGame, snapshot.StartAngleInGame, now);
            LogFieldChange("Rotation Delay (In-Game)", currentState.RotationDelayInGame, snapshot.RotationDelayInGame, now);
            LogFieldChange("Rotation Angles (In-Game)", currentState.RotationAnglesInGame, snapshot.RotationAnglesInGame, now);

            currentState = snapshot;
            UpdateSliderBaseline(snapshot);
        }

        public void Update(string arenaName, long nowUnixTime)
        {
            if (!hasInitialState)
            {
                return;
            }

            if (!string.IsNullOrEmpty(arenaName) &&
                !string.Equals(currentArenaName, arenaName, StringComparison.Ordinal))
            {
                return;
            }

            long now = nowUnixTime;
            DreamshieldState snapshot = BuildState(now);

            if (!hasCurrentState)
            {
                currentState = snapshot;
                hasCurrentState = true;
                UpdateSliderBaseline(snapshot);
                return;
            }

            LogFieldChange("Dreamshield Start Angle", currentState.StartAngle, snapshot.StartAngle, now);
            LogFieldChange("Dreamshield Start Angle (In-Game)", currentState.StartAngleInGame, snapshot.StartAngleInGame, now);

            LogSliderChanges(snapshot, now);

            currentState = snapshot;
        }

        public void WriteSection(StreamWriter writer)
        {
            if (writer == null)
            {
                return;
            }

            if (!HasData)
            {
                return;
            }

            List<string> batch = TempObjectPools.RentStringList(changes.Count + 10);
            try
            {
                batch.Add("  Dreamshield Settings:");
                if (!string.IsNullOrEmpty(initialArenaName))
                {
                    batch.Add($"    Initial Arena: {initialArenaName}");
                }
                batch.Add("    State:");
                batch.Add($"      Dreamshield Start Angle: {FormatOptionalToggle(initialState.StartAngle)}");
                batch.Add($"      Rotation Delay (sec): {FormatOptionalFloat(initialState.RotationDelay)}");
                batch.Add($"      Rotation Speed Multiplier: {FormatOptionalFloat(initialState.RotationSpeed)}");
                batch.Add($"      Dreamshield Start Angle (In-Game): {FormatOptionalToggle(initialState.StartAngleInGame)}");
                batch.Add($"      Rotation Delay (In-Game): {FormatOptionalFloat(initialState.RotationDelayInGame)}");
                batch.Add($"      Rotation Angles (In-Game): {FormatOptionalString(initialState.RotationAnglesInGame)}");
                batch.Add("    Changes:");
                if (changes.Count == 0)
                {
                    batch.Add("      (none)");
                }
                else
                {
                    foreach (string change in changes)
                    {
                        batch.Add($"      {change}");
                    }
                }

                LogWrite.EncryptedLines(writer, batch);
            }
            finally
            {
                TempObjectPools.ReturnStringList(batch);
            }
        }

        private DreamshieldState BuildState(long nowUnixTime)
        {
            bool hasStartAngle = TryGetStartAngleEnabled(out bool startAngleEnabled);
            bool hasDelay = TryGetRotationDelay(out float rotationDelay);
            bool hasSpeed = TryGetRotationSpeed(out float rotationSpeed);

            bool hasFsm = TryGetDreamshieldControlFsm(out PlayMakerFSM fsm, nowUnixTime);
            bool startAngleInGame = hasFsm && hasStartAngle && startAngleEnabled;

            Optional<string> inGameAngles = Optional<string>.None;
            if (startAngleInGame && TryGetRotationAnglesInGame(fsm, out string angles) && !string.IsNullOrEmpty(angles))
            {
                inGameAngles = new Optional<string>(angles);
            }

            return new DreamshieldState(
                hasStartAngle ? new Optional<bool>(startAngleEnabled) : Optional<bool>.None,
                hasDelay ? new Optional<float>(NormalizeFloat(rotationDelay)) : Optional<float>.None,
                hasSpeed ? new Optional<float>(NormalizeFloat(rotationSpeed)) : Optional<float>.None,
                hasFsm && hasStartAngle ? new Optional<bool>(startAngleEnabled) : Optional<bool>.None,
                startAngleInGame && hasDelay ? new Optional<float>(NormalizeFloat(rotationDelay)) : Optional<float>.None,
                inGameAngles);
        }

        private void LogSliderChanges(DreamshieldState snapshot, long now)
        {
            Optional<float> delayValue = snapshot.RotationDelay;
            Optional<float> delayInGameValue = snapshot.RotationDelayInGame;
            Optional<float> speedValue = snapshot.RotationSpeed;
            Optional<string> speedInGameValue = snapshot.RotationAnglesInGame;

            bool delayChanged = delayValue != lastLoggedRotationDelay;
            bool delayInGameChanged = delayInGameValue != lastLoggedRotationDelayInGame;
            bool speedChanged = speedValue != lastLoggedRotationSpeed;
            bool speedInGameChanged = speedInGameValue != lastLoggedRotationAnglesInGame;

            if (!delayChanged && !delayInGameChanged && !speedChanged && !speedInGameChanged)
            {
                return;
            }

            if (delayChanged)
            {
                string descriptor = !lastLoggedRotationDelay.HasValue
                    ? $"Rotation Delay (sec): {FormatOptionalFloat(delayValue)}"
                    : $"Rotation Delay (sec): {FormatOptionalFloat(lastLoggedRotationDelay)} -> {FormatOptionalFloat(delayValue)}";
                long delta = currentBaseUnixTime > 0 ? now - currentBaseUnixTime : 0;
                changes.Add($"|{currentArenaName}|+{delta}|{descriptor}");
                lastLoggedRotationDelay = delayValue;
            }

            if (delayInGameChanged)
            {
                string descriptor = !lastLoggedRotationDelayInGame.HasValue
                    ? $"Rotation Delay (In-Game): {FormatOptionalFloat(delayInGameValue)}"
                    : $"Rotation Delay (In-Game): {FormatOptionalFloat(lastLoggedRotationDelayInGame)} -> {FormatOptionalFloat(delayInGameValue)}";
                long delta = currentBaseUnixTime > 0 ? now - currentBaseUnixTime : 0;
                changes.Add($"|{currentArenaName}|+{delta}|{descriptor}");
                lastLoggedRotationDelayInGame = delayInGameValue;
            }

            if (speedChanged)
            {
                string descriptor = !lastLoggedRotationSpeed.HasValue
                    ? $"Rotation Speed Multiplier: {FormatOptionalFloat(speedValue)}"
                    : $"Rotation Speed Multiplier: {FormatOptionalFloat(lastLoggedRotationSpeed)} -> {FormatOptionalFloat(speedValue)}";
                long delta = currentBaseUnixTime > 0 ? now - currentBaseUnixTime : 0;
                changes.Add($"|{currentArenaName}|+{delta}|{descriptor}");
                lastLoggedRotationSpeed = speedValue;
            }

            if (speedInGameChanged)
            {
                string descriptor = !lastLoggedRotationAnglesInGame.HasValue
                    ? $"Rotation Angles (In-Game): {FormatOptionalString(speedInGameValue)}"
                    : $"Rotation Angles (In-Game): {FormatOptionalString(lastLoggedRotationAnglesInGame)} -> {FormatOptionalString(speedInGameValue)}";
                long delta = currentBaseUnixTime > 0 ? now - currentBaseUnixTime : 0;
                changes.Add($"|{currentArenaName}|+{delta}|{descriptor}");
                lastLoggedRotationAnglesInGame = speedInGameValue;
            }
        }

        private void UpdateSliderBaseline(DreamshieldState snapshot)
        {
            lastLoggedRotationDelay = snapshot.RotationDelay;
            lastLoggedRotationDelayInGame = snapshot.RotationDelayInGame;
            lastLoggedRotationSpeed = snapshot.RotationSpeed;
            lastLoggedRotationAnglesInGame = snapshot.RotationAnglesInGame;
        }

        private void LogFieldChange(string key, Optional<bool> previous, Optional<bool> current, long now)
        {
            if (previous == current)
            {
                return;
            }

            string descriptor = $"{key}: {FormatOptionalToggle(previous)} -> {FormatOptionalToggle(current)}";
            long delta = currentBaseUnixTime > 0 ? now - currentBaseUnixTime : 0;
            changes.Add($"|{currentArenaName}|+{delta}|{descriptor}");
        }

        private void LogFieldChange(string key, Optional<float> previous, Optional<float> current, long now)
        {
            if (previous == current)
            {
                return;
            }

            string descriptor = $"{key}: {FormatOptionalFloat(previous)} -> {FormatOptionalFloat(current)}";
            long delta = currentBaseUnixTime > 0 ? now - currentBaseUnixTime : 0;
            changes.Add($"|{currentArenaName}|+{delta}|{descriptor}");
        }

        private void LogFieldChange(string key, Optional<string> previous, Optional<string> current, long now)
        {
            if (previous == current)
            {
                return;
            }

            string descriptor = $"{key}: {FormatOptionalString(previous)} -> {FormatOptionalString(current)}";
            long delta = currentBaseUnixTime > 0 ? now - currentBaseUnixTime : 0;
            changes.Add($"|{currentArenaName}|+{delta}|{descriptor}");
        }

        private static float NormalizeFloat(float value)
        {
            return (float)Math.Round(value, 2, MidpointRounding.ToEven);
        }

        private static string FormatOptionalToggle(Optional<bool> value)
        {
            return value.HasValue ? FormatToggle(value.Value) : "N/A";
        }

        private static string FormatOptionalFloat(Optional<float> value)
        {
            return value.HasValue
                ? value.Value.ToString("0.##", CultureInfo.InvariantCulture)
                : "N/A";
        }

        private static string FormatOptionalString(Optional<string> value)
        {
            return value.HasValue && !string.IsNullOrEmpty(value.Value)
                ? value.Value
                : "N/A";
        }

        private readonly struct DreamshieldState
        {
            internal DreamshieldState(
                Optional<bool> startAngle,
                Optional<float> rotationDelay,
                Optional<float> rotationSpeed,
                Optional<bool> startAngleInGame,
                Optional<float> rotationDelayInGame,
                Optional<string> rotationAnglesInGame)
            {
                StartAngle = startAngle;
                RotationDelay = rotationDelay;
                RotationSpeed = rotationSpeed;
                StartAngleInGame = startAngleInGame;
                RotationDelayInGame = rotationDelayInGame;
                RotationAnglesInGame = rotationAnglesInGame;
            }

            internal Optional<bool> StartAngle { get; }
            internal Optional<float> RotationDelay { get; }
            internal Optional<float> RotationSpeed { get; }
            internal Optional<bool> StartAngleInGame { get; }
            internal Optional<float> RotationDelayInGame { get; }
            internal Optional<string> RotationAnglesInGame { get; }
        }

        private bool TryGetStartAngleEnabled(out bool enabled)
        {
            return TryGetBoolSetting("startAngleEnabled", "StartAngleEnabled", ref startAngleEnabledField, ref startAngleEnabledProperty, out enabled);
        }

        private bool TryGetRotationDelay(out float delay)
        {
            return TryGetFloatSetting("rotationDelay", "RotationDelay", ref rotationDelayField, ref rotationDelayProperty, out delay);
        }

        private bool TryGetRotationSpeed(out float speed)
        {
            return TryGetFloatSetting("rotationSpeed", "RotationSpeed", ref rotationSpeedField, ref rotationSpeedProperty, out speed);
        }

        private bool TryGetRotationAnglesInGame(PlayMakerFSM fsm, out string angles)
        {
            angles = null;
            if (fsm == null)
            {
                return false;
            }

            if (cachedRotateAction != null && cachedRotateActionFsmId == fsm.GetInstanceID())
            {
                if (TryReadRotationAngles(cachedRotateAction, out angles))
                {
                    return true;
                }

                cachedRotateAction = null;
                cachedRotateActionFsmId = 0;
            }

            Rotate rotate = FindRotateAction(fsm);
            if (rotate == null)
            {
                return false;
            }

            cachedRotateAction = rotate;
            cachedRotateActionFsmId = fsm.GetInstanceID();
            return TryReadRotationAngles(rotate, out angles);
        }

        private Rotate FindRotateAction(PlayMakerFSM fsm)
        {
            if (fsm == null)
            {
                return null;
            }

            try
            {
                FsmState state = fsm.Fsm?.GetState("Follow");
                if (state?.Actions == null)
                {
                    return null;
                }

                foreach (FsmStateAction action in state.Actions)
                {
                    if (action is Rotate rotate)
                    {
                        return rotate;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static bool TryReadRotationAngles(Rotate rotate, out string angles)
        {
            angles = null;
            if (rotate == null)
            {
                return false;
            }

            float xAngle = GetRotateFloat(rotate, "xAngle");
            float yAngle = GetRotateFloat(rotate, "yAngle");
            float zAngle = GetRotateFloat(rotate, "zAngle");
            angles = string.Format(
                CultureInfo.InvariantCulture,
                "x={0:0.##}, y={1:0.##}, z={2:0.##}",
                xAngle,
                yAngle,
                zAngle);
            return true;
        }

        private bool TryGetDreamshieldControlFsm(out PlayMakerFSM fsm, long nowUnixTime)
        {
            EnsureFsmSceneCacheCurrent();

            fsm = dreamshieldControlFsm;
            if (IsDreamshieldControlFsmValid(fsm))
            {
                return true;
            }

            long now = nowUnixTime;
            if (lastFsmSearchTime > 0 && now - lastFsmSearchTime < FsmSearchThrottleMs)
            {
                return false;
            }

            lastFsmSearchTime = now;

            PlayMakerFSM best = FindDreamshieldFsm(currentArenaName, forceRefresh: false);
            if (best == null && !string.IsNullOrEmpty(currentArenaName))
            {
                best = FindDreamshieldFsm(null, forceRefresh: false);
            }

            if (best == null)
            {
                best = FindDreamshieldFsm(currentArenaName, forceRefresh: true);
                if (best == null && !string.IsNullOrEmpty(currentArenaName))
                {
                    best = FindDreamshieldFsm(null, forceRefresh: true);
                }
            }

            if (best == null)
            {
                return false;
            }

            dreamshieldControlFsm = best;
            int instanceId = best.GetInstanceID();
            if (dreamshieldControlFsmId != instanceId)
            {
                dreamshieldControlFsmId = instanceId;
                cachedRotateAction = null;
                cachedRotateActionFsmId = 0;
            }

            fsm = best;
            return true;
        }

        private static bool IsDreamshieldControlFsmValid(PlayMakerFSM fsm)
        {
            if (fsm == null)
            {
                return false;
            }

            GameObject go = fsm.gameObject;
            if (go == null || !go.activeInHierarchy)
            {
                return false;
            }

            if (!fsm.enabled)
            {
                return false;
            }

            if (!string.Equals(fsm.FsmName, "Control", StringComparison.Ordinal))
            {
                return false;
            }

            string objName = go.name ?? string.Empty;
            if (objName.IndexOf("Orbit Shield", StringComparison.OrdinalIgnoreCase) < 0 &&
                objName.IndexOf("Dreamshield", StringComparison.OrdinalIgnoreCase) < 0 &&
                objName.IndexOf("Shield", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            return true;
        }

        private PlayMakerFSM FindDreamshieldFsm(string sceneName, bool forceRefresh)
        {
            PlayMakerFSM best = null;
            int bestScore = int.MinValue;

            PlayMakerFSM[] fsms = GetSceneFsmCache(forceRefresh);
            foreach (PlayMakerFSM fsm in fsms)
            {
                if (!IsDreamshieldControlFsmValid(fsm))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(sceneName) &&
                    !string.Equals(fsm.gameObject.scene.name, sceneName, StringComparison.Ordinal))
                {
                    continue;
                }

                int score = 0;
                try
                {
                    score += fsm.gameObject.GetInstanceID() & 0xFF;
                }
                catch
                {
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = fsm;
                }
            }

            return best;
        }

        private void EnsureFsmSceneCacheCurrent()
        {
            string activeSceneName = GameManager.instance?.sceneName;
            if (string.Equals(fsmCacheSceneName, activeSceneName, StringComparison.Ordinal))
            {
                return;
            }

            fsmCacheSceneName = activeSceneName;
            PlayMakerFsmSceneCache.Invalidate();
            dreamshieldControlFsm = null;
            dreamshieldControlFsmId = 0;
            cachedRotateAction = null;
            cachedRotateActionFsmId = 0;
        }

        private PlayMakerFSM[] GetSceneFsmCache(bool forceRefresh)
        {
            return PlayMakerFsmSceneCache.Get(forceRefresh);
        }

        private bool TryGetBoolSetting(string primaryName, string altName, ref FieldInfo field, ref PropertyInfo property, out bool enabled)
        {
            enabled = false;
            Type type = GetDreamshieldType();
            if (type == null)
            {
                return false;
            }

            if (field == null && property == null)
            {
                const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                field = type.GetField(primaryName, flags) ?? type.GetField(altName, flags);
                if (field == null)
                {
                    property = type.GetProperty(primaryName, flags) ?? type.GetProperty(altName, flags);
                }
            }

            try
            {
                object raw = property != null
                    ? property.GetCachedValue(null)
                    : field?.GetCachedValue(null);

                if (raw is bool flag)
                {
                    enabled = flag;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private bool TryGetFloatSetting(string primaryName, string altName, ref FieldInfo field, ref PropertyInfo property, out float value)
        {
            value = 0f;
            Type type = GetDreamshieldType();
            if (type == null)
            {
                return false;
            }

            if (field == null && property == null)
            {
                const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                field = type.GetField(primaryName, flags) ?? type.GetField(altName, flags);
                if (field == null)
                {
                    property = type.GetProperty(primaryName, flags) ?? type.GetProperty(altName, flags);
                }
            }

            try
            {
                object raw = property != null
                    ? property.GetCachedValue(null)
                    : field?.GetCachedValue(null);

                if (raw == null)
                {
                    return false;
                }

                value = Convert.ToSingle(raw, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
            }

            return false;
        }

        private Type GetDreamshieldType()
        {
            if (!dreamshieldResolved)
            {
                dreamshieldType = FindType("GodhomeQoL.Modules.QoL.DreamshieldStartAngle");
                dreamshieldResolved = true;
            }

            return dreamshieldType;
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

        private static float GetRotateFloat(Rotate rotate, string fieldName)
        {
            if (rotate == null)
            {
                return 0f;
            }

            try
            {
                FieldInfo field = rotate.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field == null)
                {
                    return 0f;
                }

                object raw = field.GetCachedValue(rotate);
                if (raw is FsmFloat fsmFloat)
                {
                    return fsmFloat.Value;
                }

                if (raw is float f)
                {
                    return f;
                }
            }
            catch
            {
            }

            return 0f;
        }

        private static string FormatToggle(bool value) => value ? "On" : "Off";
    }
}
