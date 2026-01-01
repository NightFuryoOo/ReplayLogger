using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace ReplayLogger
{
    internal static class InlineEventRecorder
    {
        private static BinaryEventLog activeLog;
        private static string activePath;
        private static string activeMetaPath;
        private static bool recoveryAttempted;

        internal static bool IsActive => activeLog != null;

        internal static void Start(string basePath)
        {
            Start(basePath, null);
        }

        internal static void Start(string basePath, string sessionKeyBlob)
        {
            Stop(discard: true);

            if (string.IsNullOrWhiteSpace(basePath))
            {
                return;
            }

            activePath = basePath + ".events.bin";
            activeMetaPath = basePath + ".events.meta";
            activeLog = new BinaryEventLog(activePath);
            WriteRecoveryMeta(sessionKeyBlob);
        }

        internal static void StopAndWrite(StreamWriter writer)
        {
            if (activeLog == null)
            {
                return;
            }

            string path = activePath;
            string metaPath = activeMetaPath;
            activeLog.Dispose();
            activeLog = null;
            activePath = null;
            activeMetaPath = null;

            BinaryEventLog.ConvertToText(path, writer);
            TryDelete(path);
            TryDelete(metaPath);
        }

        internal static void Stop(bool discard)
        {
            if (activeLog == null)
            {
                return;
            }

            string path = activePath;
            string metaPath = activeMetaPath;
            activeLog.Dispose();
            activeLog = null;
            activePath = null;
            activeMetaPath = null;

            if (discard)
            {
                TryDelete(path);
                TryDelete(metaPath);
            }
        }

        internal static void RecordKeyEvent(StreamWriter writer, int deltaMs, KeyCode keyCode, bool isDown, int watermarkNumber, Color watermarkColor, int fps)
        {
            if (activeLog != null)
            {
                activeLog.WriteKeyEvent(deltaMs, keyCode, isDown, watermarkNumber, watermarkColor, fps);
                return;
            }

            if (writer == null)
            {
                return;
            }

            string formattedKey = JoystickKeyMapper.FormatKey(keyCode);
            string status = isDown ? "+" : "-";
            string colorHex = ColorUtility.ToHtmlStringRGBA(watermarkColor);
            string fpsText = fps.ToString(CultureInfo.InvariantCulture);
            string logEntry = $"+{deltaMs}|{formattedKey}|{status}|{watermarkNumber}|#{colorHex}|{fpsText}|";
            LogWrite.EncryptedLine(writer, logEntry);
        }

        internal static void RecordLine(StreamWriter writer, string line)
        {
            if (activeLog != null)
            {
                activeLog.WriteLine(line);
                return;
            }

            if (writer == null)
            {
                return;
            }

            LogWrite.EncryptedLine(writer, line);
        }

        internal static void RecoverPendingLogs(params string[] directories)
        {
            if (recoveryAttempted)
            {
                return;
            }

            recoveryAttempted = true;
            if (directories == null || directories.Length == 0)
            {
                return;
            }

            foreach (string directory in directories)
            {
                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                {
                    continue;
                }

                foreach (string file in Directory.EnumerateFiles(directory, "*.events.bin", SearchOption.TopDirectoryOnly))
                {
                    RecoverPendingFile(file);
                }
            }
        }

        private static void TryDelete(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                if (System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private static void WriteRecoveryMeta(string sessionKeyBlob)
        {
            if (string.IsNullOrWhiteSpace(activeMetaPath) || string.IsNullOrWhiteSpace(sessionKeyBlob))
            {
                return;
            }

            if (!KeyloggerLogEncryption.TryExportMasterKey(out byte[] key, out byte[] iv))
            {
                return;
            }

            try
            {
                string[] lines =
                {
                    Convert.ToBase64String(key),
                    Convert.ToBase64String(iv),
                    sessionKeyBlob
                };

                File.WriteAllLines(activeMetaPath, lines);
            }
            catch
            {
            }
        }

        private static void RecoverPendingFile(string binPath)
        {
            if (string.IsNullOrWhiteSpace(binPath) || !File.Exists(binPath))
            {
                return;
            }

            const string suffix = ".events.bin";
            if (!binPath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string basePath = binPath.Substring(0, binPath.Length - suffix.Length);
            string metaPath = basePath + ".events.meta";
            if (!File.Exists(metaPath))
            {
                return;
            }

            string[] metaLines;
            try
            {
                metaLines = File.ReadAllLines(metaPath);
            }
            catch
            {
                return;
            }

            if (metaLines == null || metaLines.Length < 3)
            {
                return;
            }

            byte[] key;
            byte[] iv;
            try
            {
                key = Convert.FromBase64String(metaLines[0]);
                iv = Convert.FromBase64String(metaLines[1]);
            }
            catch
            {
                return;
            }

            string sessionKeyBlob = metaLines[2];
            if (string.IsNullOrWhiteSpace(sessionKeyBlob))
            {
                return;
            }

            try
            {
                KeyloggerLogEncryption.SetMasterKey(key, iv);
                using StreamWriter writer = new(basePath, append: true);
                BinaryEventLog.ConvertToText(binPath, writer);
                LogWrite.Raw(writer, sessionKeyBlob);
            }
            catch
            {
                return;
            }

            TryDelete(binPath);
            TryDelete(metaPath);
        }
    }
}
