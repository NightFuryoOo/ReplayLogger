using System;
using System.Collections.Generic;
using GlobalEnums;
using On;
using UnityEngine;

namespace ReplayLogger
{
    public partial class ReplayLogger
    {
        private readonly Dictionary<Color32, string> keyColorHexCache = new(128);
        private const int KeyColorHexCacheMaxSize = 512;
        private readonly List<KeyCode> adaptiveHotKeyCodes = new(64);
        private readonly HashSet<KeyCode> adaptiveHotKeySet = new();
        private readonly List<KeyCode> adaptiveColdKeyCodes = new(512);
        private readonly List<KeyCode> adaptivePromoteKeyBuffer = new(32);
        private bool adaptiveKeyScanInitialized;
        private const int AdaptiveHotKeyLimit = 48;
        private long cachedFrameUnixTime;
        private readonly struct KeyLogEvent
        {
            internal KeyLogEvent(int deltaMs, KeyCode keyCode, bool isDown, int watermarkNumber, Color32 color, int fps)
            {
                DeltaMs = deltaMs;
                KeyCode = keyCode;
                IsDown = isDown;
                WatermarkNumber = watermarkNumber;
                Color = color;
                Fps = fps;
            }

            internal int DeltaMs { get; }
            internal KeyCode KeyCode { get; }
            internal bool IsDown { get; }
            internal int WatermarkNumber { get; }
            internal Color32 Color { get; }
            internal int Fps { get; }
        }

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
            long frameUnixTime = CaptureFrameUnixTime();
            MonitorDebugModUi(frameUnixTime);
            if (!isManualLogging && GameManager.instance != null && GameManager.instance.gameState == GlobalEnums.GameState.CUTSCENE && lastScene == "GG_Radiance")
            {
                Close();
            }

            string roomName = GameManager.instance?.sceneName ?? lastScene;
            speedWarnTracker.Update(writer, roomName, lastUnixTime, frameUnixTime);
            hitWarnTracker.Update(writer, roomName, lastUnixTime, frameUnixTime);
            MirrorInlineTimelineEvents();
            FlushWarningsIfNeeded(speedWarnBuffer, speedWarnTracker.Warnings, speedWarnTracker.ClearWarnings);
            FlushWarningsIfNeeded(hitWarnBuffer, hitWarnTracker.Warnings, hitWarnTracker.ClearWarnings);

            long relativeMs = Math.Max(0, frameUnixTime - startUnixTime);
            int elapsedSeconds = (int)(relativeMs / 1000);
            if (elapsedSeconds != lastHudElapsedSeconds)
            {
                lastHudElapsedSeconds = elapsedSeconds;
                customCanvas?.UpdateTime(FormatHudElapsedTime(relativeMs));
            }

            PollKeyEvents(frameUnixTime);
            MirrorInlineTimelineEvents();

            FlushKeyLogBufferIfNeeded(frameUnixTime);

        }

        private void PollKeyEvents(long pollUnixTime)
        {
            bool hasNewKeyDown = Input.anyKeyDown;
            if (!hasNewKeyDown && pressedKeys.Count == 0)
            {
                return;
            }

            if (hasNewKeyDown)
            {
                EnsureAdaptiveKeyScanInitialized();
                adaptivePromoteKeyBuffer.Clear();
                ScanKeyDownCandidates(adaptiveHotKeyCodes, pollUnixTime);
                ScanKeyDownCandidates(adaptiveColdKeyCodes, pollUnixTime);
                PromoteAdaptiveKeys();
            }

            if (pressedKeys.Count == 0)
            {
                return;
            }

            pressedKeysBuffer.Clear();
            pressedKeysBuffer.AddRange(pressedKeys);
            foreach (KeyCode keyCode in pressedKeysBuffer)
            {
                if (Input.GetKey(keyCode))
                {
                    continue;
                }

                HandleKeyEvent(keyCode, isDown: false, pollUnixTime);
                pressedKeys.Remove(keyCode);
            }
        }

        private void EnsureAdaptiveKeyScanInitialized()
        {
            if (adaptiveKeyScanInitialized)
            {
                return;
            }

            adaptiveHotKeyCodes.Clear();
            adaptiveHotKeySet.Clear();
            adaptiveColdKeyCodes.Clear();
            adaptivePromoteKeyBuffer.Clear();
            foreach (KeyCode keyCode in AllKeyCodes)
            {
                adaptiveColdKeyCodes.Add(keyCode);
            }

            adaptiveKeyScanInitialized = true;
        }

        private void ScanKeyDownCandidates(IReadOnlyList<KeyCode> candidates, long pollUnixTime)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                KeyCode keyCode = candidates[i];
                if (pressedKeys.Contains(keyCode))
                {
                    continue;
                }

                if (!Input.GetKeyDown(keyCode))
                {
                    continue;
                }

                if (pressedKeys.Add(keyCode))
                {
                    HandleKeyEvent(keyCode, isDown: true, pollUnixTime);
                }

                if (!adaptiveHotKeySet.Contains(keyCode))
                {
                    adaptivePromoteKeyBuffer.Add(keyCode);
                }
            }
        }

        private void PromoteAdaptiveKeys()
        {
            if (adaptivePromoteKeyBuffer.Count == 0)
            {
                return;
            }

            for (int i = 0; i < adaptivePromoteKeyBuffer.Count; i++)
            {
                KeyCode keyCode = adaptivePromoteKeyBuffer[i];
                if (!adaptiveHotKeySet.Add(keyCode))
                {
                    continue;
                }

                adaptiveHotKeyCodes.Add(keyCode);
                RemoveKeyCode(adaptiveColdKeyCodes, keyCode);
            }

            while (adaptiveHotKeyCodes.Count > AdaptiveHotKeyLimit)
            {
                KeyCode demoted = adaptiveHotKeyCodes[0];
                adaptiveHotKeyCodes.RemoveAt(0);
                adaptiveHotKeySet.Remove(demoted);
                if (!adaptiveColdKeyCodes.Contains(demoted))
                {
                    adaptiveColdKeyCodes.Add(demoted);
                }
            }

            adaptivePromoteKeyBuffer.Clear();
        }

        private static void RemoveKeyCode(List<KeyCode> source, KeyCode keyCode)
        {
            if (source == null || source.Count == 0)
            {
                return;
            }

            for (int i = 0; i < source.Count; i++)
            {
                if (source[i] == keyCode)
                {
                    source.RemoveAt(i);
                    return;
                }
            }
        }

        private void HandleKeyEvent(KeyCode keyCode, bool isDown, long unixTime)
        {
            float fps = Time.unscaledDeltaTime == 0 ? lastFps : 1f / Time.unscaledDeltaTime;
            lastFps = fps;
            customCanvas?.UpdateWatermark(keyCode);
            int watermarkNumber = customCanvas?.numberInCanvas?.Number ?? 0;
            Color watermarkColorStruct = customCanvas?.numberInCanvas?.Color ?? Color.white;
            try
            {
                int fpsValue = Mathf.RoundToInt(fps);
                WriteKeyEventLine((int)(unixTime - lastUnixTime), keyCode, isDown, watermarkNumber, watermarkColorStruct, fpsValue);
            }
            catch (Exception e)
            {
                global::ReplayLogger.InternalDiagnostics.Error("Key log write failed: " + e.Message);
            }

            if (isDown &&
                debugHotkeysTracker.ActionsByKey.Count > 0 &&
                debugHotkeysTracker.ActionsByKey.ContainsKey(keyCode))
            {
                string arenaForHotkey = lastScene;
                debugHotkeysTracker.TrackActivation(keyCode, arenaForHotkey, lastUnixTime, unixTime);
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

        private void MirrorInlineTimelineEvents()
        {
            if (!isPlayChalange || DamageAnfInv == null)
            {
                return;
            }

            AppendPrefixedInlineEvents(speedWarnTracker.Warnings, ref speedWarnInlineCursor, "SpeedWarn");
            AppendPrefixedInlineEvents(hitWarnTracker.Warnings, ref hitWarnInlineCursor, "HitWarn");
            AppendPrefixedInlineEvents(debugModEventsTracker.Events, ref debugModEventsInlineCursor, "DebugModUi");
            AppendPrefixedInlineEvents(debugHotkeysTracker.Activations, ref debugHotkeysInlineCursor, "DebugHotkey");
            AppendPrefixedInlineEvents(debugMenuTracker.Entries, ref debugMenuInlineCursor, "DebugMenu");
            AppendRawInlineEvents(charmsChangeTracker.InlineEvents, ref charmsInlineCursor);
        }

        private void ResetInlineTimelineCursors()
        {
            speedWarnInlineCursor = 0;
            hitWarnInlineCursor = 0;
            debugModEventsInlineCursor = 0;
            debugHotkeysInlineCursor = 0;
            debugMenuInlineCursor = 0;
            charmsInlineCursor = 0;
        }

        private void AppendPrefixedInlineEvents(IReadOnlyList<string> source, ref int cursor, string prefix)
        {
            if (source == null || DamageAnfInv == null)
            {
                cursor = 0;
                return;
            }

            if (cursor > source.Count)
            {
                cursor = 0;
            }

            for (int i = cursor; i < source.Count; i++)
            {
                string raw = source[i];
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                string payload = raw.TrimStart();
                if (payload.StartsWith("|", StringComparison.Ordinal))
                {
                    DamageAnfInv.Add($"{prefix}{payload}");
                }
                else
                {
                    DamageAnfInv.Add($"{prefix}|{payload}");
                }
            }

            cursor = source.Count;
        }

        private void AppendRawInlineEvents(IReadOnlyList<string> source, ref int cursor)
        {
            if (source == null || DamageAnfInv == null)
            {
                cursor = 0;
                return;
            }

            if (cursor > source.Count)
            {
                cursor = 0;
            }

            for (int i = cursor; i < source.Count; i++)
            {
                string raw = source[i];
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                DamageAnfInv.Add(raw);
            }

            cursor = source.Count;
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

            keyLogFlushLines.Clear();
            foreach (KeyLogEvent keyEvent in keyLogBuffer)
            {
                string formattedKey = JoystickKeyMapper.FormatKey(keyEvent.KeyCode);
                string status = keyEvent.IsDown ? "+" : "-";
                string colorHex = GetCachedColorHex(keyEvent.Color);
                string logEntry = $"{keyEvent.DeltaMs}|{formattedKey}|{status}|{keyEvent.WatermarkNumber}|#{colorHex}|{keyEvent.Fps}|";
                keyLogFlushLines.Add(logEntry);
            }

            bool wroteToSection = false;
            if (isPlayChalange)
            {
                if (!isManualLogging && pressedButtonsLog != null)
                {
                    pressedButtonsLog.AddRange(keyLogFlushLines);
                    wroteToSection = true;
                }
                else if (DamageAnfInv != null)
                {
                    DamageAnfInv.AddRange(keyLogFlushLines);
                    wroteToSection = true;
                }
            }

            if (!wroteToSection)
            {
                LogWrite.EncryptedLines(writer, keyLogFlushLines);
            }

            keyLogBuffer.Clear();
            keyLogFlushLines.Clear();
            lastKeyLogFlushTime = now;
        }

        private void WriteKeyEventLine(int deltaMs, KeyCode keyCode, bool isDown, int watermarkNumber, Color color, int fps)
        {
            if (writer == null)
            {
                return;
            }

            keyLogBuffer.Add(new KeyLogEvent(deltaMs, keyCode, isDown, watermarkNumber, (Color32)color, fps));
        }

        private string GetCachedColorHex(Color32 color)
        {
            if (keyColorHexCache.TryGetValue(color, out string colorHex))
            {
                return colorHex;
            }

            if (keyColorHexCache.Count >= KeyColorHexCacheMaxSize)
            {
                keyColorHexCache.Clear();
            }

            colorHex = ColorUtility.ToHtmlStringRGBA(color);
            keyColorHexCache[color] = colorHex;
            return colorHex;
        }

        private static string FormatHudElapsedTime(long relativeMs)
        {
            long totalSeconds = Math.Max(0L, relativeMs) / 1000L;
            int hours = (int)((totalSeconds / 3600L) % 24L);
            int minutes = (int)((totalSeconds / 60L) % 60L);
            int seconds = (int)(totalSeconds % 60L);
            return $"{hours:D2}:{minutes:D2}:{seconds:D2}";
        }

        private long GetCachedFrameUnixTimeOrNow()
        {
            long cached = cachedFrameUnixTime;
            return cached > 0 ? cached : DateTimeOffset.Now.ToUnixTimeMilliseconds();
        }

        private long CaptureFrameUnixTime()
        {
            long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            cachedFrameUnixTime = now;
            return now;
        }
    }
}



