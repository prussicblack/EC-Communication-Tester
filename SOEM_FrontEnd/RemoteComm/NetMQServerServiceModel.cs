using NetMQ;
using NetMQ.Sockets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SOEM_FrontEnd.NetMQ
{
    ///1. 주 백그라운드 스레드인 StateMachine에서 통신으로 뿌려줌.
    /// 2. 여기에 스레드 만들어서 여기서 뿌려주는 방식 중
    /// 2번방식을 사용하기로 함.
    /// Topic은 기본 주기 10ms발행
    /// service는 0.5~1Sec단위 HeartBit.
    /// service모델은 첫 연결시 클라이언트의 Key를 저장하고, 해당 키를 보관하여 클라이언트를 1대로 제한.
    /// HeartBit 3~5초 정도 끊기면 키 삭제하고 새 키 받아들일 준비.
    /// 연결 끊길때 키 삭제 관련 메세지 전송.


    public class NetMQServerServiceModel : IDisposable
    {
        private ResponseSocket _responseSocket;
        private CancellationTokenSource _cts;

        public event Action<string> OnRequestReceived;

        public event Action<string> OnLogProcess;

        private Func<string, string> _onRequestHandler;
        public NetMQServerServiceModel()
        {
            
            
        }


        public void Start(string bindAddress = "0.0.0.0:5555")
        {
            string ServiceBindIPPort = $"tcp://{bindAddress}";

            
            _cts = new CancellationTokenSource();
            Task.Run(() =>
            {
                _responseSocket = new ResponseSocket();
                _responseSocket.Bind(ServiceBindIPPort);

                OnLogProcess?.Invoke("서비스 시작됨");

                while (!_cts.IsCancellationRequested)
                {
                    try
                    {
                        string message = _responseSocket.ReceiveFrameString();
                        OnRequestReceived?.Invoke(message);

                        // 응답 전송
                        _responseSocket.SendFrame($"Echo: {message}");
                        OnLogProcess?.Invoke($"응답 반환됨{message}");
                    }
                    catch
                    {
                        
                    }
                }
            }, _cts.Token);
        }

        public void StartSafe(string bindAddress = "0.0.0.0:5555")
        {
            string ServiceBindIPPort = $"tcp://{bindAddress}";


            _cts = new CancellationTokenSource();
            Task.Run(() =>
            {
                _responseSocket = new ResponseSocket();
                _responseSocket.Bind(ServiceBindIPPort);

                OnLogProcess?.Invoke("서비스 시작됨");

                while (!_cts.IsCancellationRequested)
                {
                    try
                    {
                        //string message = _responseSocket.ReceiveFrameString();

                        bool ret = _responseSocket.TryReceiveFrameString(TimeSpan.FromMilliseconds(1), out string message);
                        if (ret == true)
                        {
                            OnRequestReceived?.Invoke(message);

                            // 응답 전송
                            _responseSocket.SendFrame($"Echo: {message}");
                            OnLogProcess?.Invoke($"응답 반환됨{message}");
                        }
                    }
                    catch
                    {

                    }
                }
            }, _cts.Token);
        }

        public void StartSafeCallback(string bindAddress = "0.0.0.0:5555")
        {
            string ServiceBindIPPort = $"tcp://{bindAddress}";


            _cts = new CancellationTokenSource();
            Task.Run(() =>
            {
                _responseSocket = new ResponseSocket();
                _responseSocket.Bind(ServiceBindIPPort);

                OnLogProcess?.Invoke("서비스 시작됨");

                while (!_cts.IsCancellationRequested)
                {
                    try
                    {
                        //string message = _responseSocket.ReceiveFrameString();

                        bool ret = _responseSocket.TryReceiveFrameString(TimeSpan.FromMilliseconds(1), out string message);
                        if (ret == true)
                        {
                            OnRequestReceived?.Invoke(message);

                            // 응답 전송
                            //_responseSocket.SendFrame($"Echo: {message}");
                            string Reply = _onRequestHandler?.Invoke(message) ?? "No handler";

                            _responseSocket.SendFrame(Reply);

                            OnLogProcess?.Invoke($"응답 반환됨{Reply}");
                        }
                    }
                    catch
                    {

                    }
                }
            }, _cts.Token);
        }
        public void SetRequestHandler(Func<string, string> handler)
        {
            _onRequestHandler = handler;
        }


        public void Stop()
        {
            _cts?.Cancel();
            _responseSocket?.Dispose();
        }

        public void Dispose() => Stop();





    }
}
