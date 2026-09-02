using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace ReplayLogger
{
    internal static class CustomKnightSettingsManager
    {
        private const string CustomKnightTypeName = "CustomKnight.CustomKnight";
        private const string SkinManagerTypeName = "CustomKnight.SkinManager";
        private const string UnavailableValue = "unavailable";

        private static readonly List<string> skinChanges = new();

        private static bool typeResolutionAttempted;
        private static Type customKnightType;
        private static Type skinManagerType;
        private static PropertyInfo globalSettingsProperty;
        private static PropertyInfo saveSettingsProperty;
        private static PropertyInfo dataDirectoryProperty;
        private static FieldInfo skinsFolderField;
        private static MethodInfo getCurrentSkinMethod;
        private static EventInfo onSetSkinEvent;
        private static Delegate onSetSkinHandler;
        private static SkinSnapshot initialSnapshot;
        private static SkinSnapshot currentSnapshot;
        private static string trackingArena;
        private static long trackingStartUnixTime;
        private static bool trackingActive;

        public static void StartTracking(string arenaName, long startUnixTime)
        {
            StopTracking();
            skinChanges.Clear();
            trackingArena = string.IsNullOrWhiteSpace(arenaName) ? "UnknownArena" : arenaName;
            trackingStartUnixTime = startUnixTime;
            trackingActive = true;

            try
            {
                initialSnapshot = CaptureCurrentSnapshot(includeHash: true);
                currentSnapshot = initialSnapshot;
                SubscribeToSkinChanges();
            }
            catch (Exception e)
            {
                initialSnapshot = SkinSnapshot.Unavailable;
                currentSnapshot = initialSnapshot;
                InternalDiagnostics.Warn($"ReplayLogger: failed to start CustomKnight skin tracking: {e.Message}");
            }
        }

        public static void Reset()
        {
            StopTracking();
            skinChanges.Clear();
            initialSnapshot = null;
            currentSnapshot = null;
            trackingArena = null;
            trackingStartUnixTime = 0;

            typeResolutionAttempted = false;
            customKnightType = null;
            skinManagerType = null;
            globalSettingsProperty = null;
            saveSettingsProperty = null;
            dataDirectoryProperty = null;
            skinsFolderField = null;
            getCurrentSkinMethod = null;
        }

        public static string BuildSettingsLine()
        {
            SkinSnapshot snapshot = initialSnapshot ?? CaptureCurrentSnapshot(includeHash: false);
            return $"CustomKnight Skin: {snapshot.DisplayName}";
        }

        public static void WriteSettingsWithSeparator(StreamWriter writer, string separator = "---------------------------------------------------")
        {
            if (writer == null)
            {
                return;
            }

            SkinSnapshot snapshot = initialSnapshot ?? CaptureCurrentSnapshot(includeHash: true);
            List<string> lines = new(skinChanges.Count + 6)
            {
                $"CustomKnight Skin: {snapshot.DisplayName}",
                $"CustomKnight Skin ID: {FormatValue(snapshot.Id)}",
                $"CustomKnight Skin SHA-256: {FormatValue(snapshot.Sha256)}",
                "CustomKnight Skin Changes:"
            };

            if (skinChanges.Count == 0)
            {
                lines.Add("  (none)");
            }
            else
            {
                foreach (string change in skinChanges)
                {
                    lines.Add($"  {change}");
                }
            }

            if (!string.IsNullOrEmpty(separator))
            {
                lines.Add(separator);
            }

            LogWrite.EncryptedLines(writer, lines);
        }

        private static void SubscribeToSkinChanges()
        {
            if (!TryResolveTypes() || onSetSkinHandler != null)
            {
                return;
            }

            const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            onSetSkinEvent = skinManagerType.GetEvent("OnSetSkin", staticFlags);
            if (onSetSkinEvent?.EventHandlerType == null)
            {
                return;
            }

            MethodInfo callback = typeof(CustomKnightSettingsManager).GetMethod(
                nameof(HandleSkinChanged),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (callback == null)
            {
                return;
            }

            onSetSkinHandler = Delegate.CreateDelegate(onSetSkinEvent.EventHandlerType, callback);
            onSetSkinEvent.AddEventHandler(null, onSetSkinHandler);
        }

        private static void StopTracking()
        {
            trackingActive = false;
            if (onSetSkinEvent == null || onSetSkinHandler == null)
            {
                onSetSkinEvent = null;
                onSetSkinHandler = null;
                return;
            }

            try
            {
                onSetSkinEvent.RemoveEventHandler(null, onSetSkinHandler);
            }
            catch (Exception e)
            {
                InternalDiagnostics.Warn($"ReplayLogger: failed to detach CustomKnight skin tracking: {e.Message}");
            }
            finally
            {
                onSetSkinEvent = null;
                onSetSkinHandler = null;
            }
        }

        private static void HandleSkinChanged(object sender, EventArgs args)
        {
            _ = sender;
            _ = args;

            if (!trackingActive)
            {
                return;
            }

            try
            {
                object skin = GetCurrentSkin();
                SkinSnapshot identity = CaptureSnapshot(skin, includeHash: false);
                if (currentSnapshot != null &&
                    string.Equals(currentSnapshot.Id, identity.Id, StringComparison.Ordinal))
                {
                    return;
                }

                SkinSnapshot nextSnapshot = CaptureSnapshot(skin, includeHash: true);
                SkinSnapshot previousSnapshot = currentSnapshot ?? SkinSnapshot.Unavailable;
                long nowUnixTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                long elapsed = trackingStartUnixTime > 0
                    ? Math.Max(0, nowUnixTime - trackingStartUnixTime)
                    : 0;
                string arena = GameManager.instance?.sceneName;
                if (string.IsNullOrWhiteSpace(arena))
                {
                    arena = trackingArena ?? "UnknownArena";
                }

                skinChanges.Add(
                    $"|{NormalizeLogValue(arena)}|+{elapsed}|CustomKnight Skin Changed: " +
                    $"{previousSnapshot.DisplayName} [SHA-256: {FormatValue(previousSnapshot.Sha256)}] -> " +
                    $"{nextSnapshot.DisplayName} [SHA-256: {FormatValue(nextSnapshot.Sha256)}]");
                currentSnapshot = nextSnapshot;
            }
            catch (Exception e)
            {
                InternalDiagnostics.Warn($"ReplayLogger: failed to record CustomKnight skin change: {e.Message}");
            }
        }

        private static SkinSnapshot CaptureCurrentSnapshot(bool includeHash)
        {
            if (!TryResolveTypes())
            {
                return SkinSnapshot.Unavailable;
            }

            return CaptureSnapshot(GetCurrentSkin(), includeHash);
        }

        private static object GetCurrentSkin()
        {
            try
            {
                return getCurrentSkinMethod?.InvokeCached(null);
            }
            catch
            {
                return null;
            }
        }

        private static SkinSnapshot CaptureSnapshot(object currentSkin, bool includeHash)
        {
            string currentSkinName = null;
            string currentSkinId = null;
            string saveDefaultSkin = null;
            string globalDefaultSkin = null;

            try
            {
                object globalSettings = globalSettingsProperty?.GetCachedValue(null);
                globalDefaultSkin = TryGetRuntimeStringProperty(globalSettings, "DefaultSkin");
            }
            catch
            {
            }

            try
            {
                object saveSettings = saveSettingsProperty?.GetCachedValue(null);
                saveDefaultSkin = TryGetRuntimeStringProperty(saveSettings, "DefaultSkin");
            }
            catch
            {
            }

            if (currentSkin != null)
            {
                currentSkinName = TryGetRuntimeStringMethod(currentSkin, "GetName");
                currentSkinId = TryGetRuntimeStringMethod(currentSkin, "GetId");
            }

            string effectiveId = FirstNonEmpty(currentSkinId, saveDefaultSkin, globalDefaultSkin);
            string displayName = ResolveDisplaySkin(currentSkinName, currentSkinId, saveDefaultSkin, globalDefaultSkin);
            string sha256 = includeHash ? ComputeSkinSha256(currentSkin, effectiveId) : null;

            return new SkinSnapshot(
                NormalizeLogValue(displayName),
                NormalizeLogValue(effectiveId),
                sha256);
        }

        private static string ComputeSkinSha256(object currentSkin, string skinId)
        {
            try
            {
                string skinDirectory = ResolveSkinDirectory(currentSkin, skinId);
                if (string.IsNullOrWhiteSpace(skinDirectory) || !Directory.Exists(skinDirectory))
                {
                    return UnavailableValue;
                }

                return ComputeDirectorySha256(skinDirectory);
            }
            catch (Exception e)
            {
                InternalDiagnostics.Warn($"ReplayLogger: failed to hash CustomKnight skin '{NormalizeLogValue(skinId)}': {e.Message}");
                return UnavailableValue;
            }
        }

        private static string ResolveSkinDirectory(object currentSkin, string skinId)
        {
            string skinPath = TryGetRuntimeStringMethod(currentSkin, "getSwapperPath");
            if (!string.IsNullOrWhiteSpace(skinPath) && Directory.Exists(skinPath))
            {
                return Path.GetFullPath(skinPath);
            }

            string skinsFolder = null;
            try
            {
                skinsFolder = skinsFolderField?.GetValue(null) as string;
            }
            catch
            {
            }

            if (string.IsNullOrWhiteSpace(skinsFolder))
            {
                try
                {
                    string dataDirectory = dataDirectoryProperty?.GetValue(null, null) as string;
                    if (!string.IsNullOrWhiteSpace(dataDirectory))
                    {
                        skinsFolder = Path.Combine(dataDirectory, "Skins");
                    }
                }
                catch
                {
                }
            }

            if (string.IsNullOrWhiteSpace(skinsFolder) || string.IsNullOrWhiteSpace(skinId))
            {
                return null;
            }

            string root = Path.GetFullPath(skinsFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string candidate = Path.GetFullPath(Path.Combine(root, skinId));
            string rootPrefix = root + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return candidate;
        }

        private static string ComputeDirectorySha256(string directory)
        {
            string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string rootPrefix = root + Path.DirectorySeparatorChar;
            string[] files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
            List<SkinFileEntry> entries = new(files.Length);

            foreach (string file in files)
            {
                string fullPath = Path.GetFullPath(file);
                if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string relativePath = fullPath.Substring(rootPrefix.Length).Replace('\\', '/');
                entries.Add(new SkinFileEntry(fullPath, relativePath));
            }

            entries.Sort((left, right) => string.Compare(left.RelativePath, right.RelativePath, StringComparison.Ordinal));

            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] buffer = new byte[81920];
                foreach (SkinFileEntry entry in entries)
                {
                    byte[] pathBytes = Encoding.UTF8.GetBytes(entry.RelativePath);
                    AppendInt32(sha256, pathBytes.Length);
                    AppendBytes(sha256, pathBytes, pathBytes.Length);

                    using (FileStream stream = new(
                        entry.FullPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        buffer.Length,
                        FileOptions.SequentialScan))
                    {
                        AppendInt64(sha256, stream.Length);
                        int read;
                        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            AppendBytes(sha256, buffer, read);
                        }
                    }
                }

                sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return BitConverter.ToString(sha256.Hash).Replace("-", string.Empty);
            }
        }

        private static void AppendInt32(HashAlgorithm hash, int value)
        {
            byte[] bytes =
            {
                (byte)(value >> 24),
                (byte)(value >> 16),
                (byte)(value >> 8),
                (byte)value
            };
            AppendBytes(hash, bytes, bytes.Length);
        }

        private static void AppendInt64(HashAlgorithm hash, long value)
        {
            byte[] bytes = new byte[8];
            for (int i = bytes.Length - 1; i >= 0; i--)
            {
                bytes[i] = (byte)value;
                value >>= 8;
            }
            AppendBytes(hash, bytes, bytes.Length);
        }

        private static void AppendBytes(HashAlgorithm hash, byte[] bytes, int count)
        {
            hash.TransformBlock(bytes, 0, count, bytes, 0);
        }

        private static bool TryResolveTypes()
        {
            if (typeResolutionAttempted)
            {
                return customKnightType != null && skinManagerType != null && getCurrentSkinMethod != null;
            }

            typeResolutionAttempted = true;
            customKnightType = FindType(CustomKnightTypeName);
            skinManagerType = FindType(SkinManagerTypeName);
            if (customKnightType == null || skinManagerType == null)
            {
                return false;
            }

            const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            globalSettingsProperty = customKnightType.GetProperty("GlobalSettings", staticFlags);
            saveSettingsProperty = customKnightType.GetProperty("SaveSettings", staticFlags);
            dataDirectoryProperty = skinManagerType.GetProperty("DATA_DIR", staticFlags);
            skinsFolderField = skinManagerType.GetField("SKINS_FOLDER", staticFlags);
            getCurrentSkinMethod = skinManagerType.GetMethod("GetCurrentSkin", staticFlags);
            return getCurrentSkinMethod != null;
        }

        private static Type FindType(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
            {
                return null;
            }

            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = asm.GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static string TryGetRuntimeStringProperty(object instance, string propertyName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return null;
            }

            if (!ReflectionMemberAccessCache.TryGetCachedRuntimePropertyValue(instance, propertyName, out object raw))
            {
                return null;
            }

            return raw as string ?? raw?.ToString();
        }

        private static string TryGetRuntimeStringMethod(object instance, string methodName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(methodName))
            {
                return null;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            MethodInfo method = instance.GetType().GetMethod(methodName, flags, null, Type.EmptyTypes, null);
            if (method == null)
            {
                return null;
            }

            object raw = method.InvokeCached(instance);
            return raw as string ?? raw?.ToString();
        }

        private static string ResolveDisplaySkin(string currentSkinName, string currentSkinId, string saveDefaultSkin, string globalDefaultSkin)
        {
            if (!string.IsNullOrWhiteSpace(currentSkinName) && !string.IsNullOrWhiteSpace(currentSkinId))
            {
                if (string.Equals(currentSkinName, currentSkinId, StringComparison.Ordinal))
                {
                    return currentSkinName;
                }

                return $"{currentSkinName} ({currentSkinId})";
            }

            return FirstNonEmpty(currentSkinName, currentSkinId, saveDefaultSkin, globalDefaultSkin) ?? "N/A";
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        private static string FormatValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "N/A" : NormalizeLogValue(value);
        }

        private static string NormalizeLogValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "N/A";
            }

            return value.Replace('\r', ' ').Replace('\n', ' ').Replace('|', '/').Trim();
        }

        private sealed class SkinSnapshot
        {
            public static readonly SkinSnapshot Unavailable = new(UnavailableValue, UnavailableValue, UnavailableValue);

            public SkinSnapshot(string displayName, string id, string sha256)
            {
                DisplayName = displayName;
                Id = id;
                Sha256 = sha256;
            }

            public string DisplayName { get; }
            public string Id { get; }
            public string Sha256 { get; }
        }

        private sealed class SkinFileEntry
        {
            public SkinFileEntry(string fullPath, string relativePath)
            {
                FullPath = fullPath;
                RelativePath = relativePath;
            }

            public string FullPath { get; }
            public string RelativePath { get; }
        }
    }
}
