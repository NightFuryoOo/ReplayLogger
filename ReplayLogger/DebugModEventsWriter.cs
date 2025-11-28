using System.Collections.Generic;
using System.IO;

namespace ReplayLogger
{
    internal static class DebugModEventsWriter
    {
        public static void Write(StreamWriter writer, IEnumerable<string> events, string separator = "---------------------------------------------------")
        {
            if (writer == null)
            {
                return;
            }

            writer.WriteLine(KeyloggerLogEncryption.EncryptLog("DebugUI:"));
            bool any = false;
            if (events != null)
            {
                foreach (string evt in events)
                {
                    writer.WriteLine(KeyloggerLogEncryption.EncryptLog(evt));
                    any = true;
                }
            }

            if (!any)
            {
                writer.WriteLine(KeyloggerLogEncryption.EncryptLog("  (none)"));
            }

            writer.WriteLine(KeyloggerLogEncryption.EncryptLog(string.Empty));
            if (!string.IsNullOrEmpty(separator))
            {
                writer.WriteLine(KeyloggerLogEncryption.EncryptLog(separator));
            }
        }
    }
}
