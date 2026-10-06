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
        private readonly GodhomeQolCheatsTracker cheatsSettings = new();
        private readonly MaskDamageSettingsTracker maskDamageSettings = new();
        private readonly GearSwitcherSettingsTracker gearSwitcherSettings = new();
        private readonly GameOptimizeSettingsTracker gameOptimizeSettings = new();

        private readonly ZoteHelperSettingsTracker zoteHelperSettings = new();

        private readonly GodhomeQolBossManipulateTracker bossManipulateSettings = new();
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
            cheatsSettings.Reset();
            maskDamageSettings.Reset();
            gearSwitcherSettings.Reset();
            gameOptimizeSettings.Reset();
            zoteHelperSettings.Reset();
            bossManipulateSettings.Reset();
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
            cheatsSettings.StartFight(arenaName, baseUnixTime);
            maskDamageSettings.StartFight(arenaName, baseUnixTime);
            gearSwitcherSettings.StartFight(arenaName, baseUnixTime);
            gameOptimizeSettings.StartFight(arenaName, baseUnixTime);
            if (includeBossManipulate)
            {
                zoteHelperSettings.StartFight(arenaName, baseUnixTime);
                bossManipulateSettings.StartFight(arenaName, baseUnixTime);
            }
            else
            {
                zoteHelperSettings.Reset();
                bossManipulateSettings.Reset();
            }
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
            cheatsSettings.Update(arenaName, now);
            maskDamageSettings.Update(arenaName, now);
            gearSwitcherSettings.Update(arenaName, now);
            gameOptimizeSettings.Update(arenaName, now);
            if (includeBossManipulate)
            {
                zoteHelperSettings.Update(arenaName, now);
                bossManipulateSettings.Update(arenaName, now);
            }

            if (debugUiVisible)
            {
                stablePollCount = 0;
            }
            else
            {
                stablePollCount = Math.Min(stablePollCount + 1, 1000);
            }
        }

        public void RecordObservedSoulGain(string arenaName, long lastUnixTime, long nowUnixTime, int mainGained, int reserveGained)
        {
            gearSwitcherSettings.RecordObservedSoulGain(arenaName, lastUnixTime, nowUnixTime, mainGained, reserveGained);
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
                !cheatsSettings.HasData &&
                !maskDamageSettings.HasData &&
                !gearSwitcherSettings.HasData &&
                !gameOptimizeSettings.HasData &&
                !(includeBossManipulate && zoteHelperSettings.HasData) &&
                !(includeBossManipulate && bossManipulateSettings.HasData))
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

                if (includeBossManipulate && zoteHelperSettings.HasData)
                {
                    if (blocksWritten > 0)
                    {
                        LogWrite.EncryptedLine(writer, blockSeparator);
                    }
                    zoteHelperSettings.WriteSection(writer);
                    blocksWritten++;
                }

                if (includeBossManipulate && bossManipulateSettings.HasData)
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

                if (maskDamageSettings.HasData)
                {
                    if (blocksWritten > 0)
                    {
                        LogWrite.EncryptedLine(writer, blockSeparator);
                    }
                    maskDamageSettings.WriteSection(writer);
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

                if (gameOptimizeSettings.HasData)
                {
                    if (blocksWritten > 0)
                    {
                        LogWrite.EncryptedLine(writer, blockSeparator);
                    }
                    gameOptimizeSettings.WriteSection(writer);
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
