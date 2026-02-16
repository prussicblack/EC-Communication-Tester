using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SOEM_FrontEnd.Ethercat.EthercatProfile.Interfaces
{
    public interface IEthercatStateTransition
    {
        //상태머신에서 작업하기 위해 불러오는 인터페이스.

        //상태머신 특정 위치에서 수행해야 하는 작업이 있을경우 사용할것.

        //402, PPMode에서 PP모드 설정 및 초기가속, 속도 설정 등의 초기화 수행 등.
        bool EnsurePreOp(int timeoutMs);
        bool EnsureSafeOp(int timeoutMs);
        bool EnsureOp(int timeoutMs);
    }
}
