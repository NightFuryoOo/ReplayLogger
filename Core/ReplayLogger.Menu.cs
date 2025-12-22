using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
        public int CopyLastSavedLogKeyCode = (int)KeyCode.L;
        public int OpenReplayLoggerFolderKeyCode = (int)KeyCode.F7;
        public float HudToastSeconds = DefaultToastSeconds;
        public string CopyLogsRootPath = string.Empty;
    }

    public partial class ReplayLogger
    {
        private const float ToastSecondsStep = 0.5f;
        private const string NotSetLabel = "Not Set";
        private const string SetKeyLabel = "Set Key...";
        private const string SetPathLabel = "Set Path...";
        private const string DefaultCopyPathLabel = "Default (Desktop\\Save Logs)";
        private const int MaxPathLabelLength = 48;

        private static readonly KeyCode[] AllKeyCodes = (KeyCode[])Enum.GetValues(typeof(KeyCode));
        private static readonly HashSet<char> InvalidPathChars = new(Path.GetInvalidPathChars());

        internal static ReplayLoggerSettings Settings { get; private set; } = new ReplayLoggerSettings();

        private static MenuButton showLastSavedLogButton;
        private static MenuButton copyLastSavedLogButton;
        private static MenuButton openReplayLoggerFolderButton;
        private static MenuButton copyLogsPathButton;
        private static bool waitingForShowLastSavedLogRebind;
        private static bool waitingForCopyLastSavedLogRebind;
        private static bool waitingForOpenReplayLoggerFolderRebind;
        private static bool waitingForCopyLogsPathInput;
        private static KeyCode previousShowLastSavedLogKey;
        private static KeyCode previousCopyLastSavedLogKey;
        private static KeyCode previousOpenReplayLoggerFolderKey;
        private static string previousCopyLogsRootPath;
        private static string copyLogsPathBuffer;
        private static RebindListener listener;

        internal static bool IsRebindInProgress =>
            waitingForShowLastSavedLogRebind
            || waitingForCopyLastSavedLogRebind
            || waitingForOpenReplayLoggerFolderRebind
            || waitingForCopyLogsPathInput;

        internal static KeyCode GetShowLastSavedLogKey()
        {
            EnsureSettings();
            return (KeyCode)Settings.ShowLastSavedLogKeyCode;
        }

        internal static KeyCode GetCopyLastSavedLogKey()
        {
            EnsureSettings();
            return (KeyCode)Settings.CopyLastSavedLogKeyCode;
        }

        internal static KeyCode GetOpenReplayLoggerFolderKey()
        {
            EnsureSettings();
            return (KeyCode)Settings.OpenReplayLoggerFolderKeyCode;
        }

        internal static string GetCopyLogsRootPath()
        {
            EnsureSettings();
            if (!string.IsNullOrWhiteSpace(Settings.CopyLogsRootPath))
            {
                return Settings.CopyLogsRootPath.Trim();
            }

            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktopPath))
            {
                return string.Empty;
            }

            return Path.Combine(desktopPath, "Save Logs");
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
                OpenReplayLoggerFolderBindButton(),
                CopyLastSavedLogBindButton(),
                CopyLogsPathButton(),
                ShowLastSavedLogBindButton()
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

            if (!Enum.IsDefined(typeof(KeyCode), Settings.CopyLastSavedLogKeyCode))
            {
                Settings.CopyLastSavedLogKeyCode = (int)KeyCode.None;
            }

            if (!Enum.IsDefined(typeof(KeyCode), Settings.OpenReplayLoggerFolderKeyCode))
            {
                Settings.OpenReplayLoggerFolderKeyCode = (int)KeyCode.None;
            }

            if (Settings.CopyLogsRootPath == null)
            {
                Settings.CopyLogsRootPath = string.Empty;
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

        private static MenuButton ShowLastSavedLogBindButton() =>
            showLastSavedLogButton = new MenuButton(
                FormatButtonName("Show last saved log", GetShowLastSavedLogKey()),
                "Press to pick a key (Esc cancels, same key clears).",
                _ => StartShowLastSavedLogRebind(),
                false
            );

        private static MenuButton CopyLastSavedLogBindButton() =>
            copyLastSavedLogButton = new MenuButton(
                FormatButtonName("Copy last saved log", GetCopyLastSavedLogKey()),
                "Press to pick a key (Esc cancels, same key clears).",
                _ => StartCopyLastSavedLogRebind(),
                false
            );

        private static MenuButton OpenReplayLoggerFolderBindButton() =>
            openReplayLoggerFolderButton = new MenuButton(
                FormatButtonName("Open ReplayLogger folder", GetOpenReplayLoggerFolderKey()),
                "Press to pick a key (Esc cancels, same key clears).",
                _ => StartOpenReplayLoggerFolderRebind(),
                false
            );

        private static MenuButton CopyLogsPathButton() =>
            copyLogsPathButton = new MenuButton(
                FormatButtonName("Copy logs path", GetCopyLogsPathLabel()),
                "Press to set a folder path (Enter saves, Esc cancels).",
                _ => StartCopyLogsPathInput(),
                false
            );

        private static void StartShowLastSavedLogRebind()
        {
            if (IsRebindInProgress)
            {
                return;
            }

            waitingForShowLastSavedLogRebind = true;
            previousShowLastSavedLogKey = GetShowLastSavedLogKey();
            UpdateShowLastSavedLogButton(SetKeyLabel);
        }

        private static void StartCopyLastSavedLogRebind()
        {
            if (IsRebindInProgress)
            {
                return;
            }

            waitingForCopyLastSavedLogRebind = true;
            previousCopyLastSavedLogKey = GetCopyLastSavedLogKey();
            UpdateCopyLastSavedLogButton(SetKeyLabel);
        }

        private static void StartOpenReplayLoggerFolderRebind()
        {
            if (IsRebindInProgress)
            {
                return;
            }

            waitingForOpenReplayLoggerFolderRebind = true;
            previousOpenReplayLoggerFolderKey = GetOpenReplayLoggerFolderKey();
            UpdateOpenReplayLoggerFolderButton(SetKeyLabel);
        }

        private static void StartCopyLogsPathInput()
        {
            if (IsRebindInProgress)
            {
                return;
            }

            waitingForCopyLogsPathInput = true;
            previousCopyLogsRootPath = Settings.CopyLogsRootPath ?? string.Empty;
            copyLogsPathBuffer = previousCopyLogsRootPath;
            UpdateCopyLogsPathButton(SetPathLabel);
        }

        private static void HandleShowLastSavedLogRebind()
        {
            if (!waitingForShowLastSavedLogRebind)
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
                    waitingForShowLastSavedLogRebind = false;
                    UpdateShowLastSavedLogButton(FormatKeyLabel(GetShowLastSavedLogKey()));
                    return;
                }

                Settings.ShowLastSavedLogKeyCode = (int)(key == previousShowLastSavedLogKey ? KeyCode.None : key);
                waitingForShowLastSavedLogRebind = false;
                UpdateShowLastSavedLogButton(FormatKeyLabel(GetShowLastSavedLogKey()));
                return;
            }
        }

        private static void HandleCopyLastSavedLogRebind()
        {
            if (!waitingForCopyLastSavedLogRebind)
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
                    waitingForCopyLastSavedLogRebind = false;
                    UpdateCopyLastSavedLogButton(FormatKeyLabel(GetCopyLastSavedLogKey()));
                    return;
                }

                Settings.CopyLastSavedLogKeyCode = (int)(key == previousCopyLastSavedLogKey ? KeyCode.None : key);
                waitingForCopyLastSavedLogRebind = false;
                UpdateCopyLastSavedLogButton(FormatKeyLabel(GetCopyLastSavedLogKey()));
                return;
            }
        }

        private static void HandleOpenReplayLoggerFolderRebind()
        {
            if (!waitingForOpenReplayLoggerFolderRebind)
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
                    waitingForOpenReplayLoggerFolderRebind = false;
                    UpdateOpenReplayLoggerFolderButton(FormatKeyLabel(GetOpenReplayLoggerFolderKey()));
                    return;
                }

                Settings.OpenReplayLoggerFolderKeyCode = (int)(key == previousOpenReplayLoggerFolderKey ? KeyCode.None : key);
                waitingForOpenReplayLoggerFolderRebind = false;
                UpdateOpenReplayLoggerFolderButton(FormatKeyLabel(GetOpenReplayLoggerFolderKey()));
                return;
            }
        }

        private static void HandleCopyLogsPathInput()
        {
            if (!waitingForCopyLogsPathInput)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                waitingForCopyLogsPathInput = false;
                copyLogsPathBuffer = null;
                Settings.CopyLogsRootPath = previousCopyLogsRootPath ?? string.Empty;
                UpdateCopyLogsPathButton(GetCopyLogsPathLabel());
                return;
            }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                waitingForCopyLogsPathInput = false;
                Settings.CopyLogsRootPath = NormalizeCopyLogsPath(copyLogsPathBuffer);
                copyLogsPathBuffer = null;
                UpdateCopyLogsPathButton(GetCopyLogsPathLabel());
                return;
            }

            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.V))
            {
                string clipboard = GUIUtility.systemCopyBuffer;
                if (!string.IsNullOrEmpty(clipboard))
                {
                    copyLogsPathBuffer = (copyLogsPathBuffer ?? string.Empty) + SanitizePathInput(clipboard);
                    UpdateCopyLogsPathButton(FormatPathLabel(copyLogsPathBuffer));
                }

                return;
            }

            string input = Input.inputString;
            if (string.IsNullOrEmpty(input))
            {
                return;
            }

            bool updated = false;
            if (copyLogsPathBuffer == null)
            {
                copyLogsPathBuffer = string.Empty;
            }

            foreach (char c in input)
            {
                if (c == '\b')
                {
                    if (copyLogsPathBuffer.Length > 0)
                    {
                        copyLogsPathBuffer = copyLogsPathBuffer.Substring(0, copyLogsPathBuffer.Length - 1);
                        updated = true;
                    }
                    continue;
                }

                if (c == '\n' || c == '\r')
                {
                    continue;
                }

                if (InvalidPathChars.Contains(c))
                {
                    continue;
                }

                copyLogsPathBuffer += c;
                updated = true;
            }

            if (updated)
            {
                UpdateCopyLogsPathButton(FormatPathLabel(copyLogsPathBuffer));
            }
        }

        private static void UpdateShowLastSavedLogButton(string value)
        {
            if (showLastSavedLogButton == null)
            {
                return;
            }

            showLastSavedLogButton.Name = FormatButtonName("Show last saved log", value);
            showLastSavedLogButton.Update();
        }

        private static void UpdateCopyLastSavedLogButton(string value)
        {
            if (copyLastSavedLogButton == null)
            {
                return;
            }

            copyLastSavedLogButton.Name = FormatButtonName("Copy last saved log", value);
            copyLastSavedLogButton.Update();
        }

        private static void UpdateOpenReplayLoggerFolderButton(string value)
        {
            if (openReplayLoggerFolderButton == null)
            {
                return;
            }

            openReplayLoggerFolderButton.Name = FormatButtonName("Open ReplayLogger folder", value);
            openReplayLoggerFolderButton.Update();
        }

        private static void UpdateCopyLogsPathButton(string value)
        {
            if (copyLogsPathButton == null)
            {
                return;
            }

            copyLogsPathButton.Name = FormatButtonName("Copy logs path", value);
            copyLogsPathButton.Update();
        }

        private static string FormatButtonName(string title, string value) => $"{title}: {value}";

        private static string FormatButtonName(string title, KeyCode key) => $"{title}: {FormatKeyLabel(key)}";

        private static string FormatKeyLabel(KeyCode key) =>
            key == KeyCode.None ? NotSetLabel : key.ToString();

        private static string GetCopyLogsPathLabel()
        {
            EnsureSettings();
            if (string.IsNullOrWhiteSpace(Settings.CopyLogsRootPath))
            {
                return DefaultCopyPathLabel;
            }

            return FormatPathLabel(Settings.CopyLogsRootPath.Trim());
        }

        private static string NormalizeCopyLogsPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string trimmed = value.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"')
            {
                trimmed = trimmed.Substring(1, trimmed.Length - 2).Trim();
            }

            return trimmed;
        }

        private static string SanitizePathInput(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new(value.Length);
            foreach (char c in value)
            {
                if (c == '\n' || c == '\r' || InvalidPathChars.Contains(c))
                {
                    continue;
                }

                builder.Append(c);
            }

            return builder.Length == 0 ? string.Empty : builder.ToString();
        }

        private static string FormatPathLabel(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return DefaultCopyPathLabel;
            }

            string trimmed = value.Trim();
            if (trimmed.Length <= MaxPathLabelLength)
            {
                return trimmed;
            }

            int suffixLength = Math.Max(0, MaxPathLabelLength - 3);
            if (suffixLength == 0)
            {
                return "...";
            }

            return "..." + trimmed.Substring(trimmed.Length - suffixLength);
        }

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

        private sealed class RebindListener : MonoBehaviour
        {
            private void Update()
            {
                HandleShowLastSavedLogRebind();
                HandleCopyLastSavedLogRebind();
                HandleOpenReplayLoggerFolderRebind();
                HandleCopyLogsPathInput();
            }
        }
    }
}
