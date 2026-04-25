using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SOEM_FrontEnd.Ethercat.EthercatProfile.Interfaces
{
    public sealed class PdoMapRow
    {
        public int No { get; set; }
        public uint Raw { get; set; }
        public ushort Index { get; set; }

        public byte SubIndex { get; set; }

        public byte BitLength { get; set; }

        public int BitOffset { get; set; }

        public int ByteOffset
        {
            get { return BitOffset >> 3; }
        }

        public int BitInByte
        {
            get { return BitOffset & 7; }
        }

        public string AddressText
        {
            get { return "0x" + Index.ToString("X4") + ":" + SubIndex.ToString("X2"); }
        }

        public string RawText
        {
            get { return "0x" + Raw.ToString("X8"); }
        }
    }
}
