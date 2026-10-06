using System;
using System.Collections.Generic;
using System.Linq;
using Modding;
using Satchel.BetterMenus;
using UnityEngine;

namespace ReplayLogger
{
    [Serializable]
    public sealed class ReplayLoggerSettings
    {
        public const float MinToastSeconds = 1f;
        public const float MaxToastSeconds = 10f;
        public const float DefaultToastSeconds = 5f;

        public int ShowLastSavedLogKeyCode = (int)KeyCode.None;
        public int OpenReplayLoggerFolderKeyCode = (int)KeyCode.F7;
        public int ManualLogKeyCode = (int)KeyCode.None;
        public bool ManualModeEnabled = false;
        public float HudToastSeconds = DefaultToastSeconds;
    }

    public partial class ReplayLogger
    {
        private const float ToastSecondsStep = 0.5f;
        private const string NotSetLabel = "Not Set";
        private const string SetKeyLabel = "Set Key...";

        private static readonly KeyCode[] AllKeyCodes = Enum.GetValues(typeof(KeyCode)).Cast<KeyCode>().Distinct().ToArray();

        internal static ReplayLoggerSettings Settings { get; private set; } = new ReplayLoggerSettings();

        private static readonly RebindSlot showLastSavedLogSlot = new(
            "Show last saved log",
            GetShowLastSavedLogKey,
            key => Settings.ShowLastSavedLogKeyCode = (int)key);

        private static readonly RebindSlot openReplayLoggerFolderSlot = new(
            "Open ReplayLogger folder",
            GetOpenReplayLoggerFolderKey,
            key => Settings.OpenReplayLoggerFolderKeyCode = (int)key);

        private static readonly RebindSlot manualLogSlot = new(
            "Manual log hotkey",
            GetManualLogKey,
            key => Settings.ManualLogKeyCode = (int)key);

        private static MenuButton manualModeButton;
        private static RebindListener listener;
        private static readonly RebindFrameGuard rebindFrameGuard = new();

        internal static bool IsRebindInProgress =>
            showLastSavedLogSlot.IsWaiting
            || openReplayLoggerFolderSlot.IsWaiting
            || manualLogSlot.IsWaiting
            || rebindFrameGuard.IsSuppressed(Time.frameCount);

        internal static KeyCode GetShowLastSavedLogKey()
        {
            EnsureSettings();
            return (KeyCode)Settings.ShowLastSavedLogKeyCode;
        }

        internal static KeyCode GetOpenReplayLoggerFolderKey()
        {
            EnsureSettings();
            return (KeyCode)Settings.OpenReplayLoggerFolderKeyCode;
        }

        internal static KeyCode GetManualLogKey()
        {
            EnsureSettings();
            return (KeyCode)Settings.ManualLogKeyCode;
        }

        internal static bool IsManualModeEnabled()
        {
            EnsureSettings();
            return Settings.ManualModeEnabled;
        }

        internal static float GetHudToastSeconds()
        {
            EnsureSettings();
            return NormalizeToastSeconds(Settings.HudToastSeconds);
        }

        public void OnLoadGlobal(ReplayLoggerSettings settings)
        {
            Settings = settings ?? new ReplayLoggerSettings();
            EnsureSettings();
        }

        public ReplayLoggerSettings OnSaveGlobal()
        {
            EnsureSettings();
            return Settings;
        }

        public bool ToggleButtonInsideMenu => false;

        public MenuScreen GetMenuScreen(MenuScreen modListMenu, ModToggleDelegates? toggleDelegates)
        {
            EnsureSettings();
            EnsureListener();

            List<Element> elements = new()
            {
                ManualModeToggleButton(),
                manualLogSlot.CreateButton(),
                openReplayLoggerFolderSlot.CreateButton(),
                showLastSavedLogSlot.CreateButton()
            };

            CustomSlider toastSecondsSlider = new(
                "HUD display time",
                value => Settings.HudToastSeconds = NormalizeToastSeconds(value),
                () => NormalizeToastSeconds(Settings.HudToastSeconds),
                ReplayLoggerSettings.MinToastSeconds,
                ReplayLoggerSettings.MaxToastSeconds,
                false
            );
            elements.Add(toastSecondsSlider);

            Menu menu = new("ReplayLogger", elements.ToArray());
            return menu.GetMenuScreen(modListMenu);
        }

        internal static void InitializeRebindListener()
        {
            EnsureListener();
        }

        private static void EnsureSettings()
        {
            if (Settings == null)
            {
                Settings = new ReplayLoggerSettings();
            }

            if (!Enum.IsDefined(typeof(KeyCode), Settings.ShowLastSavedLogKeyCode))
            {
                Settings.ShowLastSavedLogKeyCode = (int)KeyCode.None;
            }

            if (!Enum.IsDefined(typeof(KeyCode), Settings.OpenReplayLoggerFolderKeyCode))
            {
                Settings.OpenReplayLoggerFolderKeyCode = (int)KeyCode.None;
            }

            if (!Enum.IsDefined(typeof(KeyCode), Settings.ManualLogKeyCode))
            {
                Settings.ManualLogKeyCode = (int)KeyCode.None;
            }

            Settings.HudToastSeconds = NormalizeToastSeconds(Settings.HudToastSeconds);
        }

        private static float NormalizeToastSeconds(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds))
            {
                seconds = ReplayLoggerSettings.DefaultToastSeconds;
            }

            float clamped = Mathf.Clamp(seconds, ReplayLoggerSettings.MinToastSeconds, ReplayLoggerSettings.MaxToastSeconds);
            float stepped = ToastSecondsStep > 0f
                ? Mathf.Round(clamped / ToastSecondsStep) * ToastSecondsStep
                : clamped;
            return (float)Math.Round(stepped, 1, MidpointRounding.AwayFromZero);
        }

        private static MenuButton ManualModeToggleButton() =>
            manualModeButton = new MenuButton(
                FormatToggleButtonName("Manual logging mode", Settings.ManualModeEnabled),
                "Toggle manual logging (auto logging is disabled while on).",
                _ => ToggleManualMode(),
                false
            );

        private static void ToggleManualMode()
        {
            bool enabling = !Settings.ManualModeEnabled;
            ReplayLogger instance = ReplayLogger.Instance;
            if (instance != null)
            {
                if (enabling)
                {
                    if (instance.isPlayChalange && !instance.isManualLogging)
                    {
                        instance.Close();
                    }
                }
                else
                {
                    if (instance.isManualLogging)
                    {
                        instance.StopManualLoggingFromMenu();
                    }
                }
            }

            if (enabling)
            {
                HoGLogger.StopLogging("ManualModeEnabled");
            }

            Settings.ManualModeEnabled = enabling;
            UpdateManualModeButton();
        }

        private static void UpdateManualModeButton()
        {
            if (manualModeButton == null)
            {
                return;
            }

            manualModeButton.Name = FormatToggleButtonName("Manual logging mode", Settings.ManualModeEnabled);
            manualModeButton.Update();
        }

        private static string FormatButtonName(string title, string value) => $"{title}: {value}";

        private static string FormatButtonName(string title, KeyCode key) => $"{title}: {FormatKeyLabel(key)}";

        private static string FormatToggleButtonName(string title, bool value) => $"{title}: {OptionalFormatting.FormatToggle(value)}";

        private static string FormatKeyLabel(KeyCode key) =>
            key == KeyCode.None ? NotSetLabel : key.ToString();

        private static void EnsureListener()
        {
            if (listener != null)
            {
                return;
            }

            GameObject go = new("ReplayLogger_RebindListener");
            UnityEngine.Object.DontDestroyOnLoad(go);
            listener = go.AddComponent<RebindListener>();
        }

        private sealed class RebindSlot
        {
            private readonly string label;
            private readonly Func<KeyCode> getKey;
            private readonly Action<KeyCode> setKey;
            private MenuButton button;
            private bool waiting;
            private KeyCode previousKey;

            internal RebindSlot(string label, Func<KeyCode> getKey, Action<KeyCode> setKey)
            {
                this.label = label;
                this.getKey = getKey;
                this.setKey = setKey;
            }

            internal bool IsWaiting => waiting;

            internal MenuButton CreateButton() =>
                button = new MenuButton(
                    FormatButtonName(label, getKey()),
                    "Press to pick a key (Esc cancels, same key clears).",
                    _ => StartRebind(),
                    false
                );

            internal void StartRebind()
            {
                if (IsRebindInProgress)
                {
                    return;
                }

                waiting = true;
                previousKey = getKey();
                UpdateButton(SetKeyLabel);
            }

            internal void HandleRebind()
            {
                if (!waiting)
                {
                    return;
                }

                foreach (KeyCode key in AllKeyCodes)
                {
                    if (!Input.GetKeyDown(key))
                    {
                        continue;
                    }

                    if (key == KeyCode.Escape)
                    {
                        waiting = false;
                        rebindFrameGuard.Complete(Time.frameCount);
                        UpdateButton(FormatKeyLabel(getKey()));
                        return;
                    }

                    setKey(key == previousKey ? KeyCode.None : key);
                    waiting = false;
                    rebindFrameGuard.Complete(Time.frameCount);
                    UpdateButton(FormatKeyLabel(getKey()));
                    return;
                }
            }

            private void UpdateButton(string value)
            {
                if (button == null)
                {
                    return;
                }

                button.Name = FormatButtonName(label, value);
                button.Update();
            }
        }

        private sealed class RebindListener : MonoBehaviour
        {
            private void Update()
            {
                showLastSavedLogSlot.HandleRebind();
                openReplayLoggerFolderSlot.HandleRebind();
                manualLogSlot.HandleRebind();
            }
        }
    }
}
