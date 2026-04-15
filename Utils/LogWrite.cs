using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ReplayLogger
{
    internal static class LogWrite
    {
        internal static void EncryptedLine(StreamWriter writer, string plaintext)
        {
            if (writer == null)
            {
                return;
            }

            if (writer is BlockLogWriter blockWriter)
            {
                blockWriter.WriteLine(plaintext);
                return;
            }
            if (writer is AsyncBlockLogWriter asyncBlockWriter)
            {
                asyncBlockWriter.WriteLine(plaintext);
                return;
            }
            if (writer is AsyncLogWriter asyncWriter)
            {
                asyncWriter.WriteLine(plaintext);
                return;
            }

            if (TryEncryptWithWriterSession(writer, plaintext, out string encrypted))
            {
                writer.WriteLine(encrypted);
            }
        }

        internal static void Encrypted(StreamWriter writer, string plaintext)
        {
            if (writer == null)
            {
                return;
            }

            if (writer is BlockLogWriter blockWriter)
            {
                blockWriter.Write(plaintext);
                return;
            }
            if (writer is AsyncBlockLogWriter asyncBlockWriter)
            {
                asyncBlockWriter.Write(plaintext);
                return;
            }
            if (writer is AsyncLogWriter asyncWriter)
            {
                asyncWriter.Write(plaintext);
                return;
            }

            if (TryEncryptWithWriterSession(writer, plaintext, out string encrypted))
            {
                writer.Write(encrypted);
            }
        }

        internal static void EncryptedLines(StreamWriter writer, IReadOnlyList<string> plaintextLines)
        {
            if (writer == null || plaintextLines == null || plaintextLines.Count == 0)
            {
                return;
            }

            if (writer is BlockLogWriter blockWriter)
            {
                blockWriter.WriteLines(plaintextLines);
                return;
            }
            if (writer is AsyncBlockLogWriter asyncBlockWriter)
            {
                asyncBlockWriter.WriteLines(plaintextLines);
                return;
            }
            if (writer is AsyncLogWriter asyncWriter)
            {
                foreach (string line in plaintextLines)
                {
                    asyncWriter.WriteLine(line ?? string.Empty);
                }
                return;
            }

            string newline = writer.NewLine;
            StringBuilder builder = TempObjectPools.RentStringBuilder(1024);
            try
            {
                IEncryptionSessionProvider sessionProvider = writer as IEncryptionSessionProvider;
                KeyloggerLogEncryption.Session session = sessionProvider?.EncryptionSession;
                if (session == null)
                {
                    global::ReplayLogger.InternalDiagnostics.Error("ReplayLogger: missing encryption session for StreamWriter fallback; encrypted batch write was skipped.");
                    return;
                }

                foreach (string line in plaintextLines)
                {
                    builder.Append(session.EncryptLog(line ?? string.Empty));
                    builder.Append(newline);
                }

                writer.Write(builder.ToString());
            }
            finally
            {
                TempObjectPools.ReturnStringBuilder(builder);
            }
        }

        internal static void RawLine(StreamWriter writer, string raw)
        {
            if (writer == null)
            {
                return;
            }

            if (writer is BlockLogWriter blockWriter)
            {
                blockWriter.WriteRawLine(raw);
                return;
            }
            if (writer is AsyncBlockLogWriter asyncBlockWriter)
            {
                asyncBlockWriter.WriteRawLine(raw);
                return;
            }
            if (writer is AsyncLogWriter asyncWriter)
            {
                asyncWriter.WriteRawLine(raw);
                return;
            }

            writer.WriteLine(raw);
        }

        internal static void Raw(StreamWriter writer, string raw)
        {
            if (writer == null)
            {
                return;
            }

            if (writer is BlockLogWriter blockWriter)
            {
                blockWriter.WriteRaw(raw);
                return;
            }
            if (writer is AsyncBlockLogWriter asyncBlockWriter)
            {
                asyncBlockWriter.WriteRaw(raw);
                return;
            }
            if (writer is AsyncLogWriter asyncWriter)
            {
                asyncWriter.WriteRaw(raw);
                return;
            }

            writer.Write(raw);
        }

        private static bool TryEncryptWithWriterSession(StreamWriter writer, string plaintext, out string encrypted)
        {
            encrypted = null;
            IEncryptionSessionProvider sessionProvider = writer as IEncryptionSessionProvider;
            KeyloggerLogEncryption.Session session = sessionProvider?.EncryptionSession;
            if (session == null)
            {
                global::ReplayLogger.InternalDiagnostics.Error("ReplayLogger: missing encryption session for StreamWriter fallback; encrypted write was skipped.");
                return false;
            }

            encrypted = session.EncryptLog(plaintext ?? string.Empty);
            return !string.IsNullOrEmpty(encrypted);
        }
    }
}



