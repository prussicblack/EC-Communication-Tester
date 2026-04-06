using System;
using Microsoft.Extensions.Logging;



namespace SOEM_FrontEnd.Util.Logging
{
    /// <summary>
    /// 주요 구현점.
    /// OPLogger: Microsoft.Extensions.Logging.ILogger 기반 + 내부 Serilog 파일 기록 + UI sink.
    /// - 별도 worker thread (bounded queue)
    /// - threadSafe, ConcurrentQueue
    /// - 폴더 총 용량 제한(초과 시 오래된 파일 삭제)
    /// - 파일 용량 제한
    /// - 파일 잠김/공유위반/IO 예외 시 failover(새 파일로 전환)
    /// - 예외가 나도 프로그램이 죽지 않음
    /// </summary>


    public static class OPLogger
    {
        private static readonly object _lock = new object();

        private static OPLoggerOptions _options;
        private static OPLogWorker _worker;
        private static OPLoggerProvider _provider;
        private static ILoggerFactory _factory;

        public static bool IsConfigured
        {
            get { lock (_lock) { return _factory != null; } }
        }

        public static void Configure(OPLoggerOptions options, IOPLogUiSink uiSink = null)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            lock (_lock)
            {
                Shutdown_NoLock();

                _options = options;

                _worker = new OPLogWorker(_options, uiSink);
                _provider = new OPLoggerProvider(_worker, _options);

                _factory = LoggerFactory.Create(builder =>
                {
                    builder.ClearProviders();
                    builder.AddProvider(_provider);
                    builder.SetMinimumLevel(_options.MinimumLevel);
                });
            }
        }

        public static void SetUiSink(IOPLogUiSink uiSink)
        {
            lock (_lock)
            {
                if (_worker == null) return;
                _worker.SetUiSink(uiSink);
            }
        }

        public static ILogger CreateLogger(string categoryName)
        {
            lock (_lock)
            {
                if (_factory == null) throw new InvalidOperationException("OPLogger is not configured. Call OPLogger.Configure(...) first.");
                return _factory.CreateLogger(categoryName);
            }
        }

        public static ILogger<T> CreateLogger<T>()
        {
            lock (_lock)
            {
                if (_factory == null) throw new InvalidOperationException("OPLogger is not configured. Call OPLogger.Configure(...) first.");
                return _factory.CreateLogger<T>();
            }
        }

        public static OPLogStats GetStats()
        {
            lock (_lock)
            {
                if (_worker == null) return OPLogStats.Empty;
                return _worker.GetStats();
            }
        }

        public static void Shutdown()
        {
            lock (_lock)
            {
                Shutdown_NoLock();
            }
        }

        private static void Shutdown_NoLock()
        {
            try { if (_factory != null) _factory.Dispose(); } catch { }
            _factory = null;

            try { if (_provider != null) _provider.Dispose(); } catch { }
            _provider = null;

            try { if (_worker != null) _worker.Dispose(); } catch { }
            _worker = null;

            _options = null;
        }
    }
}