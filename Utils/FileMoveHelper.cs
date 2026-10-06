using System.IO;

namespace ReplayLogger
{
    internal static class FileMoveHelper
    {
        internal static void MoveSafely(string sourcePath, string destinationPath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(destinationPath))
            {
                return;
            }

            try
            {
                File.Move(sourcePath, destinationPath);
                return;
            }
            catch (IOException moveIoEx)
            {
                global::ReplayLogger.InternalDiagnostics.Warn($"ReplayLogger: File.Move failed for '{Path.GetFileName(sourcePath)}', fallback to copy+delete: {moveIoEx.Message}");
            }

            File.Copy(sourcePath, destinationPath, overwrite: true);
            File.Delete(sourcePath);
        }
    }
}
