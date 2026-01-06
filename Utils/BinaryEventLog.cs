using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace ReplayLogger
{
    internal sealed class BinaryEventLog : IDisposable
    {
        private const byte Version = 1;
        private static readonly byte[] Magic = { (byte)'R', (byte)'L', (byte)'B', (byte)'1' };
        private const byte EventKey = 1;
        private const byte EventLine = 2;

        private readonly string path;
        private readonly FileStream stream;
        private readonly BinaryWriter writer;
        private bool disposed;

        internal string Path => path;

        internal BinaryEventLog(string path)
        {
            this.path = path;
            string dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write(Magic);
            writer.Write(Version);
        }

        internal void WriteKeyEvent(int deltaMs, KeyCode keyCode, bool isDown, int watermarkNumber, Color color, int fps)
        {
            if (disposed)
            {
                return;
            }

            writer.Write(EventKey);
            writer.Write(deltaMs);
            writer.Write((int)keyCode);
            writer.Write((byte)(isDown ? 1 : 0));
            writer.Write(watermarkNumber);

            Color32 color32 = color;
            writer.Write(color32.r);
            writer.Write(color32.g);
            writer.Write(color32.b);
            writer.Write(color32.a);
            writer.Write(fps);
        }

        internal void WriteLine(string line)
        {
            if (disposed)
            {
                return;
            }

            writer.Write(EventLine);
            string safe = line ?? string.Empty;
            byte[] bytes = Encoding.UTF8.GetBytes(safe);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        internal static void ConvertToText(string path, StreamWriter writer)
        {
            if (string.IsNullOrEmpty(path) || writer == null || !File.Exists(path))
            {
                return;
            }

            try
            {
                using FileStream fileStream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using BinaryReader reader = new(fileStream, Encoding.UTF8, leaveOpen: false);

                if (!TryReadHeader(reader))
                {
                    return;
                }

                while (fileStream.Position < fileStream.Length)
                {
                    byte eventType;
                    try
                    {
                        eventType = reader.ReadByte();
                    }
                    catch
                    {
                        break;
                    }

                    if (eventType == EventKey)
                    {
                        if (!TryReadKeyEvent(reader, out string line))
                        {
                            break;
                        }

                        LogWrite.EncryptedLine(writer, line);
                    }
                    else if (eventType == EventLine)
                    {
                        if (!TryReadLine(reader, out string line))
                        {
                            break;
                        }

                        LogWrite.EncryptedLine(writer, line);
                    }
                    else
                    {
                        break;
                    }
                }
            }
            catch
            {
            }
        }

        private static bool TryReadHeader(BinaryReader reader)
        {
            try
            {
                byte[] magic = reader.ReadBytes(Magic.Length);
                if (magic.Length != Magic.Length)
                {
                    return false;
                }

                for (int i = 0; i < Magic.Length; i++)
                {
                    if (magic[i] != Magic[i])
                    {
                        return false;
                    }
                }

                byte version = reader.ReadByte();
                return version == Version;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadKeyEvent(BinaryReader reader, out string line)
        {
            line = null;

            try
            {
                int deltaMs = reader.ReadInt32();
                int keyCodeRaw = reader.ReadInt32();
                bool isDown = reader.ReadByte() != 0;
                int watermarkNumber = reader.ReadInt32();
                byte r = reader.ReadByte();
                byte g = reader.ReadByte();
                byte b = reader.ReadByte();
                byte a = reader.ReadByte();
                int fps = reader.ReadInt32();

                KeyCode keyCode = (KeyCode)keyCodeRaw;
                string formattedKey = JoystickKeyMapper.FormatKey(keyCode);
                string status = isDown ? "+" : "-";
                string colorHex = ToHex(r, g, b, a);
                string fpsText = fps.ToString(CultureInfo.InvariantCulture);

                line = $"+{deltaMs}|{formattedKey}|{status}|{watermarkNumber}|#{colorHex}|{fpsText}|";
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadLine(BinaryReader reader, out string line)
        {
            line = null;

            try
            {
                int length = reader.ReadInt32();
                if (length < 0)
                {
                    return false;
                }

                byte[] bytes = reader.ReadBytes(length);
                if (bytes.Length != length)
                {
                    return false;
                }

                line = Encoding.UTF8.GetString(bytes);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string ToHex(byte r, byte g, byte b, byte a)
        {
            StringBuilder builder = new(8);
            builder.Append(r.ToString("X2", CultureInfo.InvariantCulture));
            builder.Append(g.ToString("X2", CultureInfo.InvariantCulture));
            builder.Append(b.ToString("X2", CultureInfo.InvariantCulture));
            builder.Append(a.ToString("X2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            try
            {
                writer.Flush();
            }
            catch
            {
            }

            writer.Dispose();
            stream.Dispose();
        }
    }
}
