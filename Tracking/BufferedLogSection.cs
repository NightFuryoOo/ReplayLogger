using System;
using System.Collections.Generic;
using System.IO;

namespace ReplayLogger
{
    internal sealed class BufferedLogSection
    {
        private readonly List<string> pending = new();
        private readonly int flushThreshold;
        private readonly string tempPath;

        internal BufferedLogSection(string tempPath, int flushThreshold)
        {
            this.tempPath = tempPath;
            this.flushThreshold = Math.Max(1, flushThreshold);
        }

        internal void Add(string line)
        {
            if (line == null)
            {
                return;
            }

            pending.Add(line);
            if (pending.Count >= flushThreshold)
            {
                Flush();
            }
        }

        internal void AddRange(IEnumerable<string> lines)
        {
            if (lines == null)
            {
                return;
            }

            foreach (string line in lines)
            {
                Add(line);
            }
        }

        internal void Flush()
        {
            if (pending.Count == 0 || string.IsNullOrEmpty(tempPath))
            {
                return;
            }

            try
            {
                string dir = Path.GetDirectoryName(tempPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                using StreamWriter writer = new(tempPath, append: true);
                foreach (string line in pending)
                {
                    string encrypted = KeyloggerLogEncryption.EncryptLog(line);
                    if (!string.IsNullOrEmpty(encrypted))
                    {
                        writer.WriteLine(encrypted);
                    }
                }
                writer.Flush();
                pending.Clear();
            }
            catch
            {
                
            }
        }

        internal void WriteEncryptedLines(StreamWriter writer)
        {
            if (writer == null)
            {
                return;
            }

            Flush();

            if (string.IsNullOrEmpty(tempPath) || !File.Exists(tempPath))
            {
                return;
            }

            try
            {
                using StreamReader reader = new(tempPath);
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    writer.WriteLine(line);
                }
            }
            catch
            {
                
            }
        }

        internal void Clear()
        {
            pending.Clear();

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
    }
}
