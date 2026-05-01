using System.Collections.Generic;

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

    public enum ValueRawType
    {
        Unknown = 0,

        Bool = 1,
        BitField = 2,
        RawBits = 3,

        Int8 = 10,
        UInt8 = 11,

        Int16 = 12,
        UInt16 = 13,

        Int32 = 14,
        UInt32 = 15,

        Int64 = 16,
        UInt64 = 17,

        Real32 = 18,
        Real64 = 19
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

        public ValueRawType RawType { get; set; }

        public string DirectionText
        {
            get { return Direction.ToString(); }
        }

        public string AddressText
        {
            get
            {
                return "0x" + Index.ToString("X4") + ":" + SubIndex.ToString("X2");
            }
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

        // ValueRawType as byte for external/MMF-friendly layout.
        public byte RawType;

        public long RawSigned;
        public ulong RawUnsigned;
        public double RawFloat;
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
}