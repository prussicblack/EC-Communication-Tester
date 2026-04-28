using SOEM_FrontEnd.Ethercat.EthercatProfile;
using SOEM_FrontEnd.Ethercat.EthercatProfile.Interfaces;
using SOEM_FrontEnd.Model;
using SOEM_FrontEnd.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime;
using System.Runtime.InteropServices;
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
        
        public int MailboxLimitPerCycle { get; set; } = 4;


        public TimeSpan Period { get; set; } = TimeSpan.FromMilliseconds(1);
        public int ReceiveTimeoutUs { get; set; } = 2000;
        public int UiPublishDiv { get; set; } = 15; // 1ms 기준 약 60Hz

        //통계를 위한 프로퍼티 추가.
        public int StatsPublishDiv { get; set; } = 100; // 1ms 기준 100ms마다 snapshot publish

        //통계를 위한 필드 추가.
        //RT 통계 누적용(accumulator)
        private long _accLoopCount;

        private double _accLastDtUs;
        private double _accMinDtUs = double.MaxValue;
        private double _accMaxDtUs = double.MinValue;
        private double _accSumDtUs;

        private double _accLastJitterUs;
        private double _accMinJitterUs = double.MaxValue;
        private double _accMaxJitterUs = double.MinValue;
        private double _accSumAbsJitterUs;

        private long _accLateCycleCount;

        private int _accLastSendRc;
        private int _accMinSendRc = int.MaxValue;
        private int _accMaxSendRc = int.MinValue;
        private long _accSendErrorCount;

        private int _accLastReceiveRc;
        private int _accMinReceiveRc = int.MaxValue;
        private int _accMaxReceiveRc = int.MinValue;
        private long _accReceiveErrorCount;

        // 스냅샷으로 UI단 처리
        private int _pubSeq;

        private long _pubLoopCount;

        private double _pubLastDtUs;
        private double _pubMinDtUs;
        private double _pubMaxDtUs;
        private double _pubAvgDtUs;

        private double _pubLastJitterUs;
        private double _pubMinJitterUs;
        private double _pubMaxJitterUs;
        private double _pubAvgAbsJitterUs;

        private long _pubLateCycleCount;

        private int _pubLastSendRc;
        private int _pubMinSendRc;
        private int _pubMaxSendRc;
        private long _pubSendErrorCount;

        private int _pubLastReceiveRc;
        private int _pubMinReceiveRc;
        private int _pubMaxReceiveRc;
        private long _pubReceiveErrorCount;

        //통계 리셋.
        private volatile bool _reqStatsReset;

        public bool IsRunning
        {
            get
            {
                return _running;
            }
        }


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
                _tmpInBySlave[slave] = pdo.Input.Length > 0 ? new byte[pdo.Input.Length] : Array.Empty<byte>();
                _tmpOutBySlave[slave] = pdo.Output.Length > 0 ? new byte[pdo.Output.Length] : Array.Empty<byte>();
            }
        }

        public void Start()
        {
            if (_thread != null && _thread.IsAlive)
            {
                return;
            }

            ResetStatsInternal();

            _running = true;
            _thread = new Thread(ThreadMain);
            _thread.IsBackground = true;
            _thread.Priority = ThreadPriority.Highest; // 일반 스케줄러에서도 최우선
            _thread.Start();
        }

        private void ThreadMain()
        {
            IntPtr mmcssHandle = IntPtr.Zero;
            var oldLatency = GCSettings.LatencyMode;

            try
            {
                // MMCSS Pro Audio 등록
                mmcssHandle = MMCSSHelper.EnterProAudio(out int errorcode);

                if(errorcode != 0)
                {
                    throw new Exception("MMCSS Failed");
                }
                GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

                //ThreadAffinityHelper.PinCurrentThread(cpuIndex: 2); // 예: 코어 2 고정


                RunPdoLoop();
            }
            finally
            {
                GCSettings.LatencyMode = oldLatency;
                MMCSSHelper.LeaveMmcss(mmcssHandle);
            }
        }

        private void RunPdoLoop()
        {
            // 지터 측정
            var jitterSw = Stopwatch.StartNew();
            long lastTicks = jitterSw.ElapsedTicks;
            double ticksPerUs = (double)Stopwatch.Frequency / 1000000.0;
            double targetPeriodUs = Period.TotalMilliseconds * 1000.0;
            long loop = 0;
            
            EthercatRtLoop.Run(Period, () =>
            {
                if (!_running) 
                    return false;

                // ---- BeforeSend: Rx(출력 PDO) -> SOEM outputs ----
                //1.outputs: Rx -> SOEM outputs
                for (int i = 0; i < _binds.Count; i++)
                {
                    var (slave, pdo) = _binds[i];
                    var outBuf = _tmpOutBySlave[slave];
                    if (outBuf.Length > 0)
                    {
                        pdo.Output.CopyTo(outBuf);
                        SOEMNative.soem_write_bytes(slave, 0, outBuf, outBuf.Length); // 래퍼 필요
                    }
                }

                // ---- Send / Receive ----
                //2.send/recv
                int sendRc = _ec.SendProcessData();
                int recvRc = _ec.ReceiveProcessData(ReceiveTimeoutUs);

                //Mailbox handler 처리.
                //이거 잘 안도는데...나주엥 확인.
                // Mailbox cyclic handler pump (XoE support)
                //if (MailboxLimitPerCycle > 0)
                //    SOEMNative.soem_mbxhandler(0, MailboxLimitPerCycle);


                // ---- AfterReceive: SOEM inputs -> Tx(입력 PDO) ----
                //3.inputs: SOEM inputs -> TxWriteSpan
                for (int j = 0; j < _binds.Count; j++)
                {
                    var (slave, pdo) = _binds[j];
                    var inBuf = _tmpInBySlave[slave];
                    if (inBuf.Length > 0)
                    {
                        SOEMNative.soem_read_bytes(slave, 0, inBuf, inBuf.Length);
                        inBuf.AsSpan().CopyTo(pdo.InputWriteSpan);
                    }

                    _binds[j].Pdo.OnAfterPdoReceived();

                }

                loop++;

                if (UiPublishDiv > 0 && (loop % UiPublishDiv) == 0)
                {
                    for (int k = 0; k < _binds.Count; k++)
                    {
                        _binds[k].Pdo.PublishSnapshots(); //스냅샷이 뒤로 와야됨.
                    }
                }

                long nowTicks = jitterSw.ElapsedTicks;
                double dtUs = (nowTicks - lastTicks) / ticksPerUs;
                lastTicks = nowTicks;

                UpdateStats(dtUs, targetPeriodUs, sendRc, recvRc);

                if (StatsPublishDiv > 0 && (loop % StatsPublishDiv) == 0)
                {
                    PublishStatsSnapshot();
                }
                

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

        private void ResetStatsInternal()
        {
            _accLoopCount = 0;

            _accLastDtUs = 0.0;
            _accMinDtUs = double.MaxValue;
            _accMaxDtUs = double.MinValue;
            _accSumDtUs = 0.0;

            _accLastJitterUs = 0.0;
            _accMinJitterUs = double.MaxValue;
            _accMaxJitterUs = double.MinValue;
            _accSumAbsJitterUs = 0.0;

            _accLateCycleCount = 0;

            _accLastSendRc = 0;
            _accMinSendRc = int.MaxValue;
            _accMaxSendRc = int.MinValue;
            _accSendErrorCount = 0;

            _accLastReceiveRc = 0;
            _accMinReceiveRc = int.MaxValue;
            _accMaxReceiveRc = int.MinValue;
            _accReceiveErrorCount = 0;

            _pubSeq = 0;

            _pubLoopCount = 0;

            _pubLastDtUs = 0.0;
            _pubMinDtUs = 0.0;
            _pubMaxDtUs = 0.0;
            _pubAvgDtUs = 0.0;

            _pubLastJitterUs = 0.0;
            _pubMinJitterUs = 0.0;
            _pubMaxJitterUs = 0.0;
            _pubAvgAbsJitterUs = 0.0;

            _pubLateCycleCount = 0;

            _pubLastSendRc = 0;
            _pubMinSendRc = 0;
            _pubMaxSendRc = 0;
            _pubSendErrorCount = 0;

            _pubLastReceiveRc = 0;
            _pubMinReceiveRc = 0;
            _pubMaxReceiveRc = 0;
            _pubReceiveErrorCount = 0;
        }

        private void UpdateStats(double dtUs, double targetPeriodUs, int sendRc, int receiveRc)
        {
            if (_reqStatsReset)
            {
                _reqStatsReset = false;
                ResetStatsInternal();
            }

            double jitterUs = dtUs - targetPeriodUs;

            _accLoopCount++;

            _accLastDtUs = dtUs;
            _accSumDtUs += dtUs;

            if (dtUs < _accMinDtUs)
            {
                _accMinDtUs = dtUs;
            }

            if (dtUs > _accMaxDtUs)
            {
                _accMaxDtUs = dtUs;
            }

            _accLastJitterUs = jitterUs;
            _accSumAbsJitterUs += Math.Abs(jitterUs);

            if (jitterUs < _accMinJitterUs)
            {
                _accMinJitterUs = jitterUs;
            }

            if (jitterUs > _accMaxJitterUs)
            {
                _accMaxJitterUs = jitterUs;
            }

            if (dtUs > targetPeriodUs)
            {
                _accLateCycleCount++;
            }

            _accLastSendRc = sendRc;

            if (sendRc < _accMinSendRc)
            {
                _accMinSendRc = sendRc;
            }

            if (sendRc > _accMaxSendRc)
            {
                _accMaxSendRc = sendRc;
            }

            if (sendRc <= 0)
            {
                _accSendErrorCount++;
            }

            _accLastReceiveRc = receiveRc;

            if (receiveRc < _accMinReceiveRc)
            {
                _accMinReceiveRc = receiveRc;
            }

            if (receiveRc > _accMaxReceiveRc)
            {
                _accMaxReceiveRc = receiveRc;
            }

            if (receiveRc <= 0)
            {
                _accReceiveErrorCount++;
            }
        }

        private void PublishStatsSnapshot()
        {
            Interlocked.Increment(ref _pubSeq);

            _pubLoopCount = _accLoopCount;

            _pubLastDtUs = _accLastDtUs;
            _pubMinDtUs = _accMinDtUs == double.MaxValue ? 0.0 : _accMinDtUs;
            _pubMaxDtUs = _accMaxDtUs == double.MinValue ? 0.0 : _accMaxDtUs;
            _pubAvgDtUs = _accLoopCount > 0 ? (_accSumDtUs / (double)_accLoopCount) : 0.0;

            _pubLastJitterUs = _accLastJitterUs;
            _pubMinJitterUs = _accMinJitterUs == double.MaxValue ? 0.0 : _accMinJitterUs;
            _pubMaxJitterUs = _accMaxJitterUs == double.MinValue ? 0.0 : _accMaxJitterUs;
            _pubAvgAbsJitterUs = _accLoopCount > 0 ? (_accSumAbsJitterUs / (double)_accLoopCount) : 0.0;

            _pubLateCycleCount = _accLateCycleCount;

            _pubLastSendRc = _accLastSendRc;
            _pubMinSendRc = _accMinSendRc == int.MaxValue ? 0 : _accMinSendRc;
            _pubMaxSendRc = _accMaxSendRc == int.MinValue ? 0 : _accMaxSendRc;
            _pubSendErrorCount = _accSendErrorCount;

            _pubLastReceiveRc = _accLastReceiveRc;
            _pubMinReceiveRc = _accMinReceiveRc == int.MaxValue ? 0 : _accMinReceiveRc;
            _pubMaxReceiveRc = _accMaxReceiveRc == int.MinValue ? 0 : _accMaxReceiveRc;
            _pubReceiveErrorCount = _accReceiveErrorCount;

            Interlocked.Increment(ref _pubSeq);
        }

        public PdoRtStats GetStatsSnapshot()
        {
            while (true)
            {
                int seq1 = Volatile.Read(ref _pubSeq);
                if ((seq1 & 1) != 0)
                {
                    continue;
                }

                long loopCount = _pubLoopCount;

                double lastDtUs = _pubLastDtUs;
                double minDtUs = _pubMinDtUs;
                double maxDtUs = _pubMaxDtUs;
                double avgDtUs = _pubAvgDtUs;

                double lastJitterUs = _pubLastJitterUs;
                double minJitterUs = _pubMinJitterUs;
                double maxJitterUs = _pubMaxJitterUs;
                double avgAbsJitterUs = _pubAvgAbsJitterUs;

                long lateCycleCount = _pubLateCycleCount;

                int lastSendRc = _pubLastSendRc;
                int minSendRc = _pubMinSendRc;
                int maxSendRc = _pubMaxSendRc;
                long sendErrorCount = _pubSendErrorCount;

                int lastReceiveRc = _pubLastReceiveRc;
                int minReceiveRc = _pubMinReceiveRc;
                int maxReceiveRc = _pubMaxReceiveRc;
                long receiveErrorCount = _pubReceiveErrorCount;

                int seq2 = Volatile.Read(ref _pubSeq);
                if (seq1 != seq2)
                {
                    continue;
                }

                return new PdoRtStats(
                    loopCount,
                    lastDtUs,
                    minDtUs,
                    maxDtUs,
                    avgDtUs,
                    lastJitterUs,
                    minJitterUs,
                    maxJitterUs,
                    avgAbsJitterUs,
                    lateCycleCount,
                    lastSendRc,
                    minSendRc,
                    maxSendRc,
                    sendErrorCount,
                    lastReceiveRc,
                    minReceiveRc,
                    maxReceiveRc,
                    receiveErrorCount);
            }
        }

        public void ResetStats()
        {
            _reqStatsReset = true;
        }



    }

    //통계기능 추가.
    public readonly struct PdoRtStats
    {
        public PdoRtStats(
            long loopCount,
            double lastDtUs,
            double minDtUs,
            double maxDtUs,
            double avgDtUs,
            double lastJitterUs,
            double minJitterUs,
            double maxJitterUs,
            double avgAbsJitterUs,
            long lateCycleCount,
            int lastSendRc,
            int minSendRc,
            int maxSendRc,
            long sendErrorCount,
            int lastReceiveRc,
            int minReceiveRc,
            int maxReceiveRc,
            long receiveErrorCount)
        {
            LoopCount = loopCount;

            LastDtUs = lastDtUs;
            MinDtUs = minDtUs;
            MaxDtUs = maxDtUs;
            AvgDtUs = avgDtUs;

            LastJitterUs = lastJitterUs;
            MinJitterUs = minJitterUs;
            MaxJitterUs = maxJitterUs;
            AvgAbsJitterUs = avgAbsJitterUs;

            LateCycleCount = lateCycleCount;

            LastSendRc = lastSendRc;
            MinSendRc = minSendRc;
            MaxSendRc = maxSendRc;
            SendErrorCount = sendErrorCount;

            LastReceiveRc = lastReceiveRc;
            MinReceiveRc = minReceiveRc;
            MaxReceiveRc = maxReceiveRc;
            ReceiveErrorCount = receiveErrorCount;
        }

        public long LoopCount { get; }

        public double LastDtUs { get; }
        public double MinDtUs { get; }
        public double MaxDtUs { get; }
        public double AvgDtUs { get; }

        public double LastJitterUs { get; }
        public double MinJitterUs { get; }
        public double MaxJitterUs { get; }
        public double AvgAbsJitterUs { get; }

        public long LateCycleCount { get; }

        public int LastSendRc { get; }
        public int MinSendRc { get; }
        public int MaxSendRc { get; }
        public long SendErrorCount { get; }

        public int LastReceiveRc { get; }
        public int MinReceiveRc { get; }
        public int MaxReceiveRc { get; }
        public long ReceiveErrorCount { get; }
    }

    internal static class ThreadAffinityHelper
    {
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentThread();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern UIntPtr SetThreadAffinityMask(IntPtr hThread, UIntPtr dwThreadAffinityMask);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetThreadPriority(IntPtr hThread, int nPriority);

        // Win32 THREAD_PRIORITY_TIME_CRITICAL = 15
        private const int THREAD_PRIORITY_TIME_CRITICAL = 15;

        public static void PinCurrentThread(int cpuIndex)
        {
            if (cpuIndex < 0 || cpuIndex >= IntPtr.Size * 8)
                throw new ArgumentOutOfRangeException(nameof(cpuIndex));

            UIntPtr mask = (UIntPtr)(1UL << cpuIndex);
            IntPtr hThread = GetCurrentThread();

            UIntPtr prev = SetThreadAffinityMask(hThread, mask);
            if (prev == UIntPtr.Zero)
                throw new InvalidOperationException("SetThreadAffinityMask failed");

            // 선택: 스레드 우선순위도 추가 상승
            SetThreadPriority(hThread, THREAD_PRIORITY_TIME_CRITICAL);
        }
    }

}
