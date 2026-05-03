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
        private readonly object _syncRoot;
        private Thread _workerThread;
        private CancellationTokenSource _cts;
        private Func<string, string> _handler;
        private bool _isRunning;

        public event Action<string> OnLog;

        public NetMQServerServiceModel()
        {
            _syncRoot = new object();
        }

        public void SetHandler(Func<string, string> handler)
        {
            _handler = handler;
        }

        public bool Start(string bindAddress)
        {
            lock (_syncRoot)
            {
                if (_isRunning)
                    return false;

                _cts = new CancellationTokenSource();

                _workerThread = new Thread(() => WorkerMain(bindAddress, _cts.Token));
                _workerThread.IsBackground = true;
                _workerThread.Name = "NetMQ Command Server";
                _workerThread.Start();

                _isRunning = true;
                return true;
            }
        }

        private void WorkerMain(string bindAddress, CancellationToken token)
        {
            string endpoint = "tcp://" + bindAddress;

            try
            {
                using (ResponseSocket socket = new ResponseSocket())
                {
                    socket.Bind(endpoint);

                    OnLog?.Invoke("Command server started. " + endpoint);

                    while (!token.IsCancellationRequested)
                    {
                        string request;

                        bool received = socket.TryReceiveFrameString(TimeSpan.FromMilliseconds(20), out request);

                        if (!received)
                            continue;

                        string reply = HandleRequestSafe(request);

                        socket.SendFrame(reply);
                    }
                }
            }
            catch (Exception ex)
            {
                OnLog?.Invoke("Command server error. " + ex.Message);
            }
            finally
            {
                OnLog?.Invoke("Command server stopped.");
            }
        }

        private string HandleRequestSafe(string request)
        {
            try
            {
                Func<string, string> handler = _handler;

                if (handler == null)
                    return "{\"ok\":false,\"error\":\"No handler\"}";

                string reply = handler(request);

                if (string.IsNullOrEmpty(reply))
                    return "{\"ok\":false,\"error\":\"Empty reply\"}";

                return reply;
            }
            catch (Exception ex)
            {
                OnLog?.Invoke("Command handler error. " + ex.Message);
                return "{\"ok\":false,\"error\":\"Handler exception\"}";
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

                if (_workerThread != null && _workerThread.IsAlive)
                    _workerThread.Join(1000);

                if (_cts != null)
                {
                    _cts.Dispose();
                    _cts = null;
                }

                _workerThread = null;
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }

}

