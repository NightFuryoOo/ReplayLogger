using System;
using System.Collections.Generic;
using System.IO;

namespace ReplayLogger
{
    internal sealed class BufferedLogSection
    {
        private readonly Queue<string> pending = new();
        private readonly int flushThreshold;
        private readonly int flushChunkSize;
        private readonly string tempPath;
        private readonly string tempDirectoryPath;
        private readonly bool useDiskSpill;
        private StreamWriter tempAppendWriter;
        private uint lastFlushTickMs;
        private uint lastDiskFlushTickMs;
        private bool hasPersistedContent;
        private bool tempDirectoryReady;
        private const int MinAutoFlushIntervalMs = 150;
        private const int MinDiskFlushIntervalMs = 500;

        internal bool HasContent
        {
            get
            {
                return pending.Count > 0 || hasPersistedContent;
            }
        }

        internal BufferedLogSection(string tempPath, int flushThreshold)
        {
            this.tempPath = tempPath;
            useDiskSpill = !string.IsNullOrWhiteSpace(tempPath);
            tempDirectoryPath = useDiskSpill ? Path.GetDirectoryName(tempPath) : null;
            this.flushThreshold = Math.Max(1, flushThreshold);
            flushChunkSize = Math.Max(32, this.flushThreshold / 2);
        }

        internal void Add(string line)
        {
            if (line == null)
            {
                return;
            }

            pending.Enqueue(line);
            TryAutoFlush();
        }

        internal void AddRange(IEnumerable<string> lines)
        {
            if (lines == null)
            {
                return;
            }

            bool hasNewLines = false;
            foreach (string line in lines)
            {
                if (line == null)
                {
                    continue;
                }

                pending.Enqueue(line);
                hasNewLines = true;
            }

            if (hasNewLines)
            {
                TryAutoFlush();
            }
        }

        internal void Flush()
        {
            FlushCore(maxLines: int.MaxValue, force: true);
        }

        private void TryAutoFlush()
        {
            if (!useDiskSpill || pending.Count < flushThreshold)
            {
                return;
            }

            uint nowMs = GetTickCountMs();
            if (!HasElapsed(nowMs, lastFlushTickMs, MinAutoFlushIntervalMs))
            {
                return;
            }

            FlushCore(flushChunkSize, force: false);
        }

        private void FlushCore(int maxLines, bool force)
        {
            if (!useDiskSpill || pending.Count == 0 || string.IsNullOrEmpty(tempPath))
            {
                return;
            }

            try
            {
                StreamWriter writer = GetOrCreateTempAppendWriter();
                int written = 0;
                uint nowMs = GetTickCountMs();
                while (pending.Count > 0 && (force || written < maxLines))
                {
                    string line = pending.Dequeue();
                    writer.WriteLine(line ?? string.Empty);
                    written++;
                }

                if (written > 0)
                {
                    hasPersistedContent = true;
                    if (force || HasElapsed(nowMs, lastDiskFlushTickMs, MinDiskFlushIntervalMs))
                    {
                        writer.Flush();
                        lastDiskFlushTickMs = nowMs;
                    }
                }
                lastFlushTickMs = nowMs;
            }
            catch
            {
                CloseTempAppendWriter();
                tempDirectoryReady = false;
            }
        }

        internal void WriteEncryptedLines(StreamWriter writer)
        {
            if (writer == null)
            {
                return;
            }

            if (!useDiskSpill)
            {
                if (pending.Count == 0)
                {
                    return;
                }

                List<string> batch = TempObjectPools.RentStringList(Math.Min(512, Math.Max(32, pending.Count)));
                try
                {
                    foreach (string line in pending)
                    {
                        batch.Add(line ?? string.Empty);
                        if (batch.Count >= 512)
                        {
                            LogWrite.EncryptedLines(writer, batch);
                            batch.Clear();
                        }
                    }

                    if (batch.Count > 0)
                    {
                        LogWrite.EncryptedLines(writer, batch);
                    }
                }
                finally
                {
                    TempObjectPools.ReturnStringList(batch);
                }

                return;
            }

            Flush();
            // Ensure read access is not blocked by our own append handle.
            CloseTempAppendWriter();

            if (!hasPersistedContent || string.IsNullOrEmpty(tempPath))
            {
                return;
            }

            try
            {
                using StreamReader reader = new(tempPath);
                List<string> batch = TempObjectPools.RentStringList(512);
                try
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        batch.Add(line);
                        if (batch.Count >= 512)
                        {
                            LogWrite.EncryptedLines(writer, batch);
                            batch.Clear();
                        }
                    }

                    if (batch.Count > 0)
                    {
                        LogWrite.EncryptedLines(writer, batch);
                    }
                }
                finally
                {
                    TempObjectPools.ReturnStringList(batch);
                }
            }
            catch
            {
                hasPersistedContent = false;
                CloseTempAppendWriter();
            }
        }

        internal void Clear()
        {
            pending.Clear();
            hasPersistedContent = false;
            lastFlushTickMs = 0;
            lastDiskFlushTickMs = 0;
            CloseTempAppendWriter();

            if (string.IsNullOrEmpty(tempPath))
            {
                return;
            }

            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                
            }
        }

        private void EnsureTempDirectory()
        {
            if (tempDirectoryReady || string.IsNullOrEmpty(tempDirectoryPath))
            {
                return;
            }

            Directory.CreateDirectory(tempDirectoryPath);
            tempDirectoryReady = true;
        }

        private StreamWriter GetOrCreateTempAppendWriter()
        {
            if (tempAppendWriter != null)
            {
                return tempAppendWriter;
            }

            EnsureTempDirectory();
            tempAppendWriter = new StreamWriter(tempPath, append: true);
            return tempAppendWriter;
        }

        private void CloseTempAppendWriter()
        {
            StreamWriter writerToDispose = tempAppendWriter;
            tempAppendWriter = null;
            if (writerToDispose == null)
            {
                return;
            }

            try
            {
                writerToDispose.Flush();
            }
            catch
            {
            }

            try
            {
                writerToDispose.Dispose();
            }
            catch
            {
            }
        }

        private static uint GetTickCountMs()
        {
            return unchecked((uint)Environment.TickCount);
        }

        private static bool HasElapsed(uint nowMs, uint previousMs, int intervalMs)
        {
            return unchecked(nowMs - previousMs) >= (uint)Math.Max(0, intervalMs);
        }
    }
}
