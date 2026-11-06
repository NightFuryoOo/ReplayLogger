using System;
using System.Runtime.InteropServices;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ReplayLogger
{
    internal sealed class WitnessReminder : MonoBehaviour
    {
        private const string WitnessMutexName = "TheWitness-9c6f6a3e-9d2f-4e0e-9b8a-4c3d3d8b6f2c";
        private const string ReminderText = "The Witness is not running";
        private const float CheckIntervalSeconds = 2f;
        private const uint SynchronizeAccess = 0x00100000;
        private const int ErrorAccessDenied = 5;

        private static WitnessReminder instance;

        private TextMeshProUGUI label;
        private float nextCheckAt;
        private bool witnessRunning = true;
        private bool detectionAvailable = true;

        internal static void Create()
        {
            if (instance != null)
            {
                return;
            }

            GameObject root = new("ReplayLogger_WitnessReminder");
            Object.DontDestroyOnLoad(root);

            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;

            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            root.AddComponent<ResponsiveCanvasScale>();

            CanvasGroup group = root.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;

            instance = root.AddComponent<WitnessReminder>();
            instance.label = CreateLabel(root);
            instance.label.gameObject.SetActive(false);
        }

        private static TextMeshProUGUI CreateLabel(GameObject root)
        {
            GameObject textObject = new("WitnessReminderText");
            textObject.transform.SetParent(root.transform, false);

            TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
            text.text = ReminderText;
            text.enableAutoSizing = true;
            text.fontSizeMin = 12f;
            text.fontSizeMax = 24f;
            text.color = new Color(1f, 0.35f, 0.35f, 0.9f);
            text.alignment = TextAlignmentOptions.Top;
            text.enableWordWrapping = false;
            text.raycastTarget = false;

            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -10f);
            rect.sizeDelta = new Vector2(600f, 36f);

            return text;
        }

        private void Update()
        {
            if (label == null)
            {
                return;
            }

            if (Time.unscaledTime >= nextCheckAt)
            {
                nextCheckAt = Time.unscaledTime + CheckIntervalSeconds;
                if (ShouldCheck())
                {
                    witnessRunning = IsWitnessRunning();
                }
            }

            bool show = ShouldCheck() && !witnessRunning && !IsRunInProgress();
            if (label.gameObject.activeSelf != show)
            {
                label.gameObject.SetActive(show);
            }
        }

        private bool ShouldCheck() =>
            detectionAvailable
            && Application.platform == RuntimePlatform.WindowsPlayer
            && ReplayLogger.IsWitnessReminderEnabled();

        private static bool IsRunInProgress() =>
            ReplayLogger.IsPrimaryLoggerActive() || HoGLogger.IsLogging;

        private bool IsWitnessRunning()
        {
            try
            {
                IntPtr handle = OpenMutex(SynchronizeAccess, false, WitnessMutexName);
                if (handle != IntPtr.Zero)
                {
                    CloseHandle(handle);
                    return true;
                }

                return Marshal.GetLastWin32Error() == ErrorAccessDenied;
            }
            catch (Exception)
            {
                detectionAvailable = false;
                return true;
            }
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "OpenMutexW")]
        private static extern IntPtr OpenMutex(uint desiredAccess, bool inheritHandle, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
