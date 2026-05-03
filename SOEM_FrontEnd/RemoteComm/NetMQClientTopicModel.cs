using NetMQ;
using NetMQ.Sockets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Animation;

namespace SOEM_FrontEnd.NetMQ
{
    public class NetMQClientTopicModel : IDisposable
    {
        private PublisherSocket _publisher;
        private SubscriberSocket _subscriber;
        private CancellationTokenSource _cts;
        
        public event Action<string> OnMessageReceived;

        public event Action<string> OnClientLogProcess;


        public NetMQClientTopicModel()
        {
            
        }

        public void Start(string pubIPPort = "localhost:5557", string subIPPort = "localhost:5556")
        {
            string pubConnect = $"tcp://{pubIPPort}";
            string subConnect = $"tcp://{subIPPort}";

            _cts = new CancellationTokenSource();

            // 서버로 메시지 보내기
            _publisher = new PublisherSocket();
            _publisher.Connect(pubConnect);
            OnClientLogProcess?.Invoke($"클라이언트단 연결시도{pubConnect} - {subConnect}");

            // 서버 메시지 수신
            Task.Run(() =>
            {
                _subscriber = new SubscriberSocket();
                _subscriber.Connect(subConnect);
                _subscriber.Subscribe("");

                while (!_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        string msg = _subscriber.ReceiveFrameString();
                        OnMessageReceived?.Invoke(msg);
                        OnClientLogProcess?.Invoke("클라이언트단 메세지 수신됨");
                    }
                    catch
                    {
                         /* 로그 처리 가능 */
                    }
                }
            }, _cts.Token);
        }
        
        public void StartSafe(string pubIPPort = "localhost:5557", string subIPPort = "localhost:5556")
        {
            string pubConnect = $"tcp://{pubIPPort}";
            string subConnect = $"tcp://{subIPPort}";

            _cts = new CancellationTokenSource();

            // 서버로 메시지 보내기
            _publisher = new PublisherSocket();
            _publisher.Connect(pubConnect);
            OnClientLogProcess?.Invoke($"클라이언트단 연결시도{pubConnect} - {subConnect}");

            // 서버 메시지 수신
            Task.Run(() =>
            {
                _subscriber = new SubscriberSocket();
                _subscriber.Connect(subConnect);
                _subscriber.Subscribe("");

                while (!_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        bool ret = _subscriber.TryReceiveFrameString(TimeSpan.FromMilliseconds(1), out string msg);

                        if (ret == true)
                        {
                            OnMessageReceived?.Invoke(msg);
                            OnClientLogProcess?.Invoke("클라이언트단 메세지 수신됨");
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

        public void SendToServer(string message)
        {
            _publisher?.SendFrame(message);
            OnClientLogProcess?.Invoke($"클라이언트단 메세지 전송함 - {message}");
        }

        public void TrySendServer(string message)
        {
            try
            {
                bool ret = (bool)_publisher?.TrySendFrame(TimeSpan.FromMilliseconds(1), message);
                if (ret == true)
                {
                    OnClientLogProcess?.Invoke("클라이언트단 메세지 전송함");
                }
                else
                {
                    OnClientLogProcess?.Invoke("클라이언트단 메세지 전송실패");
                }

            }
            catch (InvalidOperationException ex)
            {

            }
        }

        public void Stop()
        {
            _cts?.Cancel();
            _publisher?.Dispose();
            _subscriber?.Dispose();
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
