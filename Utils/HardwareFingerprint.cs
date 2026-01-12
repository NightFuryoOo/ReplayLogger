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
        private static string cachedPartsLine;

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

        public static string GetPartsLine()
        {
            EnsureComputed();
            return cachedPartsLine;
        }

        public static void WriteEncryptedLine(StreamWriter writer)
        {
            if (writer == null)
            {
                return;
            }

            LogWrite.EncryptedLine(writer, GetHashLine());
            string partsLine = GetPartsLine();
            if (!string.IsNullOrEmpty(partsLine))
            {
                LogWrite.EncryptedLine(writer, partsLine);
            }
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
                    ComputeHashes(out cachedHash, out string cpuHash, out string ramHash, out string machineGuidHash, out string gpuHash);
                    cachedPartsLine = BuildPartsLine(cpuHash, ramHash, machineGuidHash, gpuHash);
                }
                catch (Exception ex)
                {
                    Modding.Logger.LogWarn($"ReplayLogger: failed to compute hardware hash: {ex.Message}");
                    cachedHash = null;
                    cachedPartsLine = BuildPartsLine(null, null, null, null);
                }

                cachedLine = string.IsNullOrWhiteSpace(cachedHash)
                    ? "N/A"
                    : cachedHash;

                computed = true;
            }
        }

        private static void ComputeHashes(out string overallHash, out string cpuHash, out string ramHash, out string machineGuidHash, out string gpuHash)
        {
            List<string> parts = new();

            string machineGuid = ReadMachineGuid();
            if (!string.IsNullOrEmpty(machineGuid))
            {
                parts.Add($"MG={machineGuid}");
                machineGuidHash = HashString($"MG={machineGuid}");
            }
            else
            {
                machineGuidHash = null;
            }

            string cpu = NormalizeValue(SystemInfo.processorType);
            List<string> cpuParts = new();
            if (!string.IsNullOrEmpty(cpu))
            {
                parts.Add($"CPU={cpu}");
                cpuParts.Add($"CPU={cpu}");
            }

            int cpuCount = SystemInfo.processorCount;
            if (cpuCount > 0)
            {
                parts.Add($"CPUCOUNT={cpuCount}");
                cpuParts.Add($"CPUCOUNT={cpuCount}");
            }

            cpuHash = HashParts(cpuParts);

            int ramMb = SystemInfo.systemMemorySize;
            if (ramMb > 0)
            {
                parts.Add($"RAMMB={ramMb}");
                ramHash = HashString($"RAMMB={ramMb}");
            }
            else
            {
                ramHash = null;
            }

            string gpuName = NormalizeValue(SystemInfo.graphicsDeviceName);
            List<string> gpuParts = new();
            if (!string.IsNullOrEmpty(gpuName))
            {
                parts.Add($"GPU={gpuName}");
                gpuParts.Add($"GPU={gpuName}");
            }

            string gpuVendor = NormalizeValue(SystemInfo.graphicsDeviceVendor);
            if (!string.IsNullOrEmpty(gpuVendor))
            {
                parts.Add($"GPUV={gpuVendor}");
                gpuParts.Add($"GPUV={gpuVendor}");
            }

            int gpuId = SystemInfo.graphicsDeviceID;
            if (gpuId != 0)
            {
                parts.Add($"GPUID={gpuId}");
                gpuParts.Add($"GPUID={gpuId}");
            }

            gpuHash = HashParts(gpuParts);

            if (parts.Count == 0)
            {
                overallHash = null;
                return;
            }

            overallHash = HashParts(parts);
        }

        private static string HashParts(List<string> parts)
        {
            if (parts == null || parts.Count == 0)
            {
                return null;
            }

            string raw = string.Join("|", parts);
            return HashString(raw);
        }

        private static string HashString(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return null;
            }

            using SHA256 sha = SHA256.Create();
            byte[] bytes = Encoding.UTF8.GetBytes(raw);
            byte[] hash = sha.ComputeHash(bytes);
            return BitConverter.ToString(hash).Replace("-", string.Empty);
        }

        private static string BuildPartsLine(string cpuHash, string ramHash, string machineGuidHash, string gpuHash)
        {
            return $"{cpuHash ?? "N/A"} | {ramHash ?? "N/A"} | {machineGuidHash ?? "N/A"} | {gpuHash ?? "N/A"}";
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
