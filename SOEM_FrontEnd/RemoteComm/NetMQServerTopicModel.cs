using NetMQ;
using NetMQ.Sockets;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SOEM_FrontEnd.NetMQ
{
    public class NetMQServerTopicModel : IDisposable
    {
        private readonly object _syncRoot;
        private readonly Func<TelemetryFrame> _snapshotProvider;
        private Thread _thread;
        private CancellationTokenSource _cts;
        private bool _isRunning;

        public event Action<string> OnLog;

        public NetMQServerTopicModel(Func<TelemetryFrame> snapshotProvider)
        {
            if (snapshotProvider == null)
                throw new ArgumentNullException(nameof(snapshotProvider));

            _snapshotProvider = snapshotProvider;
            _syncRoot = new object();
        }

        public bool Start(string endpoint, int publishPeriodMs)
        {
            lock (_syncRoot)
            {
                if (_isRunning)
                    return false;

                _cts = new CancellationTokenSource();

                _thread = new Thread(() => WorkerMain(endpoint, publishPeriodMs, _cts.Token));
                _thread.IsBackground = true;
                _thread.Name = "NetMQ Telemetry Publisher";
                _thread.Start();

                _isRunning = true;
                return true;
            }
        }

        private void WorkerMain(string endpoint, int publishPeriodMs, CancellationToken token)
        {
            try
            {
                using (PublisherSocket socket = new PublisherSocket())
                {
                    socket.Options.SendHighWatermark = 2;
                    socket.Bind(endpoint);

                    OnLog?.Invoke("Telemetry publisher started. Endpoint=" + endpoint);

                    Stopwatch stopwatch = Stopwatch.StartNew();
                    long nextTick = stopwatch.ElapsedMilliseconds;

                    while (!token.IsCancellationRequested)
                    {
                        long now = stopwatch.ElapsedMilliseconds;

                        if (now < nextTick)
                        {
                            int sleepMs = (int)(nextTick - now);

                            if (sleepMs > 1)
                                Thread.Sleep(sleepMs - 1);

                            continue;
                        }

                        nextTick += publishPeriodMs;

                        TelemetryFrame frame = _snapshotProvider();

                        string json = JsonSerializer.Serialize(frame,TelemetryJsonContext.Default.TelemetryFrame);

                        socket.SendMoreFrame("telemetry");
                        socket.SendFrame(json);
                    }
                }
            }
            catch (Exception ex)
            {
                OnLog?.Invoke("Telemetry publisher error. " + ex.Message);
            }
            finally
            {
                OnLog?.Invoke("Telemetry publisher stopped.");
            }
        }

        public void Stop()
        {
            lock (_syncRoot)
            {
                if (!_isRunning)
                    return;

                _isRunning = false;

                if (_cts != null)
                    _cts.Cancel();

                if (_thread != null && _thread.IsAlive)
                    _thread.Join(1000);

                if (_cts != null)
                {
                    _cts.Dispose();
                    _cts = null;
                }

                _thread = null;
            }
        }

        public void Dispose()
        {
            Stop();
        }


    }

}
