namespace ReplayLogger
{
    internal sealed class RebindFrameGuard
    {
        private int completedFrame = int.MinValue;

        internal void Complete(int frame)
        {
            completedFrame = frame;
        }

        internal bool IsSuppressed(int frame)
        {
            return frame == completedFrame;
        }
    }
}
