using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace SOEM_FrontEnd.Util.Logging
{
    internal readonly struct LogEnvelope
    {
        public LogEnvelope(DateTime utc, DateTime local, LogLevel level, string category, EventId eventId, string message, Exception exception, IReadOnlyDictionary<string, object> props)
        {
            Utc = utc;
            Local = local;
            Level = level;
            Category = category ?? "";
            EventId = eventId;
            Message = message ?? "";
            Exception = exception;
            Props = props;
        }

        public DateTime Utc { get; }
        public DateTime Local { get; }
        public LogLevel Level { get; }
        public string Category { get; }
        public EventId EventId { get; }
        public string Message { get; }
        public Exception Exception { get; }
        public IReadOnlyDictionary<string, object> Props { get; }
    }

    internal sealed class OPLogWorker : IDisposable
    {
        private readonly OPLoggerOptions _opt;

        private readonly BlockingCollection<LogEnvelope> _queue;
        private readonly Thread _thread;

        private readonly object _uiLock = new object();
        private IOpLogUiSink _uiSink;

        private Logger _fileLogger;
        private string _currentFilePath = "";
        private volatile bool _fileEnabled = true;

        private int _consecutiveFileFailures;
        private int _failoverSeq;

        // stats (ticks + Interlocked)
        private long _enqueued;
        private long _written;
        private long _dropped;
        private int _maxDepth;
        private long _lastEnqueueUtcTicks;
        private long _lastWriteUtcTicks;

        private long _nextTrimUtcTicks;
        private long _nextSizeCheckUtcTicks;

        public OPLogWorker(OPLoggerOptions opt, IOpLogUiSink uiSink)
        {
            if (opt == null) throw new ArgumentNullException(nameof(opt));
            _opt = opt;

            if (string.IsNullOrWhiteSpace(_opt.LogFolder))
                _opt.LogFolder = Path.Combine(AppContext.BaseDirectory, "Logs");

            Directory.CreateDirectory(_opt.LogFolder);

            _uiSink = uiSink ?? NullUiSink.Instance;

            if (_opt.QueueCapacity <= 0) _opt.QueueCapacity = 1024;

            _queue = new BlockingCollection<LogEnvelope>(new ConcurrentQueue<LogEnvelope>(), _opt.QueueCapacity);

            _currentFilePath = BuildNewLogFilePath(_opt.FileNamePrefix);
            _fileEnabled = TryCreateFileLogger(_currentFilePath);

            _thread = new Thread(ThreadMain);
            _thread.IsBackground = true;
            _thread.Name = "OPLogWorker";
            _thread.Priority = ThreadPriority.AboveNormal;
            _thread.Start();
        }

        public void SetUiSink(IOpLogUiSink uiSink)
        {
            lock (_uiLock)
            {
                _uiSink = uiSink ?? NullUiSink.Instance;
            }
        }

        public void Enqueue(in LogEnvelope e)
        {
            try
            {
                Interlocked.Exchange(ref _lastEnqueueUtcTicks, DateTime.UtcNow.Ticks);

                bool added;
                if (_opt.QueueMode == OPLogQueueMode.DropNewest)
                {
                    added = _queue.TryAdd(e);
                    if (!added)
                    {
                        Interlocked.Increment(ref _dropped);
                        return;
                    }
                }
                else
                {
                    _queue.Add(e); // Block
                }

                Interlocked.Increment(ref _enqueued);

                int d = _queue.Count;
                if (d > _maxDepth) _maxDepth = d;
            }
            catch
            {
                Interlocked.Increment(ref _dropped);
            }
        }

        private void ThreadMain()
        {
            try
            {
                foreach (var item in _queue.GetConsumingEnumerable())
                {
                    ProcessOne(item);
                }
            }
            catch
            {
                // 워커가 죽으면 안 되므로 삼킴
            }
        }

        private void ProcessOne(in LogEnvelope e)
        {
            // UI
            try
            {
                IOpLogUiSink ui;
                lock (_uiLock) { ui = _uiSink; }
                ui.Enqueue(FormatForUi(e));
            }
            catch { }

            // File
            if (_fileEnabled)
            {
                TryWriteToFile(e);
            }

            Interlocked.Exchange(ref _lastWriteUtcTicks, DateTime.UtcNow.Ticks);
            Interlocked.Increment(ref _written);

            MaybeTrimFolder();
            MaybeRotateOnSize();
        }

        private string FormatForUi(in LogEnvelope e)
        {
            string msg = e.Message ?? "";
            if (_opt.MaxMessageChars > 0 && msg.Length > _opt.MaxMessageChars)
                msg = msg.Substring(0, _opt.MaxMessageChars) + " …(truncated)";
            return string.Format(CultureInfo.InvariantCulture, "{0:HH:mm:ss.fff} [{1}] {2}", e.Local, e.Level, msg);
        }

        private void TryWriteToFile(in LogEnvelope e)
        {
            string msg = e.Message ?? "";
            if (_opt.MaxMessageChars > 0 && msg.Length > _opt.MaxMessageChars)
                msg = msg.Substring(0, _opt.MaxMessageChars) + " …(truncated)";

            Exception ex = e.Exception;
            if (ex != null && _opt.MaxExceptionChars > 0)
            {
                string exText;
                try { exText = ex.ToString(); } catch { exText = "<exception>"; }
                if (exText.Length > _opt.MaxExceptionChars)
                {
                    exText = exText.Substring(0, _opt.MaxExceptionChars) + " …(truncated)";
                    msg = msg + Environment.NewLine + exText;
                    ex = null;
                }
            }

            var logger = _fileLogger;
            if (logger == null) return;

            try
            {
                var level = MapLevel(e.Level);

                Serilog.ILogger l = logger.ForContext("Category", e.Category);
                if (e.EventId.Id != 0 || !string.IsNullOrWhiteSpace(e.EventId.Name))
                    l = l.ForContext("EventId", e.EventId.Id);

                if (e.Props != null)
                {
                    foreach (var kv in e.Props)
                    {
                        if (kv.Key == null) continue;
                        l = l.ForContext(kv.Key, kv.Value, destructureObjects: true);
                    }
                }

                // 중괄호/템플릿 해석 이슈 방지
                l.Write(level, ex, "{Text}", msg);

                _consecutiveFileFailures = 0;
            }
            catch (Exception ioEx)
            {
                _consecutiveFileFailures++;

                if (_opt.EnableFailover)
                {
                    if (TryFailover(ioEx))
                    {
                        try
                        {
                            var level = MapLevel(e.Level);
                            _fileLogger.Write(level, ex, "{Text}", msg);
                            _consecutiveFileFailures = 0;
                            return;
                        }
                        catch
                        {
                            _consecutiveFileFailures++;
                        }
                    }
                }

                if (_opt.DisableFileAfterConsecutiveFailures > 0 &&
                    _consecutiveFileFailures >= _opt.DisableFileAfterConsecutiveFailures)
                {
                    _fileEnabled = false;
                    SafeUi("[LOG] File logging disabled (too many IO failures): " + ioEx.Message);
                }
            }
        }

        private void SafeUi(string line)
        {
            try
            {
                IOpLogUiSink ui;
                lock (_uiLock) { ui = _uiSink; }
                ui.Enqueue(line);
            }
            catch { }
        }

        private bool TryFailover(Exception reason)
        {
            try
            {
                _failoverSeq++;
                string prefix = _opt.FileNamePrefix + "fail_";
                string newPath = BuildNewLogFilePath(prefix, _failoverSeq);

                Logger old = _fileLogger;

                if (TryCreateFileLogger(newPath))
                {
                    try { if (old != null) old.Dispose(); } catch { }
                    SafeUi("[LOG] Failover -> " + Path.GetFileName(newPath));
                    return true;
                }
            }
            catch { }

            return false;
        }

        private bool TryCreateFileLogger(string path)
        {
            try
            {
                Directory.CreateDirectory(_opt.LogFolder);

                var min = MapLevel(_opt.MinimumLevel);

                var cfg = new LoggerConfiguration()
                    .MinimumLevel.Is(min)
                    .WriteTo.File(
                        path: path,
                        shared: _opt.SharedFileSink,
                        flushToDiskInterval: _opt.FlushToDiskInterval,
                        outputTemplate: _opt.OutputTemplate);

                var created = cfg.CreateLogger();

                _fileLogger = (Logger)created;
                _currentFilePath = path;
                _fileEnabled = true;
                return true;
            }
            catch
            {
                _fileLogger = null;
                _fileEnabled = false;
                return false;
            }
        }

        private string BuildNewLogFilePath(string prefix)
        {
            return BuildNewLogFilePath(prefix, 0);
        }

        private string BuildNewLogFilePath(string prefix, int seq)
        {
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);

            string ext = _opt.FileExtension;
            if (string.IsNullOrWhiteSpace(ext)) ext = ".log";
            if (!ext.StartsWith(".")) ext = "." + ext;

            string name;
            if (seq > 0) name = prefix + stamp + "_" + seq.ToString("D4", CultureInfo.InvariantCulture) + ext;
            else name = prefix + stamp + ext;

            return Path.Combine(_opt.LogFolder, name);
        }

        private void MaybeTrimFolder()
        {
            if (_opt.MaxFolderBytes <= 0) return;
            if (_opt.FolderTrimInterval <= TimeSpan.Zero) return;

            long now = DateTime.UtcNow.Ticks;
            long next = Interlocked.Read(ref _nextTrimUtcTicks);
            if (next != 0 && now < next) return;

            Interlocked.Exchange(ref _nextTrimUtcTicks, now + _opt.FolderTrimInterval.Ticks);

            try
            {
                TrimFolderToLimit(_opt.LogFolder, _opt.MaxFolderBytes, _currentFilePath, _opt.FileExtension);
            }
            catch { }
        }

        private void MaybeRotateOnSize()
        {
            if (_opt.MaxActiveFileBytes <= 0) return;
            if (_opt.ActiveFileSizeCheckInterval <= TimeSpan.Zero) return;
            if (string.IsNullOrWhiteSpace(_currentFilePath)) return;

            long now = DateTime.UtcNow.Ticks;
            long next = Interlocked.Read(ref _nextSizeCheckUtcTicks);
            if (next != 0 && now < next) return;

            Interlocked.Exchange(ref _nextSizeCheckUtcTicks, now + _opt.ActiveFileSizeCheckInterval.Ticks);

            try
            {
                var fi = new FileInfo(_currentFilePath);
                if (!fi.Exists) return;
                if (fi.Length <= _opt.MaxActiveFileBytes) return;

                Logger old = _fileLogger;
                string newPath = BuildNewLogFilePath(_opt.FileNamePrefix);

                if (TryCreateFileLogger(newPath))
                {
                    try { if (old != null) old.Dispose(); } catch { }
                    SafeUi("[LOG] Rotate(size) -> " + Path.GetFileName(newPath));
                }
            }
            catch { }
        }

        private static void TrimFolderToLimit(string folder, long maxBytes, string currentFile, string fileExtension)
        {
            if (!Directory.Exists(folder)) return;

            if (string.IsNullOrWhiteSpace(fileExtension)) fileExtension = ".log";
            if (!fileExtension.StartsWith(".")) fileExtension = "." + fileExtension;

            var files = new DirectoryInfo(folder)
                .GetFiles("*" + fileExtension, SearchOption.TopDirectoryOnly)
                .OrderBy(f => f.LastWriteTimeUtc)
                .ToList();

            long total = 0;
            for (int i = 0; i < files.Count; i++) total += files[i].Length;
            if (total <= maxBytes) return;

            for (int i = 0; i < files.Count && total > maxBytes; i++)
            {
                var f = files[i];
                if (string.Equals(f.FullName, currentFile, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    long len = f.Length;
                    f.Delete();
                    total -= len;
                }
                catch
                {
                    // 삭제 실패는 무시
                }
            }
        }

        private static LogEventLevel MapLevel(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Trace: return LogEventLevel.Verbose;
                case LogLevel.Debug: return LogEventLevel.Debug;
                case LogLevel.Information: return LogEventLevel.Information;
                case LogLevel.Warning: return LogEventLevel.Warning;
                case LogLevel.Error: return LogEventLevel.Error;
                case LogLevel.Critical: return LogEventLevel.Fatal;
                case LogLevel.None: return LogEventLevel.Fatal;
                default: return LogEventLevel.Information;
            }
        }

        public OPLogStats GetStats()
        {
            int depth = 0;
            try { depth = _queue.Count; } catch { }

            bool fileOk = _fileEnabled && _fileLogger != null;

            return new OPLogStats(
                configured: true,
                fileEnabled: fileOk,
                currentFile: _currentFilePath,
                enqueued: Interlocked.Read(ref _enqueued),
                written: Interlocked.Read(ref _written),
                dropped: Interlocked.Read(ref _dropped),
                queueDepth: depth,
                maxQueueDepth: _maxDepth,
                lastEnqueueUtcTicks: Interlocked.Read(ref _lastEnqueueUtcTicks),
                lastWriteUtcTicks: Interlocked.Read(ref _lastWriteUtcTicks),
                consecutiveFileFailures: _consecutiveFileFailures);
        }

        public void Dispose()
        {
            try { _queue.CompleteAdding(); } catch { }

            try
            {
                if (_thread != null && _thread.IsAlive)
                    _thread.Join(2000);
            }
            catch { }

            try { if (_fileLogger != null) _fileLogger.Dispose(); } catch { }
            _fileLogger = null;

            try { _queue.Dispose(); } catch { }
        }
    }


}
