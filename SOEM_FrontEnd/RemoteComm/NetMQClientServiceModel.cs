using NetMQ.Sockets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NetMQ;

namespace SOEM_FrontEnd.NetMQ
{
    public class NetMQClientServiceModel : IDisposable
    {
        private RequestSocket _requestSocket;

        public event Action<string> OnLogProcess;

        private string LastConnectAddress = "";

        public void Connect(string serverAddress = "127.0.0.1:5555")
        {
            string ConnectString = $"tcp://{serverAddress}";

            OnLogProcess?.Invoke("서비스 연결.");
            _requestSocket = new RequestSocket();
            _requestSocket.Connect(ConnectString);
            LastConnectAddress = ConnectString;
        }

        public string SendRequest(string message)
        {
            _requestSocket.SendFrame(message);
            OnLogProcess?.Invoke($"서비스 메세지 전송{message}.");

            string ret = _requestSocket.ReceiveFrameString();

            OnLogProcess?.Invoke($"서비스 메세지 응답{ret}.");

            return ret;
        }

        public string TrySendRequestWithTimeout(string message, int Timeoutms = 1000)
        {
            bool ret = _requestSocket.TrySendFrame(message);
            if (ret == false)
            {
                return "[SendFail] MessageSendFail";
            }
            OnLogProcess?.Invoke($"서비스 메세지 전송{message}.");

            if (_requestSocket.TryReceiveFrameString(TimeSpan.FromMilliseconds(Timeoutms), out string response))
            {
                OnLogProcess?.Invoke($"서비스 메세지 응답{response}.");
                return response;
            }
            else
            {
                OnLogProcess?.Invoke($"[Timeout] No response received.");
                //응답 없을 시 소켓 재생성.
                _requestSocket.Dispose();
                _requestSocket = new RequestSocket();
                _requestSocket.Connect(LastConnectAddress);
                OnLogProcess?.Invoke($"타임아웃으로 소켓 재생성됨.");

                return "[Timeout] No response received.";
            }
        }

        
        

        public void Dispose()
        {
            _requestSocket?.Dispose();
        }


    }
}
