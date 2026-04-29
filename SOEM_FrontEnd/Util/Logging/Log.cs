using Avalonia.Controls.Shapes;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace SOEM_FrontEnd.Util.Logging
{
    public static class Log
    {

        /// <summary>
        /// 로깅 기본 사용법.
        /// 
        /// 1. 로깅시작 초기화 --> 필수.
        /// OPLogger.Configure(new OPLoggerOptions
        /// {
        ///    LogFolder = "OPLogs",
        ///    FileNamePrefix = "soem_",
        ///    MaxFolderBytes = 512L * 1024L * 1024L,   // 512MB
        ///    //MaxFolderBytes = 128L * 1024L,   // 128KB
        ///    MaxActiveFileBytes = 32L * 1024L * 1024L, // 32MB
        ///    //MaxActiveFileBytes = 32L * 1024L, // 32KB
        ///    QueueCapacity = 8192,
        ///    QueueMode = OPLogQueueMode.Block,
        ///    MinimumLevel = LogLevel.Information,
        ///    EnableFailover = true
        /// });
        ///
        /// 2. 로거 생성 후 
        /// ILogger log = OPLogger.CreateLogger("App");
        /// 3. 로거내부 메소드 호출.
        /// log.LogInformation("App boot");
        ///
        /// 생성도 귀찮으면, 로깅 빠른 사용법.
        /// 1. 초기화는 필수로 해줘야하고...
        /// OPLogger.Configure(---);
        /// 
        /// 2. 선택사항, 기본 카테고리 설정.(비선언 타입 초기화용)
        /// Log.SetDefaultCategory("SOEM"); 
        ///
        /// 3. Static으로 그냥 로그 기록.
        /// Log.I/W/E 사용.
        /// 
        /// </summary>


        /// <summary>
        /// UI로그 연결기능을 위한 코드 정리.
        ///
        /// 
        /// 1. 로그 싱크 Create. Action이라 동작을 입력한다.
        /// _sink = new AvaloniaUiLogSink(line =>
        /// {
        ///    // AvaloniaUiLogSink가 UI thread로 flush하니까 여기선 Add만
        ///    UiLogs.Add(line);
        ///
        ///    //너무 길어지면 오래된 것 삭제
        ///    const int max = 3000;
        ///    if (UiLogs.Count > max)
        ///        UiLogs.RemoveAt(0);
        /// });
        /// 
        /// 2. 로그 싱크 설정
        /// OPLogger.SetUiSink(_sink);
        /// 이러면 로그 발생시 위쪽의 Action이 동작하게 된다.
        ///</summary>




        private static ILogger _default;

        public static void SetDefaultCategory(string category)
        {
            if (string.IsNullOrWhiteSpace(category)) category = "App";
            _default = OPLogger.CreateLogger(category);
        }

        private static ILogger L
        {
            get
            {
                if (_default != null) return _default;
                // Configure 이후라면 이게 동작
                _default = OPLogger.CreateLogger("App");
                return _default;
            }
        }

        public static void T(string msg, params object[] args) => L.LogTrace(msg, args);
        public static void D(string msg, params object[] args) => L.LogDebug(msg, args);
        public static void I(string msg, params object[] args) => L.LogInformation(msg, args);
        public static void W(string msg, params object[] args) => L.LogWarning(msg, args);

        public static void E(string msg, params object[] args) => L.LogError(msg, args);
        public static void E(Exception ex, string msg, params object[] args) => L.LogError(ex, msg, args);

        public static void C(string msg, params object[] args) => L.LogCritical(msg, args);
        public static void C(Exception ex, string msg, params object[] args) => L.LogCritical(ex, msg, args);
    }
}
