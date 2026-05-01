using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SOEM_FrontEnd.Ethercat.EthercatProfile.Interfaces
{
    public interface IValuePdoView
    {
        IReadOnlyList<ValueChannelDefinition> ValueDefinitions { get; }

        long ValueSequence { get; }

        ValueSnapshotFrame GetValueSnapshot();

    }

    public enum ValuePdoDirection
    {
        Input = 0,   // TxPDO: slave -> master
        Output = 1   // RxPDO: master -> slave
    }

    public sealed class ValueChannelDefinition
    {
        public int SlaveNo { get; set; }
        public int ChannelNo { get; set; }

        public ValuePdoDirection Direction { get; set; }

        public ushort BaseIndex { get; set; }

        public ushort Index { get; set; }
        public byte SubIndex { get; set; }

        public string Name { get; set; } = "";
        public string DataType { get; set; } = "";

        public int BitLength { get; set; }
        public int ByteOffset { get; set; }
        public int BitInByte { get; set; }

        public ValueRawKind RawKind { get; set; }

        public string AddressText
        {
            get
            {
                return "0x" + Index.ToString("X4") + ":" + SubIndex.ToString("X2");
            }
        }
    }

    public sealed class ValueSnapshotFrame
    {
        public long Sequence { get; set; }

        public ValueChannelSnapshot[] Channels { get; set; }

        public ValueSnapshotFrame()
        {
            Channels = new ValueChannelSnapshot[0];
        }
    }

    public struct ValueChannelSnapshot
    {
        public int SlaveNo;
        public int ChannelNo;

        // 0 = Input(TxPDO), 1 = Output(RxPDO)
        public int Direction;

        public ushort Index;
        public byte SubIndex;
        public byte RawKind;

        public long RawSigned;
        public ulong RawUnsigned;
        public double RawFloat;
    }

    public enum ValueRawKind
    {
        Unknown = 0,

        Int8 = 1,
        UInt8 = 2,

        Int16 = 3,
        UInt16 = 4,

        Int32 = 5,
        UInt32 = 6,

        Int64 = 7,
        UInt64 = 8,

        Real32 = 9,
        Real64 = 10
    }


}
