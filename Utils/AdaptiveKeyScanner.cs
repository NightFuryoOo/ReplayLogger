using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ReplayLogger
{

    internal sealed class AdaptiveKeyScanner
    {
        private const int AdaptiveHotKeyLimit = 48;
        private static readonly KeyCode[] AllKeyCodes = Enum.GetValues(typeof(KeyCode)).Cast<KeyCode>().Distinct().ToArray();

        private readonly HashSet<KeyCode> pressedKeys = new();
        private readonly List<KeyCode> pressedKeysBuffer = new(64);
        private readonly List<KeyCode> adaptiveHotKeyCodes = new(64);
        private readonly HashSet<KeyCode> adaptiveHotKeySet = new();
        private readonly List<KeyCode> adaptiveColdKeyCodes = new(512);
        private readonly List<KeyCode> adaptivePromoteKeyBuffer = new(32);
        private bool adaptiveKeyScanInitialized;

        internal void Clear()
        {
            pressedKeys.Clear();
            pressedKeysBuffer.Clear();
        }

        internal void Poll(long pollUnixTime, Action<KeyCode, bool, long> handleKeyEvent)
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
                ScanKeyDownCandidates(adaptiveHotKeyCodes, pollUnixTime, handleKeyEvent);
                ScanKeyDownCandidates(adaptiveColdKeyCodes, pollUnixTime, handleKeyEvent);
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

                handleKeyEvent(keyCode, false, pollUnixTime);
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

        private void ScanKeyDownCandidates(List<KeyCode> candidates, long pollUnixTime, Action<KeyCode, bool, long> handleKeyEvent)
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
                    handleKeyEvent(keyCode, true, pollUnixTime);
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
    }
}
