using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Threading;


namespace SOEM_FrontEnd.Model
{
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
                throw new InvalidOperationException("SOEM init failed (ecx_init).");

            rc = SOEMNative.soem_config_init(0);
            if (rc != 0)
            {
                SOEMNative.soem_close(); 
                throw new InvalidOperationException("config_init failed.");
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

        public void EnsureState(ushort state, int timeoutMs)
        {
            int rc = SOEMNative.soem_set_state(state, timeoutMs);
            if (rc != 0) 
                throw new InvalidOperationException($"State transition failed -> 0x{state:X}.");
        }

        public int SlaveCount => SOEMNative.soem_slave_count();

        // ---------- SDO helpers ----------
        public byte[] SdoReadRaw(ushort slave, ushort index, byte subIndex, int maxLen = 64)
        {
            var buf = new byte[maxLen];
            uint len = (uint)maxLen;
            int rc = SOEMNative.soem_sdo_read(slave, index, subIndex, buf, ref len);
            if (rc != 0) throw new InvalidOperationException($"SDO read 0x{index:X4}:{subIndex} failed.");
            if (len < buf.Length) Array.Resize(ref buf, (int)len);
            return buf;
        }

        public void SdoWriteRaw(ushort slave, ushort index, byte subIndex, ReadOnlySpan<byte> data)
        {
            var arr = data.ToArray();
            int rc = SOEMNative.soem_sdo_write(slave, index, subIndex, arr, (uint)arr.Length);
            if (rc != 0) throw new InvalidOperationException($"SDO write 0x{index:X4}:{subIndex} failed.");
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
    }

    /// <summary>
    /// (선택) 10ms 주기 루프 헬퍼 — CPU 점유 낮추는 하이브리드 대기
    /// </summary>
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
}
