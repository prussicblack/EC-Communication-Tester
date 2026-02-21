using SOEM_FrontEnd.Ethercat.EthercatProfile;
using SOEM_FrontEnd.Ethercat.EthercatProfile.Interfaces;
using SOEM_FrontEnd.Model;
using System;
using System.Collections.Generic;
using System.Buffers.Binary;

namespace SOEM_FrontEnd.Ethercat
{

    //402Profile 기준의 모터구동을 위한 클래스. PPMode. 전용임.

    //상속구조.
    //각 StateMachine에서 사용될 인터페이스 Run코드 필요.
    //PDO접근을 위한 Byte코드 필요.(PDO(RX)에서 읽어와서 PDO(TX)에서 써주는.)

    //상속구조 정리.

    //PDOBase에서 PDO루프에서 통신담당.
    //IEthercatStateTransition에서 preop, safeop, op 간 이동시 매크로 작성.
    //IMotorCommands에서 ViewModel 통신담당. 

    public sealed class NormalMotorWithPPMode : PDOBase, IEthercatStateTransition, IMotorCommands
    {

        private Dictionary<OdKey, PdoField> _rxMapTable = new Dictionary<OdKey, PdoField>(); // outputs
        private Dictionary<OdKey, PdoField> _txMapTable = new Dictionary<OdKey, PdoField>(); // inputs
        //EZServo 기준으로, 
        //rxMapTable 0x6040(CW), 0x607a(Target Position) 존재.
        //txMapTable 0x6041(SW), 0x6064(Actual Position) 존재.


        private readonly ushort _SlaveNo;

        private int _off6040cw = -1; // Rx Offset.(매번 lookup 참조가 아니라 빠르게 접근용)
        private int _off6041sw = -1; // Tx Offset.
        private readonly EcClient _ECClient;

        //IMotorCommands 구현부.
        public int AxisID => throw new NotImplementedException();

        public bool IsServoOn => throw new NotImplementedException();

        public bool IsHome => throw new NotImplementedException();

        public bool IsError => throw new NotImplementedException();

        public int ActualPosition => getActualPosition();

        private int getActualPosition()
        {
            bool ret = TryReadActualPosition6064(out int actualPos);
            if (ret == true)
            {
                return actualPos;
            }

            return 0;

        }

        public NormalMotorWithPPMode(int rxSize, int txSize, ushort slaveNo, EcClient ECClient) : base(rxSize, txSize)
        {
            _SlaveNo = slaveNo;
            _ECClient = ECClient;
        }


        bool IEthercatStateTransition.PrepareSafeOp(int timeoutMs)
        {
            //safeop로 넘어가기 전 실행될 코드.

            return true;
        }

        bool IEthercatStateTransition.PrepareOp(int timeoutMs)
        {
            //op로 넘어 가기 전 실행될 코드.
            //PP모드 전환 전 PP모드 변환.
            //_ECClient.SetModePP(_SlaveNo);

            _ECClient.SdoWriteI8(_SlaveNo, 0x6060, 0x00, 1); //PPMode 1

            //초기 프로파일 입력.
            //외부에서 설정 가능하도록 처리할것.
            //_ECClient.SetProfile(_SlaveNo, 1000000, 5000000, 5000000); // 예: vel/acc/dec
            //_ECClient.SdoWriteU32(_SlaveNo, 0x6081, 0x00, vel);
            //_ECClient.SdoWriteU32(_SlaveNo, 0x6083, 0x00, acc);
            //_ECClient.SdoWriteU32(_SlaveNo, 0x6084, 0x00, dec);

            //초기 알람 클리어. //따로 해줄것.
            //_ECClient.SdoWriteI16(_SlaveNo, 0x6040, 00, 0x0080);  //slave alarm reset. SDO로 써도 먹네..

            return true;
        }

        public void SetPdoMapping(List<uint> rxAllMap, List<uint> txAllMap)
        {
            Build(_rxMapTable, rxAllMap);
            Build(_txMapTable, txAllMap);

            TryResolve402();
        }
        private static void Build(Dictionary<OdKey, PdoField> dict, List<uint> allmap)
        {
            dict.Clear();

            int bitOffset = 0;
            for (int i = 0; i < allmap.Count; i++)
            {
                uint mapWord = allmap[i];

                ushort idx = (ushort)(mapWord >> 16);
                byte sub = (byte)(mapWord >> 8);
                byte bitLen = (byte)(mapWord & 0xFF);

                var key = new OdKey(idx, sub);
                if (dict.ContainsKey(key))
                {
                    bitOffset += bitLen;
                    continue;
                }

                dict.Add(key, new PdoField(bitOffset, bitLen));

                bitOffset += bitLen;
            }
        }

        public bool TryResolve402()
        {
            _off6040cw = TryGetByteOffset(_rxMapTable, 0x6040, 0x00);
            _off6041sw = TryGetByteOffset(_txMapTable, 0x6041, 0x00);
            return _off6040cw >= 0 && _off6041sw >= 0;
        }

        private static int TryGetByteOffset(Dictionary<OdKey, PdoField> dict, ushort idx, byte sub)
        {
            if (dict.TryGetValue(new OdKey(idx, sub), out var f))
                return f.ByteOffset;
            return -1;
        }

        public bool TryReadTxI32(ushort idx, byte sub, out int value)
        {
            value = 0;

            if (!_txMapTable.TryGetValue(new OdKey(idx, sub), out var f))
                return false;

            // PDO는 보통 byte-aligned로 매핑됨 (0x6064는 INT32=32bit)
            if (f.BitLen != 32 || f.BitInByte != 0)
                return false;

            var span = InputSnapshot.Span; // Slave→Master (TxPDO) snapshot
            int off = f.ByteOffset;
            if ((uint)off + 4u > (uint)span.Length)
                return false;

            value = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(off, 4));

            return true;
        }

        public bool TryReadTxU16(ushort idx, byte sub, out ushort value)
        {
            value = 0;

            if (!_txMapTable.TryGetValue(new OdKey(idx, sub), out var f))
                return false;

            if (f.BitLen != 16 || f.BitInByte != 0)
                return false;

            var span = InputSnapshot.Span;
            int off = f.ByteOffset;

            if ((uint)off + 2u > (uint)span.Length)
                return false;

            value = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(off, 2));
            return true;
        }

        // === 자주 쓰는 래퍼 ===
        public bool TryReadActualPosition6064(out int actualPos)
        {
            return TryReadTxI32(0x6064, 0x00, out actualPos);
        }

        public bool TryReadStatusword6041(out ushort statusword)
        {
            return TryReadTxU16(0x6041, 0x00, out statusword);
        }



        //IMotorCommand 구현부.
        public bool MoveABS(double position)
        {
            throw new NotImplementedException();
        }

        public bool MoveINC(double position)
        {
            throw new NotImplementedException();
        }

        public bool Stop()
        {
            throw new NotImplementedException();
        }

        public bool JogPlus()
        {
            throw new NotImplementedException();
        }

        public bool JogMinus()
        {
            throw new NotImplementedException();
        }

        public bool JogStop()
        {
            throw new NotImplementedException();
        }

        public bool AlarmClear()
        {
            throw new NotImplementedException();
        }

        public bool ServoOn()
        {
            throw new NotImplementedException();
        }

        public bool ServoOff()
        {
            throw new NotImplementedException();
        }

        public bool Home()
        {
            throw new NotImplementedException();
        }

        public readonly struct PdoMapEntry
        {
            public readonly ushort Index;
            public readonly byte SubIndex;
            public readonly byte BitLen;

            public readonly int BitOffset;   // 누적
            public int ByteOffset => BitOffset >> 3;  // BitOffset / 8
            public int BitInByte => BitOffset & 7;   // BitOffset % 8

            public PdoMapEntry(ushort index, byte subIndex, byte bitLen, int bitOffset)
            {
                Index = index;
                SubIndex = subIndex;
                BitLen = bitLen;
                BitOffset = bitOffset;
            }
            public static PdoMapEntry FromMapWord(uint mapWord, int bitOffset)
            {
                ushort idx = (ushort)(mapWord >> 16);
                byte sub = (byte)(mapWord >> 8);
                byte bitLen = (byte)(mapWord & 0xFF);
                return new PdoMapEntry(idx, sub, bitLen, bitOffset);
            }
        }


        private readonly struct OdKey : IEquatable<OdKey>
        {
            public readonly ushort Index;
            public readonly byte SubIndex;

            public OdKey(ushort index, byte subIndex) { Index = index; SubIndex = subIndex; }

            public bool Equals(OdKey other) => Index == other.Index && SubIndex == other.SubIndex;
            public override bool Equals(object obj) => obj is OdKey other && Equals(other);
            public override int GetHashCode() => (Index << 8) ^ SubIndex;
        }

        private readonly struct PdoField
        {
            public readonly int BitOffset;
            public readonly byte BitLen;
            public PdoField(int bitOffset, byte bitLen) { BitOffset = bitOffset; BitLen = bitLen; }

            public int ByteOffset => BitOffset >> 3;
            public int BitInByte => BitOffset & 7;
        }


    }
}
