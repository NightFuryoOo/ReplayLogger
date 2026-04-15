using System.IO;

namespace ReplayLogger
{
    internal sealed class AsyncLogWriter : StreamWriter, IEncryptionSessionProvider
    {
        private readonly AsyncLineWriter writer;
        private readonly KeyloggerLogEncryption.Session session;
        private bool disposed;

        internal int DroppedCount => writer?.DroppedCount ?? 0;
        KeyloggerLogEncryption.Session IEncryptionSessionProvider.EncryptionSession => session;

        internal AsyncLogWriter(string path, bool append, int capacity, KeyloggerLogEncryption.Session session)
            : base(Stream.Null)
        {
            this.session = session ?? throw new System.ArgumentNullException(nameof(session));
            writer = new AsyncLineWriter(path, append, this.session.EncryptLog, capacity);
        }

        public override void WriteLine(string value)
        {
            if (disposed)
            {
                return;
            }

            writer.EnqueueLine(value, raw: false);
        }

        public override void WriteLine()
        {
            if (disposed)
            {
                return;
            }

            writer.EnqueueLine(string.Empty, raw: false);
        }

        public override void Write(string value)
        {
            if (disposed)
            {
                return;
            }

            writer.Enqueue(value, raw: false);
        }

        public override void Write(char value)
        {
            if (disposed)
            {
                return;
            }

            writer.Enqueue(value.ToString(), raw: false);
        }

        public override void Write(char[] buffer, int index, int count)
        {
            if (disposed || buffer == null || count <= 0)
            {
                return;
            }

            writer.Enqueue(new string(buffer, index, count), raw: false);
        }

        internal void WriteRawLine(string value)
        {
            if (disposed)
            {
                return;
            }

            writer.EnqueueLine(value, raw: true);
        }

        internal void WriteRaw(string value)
        {
            if (disposed)
            {
                return;
            }

            writer.Enqueue(value, raw: true);
        }

        public override void Flush()
        {
            
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
                writer.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
