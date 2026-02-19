using Microsoft.Extensions.Logging;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static SOEM_FrontEnd.Util.Logging.OPLogWorker;

namespace SOEM_FrontEnd.Util.Logging
{
    internal sealed class OpLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private readonly OPLogWorker _worker;
        private readonly OPLoggerOptions _opt;
        private IExternalScopeProvider _scopeProvider;

        public OpLoggerProvider(OPLogWorker worker, OPLoggerOptions opt)
        {
            _worker = worker;
            _opt = opt;
        }

        public IExternalScopeProvider ScopeProvider
        {
            get { return _scopeProvider; }
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new OpLogger(categoryName, _worker, _opt, this);
        }

        public void SetScopeProvider(IExternalScopeProvider scopeProvider)
        {
            _scopeProvider = scopeProvider;
        }

        public void Dispose()
        {
            // worker는 OPLogger가 관리
        }
    }

    internal sealed class OpLogger : ILogger
    {
        private readonly string _category;
        private readonly OPLogWorker _worker;
        private readonly OPLoggerOptions _opt;
        private readonly OpLoggerProvider _provider;

        public OpLogger(string category, OPLogWorker worker, OPLoggerOptions opt, OpLoggerProvider provider)
        {
            _category = category ?? "";
            _worker = worker;
            _opt = opt;
            _provider = provider;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            var sp = _provider.ScopeProvider;
            if (sp != null)
            {
                try { return sp.Push(state); }
                catch { return NullScope.Instance; }
            }
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            if (logLevel == LogLevel.None) return false;
            return logLevel >= _opt.MinimumLevel;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            string message;
            try
            {
                if (formatter != null) message = formatter(state, exception);
                else message = state != null ? state.ToString() ?? "" : "";
            }
            catch
            {
                message = "<log format failed>";
            }

            IReadOnlyDictionary<string, object> props = null;
            try
            {
                props = ExtractStructuredState(state);

                if (_opt.IncludeScopes)
                {
                    var sp = _provider.ScopeProvider;
                    if (sp != null)
                    {
                        var scopes = new List<object>();
                        sp.ForEachScope((s, list) => list.Add(s), scopes);

                        if (scopes.Count > 0)
                        {
                            Dictionary<string, object> dict;
                            if (props != null) dict = new Dictionary<string, object>(props);
                            else dict = new Dictionary<string, object>();

                            dict["Scopes"] = scopes;
                            props = dict;
                        }
                    }
                }
            }
            catch { }

            var env = new LogEnvelope(DateTime.UtcNow, logLevel, _category, eventId, message, exception, props);
            _worker.Enqueue(env);
        }

        private static IReadOnlyDictionary<string, object> ExtractStructuredState<TState>(TState state)
        {
            if (state == null) return null;

            var kvps = state as IEnumerable<KeyValuePair<string, object>>;
            if (kvps == null) return null;

            var dict = new Dictionary<string, object>();
            foreach (var kv in kvps)
            {
                if (kv.Key == "{OriginalFormat}") continue;
                dict[kv.Key] = kv.Value;
            }

            if (dict.Count == 0) return null;
            return dict;
        }
    }

    internal sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new NullScope();
        private NullScope() { }
        public void Dispose() { }
    }
}
