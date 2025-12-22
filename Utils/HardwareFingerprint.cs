using Microsoft.Win32;
using Modding;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace ReplayLogger
{
    internal static class HardwareFingerprint
    {
        private static readonly object Sync = new();
        private static bool computed;
        private static string cachedHash;
        private static string cachedLine;

        public static void Prime()
        {
            EnsureComputed();
        }

        public static string GetHash()
        {
            EnsureComputed();
            return cachedHash;
        }

        public static string GetHashLine()
        {
            EnsureComputed();
            return cachedLine ?? "HardwareHash: N/A";
        }

        public static void WriteEncryptedLine(StreamWriter writer)
        {
            if (writer == null)
            {
                return;
            }

            LogWrite.EncryptedLine(writer, GetHashLine());
        }

        private static void EnsureComputed()
        {
            if (computed)
            {
                return;
            }

            lock (Sync)
            {
                if (computed)
                {
                    return;
                }

                try
                {
                    cachedHash = ComputeHash();
                }
                catch (Exception ex)
                {
                    Modding.Logger.LogWarn($"ReplayLogger: failed to compute hardware hash: {ex.Message}");
                    cachedHash = null;
                }

                cachedLine = string.IsNullOrWhiteSpace(cachedHash)
                    ? "N/A"
                    : cachedHash;

                computed = true;
            }
        }

        private static string ComputeHash()
        {
            List<string> parts = new();

            string machineGuid = ReadMachineGuid();
            if (!string.IsNullOrEmpty(machineGuid))
            {
                parts.Add($"MG={machineGuid}");
            }

            string cpu = NormalizeValue(SystemInfo.processorType);
            if (!string.IsNullOrEmpty(cpu))
            {
                parts.Add($"CPU={cpu}");
            }

            int cpuCount = SystemInfo.processorCount;
            if (cpuCount > 0)
            {
                parts.Add($"CPUCOUNT={cpuCount}");
            }

            int ramMb = SystemInfo.systemMemorySize;
            if (ramMb > 0)
            {
                parts.Add($"RAMMB={ramMb}");
            }

            string gpuName = NormalizeValue(SystemInfo.graphicsDeviceName);
            if (!string.IsNullOrEmpty(gpuName))
            {
                parts.Add($"GPU={gpuName}");
            }

            string gpuVendor = NormalizeValue(SystemInfo.graphicsDeviceVendor);
            if (!string.IsNullOrEmpty(gpuVendor))
            {
                parts.Add($"GPUV={gpuVendor}");
            }

            int gpuId = SystemInfo.graphicsDeviceID;
            if (gpuId != 0)
            {
                parts.Add($"GPUID={gpuId}");
            }

            if (parts.Count == 0)
            {
                return null;
            }

            string raw = string.Join("|", parts);
            using SHA256 sha = SHA256.Create();
            byte[] bytes = Encoding.UTF8.GetBytes(raw);
            byte[] hash = sha.ComputeHash(bytes);
            return BitConverter.ToString(hash).Replace("-", string.Empty);
        }

        private static string ReadMachineGuid()
        {
            try
            {
                using RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                if (key == null)
                {
                    return null;
                }

                object value = key.GetValue("MachineGuid");
                return NormalizeValue(value);
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizeValue(object value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            string text = value.ToString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            text = text.Trim();
            if (IsPlaceholder(text))
            {
                return string.Empty;
            }

            return text.Replace("\0", string.Empty).ToUpperInvariant();
        }

        private static bool IsPlaceholder(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            return text.Equals("To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase)
                || text.Equals("To Be Filled By O.E.M.", StringComparison.OrdinalIgnoreCase)
                || text.Equals("Default string", StringComparison.OrdinalIgnoreCase)
                || text.Equals("None", StringComparison.OrdinalIgnoreCase)
                || text.Equals("Unknown", StringComparison.OrdinalIgnoreCase);
        }
    }
}
