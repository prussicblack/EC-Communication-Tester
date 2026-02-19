using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;


namespace SOEM_FrontEnd.Util.Logging
{
    public enum OPLogQueueMode
    {
        Block,
        DropNewest
    }

    public sealed class OPLoggerOptions
    {
        public string LogFolder { get; set; } = "";

        public string FileNamePrefix { get; set; } = "soem_";
        public string FileExtension { get; set; } = ".log";

        public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

        public long MaxFolderBytes { get; set; } = ByteSize.Parse("512MB");
        public long MaxActiveFileBytes { get; set; } = ByteSize.Parse("64MB");

        public TimeSpan FolderTrimInterval { get; set; } = TimeSpan.FromSeconds(5);
        public TimeSpan ActiveFileSizeCheckInterval { get; set; } = TimeSpan.FromSeconds(1);

        public bool SharedFileSink { get; set; } = false;
        public TimeSpan? FlushToDiskInterval { get; set; } = TimeSpan.FromSeconds(1);

        public string OutputTemplate { get; set; } =
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] ({Category}) {Message:lj}{NewLine}{Exception}";

        public bool EnableFailover { get; set; } = true;
        public int DisableFileAfterConsecutiveFailures { get; set; } = 10;

        public int QueueCapacity { get; set; } = 8192;
        public OPLogQueueMode QueueMode { get; set; } = OPLogQueueMode.Block;

        public int MaxMessageChars { get; set; } = 4000;
        public int MaxExceptionChars { get; set; } = 8000;

        public bool IncludeScopes { get; set; } = false;
    }
}
