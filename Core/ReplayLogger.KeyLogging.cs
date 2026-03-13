using System;
using System.Collections.Generic;
using GlobalEnums;
using On;
using UnityEngine;

namespace ReplayLogger
{
    public partial class ReplayLogger
    {
        private readonly HashSet<KeyCode> _currentlyPressedKeys = new HashSet<KeyCode>();
        private readonly List<KeyCode> _keysToRemove = new List<KeyCode>();
        private bool _keyLogFirstFrame = true;
        private long _lastCanvasUpdateSecond = -1;

        private void CheckPressedKey(On.GameManager.orig_Update orig, GameManager self)
        {
            orig(self);
            HandleManualLoggingHotkey();
            UpdateManualStatus();
            if (!isPlayChalange)
            {
                return;
            }

            UpdateManualRoomTransition();
            MonitorDebugModUi();
            if (!isManualLogging && GameManager.instance.gameState == GlobalEnums.GameState.CUTSCENE && lastScene == "GG_Radiance")
            {
                Close();
            }

            string roomName = GameManager.instance?.sceneName ?? lastScene;
            speedWarnTracker.Update(writer, roomName, lastUnixTime);
            hitWarnTracker.Update(writer, roomName, lastUnixTime);
            FlushWarningsIfNeeded(speedWarnBuffer, speedWarnTracker.Warnings, speedWarnTracker.ClearWarnings);
            FlushWarningsIfNeeded(hitWarnBuffer, hitWarnTracker.Warnings, hitWarnTracker.ClearWarnings);

            long nowMs = DateTimeOffset.Now.ToUnixTimeMilliseconds();

            long currentSecond = nowMs / 1000;
            if (currentSecond != _lastCanvasUpdateSecond)
            {
                _lastCanvasUpdateSecond = currentSecond;
                DateTimeOffset dateTimeOffset = DateTimeOffset.FromUnixTimeMilliseconds(nowMs - startUnixTime);
                customCanvas?.UpdateTime(dateTimeOffset.ToString("HH:mm:ss"));
            }

            if (_keyLogFirstFrame)
            {
                _keyLogFirstFrame = false;
                foreach (KeyCode keyCode in AllKeyCodes)
                {
                    if (Input.GetKey(keyCode))
                    {
                        _currentlyPressedKeys.Add(keyCode);
                    }
                }
            }

            if (Input.anyKeyDown)
            {
                foreach (KeyCode keyCode in AllKeyCodes)
                {
                    if (Input.GetKeyDown(keyCode))
                    {
                        _currentlyPressedKeys.Add(keyCode);
                        HandleKeyEvent(keyCode, isDown: true, nowMs);
                    }
                }
            }

            if (_currentlyPressedKeys.Count > 0)
            {
                _keysToRemove.Clear();
                foreach (KeyCode keyCode in _currentlyPressedKeys)
                {
                    if (Input.GetKeyUp(keyCode))
                    {
                        HandleKeyEvent(keyCode, isDown: false, nowMs);
                        _keysToRemove.Add(keyCode);
                    }
                }
                for (int i = 0; i < _keysToRemove.Count; i++)
                {
                    _currentlyPressedKeys.Remove(_keysToRemove[i]);
                }
            }

            FlushKeyLogBufferIfNeeded(nowMs);

        }

        private void HandleKeyEvent(KeyCode keyCode, bool isDown, long nowMs)
        {
            float fps = Time.unscaledDeltaTime == 0 ? lastFps : 1f / Time.unscaledDeltaTime;
            lastFps = fps;
            customCanvas?.UpdateWatermark(keyCode);
            int watermarkNumber = customCanvas?.numberInCanvas?.Number ?? 0;
            Color watermarkColorStruct = customCanvas?.numberInCanvas?.Color ?? Color.white;
            try
            {
                int fpsValue = Mathf.RoundToInt(fps);
                WriteKeyEventLine((int)(nowMs - lastUnixTime), keyCode, isDown, watermarkNumber, watermarkColorStruct, fpsValue);
            }
            catch (Exception e)
            {
                Modding.Logger.LogError("Key log write failed: " + e.Message);
            }

            if (isDown)
            {
                string arenaForHotkey = lastScene;
                debugHotkeysTracker.TrackActivation(keyCode, arenaForHotkey, lastUnixTime, nowMs);
            }
        }

        private void FlushWarningsIfNeeded(BufferedLogSection buffer, IReadOnlyList<string> warnings, Action clearAction)
        {
            if (buffer == null || warnings == null)
            {
                return;
            }

            if (warnings.Count < BufferedSectionThreshold)
            {
                return;
            }

            buffer.AddRange(warnings);
            clearAction?.Invoke();
        }

        private void FlushKeyLogBufferIfNeeded(long now, bool force = false)
        {
            if (writer == null || keyLogBuffer.Count == 0)
            {
                return;
            }

            if (!force)
            {
                if (now - lastKeyLogFlushTime < KeyLogFlushIntervalMs && keyLogBuffer.Count < KeyLogFlushBatchSize)
                {
                    return;
                }
            }

            foreach (string entry in keyLogBuffer)
            {
                LogWrite.EncryptedLine(writer, entry);
            }

            keyLogBuffer.Clear();
            lastKeyLogFlushTime = now;
        }

        private void WriteKeyEventLine(int deltaMs, KeyCode keyCode, bool isDown, int watermarkNumber, Color color, int fps)
        {
            if (writer == null)
            {
                return;
            }

            string formattedKey = JoystickKeyMapper.FormatKey(keyCode);
            string status = isDown ? "+" : "-";
            string colorHex = ColorUtility.ToHtmlStringRGBA(color);
            string fpsText = fps.ToString();
            string logEntry = $"+{deltaMs}|{formattedKey}|{status}|{watermarkNumber}|#{colorHex}|{fpsText}|";
            keyLogBuffer.Add(logEntry);
        }
    }
}
