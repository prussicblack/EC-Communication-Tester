using SOEM_FrontEnd.Ethercat.EthercatProfile;
using SOEM_FrontEnd.Ethercat.EthercatProfile.Interfaces;
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

        public NormalMotorWithPPMode(int rxSize, int txSize) : base(rxSize, txSize)
        {


        }


        bool IEthercatStateTransition.EnsurePreOp(int timeoutMs)
        {
            //preop로 넘어가기 전 실행될 코드.

            return true;
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


        //여기까지.







    }
}
