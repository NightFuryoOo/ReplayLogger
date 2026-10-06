using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace ReplayLogger
{
    internal sealed class MaskDamageSettingsTracker
    {
        private const string MaskDamageTypeName = "GodhomeQoL.Modules.Tools.MaskDamage";

        private bool hasInitialState;
        private string initialArenaName;
        private string currentArenaName;
        private long currentBaseUnixTime;
        private MaskDamageState initialState;
        private MaskDamageState currentState;
        private bool hasCurrentState;
        private readonly List<string> changes = new();

        private Type maskDamageType;
        private bool maskDamageTypeResolved;
        private MethodInfo getEnabledMethod;
        private MethodInfo getMultiplierMethod;
        private bool methodsResolved;

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
        }

        public void StartFight(string arenaName, long baseUnixTime)
        {
            currentArenaName = ArenaNormalization.NormalizeLenient(arenaName);
            currentBaseUnixTime = baseUnixTime;
            long now = baseUnixTime;

            MaskDamageState snapshot = BuildState();

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

            LogFieldChange("Enabled", currentState.Enabled, snapshot.Enabled, now);
            LogFieldChange("Damage Multiplier", currentState.Multiplier, snapshot.Multiplier, now);

            currentState = snapshot;
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

            MaskDamageState snapshot = BuildState();

            if (!hasCurrentState)
            {
                currentState = snapshot;
                hasCurrentState = true;
                return;
            }

            long now = nowUnixTime;
            LogFieldChange("Enabled", currentState.Enabled, snapshot.Enabled, now);
            LogFieldChange("Damage Multiplier", currentState.Multiplier, snapshot.Multiplier, now);

            currentState = snapshot;
        }

        public void WriteSection(StreamWriter writer)
        {
            if (writer == null || !HasData)
            {
                return;
            }

            List<string> batch = TempObjectPools.RentStringList(changes.Count + 8);
            try
            {
                batch.Add("  Mask Damage:");
                if (!string.IsNullOrEmpty(initialArenaName))
                {
                    batch.Add($"    Initial Arena: {initialArenaName}");
                }

                batch.Add("    State:");
                batch.Add($"      Enabled: {OptionalFormatting.FormatOptionalToggle(initialState.Enabled)}");
                batch.Add($"      Damage Multiplier: {OptionalFormatting.FormatOptionalFloat(initialState.Multiplier)}");
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

        private MaskDamageState BuildState()
        {
            bool hasEnabled = TryGetEnabled(out bool enabled);
            bool hasMultiplier = TryGetMultiplier(out float multiplier);

            return new MaskDamageState(
                hasEnabled ? new Optional<bool>(enabled) : Optional<bool>.None,
                hasMultiplier ? new Optional<float>(OptionalFormatting.NormalizeFloat(multiplier)) : Optional<float>.None);
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

        private readonly struct MaskDamageState
        {
            internal MaskDamageState(Optional<bool> enabled, Optional<float> multiplier)
            {
                Enabled = enabled;
                Multiplier = multiplier;
            }

            internal Optional<bool> Enabled { get; }
            internal Optional<float> Multiplier { get; }
        }

        private bool TryGetEnabled(out bool value)
        {
            value = false;
            EnsureMethods();

            try
            {
                object raw = getEnabledMethod?.InvokeCached(null);
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

        private bool TryGetMultiplier(out float value)
        {
            value = 0f;
            EnsureMethods();

            try
            {
                object raw = getMultiplierMethod?.InvokeCached(null);
                if (raw is float multiplier)
                {
                    value = multiplier;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private void EnsureMethods()
        {
            if (methodsResolved)
            {
                return;
            }

            Type type = GetMaskDamageType();
            if (type != null)
            {
                const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                getEnabledMethod = type.GetMethod("GetEnabled", flags);
                getMultiplierMethod = type.GetMethod("GetMultiplier", flags);
            }

            methodsResolved = true;
        }

        private Type GetMaskDamageType()
        {
            if (!maskDamageTypeResolved)
            {
                maskDamageType = TypeLookup.FindType(MaskDamageTypeName);
                maskDamageTypeResolved = true;
            }

            return maskDamageType;
        }
    }
}
