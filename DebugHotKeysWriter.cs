using System.Collections.Generic;
using System.IO;

namespace ReplayLogger
{
    internal static class DebugHotKeysWriter
    {
        public static void Write(StreamWriter writer, IReadOnlyList<string> bindings, IReadOnlyList<string> activations, string separator = "---------------------------------------------------")
        {
            if (writer == null)
            {
                return;
            }

            writer.WriteLine(KeyloggerLogEncryption.EncryptLog("DebugHotKeys:"));
            writer.WriteLine(KeyloggerLogEncryption.EncryptLog("Bindings:"));
            if (bindings == null || bindings.Count == 0)
            {
                writer.WriteLine(KeyloggerLogEncryption.EncryptLog("  (none)"));
            }
            else
            {
                foreach (string binding in bindings)
                {
                    writer.WriteLine(KeyloggerLogEncryption.EncryptLog(binding));
                }
            }

            writer.WriteLine(KeyloggerLogEncryption.EncryptLog("Activations:"));
            if (activations == null || activations.Count == 0)
            {
                writer.WriteLine(KeyloggerLogEncryption.EncryptLog("  (none)"));
            }
            else
            {
                foreach (string evt in activations)
                {
                    writer.WriteLine(KeyloggerLogEncryption.EncryptLog(evt));
                }
            }

            writer.WriteLine(KeyloggerLogEncryption.EncryptLog(string.Empty));
            if (!string.IsNullOrEmpty(separator))
            {
                writer.WriteLine(KeyloggerLogEncryption.EncryptLog(separator));
            }
        }
    }
}
