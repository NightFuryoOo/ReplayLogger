using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GlobalEnums;
using System.Globalization;
using UnityEngine;
using System.Reflection;

namespace ReplayLogger
{
    
    
    
    internal static class CoreSessionLogger
    {
        private static readonly Dictionary<int, string> CustomCharmDisplayNames = new()
        {
            { (int)Charm.MarkOfPurity, "Mark of Purity" },
            { (int)Charm.VesselsLament, "Vessel's Lament" },
            { (int)Charm.BoonOfHallownest, "Boon of Hallownest" },
            { (int)Charm.AbyssalBloom, "Abyssal Bloom" }
        };
        private static readonly Dictionary<string, FieldInfo> PlayerDataIntFieldCache = new(StringComparer.Ordinal);

        public static IReadOnlyList<string> GetEncryptedModSnapshot(string modsDir)
        {
            if (string.IsNullOrEmpty(modsDir) || !Directory.Exists(modsDir))
            {
                return new List<string>();
            }

            List<string> snapshot = ModsChecking.ScanMods(modsDir);
            if (snapshot == null || snapshot.Count == 0)
            {
                return new List<string>();
            }
            return snapshot;
        }

        public static void WriteEncryptedModSnapshot(StreamWriter writer, string modsDir, string separatorAfter = null)
        {
            if (writer == null)
            {
                return;
            }

            IReadOnlyList<string> snapshot = GetEncryptedModSnapshot(modsDir);
            bool hasSeparator = !string.IsNullOrEmpty(separatorAfter);
            if (snapshot.Count == 0 && !hasSeparator)
            {
                return;
            }

            List<string> batch = TempObjectPools.RentStringList(snapshot.Count + (hasSeparator ? 1 : 0));
            try
            {
                for (int i = 0; i < snapshot.Count; i++)
                {
                    batch.Add(snapshot[i]);
                }

                if (hasSeparator)
                {
                    batch.Add(separatorAfter);
                }

                LogWrite.EncryptedLines(writer, batch);
            }
            finally
            {
                TempObjectPools.ReturnStringList(batch);
            }
        }

        public static string BuildEquippedCharmsLine()
        {
            StringBuilder builder = new("\nEquipped charms => ");
            int initialLength = builder.Length;
            HashSet<string> seen = new(StringComparer.Ordinal);
            int totalCost = 0;

            void AppendCharm(int charmId, string name)
            {
                if (string.IsNullOrWhiteSpace(name) || !seen.Add(name))
                {
                    return;
                }

                int cost = GetCharmCost(charmId);
                totalCost += Math.Max(cost, 0);
                if (cost >= 0)
                {
                    builder.Append($"{name} ({cost}), ");
                }
                else
                {
                    builder.Append($"{name}, ");
                }
            }

            if (PlayerData.instance?.equippedCharms != null)
            {
                foreach (int charm in PlayerData.instance.equippedCharms)
                {
                    AppendCharm(charm, GetCharmDisplayName(charm));
                }
            }

            foreach (string customCharm in PaleCourtCharmIntegration.GetEquippedCharmNames())
            {
                if (!string.IsNullOrWhiteSpace(customCharm) && seen.Add(customCharm))
                {
                    builder.Append($"{customCharm}, ");
                }
            }

            bool hasAnyCharms = builder.Length > initialLength;
            if (hasAnyCharms)
            {
                builder.Length -= 2;
            }
            else
            {
                builder.Append("NO CHARMS");
            }

            builder.Append($" | Total Cost: {totalCost}");

            if (BossSequenceController.BoundNail)
            {
                builder.Append(" [Nail]");
            }

            if (BossSequenceController.BoundShell)
            {
                builder.Append(" [Shell]");
            }

            if (BossSequenceController.BoundCharms)
            {
                builder.Append(" [Charms]");
            }

            if (BossSequenceController.BoundSoul)
            {
                builder.Append(" [Soul]");
            }

            builder.Append('\n');
            return builder.ToString();
        }

        public static IReadOnlyList<string> BuildSkillLines()
        {
            PlayerData data = PlayerData.instance;
            if (data == null)
            {
                return Array.Empty<string>();
            }

            string OnOff(bool value) => value ? "On" : "Off";

            List<string> lines = new() { "Skills:" };

            lines.Add($"  Scream: {data.screamLevel}");
            lines.Add($"  Fireball: {data.fireballLevel}");
            lines.Add($"  Quake: {data.quakeLevel}");

            string dash = data.hasShadowDash ? "Shade" : data.hasDash ? "Dash" : "None";
            lines.Add($"  Dash: {dash}");
            lines.Add($"  Mantis Claw: {OnOff(data.hasWalljump)}");
            lines.Add($"  Monarch Wings: {OnOff(data.hasDoubleJump)}");
            lines.Add($"  Crystal Heart: {OnOff(data.hasSuperDash)}");
            lines.Add($"  Isma's Tear: {OnOff(data.hasAcidArmour)}");

            string dreamNail = data.dreamNailUpgraded ? "Awoken" : data.hasDreamNail ? "Normal" : "None";
            lines.Add($"  Dream Nail: {dreamNail}");
            lines.Add($"  Dream Gate: {OnOff(data.hasDreamGate)}");

            lines.Add($"  Great Slash: {OnOff(data.hasDashSlash)}");
            lines.Add($"  Dash Slash: {OnOff(data.hasUpwardSlash)}");
            lines.Add($"  Cyclone Slash: {OnOff(data.hasCyclone)}");

            return lines;
        }

        public static void WriteEncryptedSkillLines(StreamWriter writer, string separatorAfter = null)
        {
            if (writer == null)
            {
                return;
            }

            IReadOnlyList<string> skills = BuildSkillLines();
            bool hasSeparator = !string.IsNullOrEmpty(separatorAfter);
            if (skills.Count == 0 && !hasSeparator)
            {
                return;
            }

            List<string> batch = TempObjectPools.RentStringList(skills.Count + (hasSeparator ? 1 : 0));
            try
            {
                for (int i = 0; i < skills.Count; i++)
                {
                    batch.Add(skills[i]);
                }

                if (hasSeparator)
                {
                    batch.Add(separatorAfter);
                }

                LogWrite.EncryptedLines(writer, batch);
            }
            finally
            {
                TempObjectPools.ReturnStringList(batch);
            }
        }

        public static void WriteDamageInvSection(StreamWriter writer, IEnumerable<string> logs, string separatorAfter = "---------------------------------------------------")
        {
            if (writer == null)
            {
                return;
            }

            List<string> batch = TempObjectPools.RentStringList();
            try
            {
                batch.Add("\n------------------------DAMAGE INV------------------------\n");
                if (logs != null)
                {
                    foreach (string log in logs)
                    {
                        batch.Add(log);
                    }
                }

                if (!string.IsNullOrEmpty(separatorAfter))
                {
                    batch.Add(separatorAfter);
                }

                LogWrite.EncryptedLines(writer, batch);
            }
            finally
            {
                TempObjectPools.ReturnStringList(batch);
            }
        }

        public static void WriteDamageInvSection(StreamWriter writer, BufferedLogSection logs, string separatorAfter = "---------------------------------------------------")
        {
            if (writer == null)
            {
                return;
            }

            LogWrite.EncryptedLine(writer, "\n------------------------DAMAGE INV------------------------\n");
            logs?.WriteEncryptedLines(writer);

            if (!string.IsNullOrEmpty(separatorAfter))
            {
                LogWrite.EncryptedLine(writer, separatorAfter);
            }

        }

        public static void WriteSeparator(StreamWriter writer, string separator = "---------------------------------------------------")
        {
            if (writer == null || string.IsNullOrEmpty(separator))
            {
                return;
            }

            LogWrite.EncryptedLine(writer, separator);
        }

        public static void WriteNoBlurSettings(StreamWriter writer, string separator = "---------------------------------------------------")
        {
            if (writer == null)
            {
                return;
            }

            IReadOnlyList<string> noBlurSettings = NoBlurIntegration.GetSettingsLines();
            if (noBlurSettings.Count == 0)
            {
                return;
            }

            bool hasSeparator = !string.IsNullOrEmpty(separator);
            List<string> batch = TempObjectPools.RentStringList(noBlurSettings.Count + (hasSeparator ? 2 : 1));
            try
            {
                for (int i = 0; i < noBlurSettings.Count; i++)
                {
                    batch.Add(noBlurSettings[i]);
                }

                batch.Add(string.Empty);
                if (hasSeparator)
                {
                    batch.Add(separator);
                }

                LogWrite.EncryptedLines(writer, batch);
            }
            finally
            {
                TempObjectPools.ReturnStringList(batch);
            }
        }

        public static void WriteCustomizableAbilitiesSettings(StreamWriter writer, string separator = "---------------------------------------------------")
        {
            if (writer == null)
            {
                return;
            }

            IReadOnlyList<string> caSettings = CustomizableAbilitiesIntegration.GetSettingsLines();
            if (caSettings.Count == 0)
            {
                return;
            }

            bool hasSeparator = !string.IsNullOrEmpty(separator);
            List<string> batch = TempObjectPools.RentStringList(caSettings.Count + (hasSeparator ? 2 : 1));
            try
            {
                for (int i = 0; i < caSettings.Count; i++)
                {
                    batch.Add(caSettings[i]);
                }

                batch.Add(string.Empty);
                if (hasSeparator)
                {
                    batch.Add(separator);
                }

                LogWrite.EncryptedLines(writer, batch);
            }
            finally
            {
                TempObjectPools.ReturnStringList(batch);
            }
        }

        public static void WriteControlSettings(StreamWriter writer, string separator = "---------------------------------------------------")
        {
            if (writer == null)
            {
                return;
            }

            IReadOnlyList<string> lines = BuildControlLines();
            bool hasSeparator = !string.IsNullOrEmpty(separator);
            List<string> batch = TempObjectPools.RentStringList(lines.Count + (hasSeparator ? 3 : 2));
            try
            {
                batch.Add("CONTROL:");
                if (lines.Count == 0)
                {
                    batch.Add("  (unavailable)");
                }
                else
                {
                    for (int i = 0; i < lines.Count; i++)
                    {
                        batch.Add(lines[i]);
                    }
                }

                batch.Add(string.Empty);
                if (hasSeparator)
                {
                    batch.Add(separator);
                }

                LogWrite.EncryptedLines(writer, batch);
            }
            finally
            {
                TempObjectPools.ReturnStringList(batch);
            }
        }

        private static IReadOnlyList<string> BuildControlLines()
        {
            List<string> lines = new();

            try
            {
                GameManager gm = GameManager.instance;
                if (gm == null)
                {
                    return lines;
                }

                object inputHandler = gm.inputHandler;
                if (inputHandler == null)
                {
                    return lines;
                }

                object inputActions = GetMemberValue(inputHandler, "inputActions");
                if (inputActions == null)
                {
                    return lines;
                }

                (string Label, string Member)[] bindings =
                {
                    ("Up", "up"),
                    ("Down", "down"),
                    ("Left", "left"),
                    ("Right", "right"),
                    ("Jump", "jump"),
                    ("Attack", "attack"),
                    ("Dash", "dash"),
                    ("Focus/Cast", "castSpell"),
                    ("Focus/Cast", "cast"),
                    ("Quick Map", "quickMap"),
                    ("Super Dash", "superDash"),
                    ("Dream Nail", "dreamNail"),
                    ("Quick Cast", "quickCast"),
                    ("Inventory", "inventory"),
                    ("Inventory", "openInventory")
                };

                foreach ((string label, string member) in bindings)
                {
                    if (TryDescribeBinding(inputActions, member, out string bindingText))
                    {
                        lines.Add($"{label}: {bindingText}");
                    }
                }
            }
            catch
            {
                
            }

            return lines;
        }

        private static bool TryDescribeBinding(object inputActions, string memberName, out string description)
        {
            description = null;
            if (inputActions == null || string.IsNullOrEmpty(memberName))
            {
                return false;
            }

            object action = GetMemberValue(inputActions, memberName);
            if (action == null)
            {
                return false;
            }

            try
            {
                
                object bindingsObj = GetMemberValue(action, "Bindings");
                if (bindingsObj is System.Collections.IEnumerable enumerable)
                {
                    List<string> parts = new();
                    foreach (object binding in enumerable)
                    {
                        if (binding == null)
                        {
                            continue;
                        }

                        if (TryFormatBinding(binding, out string formatted))
                        {
                            parts.Add(formatted);
                        }
                    }

                    if (parts.Count > 0)
                    {
                        description = string.Join(" / ", parts);
                        return true;
                    }
                }
            }
            catch
            {
                
            }

            if (TryFormatBinding(action, out string fallbackFormatted))
            {
                description = fallbackFormatted;
                return true;
            }

            return false;
        }

        private static bool TryFormatBinding(object binding, out string formatted)
        {
            formatted = null;
            if (binding == null)
            {
                return false;
            }

            Type bindingType = binding.GetType();

            if (string.Equals(bindingType.FullName, "InControl.KeyBindingSource", StringComparison.Ordinal))
            {
                if (TryGetStringMember(binding, "Name", out string name) ||
                    TryGetStringMethod(binding, "ToString", out name))
                {
                    formatted = name;
                    return true;
                }

                object key = GetMemberValue(binding, "Key");
                if (key != null)
                {
                    formatted = key.ToString();
                    return true;
                }
            }

            if (string.Equals(bindingType.FullName, "InControl.DeviceBindingSource", StringComparison.Ordinal))
            {
                if (TryGetStringMember(binding, "Name", out string name) ||
                    TryGetStringMethod(binding, "ToString", out name))
                {
                    formatted = name;
                    return true;
                }

                object control = GetMemberValue(binding, "Control");
                object device = GetMemberValue(binding, "DeviceName");
                if (control != null)
                {
                    string ctrl = control.ToString();
                    string dev = device?.ToString();
                    formatted = string.IsNullOrEmpty(dev) ? ctrl : $"{dev}:{ctrl}";
                    return true;
                }
            }

            string fallback = binding.ToString();
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                formatted = fallback.Trim();
                return true;
            }

            return false;
        }

        private static object GetMemberValue(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrEmpty(memberName))
            {
                return null;
            }

            Type type = instance.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

            PropertyInfo prop = type.GetProperty(memberName, flags);
            if (prop != null)
            {
                try
                {
                    return prop.GetCachedValue(instance);
                }
                catch
                {
                    
                }
            }

            FieldInfo field = type.GetField(memberName, flags);
            if (field != null)
            {
                try
                {
                    return field.GetCachedValue(instance);
                }
                catch
                {
                    
                }
            }

            return null;
        }

        private static bool TryGetStringMember(object instance, string memberName, out string value)
        {
            value = null;
            object raw = GetMemberValue(instance, memberName);
            if (raw == null)
            {
                return false;
            }

            string str = raw.ToString();
            if (string.IsNullOrWhiteSpace(str))
            {
                return false;
            }

            value = str.Trim();
            return true;
        }

        private static bool TryGetStringMethod(object instance, string methodName, out string value)
        {
            value = null;
            if (instance == null || string.IsNullOrEmpty(methodName))
            {
                return false;
            }

            try
            {
                MethodInfo method = instance.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
                if (method != null && method.GetParameters().Length == 0)
                {
                    object result = method.Invoke(instance, null);
                    string str = result?.ToString();
                    if (!string.IsNullOrWhiteSpace(str))
                    {
                        value = str.Trim();
                        return true;
                    }
                }
            }
            catch
            {
                
            }

            return false;
        }

        public static void WriteWarningsSection(StreamWriter writer, IEnumerable<string> warnings, string separatorAfter = "---------------------------------------------------")
        {
            if (writer == null)
            {
                return;
            }

            List<string> batch = TempObjectPools.RentStringList();
            try
            {
                batch.Add("Warnings:");
                if (warnings != null)
                {
                    foreach (string warning in warnings)
                    {
                        batch.Add(warning);
                    }
                }

                if (!string.IsNullOrEmpty(separatorAfter))
                {
                    batch.Add(separatorAfter);
                }

                LogWrite.EncryptedLines(writer, batch);
            }
            finally
            {
                TempObjectPools.ReturnStringList(batch);
            }
        }

        public static void WriteWarningsSection(StreamWriter writer, BufferedLogSection warnings, string separatorAfter = "---------------------------------------------------")
        {
            if (writer == null)
            {
                return;
            }

            LogWrite.EncryptedLine(writer, "Warnings:");
            warnings?.WriteEncryptedLines(writer);

            if (!string.IsNullOrEmpty(separatorAfter))
            {
                LogWrite.EncryptedLine(writer, separatorAfter);
            }

        }

        public static void WriteSpeedWarningsSection(StreamWriter writer, IEnumerable<string> warnings, string separatorAfter = "---------------------------------------------------")
        {
            if (writer == null)
            {
                return;
            }

            List<string> batch = TempObjectPools.RentStringList();
            try
            {
                batch.Add("SpeedWarn:");
                if (warnings != null)
                {
                    foreach (string warning in warnings)
                    {
                        batch.Add(warning);
                    }
                }

                if (!string.IsNullOrEmpty(separatorAfter))
                {
                    batch.Add(separatorAfter);
                }

                LogWrite.EncryptedLines(writer, batch);
            }
            finally
            {
                TempObjectPools.ReturnStringList(batch);
            }
        }

        public static void WriteSpeedWarningsSection(StreamWriter writer, BufferedLogSection warnings, string separatorAfter = "---------------------------------------------------")
        {
            if (writer == null)
            {
                return;
            }

            LogWrite.EncryptedLine(writer, "SpeedWarn:");
            warnings?.WriteEncryptedLines(writer);

            if (!string.IsNullOrEmpty(separatorAfter))
            {
                LogWrite.EncryptedLine(writer, separatorAfter);
            }

        }

        public static void AddSpeedWarning(List<string> buffer, string arenaName, long deltaMs, float defaultScale, float currentScale, double durationSeconds)
        {
            if (buffer == null)
            {
                return;
            }

            arenaName ??= "UnknownArena";
            string warnEntry = $"|{arenaName}|+{deltaMs}|Default {(defaultScale * 100f):F0}% ({defaultScale:F3}) -> {(currentScale * 100f):F0}% ({currentScale:F3})|Duration {durationSeconds.ToString("F2", CultureInfo.InvariantCulture)}s";
            buffer.Add(warnEntry);
        }

        private static int GetCharmCost(int charmId)
        {
            PlayerData data = PlayerData.instance;
            if (data == null)
            {
                return -1;
            }

            try
            {
                return charmId switch
                {
                    36 => data.charmCost_36,
                    40 => data.charmCost_40,
                    _ => TryGetPlayerDataInt(data, $"charmCost_{charmId}", out int reflectedCost) ? reflectedCost : -1
                };
            }
            catch
            {
                return -1;
            }
        }

        private static bool TryGetPlayerDataInt(PlayerData data, string fieldName, out int value)
        {
            value = -1;
            if (data == null || string.IsNullOrWhiteSpace(fieldName))
            {
                return false;
            }

            if (!PlayerDataIntFieldCache.TryGetValue(fieldName, out FieldInfo field))
            {
                field = typeof(PlayerData).GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null && field.FieldType != typeof(int))
                {
                    field = null;
                }

                PlayerDataIntFieldCache[fieldName] = field;
            }

            if (field == null)
            {
                return false;
            }

            try
            {
                object raw = field.GetCachedValue(data);
                if (raw is int intValue)
                {
                    value = intValue;
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static string GetCharmDisplayName(int charmId)
        {
            if (charmId == (int)Charm.Kingsoul)
            {
                PlayerData data = PlayerData.instance;
                if (data != null && (data.gotShadeCharm || data.royalCharmState >= 4))
                {
                    return "Void Heart";
                }

                return "Kingsoul";
            }

            if (CustomCharmDisplayNames.TryGetValue(charmId, out string friendlyName))
            {
                return friendlyName;
            }

            return ((Charm)charmId).ToString();
        }
    }

    public sealed class SpeedWarnTracker
    {
        private const float TimeScaleTolerance = 0.001f;
        private readonly List<string> warnings = new();

        private float defaultTimeScale = 1f;
        private bool speedDeviationActive;
        private bool speedWarningIssued;
        private long speedDeviationStartUnix;
        private float deviationReferenceTimeScale = 1f;

        public IReadOnlyList<string> Warnings => warnings;

        public void Reset(float currentScale)
        {
            float clamped = Mathf.Max(currentScale, 0f);
            defaultTimeScale = clamped;
            ResetDeviationTracking();
        }

        public void ClearWarnings() => warnings.Clear();

        public void LogInitial(StreamWriter writer, long lastUnixTime)
        {
            // Intentionally left blank: we only keep SpeedWarn entries.
        }

        public void Update(StreamWriter writer, string arenaName, long lastUnixTime)
        {
            Update(writer, arenaName, lastUnixTime, DateTimeOffset.Now.ToUnixTimeMilliseconds());
        }

        public void Update(StreamWriter writer, string arenaName, long lastUnixTime, long nowUnixTime)
        {
            float currentScale = Mathf.Max(Time.timeScale, 0f);
            long now = nowUnixTime;

            if (Mathf.Abs(currentScale - defaultTimeScale) < TimeScaleTolerance)
            {
                ResetDeviationTracking();
            }
            else
            {
                if (!speedDeviationActive || Mathf.Abs(currentScale - deviationReferenceTimeScale) >= TimeScaleTolerance)
                {
                    speedDeviationActive = true;
                    speedWarningIssued = false;
                    speedDeviationStartUnix = now;
                    deviationReferenceTimeScale = currentScale;
                }
                else if (!speedWarningIssued && now - speedDeviationStartUnix >= 3000)
                {
                    LogSpeedWarning(writer, arenaName, lastUnixTime, currentScale, now);
                    speedWarningIssued = true;
                }
            }

        }

        private void LogSpeedWarning(StreamWriter writer, string arenaName, long lastUnixTime, float currentScale, long now)
        {
            if (writer == null)
            {
                return;
            }

            double durationSeconds = (now - speedDeviationStartUnix) / 1000.0;
            CoreSessionLogger.AddSpeedWarning(warnings, arenaName, now - lastUnixTime, defaultTimeScale, currentScale, durationSeconds);
        }

        private void ResetDeviationTracking()
        {
            speedDeviationActive = false;
            speedWarningIssued = false;
            speedDeviationStartUnix = 0;
            deviationReferenceTimeScale = defaultTimeScale;
        }
    }

    public sealed class HitWarnTracker
    {
        private readonly List<string> warnings = new();
        private int? lastHealth;
        private int? lastLifeblood;
        private bool boundHpLogged;
        private static FieldInfo boundShellField;
        private static PropertyInfo boundShellProperty;
        private static Func<bool> boundShellGetter;
        private static bool boundShellLookupFailed;

        public IReadOnlyList<string> Warnings => warnings;

        public void Reset()
        {
            warnings.Clear();
            lastHealth = null;
            lastLifeblood = null;
            boundHpLogged = false;
        }

        public void ClearWarnings() => warnings.Clear();

        public void LogDamageEvent(StreamWriter writer, string arenaName, long lastUnixTime, long nowUnixTime, int hazardType, int damageAmount)
        {
            if (writer == null || damageAmount <= 0)
            {
                return;
            }

            _ = hazardType;
            CaptureHealthState(arenaName, lastUnixTime, nowUnixTime, writer);
        }

        public void LogDeathEvent(StreamWriter writer, string arenaName, long lastUnixTime, long nowUnixTime)
        {
            if (writer == null)
            {
                return;
            }

            CaptureHealthState(arenaName, lastUnixTime, nowUnixTime, writer);
        }

        public void Update(StreamWriter writer, string arenaName, long lastUnixTime)
        {
            Update(writer, arenaName, lastUnixTime, DateTimeOffset.Now.ToUnixTimeMilliseconds());
        }

        public void Update(StreamWriter writer, string arenaName, long lastUnixTime, long nowUnixTime)
        {
            if (writer == null)
            {
                return;
            }

            bool boundShellActive = IsBoundShellActive();
            if (boundShellActive && !boundHpLogged)
            {
                boundHpLogged = true;
            }

            CaptureHealthState(arenaName, lastUnixTime, nowUnixTime, writer, boundShellActive);
        }

        private void TrackMasks(StreamWriter writer, string arenaName, long lastUnixTime, long nowUnixTime, int currentHealth, bool boundShellActive)
        {
            if (!lastHealth.HasValue)
            {
                lastHealth = currentHealth;
                return;
            }

            if (boundShellActive && ShouldSuppressBoundMaskChange(lastHealth.Value, currentHealth))
            {
                lastHealth = currentHealth;
                return;
            }

            if (currentHealth < lastHealth.Value)
            {
                int lost = lastHealth.Value - currentHealth;
                LogMaskChange(writer, arenaName, lastUnixTime, nowUnixTime, lastHealth.Value, currentHealth, -lost);
            }
            else if (currentHealth > lastHealth.Value)
            {
                int gained = currentHealth - lastHealth.Value;
                LogMaskChange(writer, arenaName, lastUnixTime, nowUnixTime, lastHealth.Value, currentHealth, gained);
            }

            lastHealth = currentHealth;
        }

        private void TrackLifeblood(StreamWriter writer, string arenaName, long lastUnixTime, long nowUnixTime, int currentLifeblood)
        {
            if (!lastLifeblood.HasValue)
            {
                lastLifeblood = currentLifeblood;
                return;
            }

            if (currentLifeblood < lastLifeblood.Value)
            {
                int lostBlue = lastLifeblood.Value - currentLifeblood;
                LogLifebloodChange(writer, arenaName, lastUnixTime, nowUnixTime, lastLifeblood.Value, currentLifeblood, -lostBlue);
            }
            else if (currentLifeblood > lastLifeblood.Value)
            {
                int gainedBlue = currentLifeblood - lastLifeblood.Value;
                LogLifebloodChange(writer, arenaName, lastUnixTime, nowUnixTime, lastLifeblood.Value, currentLifeblood, gainedBlue);
            }

            lastLifeblood = currentLifeblood;
        }

        private void LogMaskChange(StreamWriter writer, string arenaName, long lastUnixTime, long nowUnixTime, int prev, int current, int delta)
        {
            long unixTime = nowUnixTime;
            string sign = delta >= 0 ? "+" : string.Empty;
            string arena = string.IsNullOrEmpty(arenaName) ? "UnknownArena" : arenaName;
            string warnEntry = $"|{arena}|+{unixTime - lastUnixTime}|{prev}->{current}|{sign}{delta} mask(s)";
            warnings.Add(warnEntry);
        }

        private void LogLifebloodChange(StreamWriter writer, string arenaName, long lastUnixTime, long nowUnixTime, int prev, int current, int delta)
        {
            long unixTime = nowUnixTime;
            string sign = delta >= 0 ? "+" : string.Empty;
            string arena = string.IsNullOrEmpty(arenaName) ? "UnknownArena" : arenaName;
            string warnEntry = $"|{arena}|+{unixTime - lastUnixTime}|Lifeblood {prev}->{current}|{sign}{delta}";
            warnings.Add(warnEntry);
        }

        private void CaptureHealthState(string arenaName, long lastUnixTime, long nowUnixTime, StreamWriter writer)
        {
            CaptureHealthState(arenaName, lastUnixTime, nowUnixTime, writer, IsBoundShellActive());
        }

        private void CaptureHealthState(string arenaName, long lastUnixTime, long nowUnixTime, StreamWriter writer, bool boundShellActive)
        {
            PlayerData data = PlayerData.instance;
            if (data == null)
            {
                return;
            }

            int currentHealth = Mathf.Max(0, data.health);
            int currentLifeblood = Mathf.Max(0, data.healthBlue);

            TrackMasks(writer, arenaName, lastUnixTime, nowUnixTime, currentHealth, boundShellActive);
            TrackLifeblood(writer, arenaName, lastUnixTime, nowUnixTime, currentLifeblood);
        }

        private static bool ShouldSuppressBoundMaskChange(int previousHealth, int currentHealth)
        {
            int delta = currentHealth - previousHealth;
            if (delta == 0)
            {
                return true;
            }

            return Math.Abs(delta) >= 4;
        }

        private static bool IsBoundShellActive()
        {
            if (boundShellLookupFailed)
            {
                return false;
            }

            if (boundShellGetter == null)
            {
                const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                Type type = typeof(BossSequenceController);
                boundShellField = type.GetField("BoundShell", flags) ?? type.GetField("boundShell", flags);
                boundShellProperty = type.GetProperty("BoundShell", flags) ?? type.GetProperty("boundShell", flags);
                if (boundShellField == null && boundShellProperty == null)
                {
                    boundShellLookupFailed = true;
                    return false;
                }

                if (boundShellProperty != null)
                {
                    boundShellGetter = () => boundShellProperty.GetCachedValue(null) is bool value && value;
                }
                else if (boundShellField != null)
                {
                    boundShellGetter = () => boundShellField.GetCachedValue(null) is bool value && value;
                }
                else
                {
                    boundShellLookupFailed = true;
                    return false;
                }
            }

            try
            {
                return boundShellGetter();
            }
            catch
            {
                return false;
            }
        }
    }

    public sealed class DamageChangeTracker
    {
        private readonly Dictionary<string, OwnerDamageState> ownerStatesByKey = new(StringComparer.Ordinal);
        private readonly List<string> changes = new();
        private readonly List<DamageChangeEntry> entries = new();

        public IReadOnlyList<string> Changes => changes;

        public void Reset()
        {
            ownerStatesByKey.Clear();
            changes.Clear();
            entries.Clear();
        }

        public void Track(int ownerId, string ownerName, string sceneName, long deltaMs, int damageDealt, float multiplier)
        {
            _ = ownerId;
            Track(ownerName, sceneName, deltaMs, damageDealt, multiplier);
        }

        public void Track(string ownerName, string sceneName, long deltaMs, int damageDealt, float multiplier)
        {
            if (string.IsNullOrEmpty(ownerName))
            {
                return;
            }

            string normalizedSceneName = string.IsNullOrEmpty(sceneName) ? "UnknownScene" : sceneName;
            string ownerStateKey = BuildOwnerStateKey(ownerName);
            if (!ownerStatesByKey.TryGetValue(ownerStateKey, out OwnerDamageState ownerState) ||
                ownerState == null)
            {
                ownerState = new OwnerDamageState(ownerName);
                ownerStatesByKey[ownerStateKey] = ownerState;
            }

            int finalDamage = CalculateFinalDamage(damageDealt, multiplier);
            bool newDamage = ownerState.UniqueDamages.Add(finalDamage);
            if (newDamage)
            {
                string line = $"Add NEW unique damage: {ownerName}-{normalizedSceneName}/{deltaMs} #{finalDamage}";
                changes.Add(line);
                entries.Add(new DamageChangeEntry(ownerName, line));
            }
        }

        private static int CalculateFinalDamage(int damageDealt, float multiplier)
        {
            int baseDamage = Math.Max(0, damageDealt);
            if (baseDamage == 0)
            {
                return 0;
            }

            float normalizedMultiplier = float.IsNaN(multiplier) || float.IsInfinity(multiplier)
                ? 1f
                : Mathf.Max(0f, multiplier);

            if (Mathf.Approximately(normalizedMultiplier, 1f))
            {
                return baseDamage;
            }

            double scaled = baseDamage * normalizedMultiplier;
            if (scaled <= 0d)
            {
                return 0;
            }

            return Math.Max(0, (int)Math.Round(scaled, MidpointRounding.AwayFromZero));
        }

        private static string BuildOwnerStateKey(string ownerName)
        {
            return ownerName ?? string.Empty;
        }

        public static Dictionary<string, List<string>> SortLogsByObjectName(IEnumerable<string> logs)
        {
            Dictionary<string, List<string>> sortedLogs = new(StringComparer.Ordinal);
            if (logs == null)
            {
                return sortedLogs;
            }

            foreach (string log in logs)
            {
                string objectName = ExtractObjectName(log);
                if (objectName == null)
                {
                    continue;
                }

                if (!sortedLogs.TryGetValue(objectName, out List<string> list))
                {
                    list = new List<string>();
                    sortedLogs[objectName] = list;
                }
                list.Add(log);
            }

            return sortedLogs;
        }

        public static void WriteSection(StreamWriter writer, DamageChangeTracker tracker)
        {
            if (writer == null || tracker == null)
            {
                return;
            }

            LogWrite.EncryptedLine(writer, "DamageChange:");
            Dictionary<string, List<string>> groupedLogs = TempObjectPools.RentLogGroupMap();
            try
            {
                FillSortedLogsByOwner(tracker.entries, groupedLogs);
                foreach (KeyValuePair<string, List<string>> entry in groupedLogs)
                {
                    LogWrite.EncryptedLine(writer, $"{entry.Key}:");
                    foreach (string log in entry.Value)
                    {
                        LogWrite.EncryptedLine(writer, $"  {log}");
                    }
                    LogWrite.EncryptedLine(writer, "\n");
                }
            }
            finally
            {
                TempObjectPools.ReturnLogGroupMap(groupedLogs, returnNestedLists: true);
            }

            LogWrite.EncryptedLine(writer, "---------------------------------------------------");
        }

        private static void FillSortedLogsByOwner(IReadOnlyList<DamageChangeEntry> sourceEntries, Dictionary<string, List<string>> groupedLogs)
        {
            groupedLogs.Clear();
            if (sourceEntries == null || sourceEntries.Count == 0)
            {
                return;
            }

            for (int i = 0; i < sourceEntries.Count; i++)
            {
                DamageChangeEntry entry = sourceEntries[i];
                if (string.IsNullOrEmpty(entry.OwnerName) || string.IsNullOrEmpty(entry.Log))
                {
                    continue;
                }

                if (!groupedLogs.TryGetValue(entry.OwnerName, out List<string> list))
                {
                    list = TempObjectPools.RentGroupList();
                    groupedLogs[entry.OwnerName] = list;
                }

                list.Add(entry.Log);
            }
        }

        private static void FillSortedLogsByObjectName(IEnumerable<string> logs, Dictionary<string, List<string>> groupedLogs)
        {
            groupedLogs.Clear();
            if (logs == null)
            {
                return;
            }

            foreach (string log in logs)
            {
                string objectName = ExtractObjectName(log);
                if (objectName == null)
                {
                    continue;
                }

                if (!groupedLogs.TryGetValue(objectName, out List<string> list))
                {
                    list = TempObjectPools.RentGroupList();
                    groupedLogs[objectName] = list;
                }

                list.Add(log);
            }
        }

        private static string ExtractObjectName(string log)
        {
            if (string.IsNullOrEmpty(log))
            {
                return null;
            }

            const string damagePrefix = "Add NEW unique damage: ";
            const string multiplierPrefix = "Add NEW unique multiplier: ";
            int ownerStart;
            if (log.StartsWith(damagePrefix, StringComparison.Ordinal))
            {
                ownerStart = damagePrefix.Length;
            }
            else if (log.StartsWith(multiplierPrefix, StringComparison.Ordinal))
            {
                ownerStart = multiplierPrefix.Length;
            }
            else
            {
                return null;
            }

            int ownerEnd = log.IndexOf('-', ownerStart);
            if (ownerEnd <= ownerStart)
            {
                return null;
            }

            string owner = log.Substring(ownerStart, ownerEnd - ownerStart).Trim();
            return owner.Length == 0 ? null : owner;
        }

        private readonly struct DamageChangeEntry
        {
            internal DamageChangeEntry(string ownerName, string log)
            {
                OwnerName = ownerName;
                Log = log;
            }

            internal string OwnerName { get; }
            internal string Log { get; }
        }

        private sealed class OwnerDamageState
        {
            internal OwnerDamageState(string ownerName)
            {
                OwnerName = ownerName;
                UniqueDamages = new HashSet<int>();
            }

            internal string OwnerName { get; }
            internal HashSet<int> UniqueDamages { get; }
        }
    }

    public sealed class FlukenestTracker
    {
        private const string FlukenestDamageOwnerName = "Knight/Spells/Flukenest";
        private static readonly FieldInfo flukeDamageFieldStatic =
            typeof(SpellFluke).GetField("damage", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly List<string> entries = new();
        private int? lastDamage;
        private readonly FieldInfo flukeDamageField =
            typeof(SpellFluke).GetField("damage", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        public IReadOnlyList<string> Entries => entries;

        public void Reset()
        {
            entries.Clear();
            lastDamage = null;
        }

        public void Track(GameObject target, string arenaName, long deltaMs, SpellFluke self)
        {
            if (self == null || flukeDamageField == null)
            {
                return;
            }

            int damage = 0;
            try
            {
                object raw = flukeDamageField.GetCachedValue(self);
                if (raw is int intDamage)
                {
                    damage = intDamage;
                }
            }
            catch
            {
                
            }

            if (lastDamage.HasValue && lastDamage.Value == damage)
            {
                return;
            }

            lastDamage = damage;
            string targetName = target != null ? target.name : "null";
            string arena = string.IsNullOrEmpty(arenaName) ? "UnknownArena" : arenaName;
            entries.Add($"Flukenest: {targetName}-{arena}/{deltaMs} #{damage}");
        }

        public static void WriteSection(StreamWriter writer, FlukenestTracker tracker)
        {
            if (writer == null || tracker == null)
            {
                return;
            }

            LogWrite.EncryptedLine(writer, "Flukenest:");
            if (tracker.Entries.Count == 0)
            {
                LogWrite.EncryptedLine(writer, "  (none)");
            }
            else
            {
                foreach (string log in tracker.Entries)
                {
                    LogWrite.EncryptedLine(writer, log);
                }
            }
            LogWrite.EncryptedLine(writer, "\n");
        }

        public static void WriteSectionWithSeparator(StreamWriter writer, FlukenestTracker tracker, string separator = "---------------------------------------------------")
        {
            if (writer == null || tracker == null)
            {
                return;
            }

            WriteSection(writer, tracker);
            if (!string.IsNullOrEmpty(separator))
            {
                LogWrite.EncryptedLine(writer, separator);
            }
        }

        public static void TrackGlobal(bool isLogging, StreamWriter writer, FlukenestTracker tracker, string arenaName, long lastUnixTime, long nowUnixTime, SpellFluke self, GameObject target)
        {
            if (!isLogging || writer == null || tracker == null)
            {
                return;
            }

            long unixTime = nowUnixTime > 0 ? nowUnixTime : DateTimeOffset.Now.ToUnixTimeMilliseconds();
            tracker.Track(target, arenaName, unixTime - lastUnixTime, self);
        }

        public static void HandleDoDamage(bool isLogging, StreamWriter writer, FlukenestTracker tracker, string arenaName, long lastUnixTime, long nowUnixTime, On.SpellFluke.orig_DoDamage orig, SpellFluke self, GameObject obj, int upwardRecursionAmount, bool burst)
        {
            try
            {
                TrackGlobal(isLogging, writer, tracker, arenaName, lastUnixTime, nowUnixTime, self, obj);
            }
            catch (Exception e)
            {
                global::ReplayLogger.InternalDiagnostics.Warn($"ReplayLogger: failed to log Flukenest damage: {e.Message}");
            }

            orig(self, obj, upwardRecursionAmount, burst);
        }

        public static void HandleDoDamage(bool isLogging, StreamWriter writer, DamageChangeTracker damageChangeTracker, string arenaName, long lastUnixTime, long nowUnixTime, On.SpellFluke.orig_DoDamage orig, SpellFluke self, GameObject obj, int upwardRecursionAmount, bool burst)
        {
            try
            {
                TrackAsDamageChange(isLogging, writer, damageChangeTracker, arenaName, lastUnixTime, nowUnixTime, self);
            }
            catch (Exception e)
            {
                global::ReplayLogger.InternalDiagnostics.Warn($"ReplayLogger: failed to track Flukenest in DamageChange: {e.Message}");
            }

            orig(self, obj, upwardRecursionAmount, burst);
        }

        private static void TrackAsDamageChange(bool isLogging, StreamWriter writer, DamageChangeTracker damageChangeTracker, string arenaName, long lastUnixTime, long nowUnixTime, SpellFluke self)
        {
            if (!isLogging || writer == null || damageChangeTracker == null || self == null)
            {
                return;
            }

            if (flukeDamageFieldStatic == null)
            {
                return;
            }

            int damage;
            try
            {
                object raw = flukeDamageFieldStatic.GetCachedValue(self);
                if (raw is not int intDamage || intDamage <= 0)
                {
                    return;
                }

                damage = intDamage;
            }
            catch
            {
                return;
            }

            long unixTime = nowUnixTime > 0 ? nowUnixTime : DateTimeOffset.Now.ToUnixTimeMilliseconds();
            string scene = string.IsNullOrEmpty(arenaName) ? "UnknownArena" : arenaName;
            damageChangeTracker.Track(FlukenestDamageOwnerName, scene, unixTime - lastUnixTime, damage, 1f);
        }
    }
}



