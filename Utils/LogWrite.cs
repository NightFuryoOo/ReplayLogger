using System.IO;

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

            if (writer is AsyncLogWriter asyncWriter)
            {
                asyncWriter.Write(plaintext);
                return;
            }

            writer.Write(KeyloggerLogEncryption.EncryptLog(plaintext));
        }

        internal static void RawLine(StreamWriter writer, string raw)
        {
            if (writer == null)
            {
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

            if (writer is AsyncLogWriter asyncWriter)
            {
                asyncWriter.WriteRaw(raw);
                return;
            }

            writer.Write(raw);
        }
    }
}
