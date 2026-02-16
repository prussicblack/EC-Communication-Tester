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
    //엎긴 해야되네..
    public sealed class PDORTWorker : IDisposable
    {
        private readonly EcClient _ec;
        private readonly ushort _slave;

        // PDO 오프셋 (PP용으로 리맵해 둔 값 기준)
        private const int RX_OFF_CW = 0; // 0x6040
        private const int RX_OFF_TPOS = 2; // 0x607A

        private const int TX_OFF_SW = 2; // 0x6041
        private const int TX_OFF_POS = 5; // 0x6064

        private Thread _thread;
        private volatile bool _running;

        public PDORTWorker(EcClient ec, ushort slave)
        {
            _ec = ec;
            _slave = slave;
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
            // PP 모드라고 가정 (외부에서 6060=1, 프로파일/Enable까지 완료)
            ushort cwBase = 0x000F; // 장비에 맞게 조정

            int loop = 0;
            int currentTarget = 0;
            bool newSetPointBitHigh = false;
            bool goingPositive = true;

            // 지터 측정
            Stopwatch jitterSw = Stopwatch.StartNew();
            long lastTicks = jitterSw.ElapsedTicks;
            double ticksPerMs = (double)Stopwatch.Frequency / 1000.0;

            double lastPeriodMs = 0.0;
            double lastJitterUs = 0.0;
            double maxAbsJitterUs = 0.0;

            EthercatRtLoop.Run(TimeSpan.FromMilliseconds(1), () =>
            {
                if (!_running)
                    return false;

                // ----- 지터 측정 -----
                long nowTicks = jitterSw.ElapsedTicks;
                double dtMs = (nowTicks - lastTicks) / ticksPerMs;
                lastTicks = nowTicks;

                lastPeriodMs = dtMs;
                lastJitterUs = (dtMs - 1.0) * 1000.0;
                double absJitter = Math.Abs(lastJitterUs);
                if (absJitter > maxAbsJitterUs)
                {
                    maxAbsJitterUs = absJitter;
                }

                // ----- TxPDO 읽기 (저번 주기 결과) -----
                ushort sw = _ec.PdoReadU16(_slave, TX_OFF_SW);
                int actPos = _ec.PdoReadI32(_slave, TX_OFF_POS);
                bool targetReached = (sw & (1 << 10)) != 0; // bit10

                // ----- 새 타겟 + New set-point 펄스 결정 -----
                if (!newSetPointBitHigh)
                {
                    if (targetReached && (loop % 2000 == 0))
                    {
                        goingPositive = !goingPositive;
                        currentTarget = goingPositive ? 50000 : 0;
                        newSetPointBitHigh = true;
                    }
                }

                // ----- Controlword 구성 -----
                ushort cw = cwBase;
                cw |= (1 << 5); // Change immediately

                if (newSetPointBitHigh)
                {
                    cw |= (1 << 4); // New set-point
                }

                ushort Outout;
                //----출력테스트
                //나중에 PDOWrite Bit도 있어야 되겠는데?
                if (goingPositive)
                {
                    Outout = 0x0001;
                }
                else
                {
                    Outout = 0x0000;
                }

                // ----- RxPDO 쓰기 -----
                _ec.PdoWriteU16(_slave, RX_OFF_CW, cw);
                _ec.PdoWriteI32(_slave, RX_OFF_TPOS, currentTarget);
                //_ec.PdoWriteU16(0x09, 0, Outout);


                // ----- PDO 전송/수신 -----
                _ec.SendProcessData();
                _ec.ReceiveProcessData();

                // ----- 로그 -----
                if ((loop % 500) == 0)
                {
                    Console.WriteLine(
                        $"loop={loop}, period={lastPeriodMs:F3} ms, " +
                        $"jitter={lastJitterUs:F1} us, max|jitter|={maxAbsJitterUs:F1} us, " +
                        $"CW=0x{cw:X4}, SW=0x{sw:X4}, " +
                        $"TR={(targetReached ? 1 : 0)}, TPOS={currentTarget}, ACT={actPos}");
                }

                // bit4 펄스는 한 사이클만 유지하고 끔
                if (newSetPointBitHigh)
                {
                    newSetPointBitHigh = false;
                }

                loop++;

                // 테스트용: 60초 정도 돌리고 자동 종료
                //if (loop >= 600000)
                //    return false;

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
