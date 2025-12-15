using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace SOEM_FrontEnd.Model
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct SoemSlaveInfo
    {
        public ushort alias;       // Station Alias
        public ushort configadr;   // Station Address
        public uint vendor;        // eep_man
        public uint product;       // eep_id
        public uint revision;   // 리비전

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]  // EC_MAXNAME 기본값 64
        public string name;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SoemErrorInfo
    {
        public int ErrorCode;
        public ushort Slave;
        public ushort Index;
        public byte SubIndex;
    }

    internal static class SOEMNative
    {
        private const string Dll = "soem_wrap"; // soem_wrap.dll

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        internal static extern int soem_open(string ifname);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void soem_close();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int soem_config_init(int useMap);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int soem_config_map_only();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int soem_config_init_only();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int soem_set_state(ushort state, int timeoutMs);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int soem_slave_count();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int soem_sdo_read(ushort slv, ushort idx, byte sub,
            byte[] buf, ref uint inoutLen);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int soem_sdo_write(ushort slv, ushort idx, byte sub,
            byte[] data, uint len);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int soem_send_processdata();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int soem_receive_processdata(int timeoutUs);

        // (옵션) PDO 직접 접근 유틸 — 래퍼 DLL에 넣었으면 사용
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int soem_write_u16(ushort slv, int off, ushort v);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int soem_write_s32(ushort slv, int off, int v);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int soem_read_u16(ushort slv, int off, out ushort v);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int soem_read_s32(ushort slv, int off, out int v);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int soem_get_slave_info(int idx, out SoemSlaveInfo info);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ushort soem_slave_state(int idx);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ushort soem_slave_al_status(int idx);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void soem_readstate();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int soem_get_last_error_info(out SoemErrorInfo info);


    }

    /// <summary>
    /// 간단 사용용 관리 래퍼. 예외로 오류 처리, 리틀엔디안 보조 포함.
    /// </summary>
    public sealed class EcClient : IDisposable
    {
        public const ushort EC_STATE_INIT = 0x0001;
        public const ushort EC_STATE_PRE_OP = 0x0002;
        public const ushort EC_STATE_SAFE_OP = 0x0004;
        public const ushort EC_STATE_OPERATIONAL = 0x0008;

        public bool IsOpen { get; private set; }

        public void Open(string ifname, int opTimeoutMs = 2000)
        {
            int rc = SOEMNative.soem_open(ifname);
            if (rc != 0)
            {
                Console.WriteLine("SOEM init failed (ecx_init).");
                //throw new InvalidOperationException("SOEM init failed (ecx_init).");
            }

            rc = SOEMNative.soem_config_init(1);
            if (rc != 0)
            {
                SOEMNative.soem_close(); 
                //throw new InvalidOperationException("config_init failed.");
                Console.WriteLine("config_init failed.");
            }

            // SAFE_OP → (옵션) OP
            //EnsureState(EC_STATE_SAFE_OP, 2000);
            //if (mapPdo)
            //{
            //    // PDO 한 번 왕복 후 OP로 올리는 패턴 권장
            //    SOEMNative.soem_send_processdata();
            //    EnsureState(EC_STATE_OPERATIONAL, opTimeoutMs);
            //}

            IsOpen = true;
        }

        public int SlaveInfo(int index, out SoemSlaveInfo info)
        {
            int rc = SOEMNative.soem_get_slave_info(index, out info);
            if (rc != 1)
            {
                Console.WriteLine($"Failed to get slave info for index {index}.");

                //throw new InvalidOperationException($"Failed to get slave info for index {index}.");
            }

            return rc;
        }

        public uint MakeMapWord(ushort index, byte subIndex, byte bitLen)
        {
            // CoE PDO mapword: (Index << 16) | (SubIndex << 8) | BitLen
            return (uint)((index << 16) | (subIndex << 8) | bitLen);
        }

        public void RebuildPdoMap()
        {
            int rc = SOEMNative.soem_config_map_only();
            if (rc < 0)
            {
                Console.WriteLine($"PDO map rebuild failed.");

                //throw new InvalidOperationException("PDO map rebuild failed.");
            }
        }

        public void EnsureState(ushort state, int timeoutMs)
        {
            int rc = SOEMNative.soem_set_state(state, timeoutMs);
            if (rc != 0)
            {
                Console.WriteLine($"State transition failed -> 0x{state:X}.");
                //throw new InvalidOperationException($"State transition failed -> 0x{state:X}.");
            }
        }

        public int SlaveCount => SOEMNative.soem_slave_count();

        // ---------- SDO helpers ----------
        public byte[] SdoReadRaw(ushort slave, ushort index, byte subIndex, int maxLen = 64)
        {
            var buf = new byte[maxLen];
            uint len = (uint)maxLen;
            int rc = SOEMNative.soem_sdo_read(slave, index, subIndex, buf, ref len);
            if (rc != 0)
            {
                Console.WriteLine($"SDO read 0x{index:X4}:{subIndex} failed.");

                var info = GetLastErrorInfo();
                var sb = new StringBuilder();
                sb.AppendFormat("SDO read 0x{0:X4}:{1} failed.", index, subIndex);

                if (info.HasValue)
                {
                    sb.AppendFormat(" [Err=0x{0:X8}, Slave={1}, Idx=0x{2:X4}, Sub={3}]",
                        info.Value.ErrorCode,
                        info.Value.Slave,
                        info.Value.Index,
                        info.Value.SubIndex);
                }

                //throw new InvalidOperationException($"SDO read 0x{index:X4}:{subIndex} failed.");
            }
            if (len < buf.Length) 
                Array.Resize(ref buf, (int)len);
            return buf;
        }

        public void SdoWriteRaw(ushort slave, ushort index, byte subIndex, ReadOnlySpan<byte> data)
        {
            var arr = data.ToArray();
            int rc = SOEMNative.soem_sdo_write(slave, index, subIndex, arr, (uint)arr.Length);
            if (rc != 0)
            {
                Console.WriteLine($"SDO write 0x{index:X4}:{subIndex} failed.");

                var info = GetLastErrorInfo();
                var sb = new StringBuilder();
                sb.AppendFormat("SDO write 0x{0:X4}:{1} failed.", index, subIndex);

                if (info.HasValue)
                {
                    sb.AppendFormat(" [Err=0x{0:X8}, Slave={1}, Idx=0x{2:X4}, Sub={3}]",
                        info.Value.ErrorCode,
                        info.Value.Slave,
                        info.Value.Index,
                        info.Value.SubIndex);
                }

                //throw new InvalidOperationException($"SDO write 0x{index:X4}:{subIndex} failed.");
            }
        }

        // Typed (리틀엔디안 가정)
        public byte SdoReadU8(ushort s, ushort idx, byte sub) => SdoReadRaw(s, idx, sub, 1)[0];
        public sbyte SdoReadI8(ushort s, ushort idx, byte sub) => unchecked((sbyte)SdoReadU8(s, idx, sub));
        public ushort SdoReadU16(ushort s, ushort idx, byte sub) => BitConverter.ToUInt16(SdoReadRaw(s, idx, sub, 2), 0);
        public short SdoReadI16(ushort s, ushort idx, byte sub) => BitConverter.ToInt16(SdoReadRaw(s, idx, sub, 2), 0);
        public uint SdoReadU32(ushort s, ushort idx, byte sub) => BitConverter.ToUInt32(SdoReadRaw(s, idx, sub, 4), 0);
        public int SdoReadI32(ushort s, ushort idx, byte sub) => BitConverter.ToInt32(SdoReadRaw(s, idx, sub, 4), 0);

        public void SdoWriteU8(ushort s, ushort idx, byte sub, byte v) => SdoWriteRaw(s, idx, sub, new[] { v });
        public void SdoWriteI8(ushort s, ushort idx, byte sub, sbyte v) => SdoWriteU8(s, idx, sub, unchecked((byte)v));
        public void SdoWriteU16(ushort s, ushort idx, byte sub, ushort v) => SdoWriteRaw(s, idx, sub, BitConverter.GetBytes(v));
        public void SdoWriteI16(ushort s, ushort idx, byte sub, short v) => SdoWriteRaw(s, idx, sub, BitConverter.GetBytes(v));
        public void SdoWriteU32(ushort s, ushort idx, byte sub, uint v) => SdoWriteRaw(s, idx, sub, BitConverter.GetBytes(v));
        public void SdoWriteI32(ushort s, ushort idx, byte sub, int v) => SdoWriteRaw(s, idx, sub, BitConverter.GetBytes(v));

        // ---------- PDO cycle ----------
        public int SendProcessData() => SOEMNative.soem_send_processdata();
        public int ReceiveProcessData(int timeoutUs = 2000) => SOEMNative.soem_receive_processdata(timeoutUs);

        // (옵션) PDO 오프셋 R/W — 래퍼 DLL에 해당 함수가 있을 때만 사용
        public void PdoWriteU16(ushort slave, int offset, ushort v) =>
            SOEMNative.soem_write_u16(slave, offset, v);
        public void PdoWriteI32(ushort slave, int offset, int v) =>
            SOEMNative.soem_write_s32(slave, offset, v);
        public ushort PdoReadU16(ushort slave, int offset)
        {
            SOEMNative.soem_read_u16(slave, offset, out var val);
            return val;
        }
        public int PdoReadI32(ushort slave, int offset)
        {
            SOEMNative.soem_read_s32(slave, offset, out var val);
            return val;
        }

        // ---------- PP 모드 빠른 시퀀스(예시) ----------
        public void SetModePP(ushort slave) => SdoWriteI8(slave, 0x6060, 0x00, 1);
        public void SetProfile(ushort slave, uint vel, uint acc, uint dec)
        {
            SdoWriteU32(slave, 0x6081, 0x00, vel);
            SdoWriteU32(slave, 0x6083, 0x00, acc);
            SdoWriteU32(slave, 0x6084, 0x00, dec);
        }
        public void PpMoveAbs(ushort slave, int targetCounts, bool changeImmediately = false)
        {
            // Target Position
            SdoWriteI32(slave, 0x607A, 0x00, targetCounts);

            // Controlword: New Set-Point(bit4) 토글(+ enable 비트는 장치 상태에 따라 사전 세팅돼 있어야 함)
            ushort cw = 0x000F; // 예: Switch on + Enable operation까지 완료된 상태라고 가정
            if (changeImmediately) cw |= (1 << 5); // bit5
            SdoWriteU16(slave, 0x6040, 0x00, (ushort)(cw | (1 << 4))); // set bit4=1
            SdoWriteU16(slave, 0x6040, 0x00, cw);                      // clear bit4=0
        }

        public void Dispose()
        {
            if (IsOpen)
            {
                SOEMNative.soem_close();
                IsOpen = false;
            }
        }

        public SoemErrorInfo? GetLastErrorInfo()
        {
            try
            {
                SoemErrorInfo err;
                int rc = SOEMNative.soem_get_last_error_info(out err);
                if (rc > 0)
                    return (err);
            }
            catch
            {
                // 래퍼 에러는 무시
            }
            return (null);
        }


    }


    public static class HiResLoop
    {
        // 필요 시: MMCSS 등록/우선순위 상승은 앞서 얘기한 방식 적용
        public static void Run(TimeSpan period, Func<bool> body)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            double ticksPerMs = System.Diagnostics.Stopwatch.Frequency / 1000.0;
            long next = (long)(sw.ElapsedTicks + period.TotalMilliseconds * ticksPerMs);

            while (true)
            {
                long now = sw.ElapsedTicks;
                double msLeft = (next - now) / ticksPerMs;

                if (msLeft > 1.0)
                {
                    // coarse sleep
                    Thread.Sleep((int)(msLeft - 0.5));
                }
                else
                {
                    // short spin
                    while (sw.ElapsedTicks < next) { /* spin */ }
                }

                if (!body()) break;
                next += (long)(period.TotalMilliseconds * ticksPerMs);

                // lag catch-up
                long lag = sw.ElapsedTicks - next;
                if (lag > 0)
                {
                    long per = (long)(period.TotalMilliseconds * ticksPerMs);
                    next += ((lag / per) + 1) * per;
                }
            }
        }

    }
    public static class EthercatRtLoop
    {
        /// <summary>
        /// Sleep을 최소화하고 Spin 위주로 도는 1kHz 근접 루프.
        /// period는 보통 1ms 기준으로 사용.
        /// </summary>
        public static void Run(TimeSpan period, Func<bool> body)
        {
            Stopwatch sw = Stopwatch.StartNew();
            double ticksPerMs = (double)Stopwatch.Frequency / 1000.0;
            long periodTicks = (long)(period.TotalMilliseconds * ticksPerMs);
            long next = sw.ElapsedTicks + periodTicks;

            while (true)
            {
                // 남은 시간 계산
                long now = sw.ElapsedTicks;
                double msLeft = (next - now) / ticksPerMs;

                // 아주 많이 남았으면 짧게만 Sleep (양자 문제 있지만, 여기선 거의 안 걸리게 할 거라 영향 적음)
                if (msLeft > 2.0)
                {
                    int sleepMs = (int)(msLeft - 1.0);
                    if (sleepMs > 0)
                        Thread.Sleep(sleepMs);
                }

                // 나머지는 SpinWait 위주
                while (true)
                {
                    long t = sw.ElapsedTicks;
                    if (t >= next)
                        break;

                    // 남은 시간에 따라 Spin 강도 조절
                    double leftUs = (next - t) * 1000.0 / ticksPerMs;
                    if (leftUs > 200.0)
                        Thread.SpinWait(200);
                    else if (leftUs > 50.0)
                        Thread.SpinWait(50);
                    else
                        Thread.SpinWait(10);
                }

                if (!body())
                    break;

                next += periodTicks;

                long lag = sw.ElapsedTicks - next;
                if (lag > 0)
                {
                    long skip = (lag / periodTicks + 1) * periodTicks;
                    next += skip;
                }
            }
        }
    }
}
