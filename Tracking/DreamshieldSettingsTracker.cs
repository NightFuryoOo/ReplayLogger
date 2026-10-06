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
        private Optional<float> lastLoggedRotationSpeed;
        private Optional<string> lastLoggedRotationAngles;

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
            lastLoggedRotationSpeed = Optional<float>.None;
            lastLoggedRotationAngles = Optional<string>.None;
        }

        public void StartFight(string arenaName, long baseUnixTime)
        {
            currentArenaName = ArenaNormalization.NormalizeLenient(arenaName);
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
            LogFieldChange("Rotation Angles", currentState.RotationAngles, snapshot.RotationAngles, now);

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
                batch.Add($"      Dreamshield Start Angle: {OptionalFormatting.FormatOptionalToggle(initialState.StartAngle)}");
                batch.Add($"      Rotation Delay (sec): {OptionalFormatting.FormatOptionalFloat(initialState.RotationDelay)}");
                batch.Add($"      Rotation Speed Multiplier: {OptionalFormatting.FormatOptionalFloat(initialState.RotationSpeed)}");
                batch.Add($"      Rotation Angles: {FormatOptionalString(initialState.RotationAngles)}");
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
            bool tryReadAngles = hasFsm && hasStartAngle && startAngleEnabled;

            Optional<string> liveAngles = Optional<string>.None;
            if (tryReadAngles && TryGetRotationAngles(fsm, out string angles) && !string.IsNullOrEmpty(angles))
            {
                liveAngles = new Optional<string>(angles);
            }

            return new DreamshieldState(
                hasStartAngle ? new Optional<bool>(startAngleEnabled) : Optional<bool>.None,
                hasDelay ? new Optional<float>(OptionalFormatting.NormalizeFloat(rotationDelay)) : Optional<float>.None,
                hasSpeed ? new Optional<float>(OptionalFormatting.NormalizeFloat(rotationSpeed)) : Optional<float>.None,
                liveAngles);
        }

        private void LogSliderChanges(DreamshieldState snapshot, long now)
        {
            Optional<float> delayValue = snapshot.RotationDelay;
            Optional<float> speedValue = snapshot.RotationSpeed;
            Optional<string> anglesValue = snapshot.RotationAngles;

            bool delayChanged = delayValue != lastLoggedRotationDelay;
            bool speedChanged = speedValue != lastLoggedRotationSpeed;
            bool anglesChanged = anglesValue != lastLoggedRotationAngles;

            if (!delayChanged && !speedChanged && !anglesChanged)
            {
                return;
            }

            if (delayChanged)
            {
                string descriptor = !lastLoggedRotationDelay.HasValue
                    ? $"Rotation Delay (sec): {OptionalFormatting.FormatOptionalFloat(delayValue)}"
                    : $"Rotation Delay (sec): {OptionalFormatting.FormatOptionalFloat(lastLoggedRotationDelay)} -> {OptionalFormatting.FormatOptionalFloat(delayValue)}";
                long delta = currentBaseUnixTime > 0 ? now - currentBaseUnixTime : 0;
                changes.Add($"|{currentArenaName}|+{delta}|{descriptor}");
                lastLoggedRotationDelay = delayValue;
            }

            if (speedChanged)
            {
                string descriptor = !lastLoggedRotationSpeed.HasValue
                    ? $"Rotation Speed Multiplier: {OptionalFormatting.FormatOptionalFloat(speedValue)}"
                    : $"Rotation Speed Multiplier: {OptionalFormatting.FormatOptionalFloat(lastLoggedRotationSpeed)} -> {OptionalFormatting.FormatOptionalFloat(speedValue)}";
                long delta = currentBaseUnixTime > 0 ? now - currentBaseUnixTime : 0;
                changes.Add($"|{currentArenaName}|+{delta}|{descriptor}");
                lastLoggedRotationSpeed = speedValue;
            }

            if (anglesChanged)
            {
                string descriptor = !lastLoggedRotationAngles.HasValue
                    ? $"Rotation Angles: {FormatOptionalString(anglesValue)}"
                    : $"Rotation Angles: {FormatOptionalString(lastLoggedRotationAngles)} -> {FormatOptionalString(anglesValue)}";
                long delta = currentBaseUnixTime > 0 ? now - currentBaseUnixTime : 0;
                changes.Add($"|{currentArenaName}|+{delta}|{descriptor}");
                lastLoggedRotationAngles = anglesValue;
            }
        }

        private void UpdateSliderBaseline(DreamshieldState snapshot)
        {
            lastLoggedRotationDelay = snapshot.RotationDelay;
            lastLoggedRotationSpeed = snapshot.RotationSpeed;
            lastLoggedRotationAngles = snapshot.RotationAngles;
        }

        private void LogFieldChange(string key, Optional<bool> previous, Optional<bool> current, long now)
        {
            if (previous == current)
            {
                return;
            }

            string descriptor = $"{key}: {OptionalFormatting.FormatOptionalToggle(previous)} -> {OptionalFormatting.FormatOptionalToggle(current)}";
            long delta = currentBaseUnixTime > 0 ? now - currentBaseUnixTime : 0;
            changes.Add($"|{currentArenaName}|+{delta}|{descriptor}");
        }

        private void LogFieldChange(string key, Optional<float> previous, Optional<float> current, long now)
        {
            if (previous == current)
            {
                return;
            }

            string descriptor = $"{key}: {OptionalFormatting.FormatOptionalFloat(previous)} -> {OptionalFormatting.FormatOptionalFloat(current)}";
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
                Optional<string> rotationAngles)
            {
                StartAngle = startAngle;
                RotationDelay = rotationDelay;
                RotationSpeed = rotationSpeed;
                RotationAngles = rotationAngles;
            }

            internal Optional<bool> StartAngle { get; }
            internal Optional<float> RotationDelay { get; }
            internal Optional<float> RotationSpeed { get; }
            internal Optional<string> RotationAngles { get; }
        }

        private bool TryGetStartAngleEnabled(out bool enabled)
        {
            return ReflectionSettingLookup.TryGetBool(GetDreamshieldType(), "startAngleEnabled", "StartAngleEnabled", ref startAngleEnabledField, ref startAngleEnabledProperty, out enabled);
        }

        private bool TryGetRotationDelay(out float delay)
        {
            return ReflectionSettingLookup.TryGetFloat(GetDreamshieldType(), "rotationDelay", "RotationDelay", ref rotationDelayField, ref rotationDelayProperty, out delay);
        }

        private bool TryGetRotationSpeed(out float speed)
        {
            return ReflectionSettingLookup.TryGetFloat(GetDreamshieldType(), "rotationSpeed", "RotationSpeed", ref rotationSpeedField, ref rotationSpeedProperty, out speed);
        }

        private bool TryGetRotationAngles(PlayMakerFSM fsm, out string angles)
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

        private Type GetDreamshieldType()
        {
            if (!dreamshieldResolved)
            {
                dreamshieldType = TypeLookup.FindType("GodhomeQoL.Modules.QoL.DreamshieldStartAngle");
                dreamshieldResolved = true;
            }

            return dreamshieldType;
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
    }
}
