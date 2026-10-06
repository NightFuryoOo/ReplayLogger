using System.IO;

namespace ReplayLogger
{
    internal interface ITrackerLifecycle
    {
        void Reset();
        void Write(StreamWriter writer);
    }
}
