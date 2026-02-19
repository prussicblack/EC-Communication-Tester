using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;


namespace SOEM_FrontEnd.Util.Logging
{
    public static class Log
    {
        //초기화 선언 다 귀찮고 한방에 로그 지를때.
        
        private static ILogger _default;

        //OPLogger.Configure(...);
        //Log.SetDefaultCategory("SOEM");
        //이런형식으로 Configure밑에 질러줘야 되긴함.
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
