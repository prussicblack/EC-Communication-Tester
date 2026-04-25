using SOEM_FrontEnd.Ethercat.EthercatProfile.Interfaces;
using SOEM_FrontEnd.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SOEM_FrontEnd.Ethercat.EthercatProfile
{
    //기본 Profile.
    //Bit관련 IO가 기본.

    //raw View를 기본으로.

    public sealed class NormalOProfile : PDOBase, IEthercatStateTransition
    {
        private readonly int _SlaveNo;

        private readonly EcClient _ECClient;


        private Dictionary<OdKey, PdoField> _rxMapTable = new Dictionary<OdKey, PdoField>(); // outputs

        private Dictionary<OdKey, PdoField> _txMapTable = new Dictionary<OdKey, PdoField>(); // inputs


        public NormalOProfile(int rxSize, int txSize, int slaveNo, EcClient EcClient) : base(rxSize, txSize)
        {
            _SlaveNo = slaveNo;
            _ECClient = EcClient;
        }

        bool IEthercatStateTransition.PrepareSafeOp(int timeoutMs)
        {
            //safeop로 넘어가기 전 실행될 코드.

            return true;
        }

        bool IEthercatStateTransition.PrepareOp(int timeoutMs)
        {
            //op로 넘어 가기 전 실행될 코드.
            return true;
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
