using SOEM_FrontEnd.Ethercat.EthercatProfile;
using SOEM_FrontEnd.Ethercat.EthercatProfile.Interfaces;
using SOEM_FrontEnd.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SOEM_FrontEnd.Ethercat
{

    //402Profile 기준의 모터구동을 위한 클래스. PPMode. 전용임.

    //상속구조.
    //각 StateMachine에서 사용될 인터페이스 Run코드 필요.
    //PDO접근을 위한 Byte코드 필요.(PDO(RX)에서 읽어와서 PDO(TX)에서 써주는.)

    public sealed class NormalMotorWithPPMode : PDOBase, IEthercatStateTransition
    {

        private Dictionary<OdKey, PdoField> _rx = new Dictionary<OdKey, PdoField>(); // outputs
        private Dictionary<OdKey, PdoField> _tx = new Dictionary<OdKey, PdoField>(); // inputs

        private readonly ushort _SlaveNo;

        private int _off6040cw = -1; // Rx Offset.(매번 lookup 참조가 아니라 빠르게 접근용)
        private int _off6041sw = -1; // Tx Offset.
        private readonly EcClient _ECClient;

        public NormalMotorWithPPMode(int rxSize, int txSize, ushort slaveNo, EcClient ECClient) : base(rxSize, txSize)
        {
            _SlaveNo = slaveNo;
            _ECClient = ECClient;
        }


        bool IEthercatStateTransition.EnsureSafeOp(int timeoutMs)
        {
            //safeop로 넘어가기 전 실행될 코드.

            return true;
        }

        bool IEthercatStateTransition.EnsureOp(int timeoutMs)
        {
            //op로 넘어 가기 전 실행될 코드.
            //PP모드 전환 전 PP모드 변환.
            _ECClient.SetModePP(_SlaveNo);

            //초기 프로파일 입력.
            //외부에서 설정 가능하도록 처리할것.
            //_ECClient.SetProfile(_SlaveNo, 1000000, 5000000, 5000000); // 예: vel/acc/dec
            
            //초기 알람 클리어. //따로 해줄것.
            //_ECClient.SdoWriteI16(_SlaveNo, 0x6040, 00, 0x0080);  //slave alarm reset. SDO로 써도 먹네..

            return true;
        }

        public void SetPdoMapping(List<uint> rxAllMap, List<uint> txAllMap)
        {
            Build(_rx, rxAllMap);
            Build(_tx, txAllMap);

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
            _off6040cw = TryGetByteOffset(_rx, 0x6040, 0x00);
            _off6041sw = TryGetByteOffset(_tx, 0x6041, 0x00);
            return _off6040cw >= 0 && _off6041sw >= 0;
        }

        private static int TryGetByteOffset(Dictionary<OdKey, PdoField> dict, ushort idx, byte sub)
        {
            if (dict.TryGetValue(new OdKey(idx, sub), out var f))
                return f.ByteOffset;
            return -1;
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
