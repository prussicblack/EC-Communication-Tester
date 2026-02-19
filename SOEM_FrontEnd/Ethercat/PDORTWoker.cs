using SOEM_FrontEnd.Ethercat.EthercatProfile;
using SOEM_FrontEnd.Ethercat.EthercatProfile.Interfaces;
using SOEM_FrontEnd.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SOEM_FrontEnd.Ethercat
{
    public sealed class PDORTWorker : IDisposable
    {
        private readonly EcClient _ec;
        private List<(ushort Slave, PDOBase Pdo)> _binds = new();
        private byte[][] _tmpInBySlave = Array.Empty<byte[]>();
        private byte[][] _tmpOutBySlave = Array.Empty<byte[]>();

        private Thread _thread;
        private volatile bool _running;

        public TimeSpan Period { get; set; } = TimeSpan.FromMilliseconds(1);
        public int ReceiveTimeoutUs { get; set; } = 2000;

        public PDORTWorker(EcClient ec)
        {
            _ec = ec;
            _binds = new List<(ushort, PDOBase)>();
        }
        public void SetBinds(List<(ushort Slave, PDOBase Pdo)> binds, ushort slaveCount)
        {
            _binds = binds ?? new List<(ushort, PDOBase)>();

            _tmpInBySlave = new byte[slaveCount + 1][];
            _tmpOutBySlave = new byte[slaveCount + 1][];

            for (int i = 0; i < _binds.Count; i++)
            {
                var (slave, pdo) = _binds[i];
                _tmpInBySlave[slave] = pdo.Tx.Length > 0 ? new byte[pdo.Tx.Length] : Array.Empty<byte>();
                _tmpOutBySlave[slave] = pdo.Rx.Length > 0 ? new byte[pdo.Rx.Length] : Array.Empty<byte>();
            }
        }

        public void Start()
        {
            if (_thread != null && _thread.IsAlive)
            {
                return;
            }

            _running = true;
            _thread = new Thread(ThreadMain);
            _thread.IsBackground = true;
            _thread.Priority = ThreadPriority.Highest; // 일반 스케줄러에서도 최우선
            _thread.Start();
        }

        private void ThreadMain()
        {
            IntPtr mmcssHandle = IntPtr.Zero;
            try
            {
                // MMCSS Pro Audio 등록
                mmcssHandle = MMCSSHelper.EnterProAudio(out int errorcode);

                if(errorcode != 0)
                {
                    throw new Exception("MMCSS Failed");
                }

                RunPdoLoop();
            }
            finally
            {
                MMCSSHelper.LeaveMmcss(mmcssHandle);
            }
        }

        private void RunPdoLoop()
        {
            // 지터 측정
            var jitterSw = Stopwatch.StartNew();
            long lastTicks = jitterSw.ElapsedTicks;
            double ticksPerMs = (double)Stopwatch.Frequency / 1000.0;

            EthercatRtLoop.Run(Period, () =>
            {
                if (!_running) return false;

                // ---- BeforeSend: Rx(출력 PDO) → SOEM outputs ----
                // 1) outputs: Rx -> SOEM outputs
                for (int k = 0; k < _binds.Count; k++)
                {
                    var (slave, pdo) = _binds[k];
                    var outBuf = _tmpOutBySlave[slave];
                    if (outBuf.Length > 0)
                    {
                        pdo.Rx.CopyTo(outBuf);
                        SOEMNative.soem_write_bytes(slave, 0, outBuf, outBuf.Length); // 래퍼 필요
                    }
                }

                // ---- Send / Receive ----
                // 2) send/recv
                _ec.SendProcessData();
                _ec.ReceiveProcessData(ReceiveTimeoutUs);

                // ---- AfterReceive: SOEM inputs → Tx(입력 PDO) ----
                // 3) inputs: SOEM inputs -> TxWriteSpan
                for (int k = 0; k < _binds.Count; k++)
                {
                    var (slave, pdo) = _binds[k];
                    var inBuf = _tmpInBySlave[slave];
                    if (inBuf.Length > 0)
                    {
                        SOEMNative.soem_read_bytes(slave, 0, inBuf, inBuf.Length);
                        inBuf.AsSpan().CopyTo(pdo.TxWriteSpan);
                    }
                }

                // ---- jitter calc (원하면 외부로 뽑기) ----
                long nowTicks = jitterSw.ElapsedTicks;
                double dtMs = (nowTicks - lastTicks) / ticksPerMs;
                lastTicks = nowTicks;
                // dtMs, (dtMs - Period.TotalMilliseconds)*1000us 등 필요시 기록

                return true;
            });

        }

        public void Stop()
        {
            _running = false;
            if (_thread != null && _thread.IsAlive)
            {
                _thread.Join();
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
