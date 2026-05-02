using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SOEM_FrontEnd.Ethercat.MiniENI
{
    //ENI의 메인이 되는 클래스.
    public sealed class MiniENI
    {
        public int Version { get; set; } = 1;

        public string ProjectName { get; set; } = "";

        public bool AutoOpenAdapter { get; set; } = false;

        public bool AutoMoveToOp { get; set; } = false;

        public EniAdapterConfig Adapter { get; set; } = new EniAdapterConfig();

        //슬레이브 별로 별도 파일로 분리하는게 좋긴한데, 일단 그대로 사용.
        public List<EniSlaveConfig> Slaves { get; set; } = new List<EniSlaveConfig>();
    }

    //조건 체크.
    public sealed class EniValidationResult
    {
        public bool IsMatch { get; set; }

        public List<string> Messages { get; set; } = new List<string>();

        public string Summary
        {
            get
            {
                if (Messages.Count == 0)
                {
                    return "";
                }

                return string.Join("\r\n", Messages);
            }
        }
    }

    public sealed class EniAdapterConfig
    {
        //랜카드 이름(ifname)
        public string Name { get; set; } = "";

        public string Description { get; set; } = "";

        //해당 랜카드의 Mac Address
        public string MacAddress { get; set; } = "";
    }

    public sealed class EniSlaveConfig
    {
        public ushort SlaveNo { get; set; }

        public string Name { get; set; } = "";

        public string VendorId { get; set; } = "";

        public string ProductCode { get; set; } = "";

        public string RevisionNo { get; set; } = "";

        public int InputBits { get; set; }

        public int OutputBits { get; set; }

        //Auto 자동판별.
        public string Profile { get; set; } = "Auto";

        public List<EniStartupSdo> StartupSdos { get; set; } = new List<EniStartupSdo>();

        //public List<EniDisplayOverride> DisplayOverrides { get; set; } = new List<EniDisplayOverride>();

        public EniPdoMappingConfig PdoMapping { get; set; } = new EniPdoMappingConfig();
    }

    public sealed class EniStartupSdo
    {
        public string State { get; set; } = "PreOP";

        public string Index { get; set; } = "";

        public string SubIndex { get; set; } = "";

        public string Type { get; set; } = "";

        public string Value { get; set; } = "";
    }


    //나중에 ENI를 통한 PDO Mapping 에 사용할 기능.
    public sealed class EniPdoMappingConfig
    {
        public bool Enabled { get; set; } = false;

        public List<string> RxAssign { get; set; } = new List<string>();

        public List<string> TxAssign { get; set; } = new List<string>();

        public List<EniPdoMapObject> RxMaps { get; set; } = new List<EniPdoMapObject>();

        public List<EniPdoMapObject> TxMaps { get; set; } = new List<EniPdoMapObject>();
    }

    public sealed class EniPdoMapObject
    {
        public string MapIndex { get; set; } = "";

        public List<EniPdoMapEntry> Entries { get; set; } = new List<EniPdoMapEntry>();
    }

    public sealed class EniPdoMapEntry
    {
        public string Index { get; set; } = "";

        public string SubIndex { get; set; } = "";

        public int BitLength { get; set; }
    }


}
