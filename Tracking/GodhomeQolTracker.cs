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
        private readonly GodhomeQolCheatsTracker cheatsSettings = new();
        private readonly GearSwitcherSettingsTracker gearSwitcherSettings = new();
        private long lastUpdateTime;
        private int stablePollCount;
        private string lastArenaName;
        private bool includeBossManipulate = true;
        private bool includeBossChallenge = true;
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
            cheatsSettings.Reset();
            gearSwitcherSettings.Reset();
            lastUpdateTime = 0;
            stablePollCount = 0;
            lastArenaName = null;
            includeBossManipulate = true;
            includeBossChallenge = true;
        }

        public void StartFight(string arenaName, long baseUnixTime, bool includeBossManipulate = true, bool includeBossChallenge = true)
        {
            this.includeBossManipulate = includeBossManipulate;
            this.includeBossChallenge = includeBossChallenge;
            fastSuperDash.StartFight(arenaName, baseUnixTime);
            dreamshieldSettings.StartFight(arenaName, baseUnixTime);
            carefreeMelodyReset.StartFight(arenaName, baseUnixTime);
            if (this.includeBossChallenge)
            {
                bossChallengeSettings.StartFight(arenaName, baseUnixTime);
            }
            else
            {
                bossChallengeSettings.Reset();
            }
            if (includeBossManipulate)
            {
                bossManipulateSettings.StartFight(arenaName, baseUnixTime);
            }
            cheatsSettings.StartFight(arenaName, baseUnixTime);
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
            if (includeBossChallenge)
            {
                bossChallengeSettings.Update(arenaName, now);
            }
            if (includeBossManipulate)
            {
                bossManipulateSettings.Update(arenaName, now);
            }
            cheatsSettings.Update(arenaName, now);
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

            if (!fastSuperDash.HasData &&
                !dreamshieldSettings.HasData &&
                !carefreeMelodyReset.HasData &&
                !(includeBossChallenge && bossChallengeSettings.HasData) &&
                !bossManipulateSettings.HasData &&
                !cheatsSettings.HasData &&
                !gearSwitcherSettings.HasData)
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

                if (includeBossChallenge && bossChallengeSettings.HasData)
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

                if (cheatsSettings.HasData)
                {
                    if (blocksWritten > 0)
                    {
                        LogWrite.EncryptedLine(writer, blockSeparator);
                    }
                    cheatsSettings.WriteSection(writer);
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
