using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SOEM_FrontEnd.Ethercat.EthercatProfile.Interfaces
{
    public interface IPDOParameterWriter
    {
        bool TryWritePdoParameter(ushort index, byte subIndex, long value);
    }
}
