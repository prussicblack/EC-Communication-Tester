using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SOEM_FrontEnd.Ethercat.ESI
{
    public class ESIXMLData
    {
        public sealed class EsiDevice
        {
            public uint VendorId { get; set; }
            public uint ProductCode { get; set; }
            public uint RevisionNo { get; set; }
            public string Name { get; set; }
            public CoeProfile Coe { get; set; } = new CoeProfile();
            public DcInfo Dc { get; set; } = new DcInfo();
            public List<Pdo> RxPdos { get; set; } = new List<Pdo>();
            public List<Pdo> TxPdos { get; set; } = new List<Pdo>();
            public List<EcatObject> Objects { get; set; } = new List<EcatObject>();
        }

        public sealed class CoeProfile
        {
            public bool Enabled { get; set; }
            public bool SdoInfo { get; set; }
            public bool PdoAssign { get; set; }
            public bool PdoConfig { get; set; }
            public bool PdoUpload { get; set; }
        }

        public sealed class DcInfo
        {
            public bool Supported { get; set; }
            public long? CycleTime0Ns { get; set; }
            public long? CycleTime1Ns { get; set; }
            public long? ShiftNs { get; set; }
            public ushort? AssignActivate { get; set; }
        }

        public sealed class Pdo
        {
            public ushort Index { get; set; }
            public string Name { get; set; }
            public bool Default { get; set; }
            public List<PdoEntry> Entries { get; set; } = new List<PdoEntry>();
        }

        public sealed class PdoEntry
        {
            public ushort Index { get; set; }
            public byte SubIndex { get; set; }
            public int BitLen { get; set; }
            public string Name { get; set; }
        }

        public sealed class EcatObject
        {
            public ushort Index { get; set; }
            public string Name { get; set; }
            public string DataType { get; set; }  // e.g., UNSIGNED32, INTEGER16...
            public string Access { get; set; }    // ro/rw/wo/const 등
            public string Default { get; set; }   // 원문 보존(문자열)
            public bool? PdoMapping { get; set; } // true/false (없으면 null)
            public List<EcatSubItem> Subs { get; set; } = new List<EcatSubItem>();
        }

        public sealed class EcatSubItem
        {
            public byte SubIndex { get; set; }
            public string Name { get; set; }
            public string DataType { get; set; }
            public string Access { get; set; }
            public string Default { get; set; }
            public bool? PdoMapping { get; set; }
        }






    }
}
