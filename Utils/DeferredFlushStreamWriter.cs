using System.IO;

namespace ReplayLogger
{
    internal sealed class DeferredFlushStreamWriter : StreamWriter
    {
        private bool allowFlush;

        internal DeferredFlushStreamWriter(string path, bool append)
            : base(path, append)
        {
        }

        public override void Flush()
        {
            if (!allowFlush)
            {
                return;
            }

            base.Flush();
        }

        protected override void Dispose(bool disposing)
        {
            allowFlush = true;
            base.Dispose(disposing);
        }
    }
}
