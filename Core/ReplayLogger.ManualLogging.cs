using System;
using System.IO;
using UnityEngine;

namespace ReplayLogger
{
    public partial class ReplayLogger
    {
        private const float ManualStopHoldSeconds = 2f;
        private bool isManualLogging;
        private float manualHoldStartTime;
        private string manualStartScene;
        private long manualRoomStartUnixTime;
        private bool manualRoomHeaderWritten;
        private float manualStatusDelayUntil;

        internal void StopManualLoggingFromMenu()
        {
            if (isManualLogging)
            {
                StopManualLogging();
            }
        }

        private void HandleManualLoggingHotkey()
        {
            if (!IsManualModeEnabled() || IsRebindInProgress)
            {
                ResetManualHold();
                return;
            }

            KeyCode key = GetManualLogKey();
            if (key == KeyCode.None)
            {
                ResetManualHold();
                return;
            }

            if (!isManualLogging)
            {
                if (Input.GetKeyDown(key))
                {
                    StartManualLogging();
                }
                return;
            }

            if (Input.GetKey(key))
            {
                if (manualHoldStartTime <= 0f)
                {
                    manualHoldStartTime = Time.unscaledTime;
                }

                float heldSeconds = Time.unscaledTime - manualHoldStartTime;
                if (heldSeconds >= ManualStopHoldSeconds)
                {
                    StopManualLogging();
                }
            }
            else
            {
                ResetManualHold();
            }
        }

        private void UpdateManualRoomTransition()
        {
            if (!isManualLogging || writer == null)
            {
                return;
            }

            string sceneName = GameManager.instance?.sceneName ?? lastScene;
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                return;
            }

            if (sceneName == manualStartScene && manualRoomHeaderWritten)
            {
                return;
            }

            if (manualRoomHeaderWritten)
            {
                long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                FlushKeyLogBufferIfNeeded(now, force: true);
                FlushManualDamageBufferForTransition();
                CoreSessionLogger.WriteSeparator(writer);
            }

            manualStartScene = sceneName;
            manualRoomStartUnixTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            lastUnixTime = manualRoomStartUnixTime;
            string dataTime = DateTimeOffset.FromUnixTimeMilliseconds(manualRoomStartUnixTime).ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss.fff");
            DamageAnfInv?.Add($"{dataTime}|{manualRoomStartUnixTime}|{manualStartScene}|");
            LogWrite.EncryptedLine(writer, $"{dataTime}|{manualRoomStartUnixTime}|{manualStartScene}|");
            manualRoomHeaderWritten = true;
        }

        private void FlushManualDamageBufferForTransition()
        {
            if (writer == null || DamageAnfInv == null || !DamageAnfInv.HasContent)
            {
                return;
            }

            if (!damageSectionStarted)
            {
                LogWrite.EncryptedLine(writer, "\n------------------------DAMAGE INV------------------------\n");
                damageSectionStarted = true;
            }

            DamageAnfInv.WriteEncryptedLines(writer);
            DamageAnfInv.Clear();
        }

        private void StartManualLogging()
        {
            if (writer != null || isPlayChalange)
            {
                return;
            }

            DateTimeOffset now = DateTimeOffset.Now;
            lastUnixTime = now.ToUnixTimeMilliseconds();
            startUnixTime = lastUnixTime;
            string dataTime = now.ToString("dd.MM.yyyy HH:mm:ss.fff");
            int currentPlayTime = (int)(PlayerData.instance.playTime * 100);

            lastScene = GameManager.instance?.sceneName ?? lastScene;
            manualStartScene = string.IsNullOrWhiteSpace(lastScene) ? "Manual" : lastScene;

            isPlayChalange = true;
            isManualLogging = true;
            isChallengeCompleted = "-";
            bossCounter = 0;
            currentPanteon = (null, null);
            manualStatusDelayUntil = 0f;

            try
            {
                lastString = KeyloggerLogEncryption.GenerateKeyAndIV();
                currentNameLog = Path.Combine(dllDir, $"KeyLog{DateTime.UtcNow.Ticks}.log");
                DamageAnfInv?.Clear();
                InvWarn?.Clear();
                speedWarnBuffer?.Clear();
                hitWarnBuffer?.Clear();
                DamageAnfInv = new BufferedLogSection($"{currentNameLog}.damage.tmp", BufferedSectionThreshold);
                InvWarn = new BufferedLogSection($"{currentNameLog}.warn.tmp", BufferedSectionThreshold);
                speedWarnBuffer = new BufferedLogSection($"{currentNameLog}.speed.tmp", BufferedSectionThreshold);
                hitWarnBuffer = new BufferedLogSection($"{currentNameLog}.hit.tmp", BufferedSectionThreshold);
                writer = new BlockLogWriter(currentNameLog, lastString, BlockSizeBytes, BlockMaxAgeMs);

                AheSettingsManager.RefreshSnapshot();
                speedWarnTracker.Reset(Mathf.Max(Time.timeScale, 0f));
                hitWarnTracker.Reset();
                bool initialDebugUiVisible = DebugModIntegration.TryGetUiVisible(out bool visible) && visible;
                debugModEventsTracker.Reset(initialDebugUiVisible);
                debugMenuTracker.Reset(initialDebugUiVisible);
                debugHotkeysTracker.InitializeBindings();
                keyLogBuffer.Clear();
                lastKeyLogFlushTime = 0;
                pressedKeys.Clear();
                pressedKeysBuffer.Clear();
                hasHeroBoxState = false;
                lastHeroBoxActive = -1;
                heroBoxOffStartTime = -1f;
                damageSectionStarted = false;
                charmsChangeTracker.Reset();

                CoreSessionLogger.WriteEncryptedModSnapshot(writer, modsDir, "---------------------------------------------------");
                LogWrite.EncryptedLine(writer, CoreSessionLogger.BuildEquippedCharmsLine());
                CoreSessionLogger.WriteEncryptedSkillLines(writer, "---------------------------------------------------");
                speedWarnTracker.LogInitial(writer, lastUnixTime);
                InitializeDebugModHooks();
                godhomeQolTracker.Reset();
                godhomeQolTracker.StartFight(manualStartScene, lastUnixTime);
            }
            catch (Exception e)
            {
                Modding.Logger.LogError("Ошибка при старте ручного лога: " + e.Message);
            }

            int seed = (int)(lastUnixTime ^ currentPlayTime);
            if (customCanvas != null)
            {
                customCanvas.DestroyCanvas();
                customCanvas = null;
            }
            customCanvas = new CustomCanvas(new NumberInCanvas(seed), new LoadingSprite(lastString));
            customCanvas?.ShowManualStatus(false);

            manualRoomStartUnixTime = lastUnixTime;
            manualRoomHeaderWritten = false;
            UpdateManualRoomTransition();

            ResetManualHold();
        }

        private void StopManualLogging()
        {
            if (!isManualLogging)
            {
                return;
            }

            manualStatusDelayUntil = Time.unscaledTime + ReplayLogger.GetHudToastSeconds();
            customCanvas?.ShowManualStatus(false);
            Close(true);
            ResetManualHold();
        }

        private void ResetManualHold()
        {
            manualHoldStartTime = 0f;
        }

        private void UpdateManualStatus()
        {
            if (isPlayChalange && !isManualLogging)
            {
                return;
            }

            if (!IsManualModeEnabled())
            {
                if (!isManualLogging && customCanvas != null && customCanvas.HasCanvas)
                {
                    customCanvas.ShowManualStatus(false);
                    customCanvas.DestroyCanvas();
                    customCanvas = null;
                }
                manualStatusDelayUntil = 0f;
                return;
            }

            if (isManualLogging)
            {
                customCanvas?.ShowManualStatus(false);
                return;
            }

            if (manualStatusDelayUntil > 0f && Time.unscaledTime < manualStatusDelayUntil)
            {
                customCanvas?.ShowManualStatus(false);
                return;
            }

            if (customCanvas == null || !customCanvas.HasCanvas)
            {
                customCanvas = new CustomCanvas(new NumberInCanvas(0), new LoadingSprite("ReplayLogger"));
                customCanvas.ClearHud();
            }

            customCanvas.ShowManualStatus(true);
        }
    }
}
