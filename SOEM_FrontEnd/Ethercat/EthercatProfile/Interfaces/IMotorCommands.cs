using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SOEM_FrontEnd.Ethercat.EthercatProfile.Interfaces
{
    public interface IMotorCommands
    {
        //UI 혹은 기타 외부가 모터에 대한 접근을 위해 사용되는 인터페이스.
        //일단은 미사용.
        void MoveABS(double position);

        void MoveINC(double position);

        void Stop();

        void JogPlus();
        void JogMinus();
        void JogStop();

        void AlarmClear();
        void ServoOn();
        void ServoOff();

        void Home();

        int AxisID { get; }
        bool IsServoOn { get; }

        bool IsHome { get; }

        bool IsError { get; }

        int ActualPosition { get; }
    }
}
