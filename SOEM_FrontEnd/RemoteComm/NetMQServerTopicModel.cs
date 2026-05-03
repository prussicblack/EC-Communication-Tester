using NetMQ;
using NetMQ.Sockets;
using System;
using System.Text.Json;
using System.Threading;

namespace SOEM_FrontEnd.NetMQ
{
    public sealed class NetMQServerTopicModel : IDisposable
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
            {
                throw new ArgumentNullException(nameof(snapshotProvider));
            }

            _snapshotProvider = snapshotProvider;
            _syncRoot = new object();
        }

        public bool Start(string bindAddress = "0.0.0.0:5556", int publishPeriodMs = 50)
        {
            lock (_syncRoot)
            {
                if (_isRunning)
                {
                    return false;
                }

                if (publishPeriodMs <= 0)
                {
                    publishPeriodMs = 50;
                }

                string endpoint = NormalizeEndpoint(bindAddress);

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

                    while (token.IsCancellationRequested == false)
                    {
                        PublishOnce(socket);

                        if (token.WaitHandle.WaitOne(publishPeriodMs))
                        {
                            break;
                        }
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

        private void PublishOnce(PublisherSocket socket)
        {
            TelemetryFrame frame = _snapshotProvider();

            string json = JsonSerializer.Serialize(frame, TelemetryJsonContext.Default.TelemetryFrame);

            socket.SendMoreFrame("telemetry.snapshot");
            socket.SendFrame(json);
        }

        private static string NormalizeEndpoint(string bindAddress)
        {
            if (string.IsNullOrWhiteSpace(bindAddress))
            {
                return "tcp://0.0.0.0:5556";
            }

            if (bindAddress.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase))
            {
                return bindAddress;
            }

            return "tcp://" + bindAddress;
        }

        public void Stop()
        {
            lock (_syncRoot)
            {
                if (_isRunning == false)
                {
                    return;
                }

                _isRunning = false;

                if (_cts != null)
                {
                    _cts.Cancel();
                }

                if (_thread != null && _thread.IsAlive)
                {
                    _thread.Join(1000);
                }

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
