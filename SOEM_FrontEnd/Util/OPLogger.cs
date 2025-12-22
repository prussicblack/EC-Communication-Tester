using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace SOEM_FrontEnd.Util
{
    //기존 Log4Net을 Serilog로 교체.
    public struct OPLOG
    {
        public static string Root = "OPlog";

        public struct Folder
        {
            public static string Exception = "Exception";
            public static string Log = "Log";
        }

        public struct File
        {
            public static string ExceptionFile = "ExceptionFile";
            public static string Log = "Log";
        }
    }

    public class OPLogger : IDisposable
    {
        private readonly object locker = new object();

        public enum eLogLevel { DEBUG = 1, INFO = 2, WARN = 3, FATAL = 4, CSV = 5 }
        public enum eDatePattern { DAY = 1, HOUR = 2, SIZE = 3 }

        private Logger _logger;
        private string _strName;

        private string _strFilePath, _strFileName, _strMaximumFileSize;
        private int _iMaxSizeRollBackups;
        private eDatePattern _eDatePattern;

        public OPLogger(string name)
        {
            _strName = name;
        }

        public string GetFilePath()
        {
            return _strFilePath;
        }

        public void Dispose()
        {
            lock (locker)
            {
                if (_logger != null)
                {
                    _logger.Dispose();
                    _logger = null;
                }
            }
        }

        public void onInitialize(
            eLogLevel level,
            string file_path,
            string file_name,
            eDatePattern rolling_style,
            string maximum_file_size = "",
            int max_size_roll_backups = 0)
        {
            lock (locker)
            {
                _strFilePath = file_path;
                _strFileName = file_name;
                _eDatePattern = rolling_style;
                _strMaximumFileSize = maximum_file_size;
                _iMaxSizeRollBackups = max_size_roll_backups;

                Directory.CreateDirectory(_strFilePath);

                RebuildLogger(level);
            }
        }

        private void RebuildLogger(eLogLevel level)
        {
            if (_logger != null)
            {
                _logger.Dispose();
                _logger = null;
            }

            LogEventLevel minLevel = ToSerilogLevel(level);

            // Serilog rolling 파일은 "name-.log" 형태로 두면 날짜/시간이 - 위치에 들어갑니다.
            // DAY/HOUR: rollingInterval 사용
            // SIZE: rollOnFileSizeLimit + fileSizeLimitBytes 사용 (rollingInterval은 Infinite로 두는 편이 단순)
            string basePath = Path.Combine(_strFilePath, _strFileName);

            string path;
            RollingInterval rollingInterval = RollingInterval.Infinite;
            bool rollOnFileSizeLimit = false;
            long? fileSizeLimitBytes = null;

            if (_eDatePattern == eDatePattern.DAY)
            {
                path = basePath + "-.log";
                rollingInterval = RollingInterval.Day;
            }
            else if (_eDatePattern == eDatePattern.HOUR)
            {
                path = basePath + "-.log";
                rollingInterval = RollingInterval.Hour;
            }
            else // SIZE
            {
                path = basePath + ".log";
                rollOnFileSizeLimit = true;
                fileSizeLimitBytes = ParseFileSizeBytes(_strMaximumFileSize); // "5MB" 등
            }

            int? retained = null;
            if (_iMaxSizeRollBackups > 0)
                retained = _iMaxSizeRollBackups;

            var cfg = new LoggerConfiguration()
                .MinimumLevel.Is(minLevel)
                .WriteTo.File(
                    path: path,
                    rollingInterval: rollingInterval,
                    rollOnFileSizeLimit: rollOnFileSizeLimit,
                    fileSizeLimitBytes: fileSizeLimitBytes,
                    retainedFileCountLimit: retained,
                    shared: false,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
                );

            _logger = cfg.CreateLogger();
        }

        private static LogEventLevel ToSerilogLevel(eLogLevel level)
        {
            switch (level)
            {
                case eLogLevel.DEBUG: return LogEventLevel.Debug;
                case eLogLevel.INFO: return LogEventLevel.Information;
                case eLogLevel.WARN: return LogEventLevel.Warning;
                case eLogLevel.FATAL: return LogEventLevel.Fatal;
                default: return LogEventLevel.Information;
            }
        }

        private static long? ParseFileSizeBytes(string size)
        {
            // 간단 파서: "", null -> null(=기본값)
            // "5MB", "100KB", "10GB" 형태 지원
            if (string.IsNullOrWhiteSpace(size))
                return null;

            string s = size.Trim().ToUpperInvariant();

            long mul = 1;
            if (s.EndsWith("KB")) { mul = 1024; s = s.Substring(0, s.Length - 2).Trim(); }
            else if (s.EndsWith("MB")) { mul = 1024 * 1024; s = s.Substring(0, s.Length - 2).Trim(); }
            else if (s.EndsWith("GB")) { mul = 1024L * 1024L * 1024L; s = s.Substring(0, s.Length - 2).Trim(); }

            long n;
            if (!long.TryParse(s, out n))
                return null;

            if (n <= 0) return null;
            return n * mul;
        }

        // 기존 시그니처 유지: name이 바뀌면 파일을 바꾸는 동작도 유지 가능
        // (다만 LogExtension이 filename별로 logger를 캐싱하니, 실사용에선 name이 바뀌지 않는 게 보통입니다.)
        private void EnsureFile(string name, eLogLevel currentLevel)
        {
            if (!string.Equals(_strFileName, name, StringComparison.Ordinal))
            {
                _strFileName = name;
                Directory.CreateDirectory(_strFilePath);
                RebuildLogger(currentLevel);
            }
        }

        public void Debug(string name, string str)
        {
            lock (locker)
            {
                if (_logger == null) return;
                EnsureFile(name, eLogLevel.DEBUG);
                _logger.Debug(str);
            }
        }

        public void Info(string name, string str)
        {
            lock (locker)
            {
                if (_logger == null) return;
                EnsureFile(name, eLogLevel.INFO);
                _logger.Information(str);
            }
        }

        public void Warn(string name, string str)
        {
            lock (locker)
            {
                if (_logger == null) return;
                EnsureFile(name, eLogLevel.WARN);
                _logger.Warning(str);
            }
        }

        public void Fatal(string name, string str)
        {
            lock (locker)
            {
                if (_logger == null) return;
                EnsureFile(name, eLogLevel.FATAL);
                _logger.Fatal(str);
            }
        }
    }

    public static class LogExtension
    {
        private sealed class LoggerEntry
        {
            public string Key;
            public OPLogger Logger;

            public OPLogger.eLogLevel Level;
            public string FilePath;
            public string FileName;
            public OPLogger.eDatePattern RollingStyle;
            public string MaximumFileSize;
            public int MaxSizeRollBackups;
        }

        private static readonly object _lockLog = new object();
        private static readonly Dictionary<string, LoggerEntry> _dic = new Dictionary<string, LoggerEntry>();
        private static readonly List<string> _listLogPath = new List<string>();
        private static readonly StringBuilder _sb = new StringBuilder();

        public static List<string> CVSMessageList = new List<string>();
        public static List<string> CVSHeaderList = new List<string>();

        public static string DefaultLogPath = Path.Combine(GetCurrentPath(), "OPLogs");


        // Avalonia/다른 UI에서 "프로세스 로그" 흘려보내고 싶을 때 사용
        // 예: LogExtension.UiSink = msg => Dispatcher.UIThread.Post(() => vm.SetProcessLog(msg));
        public static Action<string> UiSink;

        private static OPLogger.eLogLevel _currentLevel = OPLogger.eLogLevel.DEBUG;

        public static string GetCurrentPath()
        {
            // 예전 코드 유지. 필요하면 AppContext.BaseDirectory로 바꿔도 됩니다.
            return Directory.GetCurrentDirectory();
        }

        public static void onInitialize()
        {
            // 필요 시 확장 포인트
        }

        public static void setLevel(OPLogger.eLogLevel level)
        {
            lock (_lockLog)
            {
                _currentLevel = level;

                // 이미 생성된 로거들도 레벨 반영하려면 재초기화 필요
                foreach (var kv in _dic)
                {
                    var e = kv.Value;
                    try
                    {
                        e.Level = level;
                        e.Logger.onInitialize(
                            level,
                            e.FilePath,
                            e.FileName,
                            e.RollingStyle,
                            e.MaximumFileSize,
                            e.MaxSizeRollBackups);
                    }
                    catch
                    {
                        // 로깅이 실패해도 앱 동작은 계속
                    }
                }
            }
        }

        public static void delLog(string path, int iLogDeleteDay)
        {
            // 기존 시그니처 유지: path는 현재 코드에선 실사용이 약함(각 logger path로 삭제)
            try
            {
                lock (_lockLog)
                {
                    var cutoff = DateTime.Today.AddDays(-iLogDeleteDay);

                    foreach (var kv in _dic)
                    {
                        var fp = kv.Value.FilePath;
                        if (string.IsNullOrWhiteSpace(fp)) continue;
                        if (!Directory.Exists(fp)) continue;

                        var di = new DirectoryInfo(fp);
                        foreach (var file in di.GetFiles("*.log", SearchOption.TopDirectoryOnly))
                        {
                            if (file.LastWriteTime.Date < cutoff)
                            {
                                try { file.Delete(); } catch { }
                            }
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        // ===== Exception =====
        public static void LogException(Exception ex, string message, string filename, string folder)
        {
            var date = DateTime.Now.ToString("yyyy-MM-dd");

            try
            {
                if (string.IsNullOrWhiteSpace(filename)) filename = "Exception";
                if (string.IsNullOrWhiteSpace(folder)) folder = "Exception";

                // 경로: {Current}\Logs\Exception\{folder}\{yyyy-MM-dd}
                var dir = Path.Combine(GetCurrentPath(), "Logs", "Exception", folder, date);
                var key = string.Format("System_Exception_{0}_{1}", folder, filename);

                OPLogger log = GetOrCreateLogger(
                    key: key,
                    level: _currentLevel,
                    filePath: dir,
                    fileName: filename,
                    rollingStyle: OPLogger.eDatePattern.DAY,
                    maximumFileSize: "",
                    maxSizeRollBackups: 30);

                log.Fatal(filename,
                    string.Format("[Exception Message] : {0}\r\n[Exception StackTrace] : {1}\r\n[Exception TargetSite] : {2}",
                        ex != null ? ex.Message : "",
                        ex != null ? ex.StackTrace : "",
                        ex != null ? ex.TargetSite : null));

                if (!string.IsNullOrWhiteSpace(message))
                {
                    log.Fatal(filename, string.Format("[Exception Message] : {0}", message));
                }
            }
            catch
            {
                // ignore
            }
        }

        public static void LogException(Exception ex, string folder, string filename)
        {
            var date = DateTime.Now.ToString("yyyy-MM-dd");

            try
            {
                if (string.IsNullOrWhiteSpace(filename)) filename = "Exception";
                if (string.IsNullOrWhiteSpace(folder)) folder = "Exception";

                var dir = Path.Combine(GetCurrentPath(), "Logs", "Exception", folder, date);
                var key = string.Format("System_Exception_{0}_{1}", folder, filename);

                OPLogger log = GetOrCreateLogger(
                    key: key,
                    level: _currentLevel,
                    filePath: dir,
                    fileName: filename,
                    rollingStyle: OPLogger.eDatePattern.DAY,
                    maximumFileSize: "",
                    maxSizeRollBackups: 30);

                log.Fatal(filename,
                    string.Format("[Exception Message] : {0}\r\n[Exception StackTrace] : {1}\r\n[Exception TargetSite] {2}\n\n",
                        ex != null ? ex.Message : "",
                        ex != null ? ex.StackTrace : "",
                        ex != null ? ex.TargetSite : null));
            }
            catch
            {
                // ignore
            }
        }

        public static void LoggingProcException(Exception ex, string filename, string folder, string message = "")
        {
            LogException(
                ex,
                string.Format("[Exception Message] : {0}\r\n[Exception StackTrace] : {1}\r\n[Exception TargetSite] : {2}",
                    ex != null ? ex.Message : "",
                    ex != null ? ex.StackTrace : "",
                    ex != null ? ex.TargetSite : null),
                filename,
                folder);
        }

        // ===== Normal Log =====

        // 폴더구조: Logs\{root}\{folder}\{yyyy-MM-dd}\{filename}.log
        public static void Log(OPLogger.eLogLevel level, string message, string root = "None", string folder = "None", string filename = "App")
        {
            if (root == "None") root = OPLOG.Root;
            if (folder == "None") folder = OPLOG.Folder.Log;
            if (filename == "App") filename = OPLOG.File.Log;

            var date = DateTime.Now.ToString("yyyy-MM-dd");

            try
            {
                var dir = Path.Combine(GetCurrentPath(), "Logs", root, folder, date);
                var key = filename; // 기존: filename 단독 키

                OPLogger log = GetOrCreateLogger(
                    key: key,
                    level: _currentLevel,
                    filePath: dir,
                    fileName: filename,
                    rollingStyle: OPLogger.eDatePattern.DAY,
                    maximumFileSize: "",
                    maxSizeRollBackups: 30);

                var line = string.Format("[{0}] {1}", folder, message);

                switch (level)
                {
                    case OPLogger.eLogLevel.INFO:
                        log.Info(filename, line);
                        break;
                    case OPLogger.eLogLevel.DEBUG:
                        log.Debug(filename, line);
                        break;
                    case OPLogger.eLogLevel.FATAL:
                        log.Fatal(filename, line);
                        break;
                    case OPLogger.eLogLevel.WARN:
                        log.Warn(filename, line);
                        break;
                    default:
                        log.Info(filename, line);
                        break;
                }

                // UI로 흘리기(Avalonia/WPF 분리)
                var sink = UiSink;
                if (sink != null)
                {
                    try
                    {
                        sink(level.ToString() + " " + message);
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        public static string[] LogFileRead(string sFilePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sFilePath)) return null;

                lock (_lockLog)
                {
                    if (!File.Exists(sFilePath)) return null;
                    return File.ReadAllLines(sFilePath, Encoding.Default).ToArray();
                }
            }
            catch
            {
                return null;
            }
        }



        // ===== Internal =====
        private static OPLogger GetOrCreateLogger(
            string key,
            OPLogger.eLogLevel level,
            string filePath,
            string fileName,
            OPLogger.eDatePattern rollingStyle,
            string maximumFileSize,
            int maxSizeRollBackups)
        {
            lock (_lockLog)
            {
                LoggerEntry entry;
                if (!_dic.TryGetValue(key, out entry))
                {
                    var logger = new OPLogger(fileName);

                    // 디렉토리 준비
                    Directory.CreateDirectory(filePath);

                    logger.onInitialize(level, filePath, fileName, rollingStyle, maximumFileSize, maxSizeRollBackups);

                    entry = new LoggerEntry
                    {
                        Key = key,
                        Logger = logger,
                        Level = level,
                        FilePath = filePath,
                        FileName = fileName,
                        RollingStyle = rollingStyle,
                        MaximumFileSize = maximumFileSize,
                        MaxSizeRollBackups = maxSizeRollBackups
                    };

                    _dic.Add(key, entry);

                    if (!_listLogPath.Contains(filePath))
                        _listLogPath.Add(filePath);
                }
                else
                {
                    // 날짜 폴더가 바뀌면(yyyy-MM-dd) path가 달라지므로 로거 재구성이 필요합니다.
                    // 현재는 Log()가 date 폴더를 포함해서 filePath를 넘기므로, filePath가 달라질 수 있습니다.
                    if (!string.Equals(entry.FilePath, filePath, StringComparison.Ordinal))
                    {
                        entry.FilePath = filePath;
                        entry.FileName = fileName;
                        entry.RollingStyle = rollingStyle;
                        entry.MaximumFileSize = maximumFileSize;
                        entry.MaxSizeRollBackups = maxSizeRollBackups;
                        entry.Level = level;

                        Directory.CreateDirectory(filePath);

                        entry.Logger.onInitialize(level, filePath, fileName, rollingStyle, maximumFileSize, maxSizeRollBackups);

                        if (!_listLogPath.Contains(filePath))
                            _listLogPath.Add(filePath);
                    }
                }

                return entry.Logger;
            }
        }

        // 기존에 있던 미구현 함수는 유지(호출처 있으면 컴파일 방지)
        public static void LogException(Exception ex, string v1, object obj, string v2)
        {
            throw new NotImplementedException();
        }
    }


}
