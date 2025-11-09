using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static SOEM_FrontEnd.Ethercat.ESI.ESIXMLData;

namespace SOEM_FrontEnd.Ethercat.ESI
{
    //ESI 데이터 타입 정의.
    //EEPROM같은거 생략.
    public class ESIXMLData
    {
        public sealed class EsiFile
        {
            public uint VendorId { get; set; }
            public string VendorName { get; set; } = "";

            public string ImageData { get; set; } = "";
            public List<ESIDevice> Devices { get; set; } = new List<ESIDevice>();
        }

        public sealed class ESIDevice
        {
            public uint ProductCode { get; set; }
            public uint Revision { get; set; }

            public string Name { get; set; } = "";
            public List<string> ProfileNo { get; set; } = new();

            public Dictionary<string, ESIDataType> Datatypes = new Dictionary<string, ESIDataType>();

            public Dictionary<string, ESISDOObject> SDOObjects = new();
            //Key는 TextID, Value는 Message.
            public Dictionary<string, ESIDiagMessage> DiagMessages = new Dictionary<string, ESIDiagMessage>();

            public List<ESIPDO> RxPdos { get; set; } = new List<ESIPDO>();
            public List<ESIPDO> TxPdos { get; set; } = new List<ESIPDO>();
            public List<ESIDC> DC { get; set; } = new List<ESIDC>();
        }

        public sealed class ESIPDO
        {
            public ushort Index { get; set; }
            public string Name { get; set; } = "";
            public List<ESIPDOEntry> Entries { get; set; } = new();
        }

        public sealed class ESIPDOEntry
        {
            public ushort Index { get; set; }
            public byte SubIndex { get; set; }
            public int BitLength { get; set; }
            public string Name { get; set; } = "";
            public string DataType { get; set; } = "";
        }

        public sealed class ESIDiagMessage
        {
            public string TextID { get; set; }
            public string MessageText { get; set; }
        }

        public sealed class ESISDOObject
        {
            public ushort Index { get; set; }
            public string Name { get; set; } = "";
            public string DataType { get; set; } = "";
            public string BitSize { get; set; } = "";
            public Flags Flags { get; set; } = new();
            public List<ESISDOSubObject> SubObjects { get; set; } = new();
        }

        public sealed class Flags
        {
            public string Access { get; set; } = "";
            public string Category { get; set; } = "";
            public string PdoMapping { get; set; } = "";
        }

        public sealed class ESISDOSubObject
        {
            public byte SubIndex { get; set; }
            public string Name { get; set; } = "";
            public string DataType { get; set; } = "";
            public string BitSize { get; set; } = "";
            public int BitLength { get; set; }

            public Flags Flag { get; set; } = new();
        }

        public sealed class ESIDC
        {
            public string Name { get; set; } = "";
            public string Desc { get; set; } = "";
            public string AssignActivate { get; set; } = "";
            public string CycleTimeSync0 { get; set; } = "";
            public string ShiftTimeSync0 { get; set; } = "";
            public string CycleTimeSync1 { get; set; } = "";

        }

        public sealed class ESIDataType
        {
            public string Name { get; set; } = "";
            public ushort BitSize { get; set; }

            public string BaseType { get; set; } = "";

            public List<ESISubDataType> SubType { get; set; } = new List<ESISubDataType>();
        }

        public sealed class ESISubDataType
        {
            public int SubIdx { get; set; }
            public string Name { get; set; } = "";
            public ushort BitSize { get; set; }
            public ushort BitOffs { get; set; }
            public string Type { get; set; } = "";

            public Flags Flag { get; set; } = new();
        }


    }
}
