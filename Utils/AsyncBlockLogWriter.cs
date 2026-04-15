using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace ReplayLogger
{
    internal sealed class AsyncBlockLogWriter : StreamWriter, IEncryptionSessionProvider
    {
        private readonly BlockingCollection<WriteItem> queue;
        private readonly Thread worker;
        private readonly KeyloggerLogEncryption.Session session;
        private readonly BlockLogWriter innerWriter;
        private readonly ConcurrentBag<BatchLease> batchLeasePool = new();
        private readonly ManualResetEventSlim processedEvent = new(false);
        private readonly int queueBacklogWarnThreshold;
        private volatile bool disposed;
        private int enqueueSequence;
        private int processedSequence;
        private int workerFaulted;
        private Exception workerFaultException;
        private string workerFaultContext;
        private int droppedWriteCount;
        private int writeAfterFaultReported;
        private long lastBacklogWarningUnixTime;
        private const int MinBatchLeaseCapacity = 64;
        private const int MaxRetainedBatchCapacity = 4096;
        private const int WorkerBatchDrainLimit = 64;
        private const int EnqueuePollTimeoutMs = 0;
        private const int FlushEnqueueTimeoutMs = 1000;
        private const int FlushWaitTimeoutMs = 10000;
        private const int DisposeJoinTimeoutMs = 5000;
        private const int WaitPollIntervalMs = 50;
        private const int DropReportInterval = 256;
        private const int BacklogWarningIntervalMs = 5000;

        KeyloggerLogEncryption.Session IEncryptionSessionProvider.EncryptionSession => session;

        internal AsyncBlockLogWriter(string path, string sessionKeyBlob, KeyloggerLogEncryption.Session session, int blockSizeBytes, int maxBlockAgeMs, int queueCapacity)
            : base(Stream.Null)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            innerWriter = new BlockLogWriter(path, sessionKeyBlob, this.session, blockSizeBytes, maxBlockAgeMs);
            int boundedCapacity = Math.Max(256, queueCapacity);
            queue = new BlockingCollection<WriteItem>(new ConcurrentQueue<WriteItem>(), boundedCapacity);
            queueBacklogWarnThreshold = Math.Max(512, (boundedCapacity * 3) / 4);
            worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "ReplayLogger.AsyncBlockLogWriter"
            };
            worker.Start();
        }

        public override void WriteLine(string value)
        {
            Enqueue(WriteItem.ForLine(value ?? string.Empty, raw: false));
        }

        public override void WriteLine()
        {
            Enqueue(WriteItem.ForLine(string.Empty, raw: false));
        }

        public override void Write(string value)
        {
            Enqueue(WriteItem.ForText(value ?? string.Empty, raw: false));
        }

        public override void Write(char value)
        {
            Enqueue(WriteItem.ForText(value.ToString(), raw: false));
        }

        public override void Write(char[] buffer, int index, int count)
        {
            if (buffer == null || count <= 0)
            {
                return;
            }

            Enqueue(WriteItem.ForText(new string(buffer, index, count), raw: false));
        }

        internal void WriteRawLine(string value)
        {
            Enqueue(WriteItem.ForLine(value ?? string.Empty, raw: true));
        }

        internal void WriteRaw(string value)
        {
            Enqueue(WriteItem.ForText(value ?? string.Empty, raw: true));
        }

        internal void WriteLines(IReadOnlyList<string> lines)
        {
            if (lines == null || lines.Count == 0)
            {
                return;
            }

            BatchLease batchLease = RentBatchLease(lines.Count);
            List<string> copy = batchLease.Lines;
            for (int i = 0; i < lines.Count; i++)
            {
                copy.Add(lines[i] ?? string.Empty);
            }

            Enqueue(WriteItem.ForBatch(batchLease));
        }

        public override void Flush()
        {
            if (disposed)
            {
                return;
            }

            if (!TryEnqueue(WriteItem.FlushMarker, FlushEnqueueTimeoutMs, out int sequence))
            {
                ThrowWorkerFaultIfPresent("flush enqueue");
                throw new IOException("ReplayLogger: failed to enqueue async log flush marker.");
            }

            if (!WaitUntilProcessed(sequence, FlushWaitTimeoutMs))
            {
                ThrowWorkerFaultIfPresent("flush wait");
                throw new IOException("ReplayLogger: timed out waiting for async log flush.");
            }

            ThrowWorkerFaultIfPresent("flush complete");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposed)
            {
                return;
            }

            if (disposing)
            {
                if (TryEnqueue(WriteItem.FlushMarker, FlushEnqueueTimeoutMs, out int flushSequence))
                {
                    WaitUntilProcessed(flushSequence, FlushWaitTimeoutMs);
                }

                queue.CompleteAdding();
                try
                {
                    if (!worker.Join(DisposeJoinTimeoutMs))
                    {
                        global::ReplayLogger.InternalDiagnostics.Warn("ReplayLogger: async log writer worker join timed out during dispose.");
                    }
                }
                catch (Exception ex)
                {
                    global::ReplayLogger.InternalDiagnostics.Warn($"ReplayLogger: failed to join async log writer worker: {ex.Message}");
                }

                queue.Dispose();
                processedEvent.Dispose();
            }

            disposed = true;
            base.Dispose(disposing);
        }

        private void Enqueue(in WriteItem item)
        {
            TryEnqueue(item, EnqueuePollTimeoutMs, out _);
        }

        private bool TryEnqueue(in WriteItem item, int timeoutMs, out int sequence)
        {
            sequence = Volatile.Read(ref enqueueSequence);
            if (disposed || queue.IsAddingCompleted)
            {
                ReturnBatchLeaseIfNeeded(item);
                return false;
            }

            if (Volatile.Read(ref workerFaulted) != 0)
            {
                ReturnBatchLeaseIfNeeded(item);
                ReportDroppedWrite("worker faulted");
                ReportWriteAfterFault();
                return false;
            }

            sequence = Interlocked.Increment(ref enqueueSequence);
            WriteItem queuedItem = item.WithSequence(sequence);
            try
            {
                bool enqueued = timeoutMs <= 0
                    ? queue.TryAdd(queuedItem)
                    : queue.TryAdd(queuedItem, timeoutMs);

                if (!enqueued)
                {
                    ReturnBatchLeaseIfNeeded(queuedItem);
                    ReportDroppedWrite("queue is full");
                    return false;
                }

                ReportBacklogIfNeeded(sequence);
            }
            catch (InvalidOperationException)
            {
                ReturnBatchLeaseIfNeeded(queuedItem);
                ReportDroppedWrite("queue is closed");
                return false;
            }

            return true;
        }

        private void ReportBacklogIfNeeded(int sequence)
        {
            int threshold = queueBacklogWarnThreshold;
            if (threshold <= 0)
            {
                return;
            }

            int backlog = sequence - Volatile.Read(ref processedSequence);
            if (backlog < threshold)
            {
                return;
            }

            long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            long previous = Interlocked.Read(ref lastBacklogWarningUnixTime);
            if (now - previous < BacklogWarningIntervalMs)
            {
                return;
            }

            Interlocked.Exchange(ref lastBacklogWarningUnixTime, now);
            global::ReplayLogger.InternalDiagnostics.Warn($"ReplayLogger: async log writer backlog is high ({backlog} pending records).");
        }

        private bool WaitUntilProcessed(int sequence, int timeoutMs)
        {
            int waitedMs = 0;
            while (Volatile.Read(ref processedSequence) < sequence)
            {
                if (Volatile.Read(ref workerFaulted) != 0)
                {
                    return false;
                }

                if (timeoutMs >= 0 && waitedMs >= timeoutMs)
                {
                    return false;
                }

                processedEvent.Wait(WaitPollIntervalMs);
                processedEvent.Reset();
                waitedMs += WaitPollIntervalMs;
            }

            return true;
        }

        private void WorkerLoop()
        {
            try
            {
                while (true)
                {
                    WriteItem item;
                    try
                    {
                        if (!queue.TryTake(out item, Timeout.Infinite))
                        {
                            if (queue.IsCompleted)
                            {
                                break;
                            }

                            continue;
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        break;
                    }

                    int latestSequence = 0;
                    int processedCount = 0;

                    do
                    {
                        ProcessItem(item);
                        latestSequence = item.Sequence;
                        processedCount++;
                    }
                    while (processedCount < WorkerBatchDrainLimit && queue.TryTake(out item));

                    if (latestSequence > 0)
                    {
                        Volatile.Write(ref processedSequence, latestSequence);
                        processedEvent.Set();
                    }
                }
            }
            catch (Exception ex)
            {
                SetWorkerFault("worker loop", ex);
            }
            finally
            {
                try
                {
                    innerWriter.Flush();
                }
                catch (Exception ex)
                {
                    SetWorkerFault("inner flush", ex);
                }

                try
                {
                    innerWriter.Dispose();
                }
                catch (Exception ex)
                {
                    SetWorkerFault("inner dispose", ex);
                }

                Volatile.Write(ref processedSequence, Volatile.Read(ref enqueueSequence));
                processedEvent.Set();
            }
        }

        private void SetWorkerFault(string context, Exception ex)
        {
            if (Interlocked.CompareExchange(ref workerFaulted, 1, 0) == 0)
            {
                workerFaultException = ex;
                workerFaultContext = string.IsNullOrEmpty(context) ? "unknown" : context;
                string message = ex?.Message ?? "unknown error";
                global::ReplayLogger.InternalDiagnostics.Error($"ReplayLogger: async log writer fault in {workerFaultContext}: {message}");
            }
        }

        private void ThrowWorkerFaultIfPresent(string operation)
        {
            if (Volatile.Read(ref workerFaulted) == 0)
            {
                return;
            }

            string context = string.IsNullOrEmpty(workerFaultContext) ? "unknown" : workerFaultContext;
            string message = workerFaultException?.Message ?? "unknown error";
            throw new IOException($"ReplayLogger: async log writer fault detected during {operation} (source: {context}): {message}", workerFaultException);
        }

        private void ReportDroppedWrite(string reason)
        {
            int dropped = Interlocked.Increment(ref droppedWriteCount);
            if (dropped == 1 || dropped % DropReportInterval == 0)
            {
                global::ReplayLogger.InternalDiagnostics.Warn($"ReplayLogger: async log writer dropped {dropped} write(s) ({reason}).");
            }
        }

        private void ReportWriteAfterFault()
        {
            if (Interlocked.Exchange(ref writeAfterFaultReported, 1) == 0)
            {
                global::ReplayLogger.InternalDiagnostics.Warn("ReplayLogger: async log writer is faulted; subsequent writes are dropped until logger restart.");
            }
        }

        private void ProcessItem(in WriteItem item)
        {
            switch (item.Kind)
            {
                case WriteKind.EncryptedLine:
                    innerWriter.WriteLine(item.Text);
                    break;
                case WriteKind.EncryptedText:
                    innerWriter.Write(item.Text);
                    break;
                case WriteKind.RawLine:
                    innerWriter.WriteRawLine(item.Text);
                    break;
                case WriteKind.RawText:
                    innerWriter.WriteRaw(item.Text);
                    break;
                case WriteKind.EncryptedBatch:
                    BatchLease batchLease = item.Batch;
                    try
                    {
                        innerWriter.WriteLines(batchLease?.Lines);
                    }
                    finally
                    {
                        ReturnBatchLease(batchLease);
                    }
                    break;
                case WriteKind.Flush:
                    innerWriter.Flush();
                    break;
            }
        }

        private BatchLease RentBatchLease(int minCapacity)
        {
            if (!batchLeasePool.TryTake(out BatchLease lease))
            {
                return new BatchLease(Math.Max(MinBatchLeaseCapacity, minCapacity));
            }

            if (minCapacity > 0 && lease.Lines.Capacity < minCapacity)
            {
                lease.Lines.Capacity = minCapacity;
            }

            return lease;
        }

        private void ReturnBatchLease(BatchLease lease)
        {
            if (lease == null)
            {
                return;
            }

            List<string> lines = lease.Lines;
            lines.Clear();
            if (lines.Capacity > MaxRetainedBatchCapacity)
            {
                lines.Capacity = MaxRetainedBatchCapacity;
            }

            batchLeasePool.Add(lease);
        }

        private void ReturnBatchLeaseIfNeeded(in WriteItem item)
        {
            if (item.Kind == WriteKind.EncryptedBatch && item.Batch != null)
            {
                ReturnBatchLease(item.Batch);
            }
        }

        private readonly struct WriteItem
        {
            internal static WriteItem FlushMarker => new(WriteKind.Flush, null, null, 0);

            private WriteItem(WriteKind kind, string text, BatchLease batch, int sequence)
            {
                Kind = kind;
                Text = text;
                Batch = batch;
                Sequence = sequence;
            }

            internal WriteKind Kind { get; }
            internal string Text { get; }
            internal BatchLease Batch { get; }
            internal int Sequence { get; }

            internal static WriteItem ForLine(string text, bool raw)
            {
                return new(raw ? WriteKind.RawLine : WriteKind.EncryptedLine, text, null, 0);
            }

            internal static WriteItem ForText(string text, bool raw)
            {
                return new(raw ? WriteKind.RawText : WriteKind.EncryptedText, text, null, 0);
            }

            internal static WriteItem ForBatch(BatchLease batch)
            {
                return new(WriteKind.EncryptedBatch, null, batch, 0);
            }

            internal WriteItem WithSequence(int sequence)
            {
                return new(Kind, Text, Batch, sequence);
            }
        }

        private sealed class BatchLease
        {
            internal BatchLease(int capacity)
            {
                Lines = new List<string>(Math.Max(MinBatchLeaseCapacity, capacity));
            }

            internal List<string> Lines { get; }
        }

        private enum WriteKind : byte
        {
            EncryptedLine,
            EncryptedText,
            RawLine,
            RawText,
            EncryptedBatch,
            Flush
        }
    }
}



