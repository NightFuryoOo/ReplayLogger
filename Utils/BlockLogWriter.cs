using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ReplayLogger
{
    internal sealed class BlockLogWriter : StreamWriter, IEncryptionSessionProvider
    {
        private const ushort FileVersion = 2;
        private const ushort FrameMagic = 0xB10C;
        private const byte FrameVersion = 1;
        private const int IvSize = 16;
        private const int HmacSize = 32;
        private const int FrameHeaderSize = 2 + 1 + 1 + 4 + 8 + 4 + 4 + IvSize;
        private const int RecordHeaderSize = 1 + 4;

        private const byte RecordTypeLine = 1;
        private const byte RecordTypeRaw = 2;
        private const byte RecordTypeRawLine = 3;
        private const byte RecordTypeKey = 4;
        private const int Utf8ScratchSize = 1024;

        private static readonly byte[] FileMagic = { (byte)'R', (byte)'P', (byte)'L', (byte)'B' };
        private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        private static readonly double StopwatchTicksToMilliseconds = 1000d / Stopwatch.Frequency;

        private readonly FileStream stream;
        private readonly KeyloggerLogEncryption.Session session;
        private readonly byte[] aesKey;
        private readonly byte[] hmacKey;
        private readonly int blockSizeBytes;
        private readonly int maxBlockAgeMs;
        private readonly Aes aes;
        private readonly HMACSHA256 hmac;
        private readonly RandomNumberGenerator rng;
        private readonly long unixTimeBaseMs;
        private readonly long timeBaseStopwatchTicks;
        private char[] singleCharBuffer;
        private byte[] utf8ScratchBuffer;

        private byte[] plaintextBuffer;
        private int plaintextCount;
        private long blockStartUnixMs;
        private uint blockSequence;
        private bool disposed;

        KeyloggerLogEncryption.Session IEncryptionSessionProvider.EncryptionSession => session;

        internal BlockLogWriter(string path, string sessionKeyBlob, KeyloggerLogEncryption.Session session, int blockSizeBytes, int maxBlockAgeMs)
            : base(Stream.Null)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            if (!this.session.TryGetBlockKeys(out aesKey, out hmacKey))
            {
                throw new InvalidOperationException("ReplayLogger: failed to acquire block encryption keys for the logging session.");
            }

            this.blockSizeBytes = Math.Max(8 * 1024, blockSizeBytes);
            this.maxBlockAgeMs = Math.Max(0, maxBlockAgeMs);
            aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            hmac = new HMACSHA256(hmacKey);
            rng = RandomNumberGenerator.Create();
            singleCharBuffer = new char[1];
            utf8ScratchBuffer = new byte[Utf8ScratchSize];
            unixTimeBaseMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            timeBaseStopwatchTicks = Stopwatch.GetTimestamp();

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 256 * 1024, FileOptions.SequentialScan);
            WriteFileHeader(sessionKeyBlob ?? string.Empty);
        }

        internal BlockLogWriter(string path, string sessionKeyBlob, KeyloggerLogEncryption.Session session)
            : this(path, sessionKeyBlob, session, blockSizeBytes: 64 * 1024, maxBlockAgeMs: 5000)
        {
        }

        public override void WriteLine(string value)
        {
            AppendTextRecord(RecordTypeLine, value ?? string.Empty);
        }

        public override void Write(string value)
        {
            AppendTextRecord(RecordTypeRaw, value ?? string.Empty);
        }

        public override void Write(char value)
        {
            if (disposed || singleCharBuffer == null)
            {
                return;
            }

            singleCharBuffer[0] = value;
            AppendTextRecord(RecordTypeRaw, singleCharBuffer, 0, 1);
        }

        public override void Write(char[] buffer, int index, int count)
        {
            if (buffer == null || count <= 0)
            {
                return;
            }

            AppendTextRecord(RecordTypeRaw, buffer, index, count);
        }

        internal void WriteRawLine(string value)
        {
            AppendTextRecord(RecordTypeRawLine, value ?? string.Empty);
        }

        internal void WriteRaw(string value)
        {
            AppendTextRecord(RecordTypeRaw, value ?? string.Empty);
        }

        internal void WriteLines(System.Collections.Generic.IReadOnlyList<string> lines)
        {
            if (lines == null || lines.Count == 0)
            {
                return;
            }

            foreach (string line in lines)
            {
                AppendTextRecord(RecordTypeLine, line ?? string.Empty);
            }
        }

        internal void WriteKeyEvent(int deltaMs, UnityEngine.KeyCode keyCode, bool isDown, int watermarkNumber, UnityEngine.Color color, int fps)
        {
            if (disposed)
            {
                return;
            }

            const int payloadLength = 25;
            int recordSize = RecordHeaderSize + payloadLength;
            long nowMs = GetNowUnixTimeMilliseconds();

            EnsureCapacityForRecord(recordSize, nowMs);
            WriteRecordHeader(RecordTypeKey, payloadLength);
            WriteInt32(deltaMs);
            WriteInt32((int)keyCode);
            plaintextBuffer[plaintextCount++] = (byte)(isDown ? 1 : 0);
            WriteInt32(watermarkNumber);
            UnityEngine.Color32 color32 = color;
            plaintextBuffer[plaintextCount++] = color32.r;
            plaintextBuffer[plaintextCount++] = color32.g;
            plaintextBuffer[plaintextCount++] = color32.b;
            plaintextBuffer[plaintextCount++] = color32.a;
            WriteInt32(fps);

            FlushIfThresholdReached(nowMs);
        }

        public override void Flush()
        {
            if (disposed)
            {
                return;
            }

            FlushBlock(GetNowUnixTimeMilliseconds());
            stream.Flush();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (disposing)
            {
                Flush();
                try
                {
                    hmac.Dispose();
                }
                catch
                {
                }

                try
                {
                    aes.Dispose();
                }
                catch
                {
                }

                try
                {
                    rng.Dispose();
                }
                catch
                {
                }
            }

            plaintextBuffer = null;
            singleCharBuffer = null;
            utf8ScratchBuffer = null;

            stream.Dispose();
            base.Dispose(disposing);
        }

        private void WriteFileHeader(string sessionKeyBlob)
        {
            byte[] blobBytes = Utf8.GetBytes(sessionKeyBlob);
            using BinaryWriter headerWriter = new(stream, Utf8, leaveOpen: true);
            headerWriter.Write(FileMagic);
            headerWriter.Write(FileVersion);
            headerWriter.Write((ushort)0);
            headerWriter.Write(blockSizeBytes);
            headerWriter.Write(blobBytes.Length);
            headerWriter.Write(blobBytes);
        }

        private void AppendTextRecord(byte recordType, string text)
        {
            if (disposed)
            {
                return;
            }

            text ??= string.Empty;
            int byteCount = Utf8.GetByteCount(text);
            int recordSize = RecordHeaderSize + byteCount;
            long nowMs = GetNowUnixTimeMilliseconds();

            EnsureCapacityForRecord(recordSize, nowMs);
            WriteRecordHeader(recordType, byteCount);

            if (byteCount > 0)
            {
                if (byteCount <= utf8ScratchBuffer.Length)
                {
                    int encoded = Utf8.GetBytes(text, 0, text.Length, utf8ScratchBuffer, 0);
                    Buffer.BlockCopy(utf8ScratchBuffer, 0, plaintextBuffer, plaintextCount, encoded);
                    plaintextCount += encoded;
                }
                else
                {
                    Utf8.GetBytes(text, 0, text.Length, plaintextBuffer, plaintextCount);
                    plaintextCount += byteCount;
                }
            }

            FlushIfThresholdReached(nowMs);
        }

        private void AppendTextRecord(byte recordType, char[] buffer, int index, int count)
        {
            if (disposed || buffer == null || count <= 0)
            {
                return;
            }

            int byteCount = Utf8.GetByteCount(buffer, index, count);
            int recordSize = RecordHeaderSize + byteCount;
            long nowMs = GetNowUnixTimeMilliseconds();

            EnsureCapacityForRecord(recordSize, nowMs);
            WriteRecordHeader(recordType, byteCount);

            if (byteCount > 0)
            {
                if (byteCount <= utf8ScratchBuffer.Length)
                {
                    int encoded = Utf8.GetBytes(buffer, index, count, utf8ScratchBuffer, 0);
                    Buffer.BlockCopy(utf8ScratchBuffer, 0, plaintextBuffer, plaintextCount, encoded);
                    plaintextCount += encoded;
                }
                else
                {
                    int encoded = Utf8.GetBytes(buffer, index, count, plaintextBuffer, plaintextCount);
                    plaintextCount += encoded;
                }
            }

            FlushIfThresholdReached(nowMs);
        }

        private void EnsureCapacityForRecord(int recordSize, long nowMs)
        {
            if (plaintextCount > 0 && maxBlockAgeMs > 0 && nowMs - blockStartUnixMs >= maxBlockAgeMs)
            {
                FlushBlock(nowMs);
            }

            if (plaintextCount > 0 && plaintextCount + recordSize > blockSizeBytes)
            {
                FlushBlock(nowMs);
            }

            if (plaintextCount == 0)
            {
                blockStartUnixMs = nowMs;
            }

            EnsureCapacity(recordSize);
        }

        private long GetNowUnixTimeMilliseconds()
        {
            long elapsedTicks = Stopwatch.GetTimestamp() - timeBaseStopwatchTicks;
            if (elapsedTicks <= 0)
            {
                return unixTimeBaseMs;
            }

            return unixTimeBaseMs + (long)(elapsedTicks * StopwatchTicksToMilliseconds);
        }

        private void EnsureCapacity(int additionalBytes)
        {
            int required = plaintextCount + additionalBytes;
            if (plaintextBuffer == null)
            {
                plaintextBuffer = new byte[Math.Max(blockSizeBytes, required)];
                return;
            }

            if (required <= plaintextBuffer.Length)
            {
                return;
            }

            int newSize = Math.Max(required, plaintextBuffer.Length * 2);
            byte[] newBuffer = new byte[newSize];
            Buffer.BlockCopy(plaintextBuffer, 0, newBuffer, 0, plaintextCount);
            plaintextBuffer = newBuffer;
        }

        private void WriteRecordHeader(byte recordType, int payloadLength)
        {
            EnsureCapacity(RecordHeaderSize);
            plaintextBuffer[plaintextCount++] = recordType;
            WriteInt32(payloadLength);
        }

        private void WriteInt32(int value)
        {
            plaintextBuffer[plaintextCount++] = (byte)value;
            plaintextBuffer[plaintextCount++] = (byte)(value >> 8);
            plaintextBuffer[plaintextCount++] = (byte)(value >> 16);
            plaintextBuffer[plaintextCount++] = (byte)(value >> 24);
        }

        private void FlushIfThresholdReached(long nowMs)
        {
            if (plaintextCount >= blockSizeBytes)
            {
                FlushBlock(nowMs);
            }
        }

        private void FlushBlock(long nowMs)
        {
            if (plaintextCount == 0)
            {
                return;
            }

            byte[] iv = new byte[IvSize];
            rng.GetBytes(iv);

            byte[] ciphertext;
            aes.Key = aesKey;
            aes.IV = iv;
            using (ICryptoTransform encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
            {
                ciphertext = encryptor.TransformFinalBlock(plaintextBuffer, 0, plaintextCount);
            }

            int cipherLen = ciphertext.Length;
            byte[] header = new byte[FrameHeaderSize];
            int offset = 0;
            WriteUInt16(header, ref offset, FrameMagic);
            header[offset++] = FrameVersion;
            header[offset++] = 0;
            WriteUInt32(header, ref offset, blockSequence);
            WriteInt64(header, ref offset, blockStartUnixMs == 0 ? nowMs : blockStartUnixMs);
            WriteInt32(header, ref offset, plaintextCount);
            WriteInt32(header, ref offset, cipherLen);
            Buffer.BlockCopy(iv, 0, header, offset, IvSize);

            byte[] frameHmac = ComputeHmac(header, ciphertext);
            stream.Write(header, 0, header.Length);
            stream.Write(ciphertext, 0, cipherLen);
            stream.Write(frameHmac, 0, frameHmac.Length);

            plaintextCount = 0;
            blockStartUnixMs = 0;
            blockSequence++;
        }

        private byte[] ComputeHmac(byte[] header, byte[] ciphertext)
        {
            hmac.Initialize();
            hmac.TransformBlock(header, 0, header.Length, null, 0);
            hmac.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
            return hmac.Hash ?? Array.Empty<byte>();
        }

        private static void WriteUInt16(byte[] buffer, ref int offset, ushort value)
        {
            buffer[offset++] = (byte)value;
            buffer[offset++] = (byte)(value >> 8);
        }

        private static void WriteUInt32(byte[] buffer, ref int offset, uint value)
        {
            buffer[offset++] = (byte)value;
            buffer[offset++] = (byte)(value >> 8);
            buffer[offset++] = (byte)(value >> 16);
            buffer[offset++] = (byte)(value >> 24);
        }

        private static void WriteInt32(byte[] buffer, ref int offset, int value)
        {
            buffer[offset++] = (byte)value;
            buffer[offset++] = (byte)(value >> 8);
            buffer[offset++] = (byte)(value >> 16);
            buffer[offset++] = (byte)(value >> 24);
        }

        private static void WriteInt64(byte[] buffer, ref int offset, long value)
        {
            buffer[offset++] = (byte)value;
            buffer[offset++] = (byte)(value >> 8);
            buffer[offset++] = (byte)(value >> 16);
            buffer[offset++] = (byte)(value >> 24);
            buffer[offset++] = (byte)(value >> 32);
            buffer[offset++] = (byte)(value >> 40);
            buffer[offset++] = (byte)(value >> 48);
            buffer[offset++] = (byte)(value >> 56);
        }
    }
}
