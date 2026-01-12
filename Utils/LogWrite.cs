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
            if (writer is AsyncLogWriter asyncWriter)
            {
                asyncWriter.WriteLine(plaintext);
                return;
            }

            writer.WriteLine(KeyloggerLogEncryption.EncryptLog(plaintext));
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
            if (writer is AsyncLogWriter asyncWriter)
            {
                asyncWriter.Write(plaintext);
                return;
            }

            writer.Write(KeyloggerLogEncryption.EncryptLog(plaintext));
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
            if (writer is AsyncLogWriter asyncWriter)
            {
                foreach (string line in plaintextLines)
                {
                    asyncWriter.WriteLine(line ?? string.Empty);
                }
                return;
            }

            string newline = writer.NewLine;
            StringBuilder builder = new();
            foreach (string line in plaintextLines)
            {
                builder.Append(KeyloggerLogEncryption.EncryptLog(line ?? string.Empty));
                builder.Append(newline);
            }

            writer.Write(builder.ToString());
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
            if (writer is AsyncLogWriter asyncWriter)
            {
                asyncWriter.WriteRaw(raw);
                return;
            }

            writer.Write(raw);
        }
    }
}
