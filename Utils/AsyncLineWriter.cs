using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

namespace ReplayLogger
{
    internal sealed class AsyncLineWriter : IDisposable
    {
        private readonly BlockingCollection<WriteItem> queue;
        private readonly StreamWriter writer;
        private readonly Thread worker;
        private readonly Func<string, string> transform;
        private int droppedCount;
        private bool disposed;

        internal int DroppedCount => droppedCount;

        internal AsyncLineWriter(string path, bool append, Func<string, string> transform, int capacity)
        {
            this.transform = transform;

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            FileStream stream = new(path, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read);
            writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 64 * 1024);

            queue = new BlockingCollection<WriteItem>(Math.Max(1, capacity));
            worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "ReplayLogger.AsyncLineWriter"
            };
            worker.Start();
        }

        internal void EnqueueLine(string text, bool raw)
        {
            Enqueue(new WriteItem(text ?? string.Empty, newLine: true, raw));
        }

        internal void Enqueue(string text, bool raw)
        {
            Enqueue(new WriteItem(text ?? string.Empty, newLine: false, raw));
        }

        private void Enqueue(WriteItem item)
        {
            if (disposed || queue.IsAddingCompleted)
            {
                return;
            }

            if (!queue.TryAdd(item))
            {
                Interlocked.Increment(ref droppedCount);
            }
        }

        private void WorkerLoop()
        {
            foreach (WriteItem item in queue.GetConsumingEnumerable())
            {
                string output = item.Text ?? string.Empty;
                if (!item.Raw && transform != null)
                {
                    output = transform(output);
                }

                if (output == null)
                {
                    output = string.Empty;
                }

                if (item.NewLine)
                {
                    writer.WriteLine(output);
                }
                else
                {
                    writer.Write(output);
                }
            }

            writer.Flush();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            queue.CompleteAdding();

            try
            {
                worker.Join();
            }
            catch
            {
                
            }

            try
            {
                writer.Flush();
            }
            catch
            {
                
            }

            writer.Dispose();
            queue.Dispose();
        }

        private readonly struct WriteItem
        {
            public readonly string Text;
            public readonly bool NewLine;
            public readonly bool Raw;

            public WriteItem(string text, bool newLine, bool raw)
            {
                Text = text;
                NewLine = newLine;
                Raw = raw;
            }
        }
    }
}
