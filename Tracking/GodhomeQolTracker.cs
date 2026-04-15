using System;
using System.Collections.Generic;
using System.IO;

namespace ReplayLogger
{
    internal sealed class GodhomeQolTracker
    {
        private readonly FastSuperDashTracker fastSuperDash = new();
        private readonly DreamshieldSettingsTracker dreamshieldSettings = new();
        private readonly CarefreeMelodyResetTracker carefreeMelodyReset = new();
        private readonly BossChallengeSettingsTracker bossChallengeSettings = new();
        private readonly GodhomeQolBossManipulateTracker bossManipulateSettings = new();
        private readonly GearSwitcherSettingsTracker gearSwitcherSettings = new();
        private long lastUpdateTime;
        private int stablePollCount;
        private string lastArenaName;
        private const int ActivePollMs = 250;
        private const int NormalPollMs = 1000;
        private const int IdlePollMs = 2000;
        private const int StablePollThreshold = 5;

        public void Reset()
        {
            fastSuperDash.Reset();
            dreamshieldSettings.Reset();
            carefreeMelodyReset.Reset();
            bossChallengeSettings.Reset();
            bossManipulateSettings.Reset();
            gearSwitcherSettings.Reset();
            lastUpdateTime = 0;
            stablePollCount = 0;
            lastArenaName = null;
        }

        public void StartFight(string arenaName, long baseUnixTime, bool includeBossManipulate = true)
        {
            fastSuperDash.StartFight(arenaName, baseUnixTime);
            dreamshieldSettings.StartFight(arenaName, baseUnixTime);
            carefreeMelodyReset.StartFight(arenaName, baseUnixTime);
            bossChallengeSettings.StartFight(arenaName, baseUnixTime);
            if (includeBossManipulate)
            {
                bossManipulateSettings.StartFight(arenaName, baseUnixTime);
            }
            gearSwitcherSettings.StartFight(arenaName, baseUnixTime);
            lastUpdateTime = 0;
            stablePollCount = 0;
            lastArenaName = arenaName;
        }

        public void Update(string arenaName, long nowUnixTime, bool debugUiVisible)
        {
            if (!string.Equals(lastArenaName, arenaName, StringComparison.Ordinal))
            {
                lastArenaName = arenaName;
                lastUpdateTime = 0;
                stablePollCount = 0;
            }

            long now = nowUnixTime;
            int throttleMs = debugUiVisible
                ? ActivePollMs
                : (stablePollCount >= StablePollThreshold ? IdlePollMs : NormalPollMs);

            if (lastUpdateTime > 0 && now - lastUpdateTime < throttleMs)
            {
                return;
            }

            lastUpdateTime = now;
            fastSuperDash.Update(arenaName, now);
            dreamshieldSettings.Update(arenaName, now);
            carefreeMelodyReset.Update(arenaName, now);
            bossChallengeSettings.Update(arenaName, now);
            bossManipulateSettings.Update(arenaName, now);
            gearSwitcherSettings.Update(arenaName, now);

            if (debugUiVisible)
            {
                stablePollCount = 0;
            }
            else
            {
                stablePollCount = Math.Min(stablePollCount + 1, 1000);
            }
        }

        public void WriteSection(StreamWriter writer, string separator = "---------------------------------------------------")
        {
            if (writer == null)
            {
                return;
            }

            if (!fastSuperDash.HasData && !dreamshieldSettings.HasData && !carefreeMelodyReset.HasData && !bossChallengeSettings.HasData && !bossManipulateSettings.HasData && !gearSwitcherSettings.HasData)
            {
                return;
            }

            List<string> batch = TempObjectPools.RentStringList(2);
            try
            {
                batch.Add("GodhomeQoL:");
                LogWrite.EncryptedLines(writer, batch);
                batch.Clear();

                string blockSeparator = string.IsNullOrEmpty(separator) ? "---------------------------------------------------" : separator;
                int blocksWritten = 0;

                if (fastSuperDash.HasData)
                {
                    if (blocksWritten > 0)
                    {
                        LogWrite.EncryptedLine(writer, blockSeparator);
                    }
                    fastSuperDash.WriteSection(writer);
                    blocksWritten++;
                }

                if (dreamshieldSettings.HasData)
                {
                    if (blocksWritten > 0)
                    {
                        LogWrite.EncryptedLine(writer, blockSeparator);
                    }
                    dreamshieldSettings.WriteSection(writer);
                    blocksWritten++;
                }

                if (carefreeMelodyReset.HasData)
                {
                    if (blocksWritten > 0)
                    {
                        LogWrite.EncryptedLine(writer, blockSeparator);
                    }
                    carefreeMelodyReset.WriteSection(writer);
                    blocksWritten++;
                }

                if (bossChallengeSettings.HasData)
                {
                    if (blocksWritten > 0)
                    {
                        LogWrite.EncryptedLine(writer, blockSeparator);
                    }
                    bossChallengeSettings.WriteSection(writer);
                    blocksWritten++;
                }

                if (bossManipulateSettings.HasData)
                {
                    if (blocksWritten > 0)
                    {
                        LogWrite.EncryptedLine(writer, blockSeparator);
                    }
                    bossManipulateSettings.WriteSection(writer);
                    blocksWritten++;
                }

                if (gearSwitcherSettings.HasData)
                {
                    if (blocksWritten > 0)
                    {
                        LogWrite.EncryptedLine(writer, blockSeparator);
                    }
                    gearSwitcherSettings.WriteSection(writer);
                    blocksWritten++;
                }

                batch.Add(string.Empty);
                if (!string.IsNullOrEmpty(separator))
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
    }
}
