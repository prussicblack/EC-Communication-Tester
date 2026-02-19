using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SOEM_FrontEnd.Util.Logging
{
    public readonly struct OPLogStats
    {
        public static readonly OPLogStats Empty = new OPLogStats(
            configured: false,
            fileEnabled: false,
            currentFile: "",
            enqueued: 0,
            written: 0,
            dropped: 0,
            queueDepth: 0,
            maxQueueDepth: 0,
            lastEnqueueUtcTicks: 0,
            lastWriteUtcTicks: 0,
            consecutiveFileFailures: 0);

        public OPLogStats(
            bool configured,
            bool fileEnabled,
            string currentFile,
            long enqueued,
            long written,
            long dropped,
            int queueDepth,
            int maxQueueDepth,
            long lastEnqueueUtcTicks,
            long lastWriteUtcTicks,
            int consecutiveFileFailures)
        {
            Configured = configured;
            FileEnabled = fileEnabled;
            CurrentFile = currentFile ?? "";
            Enqueued = enqueued;
            Written = written;
            Dropped = dropped;
            QueueDepth = queueDepth;
            MaxQueueDepth = maxQueueDepth;
            LastEnqueueUtcTicks = lastEnqueueUtcTicks;
            LastWriteUtcTicks = lastWriteUtcTicks;
            ConsecutiveFileFailures = consecutiveFileFailures;
        }

        public bool Configured { get; }
        public bool FileEnabled { get; }
        public string CurrentFile { get; }

        public long Enqueued { get; }
        public long Written { get; }
        public long Dropped { get; }

        public int QueueDepth { get; }
        public int MaxQueueDepth { get; }

        public long LastEnqueueUtcTicks { get; }
        public long LastWriteUtcTicks { get; }

        public int ConsecutiveFileFailures { get; }
    }

}
