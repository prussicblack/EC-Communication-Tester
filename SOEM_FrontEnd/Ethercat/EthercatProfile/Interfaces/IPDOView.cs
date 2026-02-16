using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Platform;

namespace SOEM_FrontEnd.Ethercat.EthercatProfile.Interfaces
{
    public interface IPDOView
    {
        ReadOnlyMemory<byte> RxSnapshot { get; }
        ReadOnlyMemory<byte> TxSnapshot { get; }
        void PublishSnapshots();

    }
}
