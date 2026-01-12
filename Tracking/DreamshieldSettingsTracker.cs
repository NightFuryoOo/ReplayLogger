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
        private Dictionary<string, string> initialState = new(StringComparer.Ordinal);
        private Dictionary<string, string> currentState = new(StringComparer.Ordinal);
        private readonly List<string> changes = new();
        private long lastSliderLogTime;
        private string lastLoggedRotationDelay;
        private string lastLoggedRotationDelayInGame;
        private string lastLoggedRotationSpeed;
        private string lastLoggedRotationAnglesInGame;
        private const int SliderThrottleMs = 1000;

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
        private const int FsmSearchThrottleMs = 1000;

        public bool HasData => hasInitialState || changes.Count > 0;

        public void Reset()
        {
            hasInitialState = false;
            initialArenaName = null;
            currentArenaName = null;
            currentBaseUnixTime = 0;
            initialState.Clear();
            currentState.Clear();
            changes.Clear();
            dreamshieldControlFsm = null;
            dreamshieldControlFsmId = 0;
            cachedRotateAction = null;
            cachedRotateActionFsmId = 0;
            lastFsmSearchTime = 0;
            lastSliderLogTime = 0;
            lastLoggedRotationDelay = null;
            lastLoggedRotationDelayInGame = null;
            lastLoggedRotationSpeed = null;
            lastLoggedRotationAnglesInGame = null;
        }

        public void StartFight(string arenaName, long baseUnixTime)
        {
            currentArenaName = string.IsNullOrWhiteSpace(arenaName) ? "UnknownArena" : arenaName;
            currentBaseUnixTime = baseUnixTime;
            dreamshieldControlFsm = null;
            dreamshieldControlFsmId = 0;
            cachedRotateAction = null;
            cachedRotateActionFsmId = 0;
            lastFsmSearchTime = 0;

            Dictionary<string, string> snapshot = BuildSnapshot();

            if (!hasInitialState)
            {
                hasInitialState = true;
                initialArenaName = currentArenaName;
                initialState = new Dictionary<string, string>(snapshot, StringComparer.Ordinal);
                currentState = new Dictionary<string, string>(snapshot, StringComparer.Ordinal);
                return;
            }

            if (currentState.Count == 0)
            {
                currentState = new Dictionary<string, string>(snapshot, StringComparer.Ordinal);
                return;
            }

            foreach (var entry in snapshot)
            {
                if (currentState.TryGetValue(entry.Key, out string previous) &&
                    string.Equals(previous, entry.Value, StringComparison.Ordinal))
                {
                    continue;
                }

                string descriptor = previous == null
                    ? $"{entry.Key}: {entry.Value}"
                    : $"{entry.Key}: {previous} -> {entry.Value}";

                long unixTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                long delta = currentBaseUnixTime > 0 ? unixTime - currentBaseUnixTime : 0;
                changes.Add($"|{currentArenaName}|+{delta}|{descriptor}");
            }

            currentState = new Dictionary<string, string>(snapshot, StringComparer.Ordinal);
            UpdateSliderBaseline(snapshot);
        }

        public void Update(string arenaName)
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

            Dictionary<string, string> snapshot = BuildSnapshot();
            if (snapshot.Count == 0)
            {
                return;
            }

            if (currentState.Count == 0)
            {
                currentState = new Dictionary<string, string>(snapshot, StringComparer.Ordinal);
                UpdateSliderBaseline(snapshot);
                return;
            }

            foreach (var entry in snapshot)
            {
                if (IsSliderKey(entry.Key))
                {
                    continue;
                }

                if (currentState.TryGetValue(entry.Key, out string previous) &&
                    string.Equals(previous, entry.Value, StringComparison.Ordinal))
                {
                    continue;
                }

                string descriptor = previous == null
                    ? $"{entry.Key}: {entry.Value}"
                    : $"{entry.Key}: {previous} -> {entry.Value}";

                long unixTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                long delta = currentBaseUnixTime > 0 ? unixTime - currentBaseUnixTime : 0;
                changes.Add($"|{currentArenaName}|+{delta}|{descriptor}");
            }

            LogSliderChanges(snapshot);

            currentState = new Dictionary<string, string>(snapshot, StringComparer.Ordinal);
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

            LogWrite.EncryptedLine(writer, "  Dreamshield Settings:");
            if (!string.IsNullOrEmpty(initialArenaName))
            {
                LogWrite.EncryptedLine(writer, $"    Initial Arena: {initialArenaName}");
            }
            LogWrite.EncryptedLine(writer, "    State:");

            if (initialState.Count == 0)
            {
                LogWrite.EncryptedLine(writer, "      (unavailable)");
            }
            else
            {
                foreach (var entry in initialState)
                {
                    LogWrite.EncryptedLine(writer, $"      {entry.Key}: {entry.Value}");
                }
            }

            LogWrite.EncryptedLine(writer, "    Changes:");
            if (changes.Count == 0)
            {
                LogWrite.EncryptedLine(writer, "      (none)");
            }
            else
            {
                foreach (string change in changes)
                {
                    LogWrite.EncryptedLine(writer, $"      {change}");
                }
            }
        }

        private Dictionary<string, string> BuildSnapshot()
        {
            bool hasStartAngle = TryGetStartAngleEnabled(out bool startAngleEnabled);
            bool hasDelay = TryGetRotationDelay(out float rotationDelay);
            bool hasSpeed = TryGetRotationSpeed(out float rotationSpeed);

            bool hasFsm = TryGetDreamshieldControlFsm(out PlayMakerFSM fsm);
            bool startAngleInGame = hasFsm && hasStartAngle && startAngleEnabled;

            string inGameDelay = (startAngleInGame && hasDelay)
                ? rotationDelay.ToString("0.##", CultureInfo.InvariantCulture)
                : "N/A";

            string inGameAngles = (startAngleInGame && TryGetRotationAnglesInGame(fsm, out string angles))
                ? angles
                : "N/A";

            Dictionary<string, string> snapshot = new(StringComparer.Ordinal)
            {
                ["Dreamshield Start Angle"] = hasStartAngle ? FormatToggle(startAngleEnabled) : "N/A",
                ["Rotation Delay (sec)"] = hasDelay ? rotationDelay.ToString("0.##", CultureInfo.InvariantCulture) : "N/A",
                ["Rotation Speed Multiplier"] = hasSpeed ? rotationSpeed.ToString("0.##", CultureInfo.InvariantCulture) : "N/A",
                ["Dreamshield Start Angle (In-Game)"] = hasFsm && hasStartAngle
                    ? FormatToggle(startAngleEnabled)
                    : "N/A",
                ["Rotation Delay (In-Game)"] = inGameDelay,
                ["Rotation Angles (In-Game)"] = inGameAngles
            };

            return snapshot;
        }

        private void LogSliderChanges(Dictionary<string, string> snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            snapshot.TryGetValue("Rotation Delay (sec)", out string delayValue);
            snapshot.TryGetValue("Rotation Delay (In-Game)", out string delayInGameValue);
            snapshot.TryGetValue("Rotation Speed Multiplier", out string speedValue);
            snapshot.TryGetValue("Rotation Angles (In-Game)", out string speedInGameValue);

            bool delayChanged = !string.Equals(lastLoggedRotationDelay, delayValue, StringComparison.Ordinal);
            bool delayInGameChanged = !string.Equals(lastLoggedRotationDelayInGame, delayInGameValue, StringComparison.Ordinal);
            bool speedChanged = !string.Equals(lastLoggedRotationSpeed, speedValue, StringComparison.Ordinal);
            bool speedInGameChanged = !string.Equals(lastLoggedRotationAnglesInGame, speedInGameValue, StringComparison.Ordinal);

            if (!delayChanged && !delayInGameChanged && !speedChanged && !speedInGameChanged)
            {
                return;
            }

            long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            if (lastSliderLogTime > 0 && now - lastSliderLogTime < SliderThrottleMs)
            {
                return;
            }

            if (delayChanged)
            {
                string descriptor = lastLoggedRotationDelay == null
                    ? $"Rotation Delay (sec): {delayValue}"
                    : $"Rotation Delay (sec): {lastLoggedRotationDelay} -> {delayValue}";
                long delta = currentBaseUnixTime > 0 ? now - currentBaseUnixTime : 0;
                changes.Add($"|{currentArenaName}|+{delta}|{descriptor}");
                lastLoggedRotationDelay = delayValue;
            }

            if (delayInGameChanged)
            {
                string descriptor = lastLoggedRotationDelayInGame == null
                    ? $"Rotation Delay (In-Game): {delayInGameValue}"
                    : $"Rotation Delay (In-Game): {lastLoggedRotationDelayInGame} -> {delayInGameValue}";
                long delta = currentBaseUnixTime > 0 ? now - currentBaseUnixTime : 0;
                changes.Add($"|{currentArenaName}|+{delta}|{descriptor}");
                lastLoggedRotationDelayInGame = delayInGameValue;
            }

            if (speedChanged)
            {
                string descriptor = lastLoggedRotationSpeed == null
                    ? $"Rotation Speed Multiplier: {speedValue}"
                    : $"Rotation Speed Multiplier: {lastLoggedRotationSpeed} -> {speedValue}";
                long delta = currentBaseUnixTime > 0 ? now - currentBaseUnixTime : 0;
                changes.Add($"|{currentArenaName}|+{delta}|{descriptor}");
                lastLoggedRotationSpeed = speedValue;
            }

            if (speedInGameChanged)
            {
                string descriptor = lastLoggedRotationAnglesInGame == null
                    ? $"Rotation Angles (In-Game): {speedInGameValue}"
                    : $"Rotation Angles (In-Game): {lastLoggedRotationAnglesInGame} -> {speedInGameValue}";
                long delta = currentBaseUnixTime > 0 ? now - currentBaseUnixTime : 0;
                changes.Add($"|{currentArenaName}|+{delta}|{descriptor}");
                lastLoggedRotationAnglesInGame = speedInGameValue;
            }

            lastSliderLogTime = now;
        }

        private void UpdateSliderBaseline(Dictionary<string, string> snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            snapshot.TryGetValue("Rotation Delay (sec)", out lastLoggedRotationDelay);
            snapshot.TryGetValue("Rotation Delay (In-Game)", out lastLoggedRotationDelayInGame);
            snapshot.TryGetValue("Rotation Speed Multiplier", out lastLoggedRotationSpeed);
            snapshot.TryGetValue("Rotation Angles (In-Game)", out lastLoggedRotationAnglesInGame);
            lastSliderLogTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        }

        private static bool IsSliderKey(string key)
        {
            return string.Equals(key, "Rotation Delay (sec)", StringComparison.Ordinal) ||
                string.Equals(key, "Rotation Delay (In-Game)", StringComparison.Ordinal) ||
                string.Equals(key, "Rotation Speed Multiplier", StringComparison.Ordinal) ||
                string.Equals(key, "Rotation Angles (In-Game)", StringComparison.Ordinal);
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

        private bool TryGetDreamshieldControlFsm(out PlayMakerFSM fsm)
        {
            fsm = dreamshieldControlFsm;
            if (IsDreamshieldControlFsmValid(fsm))
            {
                return true;
            }

            long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            if (lastFsmSearchTime > 0 && now - lastFsmSearchTime < FsmSearchThrottleMs)
            {
                return false;
            }

            lastFsmSearchTime = now;

            PlayMakerFSM best = FindDreamshieldFsm(currentArenaName);
            if (best == null && !string.IsNullOrEmpty(currentArenaName))
            {
                best = FindDreamshieldFsm(null);
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

        private static PlayMakerFSM FindDreamshieldFsm(string sceneName)
        {
            PlayMakerFSM best = null;
            int bestScore = int.MinValue;

            PlayMakerFSM[] fsms = UnityEngine.Object.FindObjectsOfType<PlayMakerFSM>();
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
                    ? property.GetValue(null)
                    : field?.GetValue(null);

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
                    ? property.GetValue(null)
                    : field?.GetValue(null);

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

                object raw = field.GetValue(rotate);
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
