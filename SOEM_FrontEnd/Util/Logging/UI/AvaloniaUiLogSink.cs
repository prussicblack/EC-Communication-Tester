using Avalonia.Threading;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SOEM_FrontEnd.Util.Logging.UI
{
    public sealed class AvaloniaUiLogSink : IOpLogUiSink, IDisposable
    {
        private readonly ConcurrentQueue<string> _q = new ConcurrentQueue<string>();
        private readonly Action<string> _append;
        private readonly DispatcherTimer _timer;

        public int MaxPending { get; set; } = 5000;

        public AvaloniaUiLogSink(Action<string> append, TimeSpan? flushInterval = null)
        {
            if (append == null) throw new ArgumentNullException(nameof(append));
            _append = append;

            _timer = new DispatcherTimer();
            _timer.Interval = flushInterval ?? TimeSpan.FromMilliseconds(100);
            _timer.Tick += (_, __) => FlushOnUiThread();
            _timer.Start();
        }

        public void Enqueue(string line)
        {
            if (line == null) return;

            _q.Enqueue(line);

            while (_q.Count > MaxPending)
            {
                string _;
                _q.TryDequeue(out _);
            }
        }

        private void FlushOnUiThread()
        {
            int guard = 2000;
            while (guard-- > 0)
            {
                string line;
                if (!_q.TryDequeue(out line))
                    break;

                try { _append(line); } catch { }
            }
        }

        public void Dispose()
        {
            try { _timer.Stop(); } catch { }
        }
    }
}
