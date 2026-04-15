using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Modding;
using Mono.Cecil;

namespace ReplayLogger
{
    internal static class ModsChecking
    {
        private static readonly string[] PaleCourtDllNames = { "PaleCourt.dll", "FiveKnights.dll" };
        private static readonly string[] PaleCourtDirectoryHints = { "palecourt", "fiveknights" };

        private static readonly string[] KnownDependencyPrefixes =
        {
            "MMHOOK_",
            "Assembly-CSharp",
            "PlayMaker",
            "Unity",
            "System",
            "mscorlib",
            "netstandard",
            "Mono.",
            "MonoMod",
            "Newtonsoft",
            "Satchel"
        };

        private const int MiniHashChunkSize = 4096;
        private static readonly object ModScanLock = new();
        private static string cachedModsDirectory;
        private static string cachedModsFingerprint;
        private static List<string> cachedModsSnapshot;
        private static readonly Dictionary<string, CachedMetadataInfo> cachedMetadataByDllPath = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, CachedHashInfo> cachedSha256ByDllPath = new(StringComparer.OrdinalIgnoreCase);

        private static CachedModInfo cachedPaleCourtInfo;
        private static bool paleCourtCacheAttempted;

        private sealed class CachedModInfo
        {
            public string DirectoryPath;
            public string DllPath;
            public string DllSignature;
            public string ModName;
            public string Version;
            public string Hash;
        }

        private sealed class ModAssemblyMetadata
        {
            public string DllPath;
            public string DllSignature;
            public string ModName;
            public string Version;
            public bool HasModEntry;
        }

        private sealed class CachedMetadataInfo
        {
            public string DllSignature;
            public ModAssemblyMetadata Metadata;
        }

        private sealed class CachedHashInfo
        {
            public string DllSignature;
            public string Hash;
        }

        public static void ClearHeavyModCache()
        {
            cachedPaleCourtInfo = null;
            paleCourtCacheAttempted = false;
            cachedModsDirectory = null;
            cachedModsFingerprint = null;
            cachedModsSnapshot = null;
            cachedMetadataByDllPath.Clear();
            cachedSha256ByDllPath.Clear();
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
                EnsurePaleCourtCache(modsDir);
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
                .Where(IsTrackableModDirectory)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
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

                    if (!TryResolveDirectoryMetadata(modDirectory, out ModAssemblyMetadata metadata))
                    {
                        unregisteredMods.Add(modDirectory);
                        continue;
                    }

                    string hash = CalculateSHA256Cached(metadata.DllPath, metadata.DllSignature) ?? string.Empty;
                    modInfo.Add($"{metadata.ModName}|{metadata.Version}|{hash}");
                }
                catch (Exception ex)
                {
                    global::ReplayLogger.InternalDiagnostics.Error($"ReplayLogger: error processing directory '{modDirectory}': {ex.Message}");
                    unregisteredMods.Add(modDirectory);
                }
            }

            if (unregisteredMods.Count == 0)
            {
                return modInfo;
            }

            List<string> report = [.. modInfo, "Unregistered mods:", .. unregisteredMods];
            return report;
        }

        private static bool TryResolveDirectoryMetadata(string modDirectory, out ModAssemblyMetadata metadata)
        {
            metadata = null;
            if (string.IsNullOrEmpty(modDirectory) || !Directory.Exists(modDirectory))
            {
                return false;
            }

            if (!TryGetOrderedDllCandidates(modDirectory, out List<string> dllCandidates))
            {
                return false;
            }

            foreach (string dllPath in dllCandidates)
            {
                if (!TryReadAssemblyMetadata(dllPath, out ModAssemblyMetadata candidate, out string errorMessage))
                {
                    if (!string.IsNullOrWhiteSpace(errorMessage))
                    {
                        global::ReplayLogger.InternalDiagnostics.Warn($"ReplayLogger: cannot inspect '{dllPath}': {errorMessage}");
                    }

                    continue;
                }

                if (candidate != null && candidate.HasModEntry)
                {
                    metadata = candidate;
                    return true;
                }
            }

            return false;
        }

        private static bool TryReadAssemblyMetadata(string dllPath, out ModAssemblyMetadata metadata, out string errorMessage)
        {
            metadata = null;
            errorMessage = null;

            if (string.IsNullOrEmpty(dllPath) || !File.Exists(dllPath))
            {
                return false;
            }

            bool hasSignature = TryGetDllSignature(dllPath, out string fullPath, out string dllSignature);
            string effectivePath = hasSignature ? fullPath : dllPath;

            if (hasSignature &&
                cachedMetadataByDllPath.TryGetValue(effectivePath, out CachedMetadataInfo cached) &&
                string.Equals(cached.DllSignature, dllSignature, StringComparison.Ordinal) &&
                cached.Metadata != null)
            {
                metadata = CloneMetadata(cached.Metadata);
                if (metadata != null)
                {
                    metadata.DllPath = effectivePath;
                    metadata.DllSignature = dllSignature;
                    return true;
                }
            }

            if (!TryReadAssemblyMetadataUncached(effectivePath, out ModAssemblyMetadata uncachedMetadata, out errorMessage))
            {
                return false;
            }

            if (uncachedMetadata == null)
            {
                return false;
            }

            uncachedMetadata.DllPath = effectivePath;
            uncachedMetadata.DllSignature = hasSignature ? dllSignature : null;
            metadata = uncachedMetadata;

            if (hasSignature)
            {
                cachedMetadataByDllPath[effectivePath] = new CachedMetadataInfo
                {
                    DllSignature = dllSignature,
                    Metadata = CloneMetadata(uncachedMetadata)
                };
            }

            return true;
        }

        private static bool TryReadAssemblyMetadataUncached(string dllPath, out ModAssemblyMetadata metadata, out string errorMessage)
        {
            metadata = null;
            errorMessage = null;

            try
            {
                ReaderParameters readerParameters = new()
                {
                    ReadSymbols = false,
                    ReadingMode = ReadingMode.Deferred
                };

                using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(dllPath, readerParameters);
                string modTypeName = TryFindModTypeName(assembly);
                string modVersion = ResolveModVersion(assembly, dllPath);

                metadata = new ModAssemblyMetadata
                {
                    DllPath = dllPath,
                    ModName = string.IsNullOrWhiteSpace(modTypeName)
                        ? Path.GetFileNameWithoutExtension(dllPath)
                        : modTypeName,
                    Version = string.IsNullOrWhiteSpace(modVersion) ? "Unknown" : modVersion,
                    HasModEntry = !string.IsNullOrWhiteSpace(modTypeName)
                };

                return true;
            }
            catch (BadImageFormatException)
            {
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private static ModAssemblyMetadata CloneMetadata(ModAssemblyMetadata metadata)
        {
            if (metadata == null)
            {
                return null;
            }

            return new ModAssemblyMetadata
            {
                DllPath = metadata.DllPath,
                DllSignature = metadata.DllSignature,
                ModName = metadata.ModName,
                Version = metadata.Version,
                HasModEntry = metadata.HasModEntry
            };
        }

        private static string TryFindModTypeName(AssemblyDefinition assembly)
        {
            if (assembly?.MainModule == null)
            {
                return null;
            }

            foreach (TypeDefinition type in assembly.MainModule.Types)
            {
                if (!IsConcreteModType(type))
                {
                    continue;
                }

                return type.Name;
            }

            return null;
        }

        private static bool IsConcreteModType(TypeDefinition type)
        {
            if (type == null || !type.IsClass || type.IsAbstract)
            {
                return false;
            }

            if (ImplementsIMod(type))
            {
                return true;
            }

            return DerivesFromModBase(type);
        }

        private static bool ImplementsIMod(TypeDefinition type)
        {
            if (type == null)
            {
                return false;
            }

            foreach (InterfaceImplementation implementation in type.Interfaces)
            {
                TypeReference iface = implementation?.InterfaceType;
                if (iface == null)
                {
                    continue;
                }

                if (string.Equals(iface.FullName, "Modding.IMod", StringComparison.Ordinal)
                    || string.Equals(iface.Name, "IMod", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool DerivesFromModBase(TypeDefinition type)
        {
            if (type == null)
            {
                return false;
            }

            TypeReference current = type.BaseType;
            int guard = 0;
            while (current != null && guard++ < 16)
            {
                if (string.Equals(current.FullName, "Modding.Mod", StringComparison.Ordinal)
                    || (string.Equals(current.Name, "Mod", StringComparison.Ordinal)
                        && string.Equals(current.Namespace, "Modding", StringComparison.Ordinal)))
                {
                    return true;
                }

                TypeDefinition resolved = SafeResolve(current);
                if (resolved == null)
                {
                    break;
                }

                if (ImplementsIMod(resolved))
                {
                    return true;
                }

                current = resolved.BaseType;
            }

            return false;
        }

        private static TypeDefinition SafeResolve(TypeReference type)
        {
            if (type == null)
            {
                return null;
            }

            try
            {
                return type.Resolve();
            }
            catch
            {
                return null;
            }
        }

        private static string BuildModsFingerprint(string modsDir)
        {
            try
            {
                List<string> entries = new();
                List<string> modDirectories = Directory.GetDirectories(modsDir)
                    .Where(IsTrackableModDirectory)
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
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

        private static bool TryGetDllSignature(string dllPath, out string fullPath, out string signature)
        {
            fullPath = null;
            signature = null;

            if (string.IsNullOrWhiteSpace(dllPath))
            {
                return false;
            }

            try
            {
                fullPath = Path.GetFullPath(dllPath);
            }
            catch
            {
                fullPath = dllPath;
            }

            try
            {
                if (!File.Exists(fullPath))
                {
                    return false;
                }

                FileInfo info = new(fullPath);
                string miniHash = ComputeMiniHash(fullPath);
                signature = $"{info.Length}|{info.LastWriteTimeUtc.Ticks}|{miniHash}";
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetPrimaryModDll(string modDirectory, out string dllPath)
        {
            dllPath = null;
            if (!TryGetOrderedDllCandidates(modDirectory, out List<string> dllCandidates) || dllCandidates.Count == 0)
            {
                return false;
            }

            dllPath = dllCandidates[0];
            return !string.IsNullOrEmpty(dllPath);
        }

        private static bool TryGetOrderedDllCandidates(string modDirectory, out List<string> dllCandidates)
        {
            dllCandidates = null;
            try
            {
                string[] dllFiles = Directory.GetFiles(modDirectory, "*.dll", SearchOption.TopDirectoryOnly);
                if (dllFiles.Length == 0)
                {
                    return false;
                }

                string folderName = Path.GetFileName(modDirectory) ?? string.Empty;
                string normalizedFolder = NormalizeModName(folderName);

                dllCandidates = dllFiles
                    .OrderByDescending(path => ScoreDllCandidate(path, normalizedFolder))
                    .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return dllCandidates.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private static int ScoreDllCandidate(string dllPath, string normalizedFolder)
        {
            if (string.IsNullOrEmpty(dllPath))
            {
                return int.MinValue;
            }

            string fileName = Path.GetFileNameWithoutExtension(dllPath) ?? string.Empty;
            string normalizedFile = NormalizeModName(fileName);
            int score = 0;

            if (!string.IsNullOrEmpty(normalizedFolder))
            {
                if (string.Equals(normalizedFile, normalizedFolder, StringComparison.OrdinalIgnoreCase))
                {
                    score += 1000;
                }
                else if (normalizedFile.StartsWith(normalizedFolder, StringComparison.OrdinalIgnoreCase))
                {
                    score += 600;
                }
                else if (normalizedFile.IndexOf(normalizedFolder, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    score += 350;
                }
            }

            foreach (string prefix in KnownDependencyPrefixes)
            {
                if (fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    score -= 500;
                    break;
                }
            }

            return score;
        }

        public static string CalculateSHA256(string filePath)
        {
            return CalculateSHA256Cached(filePath, null);
        }

        private static string CalculateSHA256Cached(string filePath, string expectedDllSignature)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath))
                {
                    return null;
                }

                string fullPath = null;
                string currentSignature = null;
                bool hasSignature = !string.IsNullOrWhiteSpace(expectedDllSignature) &&
                    TryGetDllSignature(filePath, out fullPath, out currentSignature) &&
                    string.Equals(expectedDllSignature, currentSignature, StringComparison.Ordinal);

                if (!hasSignature)
                {
                    if (!TryGetDllSignature(filePath, out fullPath, out currentSignature))
                    {
                        return null;
                    }
                }

                if (cachedSha256ByDllPath.TryGetValue(fullPath, out CachedHashInfo cachedHash) &&
                    string.Equals(cachedHash.DllSignature, currentSignature, StringComparison.Ordinal))
                {
                    return cachedHash.Hash;
                }

                using SHA256 sha256 = SHA256.Create();
                using FileStream fileStream = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                byte[] hashBytes = sha256.ComputeHash(fileStream);
                string hash = BitConverter.ToString(hashBytes).Replace("-", string.Empty).ToUpperInvariant();
                cachedSha256ByDllPath[fullPath] = new CachedHashInfo
                {
                    DllSignature = currentSignature,
                    Hash = hash
                };
                return hash;
            }
            catch
            {
                return null;
            }
        }

        private static string ResolveModVersion(AssemblyDefinition assembly, string filePath)
        {
            string modInfoVersion = TryGetModInfoVersion(assembly);
            if (!string.IsNullOrWhiteSpace(modInfoVersion))
            {
                return modInfoVersion;
            }

            if (TryGetAttributeStringArgument(assembly?.CustomAttributes, "System.Reflection.AssemblyInformationalVersionAttribute", out string informationalVersion))
            {
                return informationalVersion;
            }

            if (TryGetAttributeStringArgument(assembly?.CustomAttributes, "System.Reflection.AssemblyFileVersionAttribute", out string fileVersionAttribute))
            {
                return fileVersionAttribute;
            }

            try
            {
                Version asmVersion = assembly?.Name?.Version;
                if (asmVersion != null)
                {
                    return asmVersion.ToString();
                }
            }
            catch
            {
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
            catch
            {
            }

            return "Unknown";
        }

        private static bool TryGetAttributeStringArgument(ICollection<CustomAttribute> attributes, string attributeFullName, out string value)
        {
            value = null;
            if (attributes == null || attributes.Count == 0 || string.IsNullOrEmpty(attributeFullName))
            {
                return false;
            }

            foreach (CustomAttribute attribute in attributes)
            {
                if (!string.Equals(attribute.AttributeType?.FullName, attributeFullName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (attribute.ConstructorArguments.Count == 0)
                {
                    continue;
                }

                CustomAttributeArgument arg = attribute.ConstructorArguments[0];
                if (!string.Equals(arg.Type.FullName, "System.String", StringComparison.Ordinal))
                {
                    continue;
                }

                if (arg.Value is string str && !string.IsNullOrWhiteSpace(str))
                {
                    value = str.Trim();
                    return true;
                }
            }

            return false;
        }

        private static string TryGetModInfoVersion(AssemblyDefinition assembly)
        {
            try
            {
                if (assembly?.MainModule == null)
                {
                    return null;
                }

                foreach (TypeDefinition type in assembly.MainModule.Types)
                {
                    if (!string.Equals(type.Name, "ModInfo", StringComparison.Ordinal)
                        && (type.FullName == null || !type.FullName.EndsWith(".ModInfo", StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    FieldDefinition versionField = type.Fields.FirstOrDefault(field =>
                        string.Equals(field.Name, "Version", StringComparison.Ordinal)
                        && string.Equals(field.FieldType?.FullName, "System.String", StringComparison.Ordinal));

                    if (versionField != null && versionField.HasConstant && versionField.Constant is string fieldVersion && !string.IsNullOrWhiteSpace(fieldVersion))
                    {
                        return fieldVersion.Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                global::ReplayLogger.InternalDiagnostics.Warn($"ReplayLogger: ModInfo.Version metadata lookup failed: {ex.Message}");
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
                    if (!TryGetPrimaryModDll(paleCourtDirectory, out dllPath))
                    {
                        return;
                    }
                }

                if (!TryReadAssemblyMetadata(dllPath, out ModAssemblyMetadata metadata, out string errorMessage))
                {
                    if (!string.IsNullOrWhiteSpace(errorMessage))
                    {
                        global::ReplayLogger.InternalDiagnostics.Warn($"ReplayLogger: cannot inspect Pale Court assembly '{dllPath}': {errorMessage}");
                    }

                    return;
                }

                if (metadata == null || !metadata.HasModEntry)
                {
                    global::ReplayLogger.InternalDiagnostics.Warn("ReplayLogger: Pale Court assembly does not expose an IMod implementation.");
                    return;
                }

                string hash = CalculateSHA256Cached(dllPath, metadata?.DllSignature) ?? string.Empty;
                cachedPaleCourtInfo = new CachedModInfo
                {
                    DirectoryPath = Path.GetFullPath(paleCourtDirectory),
                    DllPath = metadata.DllPath,
                    DllSignature = metadata.DllSignature,
                    ModName = metadata.ModName,
                    Version = metadata.Version,
                    Hash = hash
                };
            }
            catch (Exception ex)
            {
                global::ReplayLogger.InternalDiagnostics.Warn($"ReplayLogger: cannot cache Pale Court info: {ex.Message}");
                cachedPaleCourtInfo = null;
            }
        }

        private static bool IsCachedPaleCourtDirectory(string directory)
        {
            if (cachedPaleCourtInfo == null || string.IsNullOrEmpty(directory))
            {
                return false;
            }

            if (!IsCachedPaleCourtInfoCurrent())
            {
                cachedPaleCourtInfo = null;
                paleCourtCacheAttempted = false;
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

        private static bool IsCachedPaleCourtInfoCurrent()
        {
            if (cachedPaleCourtInfo == null ||
                string.IsNullOrWhiteSpace(cachedPaleCourtInfo.DllPath) ||
                string.IsNullOrWhiteSpace(cachedPaleCourtInfo.DllSignature))
            {
                return false;
            }

            if (!TryGetDllSignature(cachedPaleCourtInfo.DllPath, out _, out string signature))
            {
                return false;
            }

            return string.Equals(signature, cachedPaleCourtInfo.DllSignature, StringComparison.Ordinal);
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

                    if (!IsTrackableModDirectory(directory))
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
                global::ReplayLogger.InternalDiagnostics.Warn($"ReplayLogger: cannot locate Pale Court directory: {ex.Message}");
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

        private static bool IsTrackableModDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory))
            {
                return false;
            }

            string folder = Path.GetFileName(directory);
            if (string.IsNullOrEmpty(folder))
            {
                return false;
            }

            return !folder.Equals("Disabled", StringComparison.OrdinalIgnoreCase)
                && !folder.Equals("Vasi", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeModName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new(value.Length);
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



