using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SOEM_FrontEnd.Ethercat.EthercatProfile.Interfaces
{
    public interface IPDOAccess
    {
        //PDO에서 읽어와야 하는 데이터들.
        //PDO는 이 인터페이스로 접근하므로 공통으로 존재해야함.

        ReadOnlySpan<byte> Tx { get; } // Slave -> Master (입력)
        Span<byte> Rx { get; }         // Master -> Slave (출력)

    }
}
