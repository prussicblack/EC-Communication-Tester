using NetMQ;
using NetMQ.Sockets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SOEM_FrontEnd.NetMQ
{
    public class NetMQServerTopicModel : IDisposable
    {
        private PublisherSocket _publisher;
        private SubscriberSocket _subscriber;
        private CancellationTokenSource _cts;

        public event Action<string> OnClientMessageReceived;

        public event Action<string> OnClientLogProcess;

        public NetMQServerTopicModel()
        {
            
        }


        public void Start(string pubBindIPPort = "0.0.0.0:5556", string subBindIPPort = "0.0.0.0:5557")
        {
            string pubBindIP = $"tcp://{pubBindIPPort}";
            string subBindIP = $"tcp://{subBindIPPort}";

            string portstring = pubBindIPPort.Split(':').Last();
            if (IsPortInUse(int.Parse(portstring)))
            {
                OnClientLogProcess?.Invoke($"Server단 포트 사용중.{portstring}");
                return;
            }

            portstring = subBindIPPort.Split(':').Last();
            if (IsPortInUse(int.Parse(portstring)))
            {
                OnClientLogProcess?.Invoke($"Server단 포트 사용중.{portstring}");
                return;
            }

            _cts = new CancellationTokenSource();

            // 클라이언트로 보내는 용도
            _publisher = new PublisherSocket();
            _publisher.Bind(pubBindIP);
            OnClientLogProcess?.Invoke($"Server단 Bind시도{pubBindIP} - {subBindIP}");

            // Start Subscriber
            Task.Run(() =>
            {
                _subscriber = new SubscriberSocket();
                _subscriber.Bind(subBindIP);
                _subscriber.Subscribe("");

                while (!_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        string message = _subscriber.ReceiveFrameString();

                        OnClientMessageReceived?.Invoke(message);
                        OnClientLogProcess?.Invoke("Server단 메세지 수신됨");

                    }
                    catch
                    {
                        /* 로그 처리 가능 */
                    }
                }
            }, _cts.Token);
        }

        public void StartSafe(string pubBindIPPort = "0.0.0.0:5556", string subBindIPPort = "0.0.0.0:5557")
        {
            string pubBindIP = $"tcp://{pubBindIPPort}";
            string subBindIP = $"tcp://{subBindIPPort}";

            string portstring = pubBindIPPort.Split(':').Last();
            if (IsPortInUse(int.Parse(portstring)))
            {
                OnClientLogProcess?.Invoke($"Server단 포트 사용중.{portstring}");
                return;
            }
            
            portstring = subBindIPPort.Split(':').Last();
            if (IsPortInUse(int.Parse(portstring)))
            {
                OnClientLogProcess?.Invoke($"Server단 포트 사용중.{portstring}");
                return;
            }


            _cts = new CancellationTokenSource();

            // 클라이언트로 보내는 용도
            _publisher = new PublisherSocket();
            _publisher.Bind(pubBindIP);
            OnClientLogProcess?.Invoke($"Server단 Bind시도{pubBindIP} - {subBindIP}");

            // Start Subscriber
            Task.Run(() =>
            {
                _subscriber = new SubscriberSocket();
                _subscriber.Bind(subBindIP);
                _subscriber.Subscribe("");

                while (!_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        bool ret = _subscriber.TryReceiveFrameString(TimeSpan.FromMilliseconds(1), out string message);
                        //string message = _subscriber.ReceiveFrameString();
                        if (ret == true)
                        {
                                OnClientMessageReceived?.Invoke(message);
                                OnClientLogProcess?.Invoke("Server단 메세지 수신됨");
                        }
                        else
                        {

                        }
                    }
                    catch
                    {
                         /* 로그 처리 가능 */
                    }
                }
            }, _cts.Token);
        }

        public void SendToClients(string message)
        {
            _publisher?.SendFrame(message);
            OnClientLogProcess?.Invoke($"서버단 메세지 전송함 - {message}");
        }

        public void TrySendToClients(string message)
        {
            try
            {
                bool ret = (bool)_publisher?.TrySendFrame(TimeSpan.FromMilliseconds(1), message);

                if (ret == true)
                {
                    OnClientLogProcess?.Invoke($"서버단 메세지 전송함 - {message}");
                }
                else
                {
                    OnClientLogProcess?.Invoke($"서버단 메세지 전송실패 - {message}");
                }
            }
            catch (InvalidOperationException ex)
            {
                
            }
        }



        public void Stop()
        {
            _cts?.Cancel();
            _subscriber?.Dispose();
            _publisher?.Dispose();
        }

        public void Dispose()
        {
            Stop();
        }
        public static bool IsPortInUse(int port)
        {
            var properties = IPGlobalProperties.GetIPGlobalProperties();
            var listeners = properties.GetActiveTcpListeners();

            return listeners.Any(ep => ep.Port == port);
        }



    }
}
