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
    public sealed class NormalOProfile : PDOBase, IEthercatStateTransition
    {
        private readonly int _SlaveNo;

        private readonly EcClient _ECClient;


        public NormalOProfile(int rxSize, int txSize, int slaveNo, EcClient EcClient) : base(rxSize, txSize)
        {
            _SlaveNo = slaveNo;
            _ECClient = EcClient;
        }

        bool IEthercatStateTransition.EnsureSafeOp(int timeoutMs)
        {
            //safeop로 넘어가기 전 실행될 코드.

            return true;
        }

        bool IEthercatStateTransition.EnsureOp(int timeoutMs)
        {
            //op로 넘어 가기 전 실행될 코드.

            return true;
        }

    }
}
