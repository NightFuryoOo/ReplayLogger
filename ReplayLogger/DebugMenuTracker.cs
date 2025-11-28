using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ReplayLogger
{
    internal sealed class DebugMenuTracker
    {
        private readonly Dictionary<string, string> currentState = new(StringComparer.Ordinal);
        private readonly List<string> changes = new();

        private bool allowLogging;

        public void Reset(bool initialUiVisible = false)
        {
            currentState.Clear();
            changes.Clear();
            allowLogging = true;
        }

        public void Update(StreamWriter writer, string arenaName, long lastUnixTime)
        {
            Dictionary<string, string> snapshot = BuildSnapshot();
            if (snapshot.Count == 0)
            {
                return;
            }

            if (currentState.Count == 0)
            {
                foreach (var entry in snapshot)
                {
                    currentState[entry.Key] = entry.Value;
                }
                return;
            }

            foreach (var entry in snapshot)
            {
                if (currentState.TryGetValue(entry.Key, out string previous) &&
                    string.Equals(previous, entry.Value, StringComparison.Ordinal))
                {
                    continue;
                }

                if (allowLogging)
                {
                    LogChange(writer, arenaName, lastUnixTime, entry.Key, previous, entry.Value);
                }
            }

            currentState.Clear();
            foreach (var entry in snapshot)
            {
                currentState[entry.Key] = entry.Value;
            }
        }

        public void WriteSection(StreamWriter writer, string separator = "---------------------------------------------------")
        {
            if (writer == null)
            {
                return;
            }

            writer.WriteLine(KeyloggerLogEncryption.EncryptLog("DebugEvent:"));
            writer.WriteLine(KeyloggerLogEncryption.EncryptLog("State:"));

            if (currentState.Count == 0)
            {
                writer.WriteLine(KeyloggerLogEncryption.EncryptLog("  (unavailable)"));
            }
            else
            {
                foreach (var entry in currentState.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
                {
                    writer.WriteLine(KeyloggerLogEncryption.EncryptLog($"  {entry.Key}: {entry.Value}"));
                }
            }

            writer.WriteLine(KeyloggerLogEncryption.EncryptLog("Changes:"));
            if (changes.Count == 0)
            {
                writer.WriteLine(KeyloggerLogEncryption.EncryptLog("  (none)"));
            }
            else
            {
                foreach (string evt in changes)
                {
                    writer.WriteLine(KeyloggerLogEncryption.EncryptLog(evt));
                }
            }

            writer.WriteLine(KeyloggerLogEncryption.EncryptLog(string.Empty));
            if (!string.IsNullOrEmpty(separator) && (currentState.Count > 0 || changes.Count > 0))
            {
                writer.WriteLine(KeyloggerLogEncryption.EncryptLog(separator));
            }
        }

        private void LogChange(StreamWriter writer, string arenaName, long lastUnixTime, string key, string previousValue, string newValue)
        {
            long unixTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            string descriptor = previousValue == null
                ? $"{key}: {newValue}"
                : $"{key}: {previousValue} -> {newValue}";

            writer?.WriteLine(KeyloggerLogEncryption.EncryptLog($"DebugEvent|+{unixTime - lastUnixTime}|{descriptor}"));
            writer?.Flush();

            string arena = string.IsNullOrEmpty(arenaName) ? "UnknownArena" : arenaName;
            changes.Add($"  |{arena}|+{unixTime - lastUnixTime}|{descriptor}");
        }

        public void LogManualChange(StreamWriter writer, string arenaName, long lastUnixTime, string key, string previousValue, string newValue)
        {
            if (!allowLogging)
            {
                return;
            }

            LogChange(writer, arenaName, lastUnixTime, key, previousValue, newValue);
            currentState[key] = newValue ?? string.Empty;
        }

        private static Dictionary<string, string> BuildSnapshot()
        {
            Dictionary<string, string> state = new(StringComparer.Ordinal);

            try
            {
                BuildCheatsSnapshot(state);
                BuildCharmsSnapshot(state);
                BuildSkillsSnapshot(state);
            }
            catch
            {
                // ignored
            }

            return state;
        }

        private static string FormatToggle(bool value) => value ? "On" : "Off";

        private static bool TryGetDebugModBool(string fieldName, out bool value)
        {
            value = false;
            if (string.IsNullOrEmpty(fieldName))
            {
                return false;
            }

            try
            {
                Type modType = Type.GetType("DebugMod.DebugMod, DebugMod");
                if (modType == null)
                {
                    return false;
                }

                FieldInfo field = modType.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (field?.FieldType == typeof(bool) && field.GetValue(null) is bool flag)
                {
                    value = flag;
                    return true;
                }
            }
            catch
            {
                // ignored
            }

            return false;
        }

        private static void BuildCheatsSnapshot(Dictionary<string, string> state)
        {
            if (DebugModIntegration.TryGetCheatToggleSnapshot(out var cheats))
            {
                state["Cheats/Infinite Soul"] = FormatToggle(cheats.InfiniteSoul);
                state["Cheats/Infinite HP"] = FormatToggle(cheats.InfiniteHp);
                state["Cheats/Noclip"] = FormatToggle(cheats.Noclip);
                state["Cheats/Lock Keybinds"] = FormatToggle(cheats.KeyBindLock);
            }
            else
            {
                state["Cheats/Infinite Soul"] = "N/A";
                state["Cheats/Infinite HP"] = "N/A";
                state["Cheats/Noclip"] = "N/A";
                state["Cheats/Lock Keybinds"] = "N/A";
            }

            if (TryGetDebugModBool("playerInvincible", out bool playerInv))
            {
                state["Cheats/Invincibility"] = FormatToggle(playerInv);
            }
            else
            {
                state["Cheats/Invincibility"] = "N/A";
            }

            if (TryGetDebugModBool("infiniteSoul", out bool infSoul))
            {
                state["Cheats/Infinite Soul (UI)"] = FormatToggle(infSoul);
            }

            if (TryGetDebugModBool("infiniteHP", out bool infHp))
            {
                state["Cheats/Infinite HP (UI)"] = FormatToggle(infHp);
            }

            if (TryGetDebugModBool("noclip", out bool noclip))
            {
                state["Cheats/Noclip (UI)"] = FormatToggle(noclip);
            }

            if (TryGetDebugModBool("KeyBindLock", out bool lockBinds))
            {
                state["Cheats/Lock Keybinds (UI)"] = FormatToggle(lockBinds);
            }

            if (PlayerData.instance != null)
            {
                state["Cheats/Infinite Jump"] = FormatToggle(PlayerData.instance.infiniteAirJump);
            }
        }

        private static void BuildCharmsSnapshot(Dictionary<string, string> state)
        {
            PlayerData data = PlayerData.instance;
            if (data == null)
            {
                return;
            }

            state["Charms/Charms Owned"] = $"{Mathf.Clamp(data.charmsOwned, 0, 40)}/40";
            state["Charms/Kingsoul"] = data.royalCharmState.ToString(CultureInfo.InvariantCulture);
            state["Charms/Grimmchild"] = data.grimmChildLevel.ToString(CultureInfo.InvariantCulture);
            state["Charms/Fragile Heart"] = data.brokenCharm_23 ? "Broken" : "Fixed";
            state["Charms/Fragile Greed"] = data.brokenCharm_24 ? "Broken" : "Fixed";
            state["Charms/Fragile Strength"] = data.brokenCharm_25 ? "Broken" : "Fixed";
            state["Charms/Overcharm"] = FormatToggle(data.overcharmed);
        }

        private static void BuildSkillsSnapshot(Dictionary<string, string> state)
        {
            PlayerData data = PlayerData.instance;
            if (data == null)
            {
                return;
            }

            state["Skills/Scream"] = data.screamLevel.ToString(CultureInfo.InvariantCulture);
            state["Skills/Fireball"] = data.fireballLevel.ToString(CultureInfo.InvariantCulture);
            state["Skills/Quake"] = data.quakeLevel.ToString(CultureInfo.InvariantCulture);
            state["Skills/Dash"] = data.hasShadowDash ? "Shade" : data.hasDash ? "Dash" : "None";
            state["Skills/Mantis Claw"] = FormatToggle(data.hasWalljump);
            state["Skills/Monarch Wings"] = FormatToggle(data.hasDoubleJump);
            state["Skills/Crystal Heart"] = FormatToggle(data.hasSuperDash);
            state["Skills/Isma's Tear"] = FormatToggle(data.hasAcidArmour);
            state["Skills/Dream Nail"] = data.dreamNailUpgraded ? "Awoken" : data.hasDreamNail ? "Normal" : "None";
            state["Skills/Dream Gate"] = FormatToggle(data.hasDreamGate);
            state["Skills/Great Slash"] = FormatToggle(data.hasDashSlash);
            state["Skills/Dash Slash"] = FormatToggle(data.hasUpwardSlash);
            state["Skills/Cyclone Slash"] = FormatToggle(data.hasCyclone);
        }
    }
}
