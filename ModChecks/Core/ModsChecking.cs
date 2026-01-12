using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Modding;

namespace ReplayLogger
{
    internal static class ModsChecking
    {
        private static readonly string[] PaleCourtDllNames = { "PaleCourt.dll", "FiveKnights.dll" };
        private static readonly string[] PaleCourtDirectoryHints = { "palecourt", "fiveknights" };

        private const int MiniHashChunkSize = 4096;
        private static readonly object ModScanLock = new();
        private static string cachedModsDirectory;
        private static string cachedModsFingerprint;
        private static List<string> cachedModsSnapshot;

        private static CachedModInfo cachedPaleCourtInfo;
        private static bool paleCourtCacheAttempted;

        private sealed class CachedModInfo
        {
            public string DirectoryPath;
            public string ModName;
            public string Version;
            public string Hash;
        }

        public static void ClearHeavyModCache()
        {
            cachedPaleCourtInfo = null;
            paleCourtCacheAttempted = false;
            cachedModsDirectory = null;
            cachedModsFingerprint = null;
            cachedModsSnapshot = null;
        }

        public static void PrimeHeavyModCache(string modsDir)
        {
            EnsurePaleCourtCache(modsDir);
        }

        public static List<string> ScanMods(string modsDir)
        {
            if (string.IsNullOrEmpty(modsDir) || !Directory.Exists(modsDir))
            {
                return new List<string>();
            }

            lock (ModScanLock)
            {
                string fingerprint = BuildModsFingerprint(modsDir);
                if (!string.IsNullOrEmpty(fingerprint)
                    && cachedModsSnapshot != null
                    && string.Equals(cachedModsFingerprint, fingerprint, StringComparison.Ordinal)
                    && string.Equals(cachedModsDirectory, modsDir, StringComparison.OrdinalIgnoreCase))
                {
                    return cachedModsSnapshot;
                }

                List<string> snapshot = ScanModsInternal(modsDir);
                cachedModsDirectory = modsDir;
                cachedModsFingerprint = fingerprint;
                cachedModsSnapshot = snapshot;
                return snapshot;
            }
        }

        private static List<string> ScanModsInternal(string modsDir)
        {
            List<string> modInfo = new() { ModHooks.ModVersion };
            List<string> unregisteredMods = new();

            List<string> modDirectories = Directory.GetDirectories(modsDir)
                .Where(dir => !Path.GetFileName(dir).Equals("Disabled", StringComparison.OrdinalIgnoreCase) &&
                               !Path.GetFileName(dir).Equals("Vasi", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (string modDirectory in modDirectories)
            {
                try
                {
                    if (IsCachedPaleCourtDirectory(modDirectory))
                    {
                        modInfo.Add($"{cachedPaleCourtInfo.ModName}|{cachedPaleCourtInfo.Version}|{cachedPaleCourtInfo.Hash}");
                        continue;
                    }

                    string[] dllFiles = Directory.GetFiles(modDirectory, "*.dll");
                    if (dllFiles.Length == 0)
                    {
                        unregisteredMods.Add(modDirectory);
                        continue;
                    }

                    string modDllPath = dllFiles[0];
                    try
                    {
                        Assembly modAssembly = Assembly.LoadFile(modDllPath);
                        Type[] modTypes = modAssembly.GetTypes()
                            .Where(t => typeof(IMod).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                            .ToArray();

                        if (modTypes.Length == 0)
                        {
                            unregisteredMods.Add(modDirectory);
                            continue;
                        }

                        Type modType = modTypes[0];
                        string modName = modType.Name;
                        string modVersion = ResolveModVersion(modAssembly, modType, modDllPath);
                        string hash = CalculateSHA256(modDllPath);

                        modInfo.Add($"{modName}|{modVersion}|{hash}");
                    }
                    catch (Exception ex)
                    {
                        Modding.Logger.LogError($"Error loading assembly {modDllPath}: {ex.Message}");
                        unregisteredMods.Add(modDirectory);
                    }
                }
                catch (Exception ex)
                {
                    Modding.Logger.LogError($"Error processing directory {modDirectory}: {ex.Message}");
                    unregisteredMods.Add(modDirectory);
                }
            }

            if (unregisteredMods.Count == 0)
            {
                return modInfo;
            }

            List<string> encDirs = new();
            foreach (string dir in unregisteredMods)
            {
                encDirs.Add(dir);
            }

            List<string> report = [.. modInfo, "Unregistered mods:", .. encDirs];
            return report;
        }

        private static string BuildModsFingerprint(string modsDir)
        {
            try
            {
                List<string> entries = new();
                List<string> modDirectories = Directory.GetDirectories(modsDir)
                    .Where(dir => !Path.GetFileName(dir).Equals("Disabled", StringComparison.OrdinalIgnoreCase) &&
                                   !Path.GetFileName(dir).Equals("Vasi", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (string modDirectory in modDirectories)
                {
                    string modFolder = Path.GetFileName(modDirectory);
                    if (TryGetPrimaryModDll(modDirectory, out string dllPath))
                    {
                        FileInfo info = new(dllPath);
                        string miniHash = ComputeMiniHash(dllPath);
                        entries.Add($"{modFolder}|{Path.GetFileName(dllPath)}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{miniHash}");
                    }
                    else
                    {
                        DateTime dirTime = Directory.GetLastWriteTimeUtc(modDirectory);
                        entries.Add($"{modFolder}|<no-dll>|{dirTime.Ticks}");
                    }
                }

                entries.Sort(StringComparer.Ordinal);
                string joined = string.Join("\n", entries);
                return ComputeSha256Hex(joined);
            }
            catch
            {
                return null;
            }
        }

        private static string ComputeSha256Hex(string text)
        {
            if (text == null)
            {
                return null;
            }

            byte[] data = Encoding.UTF8.GetBytes(text);
            using SHA256 sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", string.Empty);
        }

        private static string ComputeMiniHash(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return string.Empty;
            }

            try
            {
                using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                long length = stream.Length;
                if (length <= 0)
                {
                    return string.Empty;
                }

                int firstCount = (int)Math.Min(MiniHashChunkSize, length);
                int tailCount = length > MiniHashChunkSize ? (int)Math.Min(MiniHashChunkSize, length - firstCount) : 0;
                byte[] buffer = new byte[firstCount + tailCount];
                int offset = 0;
                int read = stream.Read(buffer, offset, firstCount);
                offset += read;

                if (tailCount > 0)
                {
                    stream.Seek(-tailCount, SeekOrigin.End);
                    int tailRead = stream.Read(buffer, offset, tailCount);
                    offset += tailRead;
                }

                using SHA256 sha = SHA256.Create();
                byte[] hash = sha.ComputeHash(buffer, 0, offset);
                return BitConverter.ToString(hash).Replace("-", string.Empty);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool TryGetPrimaryModDll(string modDirectory, out string dllPath)
        {
            dllPath = null;
            try
            {
                string[] dllFiles = Directory.GetFiles(modDirectory, "*.dll");
                if (dllFiles.Length == 0)
                {
                    return false;
                }

                dllPath = dllFiles[0];
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string CalculateSHA256(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath))
                {
                    return null;
                }

                if (!File.Exists(filePath))
                {
                    Modding.Logger.Log($"пїЅпїЅпїЅпїЅ пїЅпїЅ пїЅпїЅпїЅпїЅпїЅпїЅ: {filePath}");
                    return null;
                }

                using (SHA256 sha256 = SHA256.Create())
                using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    byte[] hashBytes = sha256.ComputeHash(fileStream);
                    return BitConverter.ToString(hashBytes).Replace("-", "").ToUpper();
                }
            }
            catch (Exception ex)
            {
                Modding.Logger.Log($"пїЅиЁЎпїЅпїЅ пїЅпїЅ пїЅпїЅпїЅб«ҐпїЅпїЅпїЅ SHA256 пїЅпїЅпїЅ д ©пїЅпїЅ {filePath}: {ex.Message}");
                return null;
            }
        }

        private static string ResolveModVersion(Assembly assembly, Type modType, string filePath)
        {
            string modInfoVersion = TryGetModInfoVersion(assembly);
            if (!string.IsNullOrWhiteSpace(modInfoVersion))
            {
                return modInfoVersion;
            }

            string declaredVersion = TryInvokeGetVersion(modType);
            if (!string.IsNullOrWhiteSpace(declaredVersion))
            {
                return declaredVersion;
            }

            try
            {
                AssemblyInformationalVersionAttribute info =
                    assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                if (!string.IsNullOrWhiteSpace(info?.InformationalVersion))
                {
                    return info.InformationalVersion;
                }
            }
            catch (Exception ex)
            {
                Modding.Logger.LogWarn($"ReplayLogger: cannot read informational version from '{filePath}': {ex.Message}");
            }

            try
            {
                Version asmVersion = assembly.GetName()?.Version;
                if (asmVersion != null)
                {
                    return asmVersion.ToString();
                }
            }
            catch (Exception ex)
            {
                Modding.Logger.LogWarn($"ReplayLogger: cannot read assembly version from '{filePath}': {ex.Message}");
            }

            try
            {
                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    FileVersionInfo info = FileVersionInfo.GetVersionInfo(filePath);
                    string fileVersion = info.ProductVersion ?? info.FileVersion;
                    if (!string.IsNullOrWhiteSpace(fileVersion))
                    {
                        return fileVersion;
                    }
                }
            }
            catch (Exception ex)
            {
                Modding.Logger.LogWarn($"ReplayLogger: cannot read file version from '{filePath}': {ex.Message}");
            }

            return "Unknown";
        }

        private static string TryGetModInfoVersion(Assembly assembly)
        {
            try
            {
                Type[] allTypes = assembly.GetTypes();
                foreach (Type type in allTypes)
                {
                    if (!string.Equals(type.Name, "ModInfo", StringComparison.Ordinal) &&
                        (type.FullName == null || !type.FullName.EndsWith(".ModInfo", StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

                    FieldInfo field = type.GetField("Version", flags);
                    if (field != null && field.FieldType == typeof(string))
                    {
                        if (field.IsLiteral && !field.IsInitOnly)
                        {
                            if (field.GetRawConstantValue() is string literal && !string.IsNullOrWhiteSpace(literal))
                            {
                                return literal;
                            }
                        }
                        else
                        {
                            if (field.GetValue(null) is string value && !string.IsNullOrWhiteSpace(value))
                            {
                                return value;
                            }
                        }
                    }

                    PropertyInfo property = type.GetProperty("Version", flags);
                    if (property?.PropertyType == typeof(string))
                    {
                        if (property.GetValue(null, null) is string propValue && !string.IsNullOrWhiteSpace(propValue))
                        {
                            return propValue;
                        }
                    }
                }
            }
            catch (ReflectionTypeLoadException ex)
            {
                foreach (Exception loaderEx in ex.LoaderExceptions ?? Array.Empty<Exception>())
                {
                    Modding.Logger.LogWarn($"ReplayLogger: failed to inspect ModInfo type: {loaderEx.Message}");
                }
            }
            catch (Exception ex)
            {
                Modding.Logger.LogWarn($"ReplayLogger: ModInfo.Version lookup failed: {ex.Message}");
            }

            return null;
        }

        private static string TryInvokeGetVersion(Type modType)
        {
            if (modType == null)
            {
                return null;
            }

            try
            {
                MethodInfo method = modType.GetMethod("GetVersion", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (method == null || method.GetParameters().Length != 0)
                {
                    return null;
                }

                object instance = FormatterServices.GetUninitializedObject(modType);
                object result = method.Invoke(instance, null);
                if (result is string versionString && !string.IsNullOrWhiteSpace(versionString))
                {
                    return versionString;
                }
            }
            catch (Exception ex)
            {
                Modding.Logger.LogWarn($"ReplayLogger: GetVersion reflection failed for '{modType.FullName}': {ex.Message}");
            }

            return null;
        }

        private static void EnsurePaleCourtCache(string modsDir)
        {
            if (cachedPaleCourtInfo != null || paleCourtCacheAttempted)
            {
                return;
            }

            paleCourtCacheAttempted = true;

            try
            {
                if (string.IsNullOrEmpty(modsDir) || !Directory.Exists(modsDir))
                {
                    return;
                }

                string paleCourtDirectory = FindPaleCourtDirectory(modsDir);
                if (string.IsNullOrEmpty(paleCourtDirectory))
                {
                    return;
                }

                string dllPath = GetPaleCourtDllPath(paleCourtDirectory);
                if (string.IsNullOrEmpty(dllPath))
                {
                    string[] dllFiles = Directory.GetFiles(paleCourtDirectory, "*.dll");
                    if (dllFiles.Length == 0)
                    {
                        return;
                    }

                    dllPath = dllFiles[0];
                }

                Assembly modAssembly = Assembly.LoadFile(dllPath);
                Type modType = modAssembly.GetTypes()
                    .FirstOrDefault(t => typeof(IMod).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                if (modType == null)
                {
                    Modding.Logger.LogWarn("ReplayLogger: Pale Court assembly does not expose an IMod implementation.");
                    return;
                }

                string version = ResolveModVersion(modAssembly, modType, dllPath);
                string hash = CalculateSHA256(dllPath) ?? string.Empty;

                cachedPaleCourtInfo = new CachedModInfo
                {
                    DirectoryPath = Path.GetFullPath(paleCourtDirectory),
                    ModName = modType.Name,
                    Version = version,
                    Hash = hash
                };
            }
            catch (Exception ex)
            {
                Modding.Logger.LogWarn($"ReplayLogger: cannot cache Pale Court info: {ex.Message}");
                cachedPaleCourtInfo = null;
            }
        }

        private static bool IsCachedPaleCourtDirectory(string directory)
        {
            if (cachedPaleCourtInfo == null || string.IsNullOrEmpty(directory))
            {
                return false;
            }

            try
            {
                string fullPath = Path.GetFullPath(directory);
                return string.Equals(fullPath, cachedPaleCourtInfo.DirectoryPath, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string FindPaleCourtDirectory(string modsDir)
        {
            try
            {
                foreach (string directory in Directory.GetDirectories(modsDir))
                {
                    string folderName = Path.GetFileName(directory);
                    if (string.IsNullOrEmpty(folderName))
                    {
                        continue;
                    }

                    if (folderName.Equals("Disabled", StringComparison.OrdinalIgnoreCase) ||
                        folderName.Equals("Vasi", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (DirectoryMatchesHint(folderName))
                    {
                        return directory;
                    }

                    foreach (string dllName in PaleCourtDllNames)
                    {
                        string dllCandidate = Path.Combine(directory, dllName);
                        if (File.Exists(dllCandidate))
                        {
                            return directory;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Modding.Logger.LogWarn($"ReplayLogger: cannot locate Pale Court directory: {ex.Message}");
            }

            return null;
        }

        private static string GetPaleCourtDllPath(string directory)
        {
            foreach (string dllName in PaleCourtDllNames)
            {
                string candidate = Path.Combine(directory, dllName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool DirectoryMatchesHint(string folderName)
        {
            if (string.IsNullOrEmpty(folderName))
            {
                return false;
            }

            string normalizedFolder = NormalizeModName(folderName);
            foreach (string hint in PaleCourtDirectoryHints)
            {
                if (normalizedFolder.IndexOf(NormalizeModName(hint), StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeModName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }
    }
}
