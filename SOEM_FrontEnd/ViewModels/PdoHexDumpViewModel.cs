using Avalonia.Threading;
using SOEM_FrontEnd.Ethercat.EthercatProfile.Interfaces;
using System;
using System.Text;

namespace SOEM_FrontEnd.ViewModels
{
    public class PdoHexDumpViewModel : ViewModelBase
    {
        private IPDOView _pdoView;

        private string _rxHex = "";
        public string RxHex
        {
            get { return _rxHex; }
            private set { SetProperty(ref _rxHex, value); }
        }

        private string _txHex = "";
        public string TxHex
        {
            get { return _txHex; }
            private set { SetProperty(ref _txHex, value); }
        }

        private DispatcherTimer _timer;

        public void Attach(IPDOView pdoView)
        {
            _pdoView = pdoView;

            Refresh();

            if (_pdoView != null)
                StartTimer();
            else
                StopTimer();
        }

        private void StartTimer()
        {
            if (_timer != null)
                return;

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(200); // 100~300ms 권장
            _timer.Tick += (_, __) => Refresh();
            _timer.Start();
        }

        private void StopTimer()
        {
            if (_timer == null)
                return;

            _timer.Stop();
            _timer = null;
        }

        private void Refresh()
        {
            if (_pdoView == null)
            {
                RxHex = "";
                TxHex = "";
                return;
            }

            // Snapshot만 사용 (실시간 버퍼 직접 접근 금지)
            const int bytesPerLine = 16;
            const int maxBytes = 256; // UI 부담 방지(원하면 늘려도 됨)

            RxHex = HexDump(_pdoView.OutputSnapshot.Span, bytesPerLine, maxBytes);
            TxHex = HexDump(_pdoView.InputSnapshot.Span, bytesPerLine, maxBytes);
        }

        private static string HexDump(ReadOnlySpan<byte> data, int bytesPerLine, int maxBytes)
        {
            if (data.Length == 0)
                return "";

            int len = data.Length;
            if (maxBytes > 0 && len > maxBytes)
                len = maxBytes;

            var sb = new StringBuilder(len * 3);

            int i = 0;
            while (i < len)
            {
                sb.Append("0x");
                sb.Append(i.ToString("X4"));
                sb.Append(": ");

                int end = i + bytesPerLine;
                if (end > len) end = len;

                for (int j = i; j < end; j++)
                {
                    sb.Append(data[j].ToString("X2"));
                    if (j + 1 < end) sb.Append(' ');
                }

                if (end < len) sb.AppendLine();
                i = end;
            }

            if (maxBytes > 0 && data.Length > maxBytes)
            {
                sb.AppendLine();
                sb.Append("... (truncated) total=");
                sb.Append(data.Length);
                sb.Append(" bytes");
            }

            return sb.ToString();
        }
    }
}