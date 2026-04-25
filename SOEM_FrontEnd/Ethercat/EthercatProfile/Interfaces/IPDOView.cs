using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Platform;

namespace SOEM_FrontEnd.Ethercat.EthercatProfile.Interfaces
{
    //외부에서 PDO상태를 보기 위한 인터페이스.
    //스냅샷 프로퍼티로 노출하고, PDO루프에서 Publish해줘야 됨.

    public interface IPDOView
    {
        ReadOnlyMemory<byte> OutputSnapshot { get; } //마스터 기준 출력
        ReadOnlyMemory<byte> InputSnapshot { get; } //마스터 기준 입력.
        void PublishSnapshots();

        //현재 Map 주소 표기용.

        IReadOnlyList<PdoMapRow> RxPdoMapRows { get; }
        IReadOnlyList<PdoMapRow> TxPdoMapRows { get; }

    }
}
