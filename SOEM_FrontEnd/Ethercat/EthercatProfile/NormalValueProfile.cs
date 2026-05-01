using SOEM_FrontEnd.DataMap;
using SOEM_FrontEnd.Ethercat.EthercatProfile.Interfaces;
using SOEM_FrontEnd.Model;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SOEM_FrontEnd.Ethercat.EthercatProfile
{
    public sealed class NormalValueProfile : PDOBase, IPDOView, IValuePdoView
    {
        private readonly ushort _slaveNo;

        private readonly List<ValueChannelDefinition> _valueDefinitions =
            new List<ValueChannelDefinition>();

        private ValueChannelSnapshot[] _snapshots =
            new ValueChannelSnapshot[0];

        private long _valueSequence;

        public IReadOnlyList<ValueChannelDefinition> ValueDefinitions
        {
            get { return _valueDefinitions; }
        }

        public long ValueSequence
        {
            get { return _valueSequence; }
        }

        public NormalValueProfile(int rxSize, int txSize, ushort slaveNo, EcClient ecClient)
            : base(rxSize, txSize)
        {
            _slaveNo = slaveNo;
        }

        public override void SetPdoMapping(List<uint> rxAllMap, List<uint> txAllMap)
        {
            base.SetPdoMapping(rxAllMap, txAllMap);
            BuildValueChannels();
        }

        public void BuildValueChannels()
        {
            _valueDefinitions.Clear();

            BuildFromMapRows(ValuePdoDirection.Output, RxPdoMapRows);
            BuildFromMapRows(ValuePdoDirection.Input, TxPdoMapRows);

            _snapshots = new ValueChannelSnapshot[_valueDefinitions.Count];
            _valueSequence = 0;
        }

        public override void OnAfterPdoReceived()
        {
            UpdateValueSnapshots();
        }

        public ValueSnapshotFrame GetValueSnapshot()
        {
            ValueChannelSnapshot[] copy = new ValueChannelSnapshot[_snapshots.Length];
            Array.Copy(_snapshots, copy, _snapshots.Length);

            ValueSnapshotFrame frame = new ValueSnapshotFrame();
            frame.Sequence = _valueSequence;
            frame.Channels = copy;

            return frame;
        }

        public void UpdateValueSnapshots()
        {
            _valueSequence++;

            for (int i = 0; i < _valueDefinitions.Count; i++)
            {
                ValueChannelDefinition definition = _valueDefinitions[i];

                ValueChannelSnapshot snapshot;
                bool ok = TryReadValue(definition, out snapshot);

                if (ok)
                {
                    _snapshots[i] = snapshot;
                }
            }
        }

        private void BuildFromMapRows(ValuePdoDirection direction, IReadOnlyList<PdoMapRow> mapRows)
        {
            if (mapRows == null)
            {
                return;
            }

            SlaveStore store = Datamap.Instance.GetSlave(_slaveNo);

            if (store == null)
            {
                return;
            }

            Dictionary<ushort, int> channelNoByBaseIndex = new Dictionary<ushort, int>();
            int nextChannelNo = _valueDefinitions.Count + 1;

            for (int i = 0; i < mapRows.Count; i++)
            {
                PdoMapRow mapRow = mapRows[i];

                if (mapRow == null)
                {
                    continue;
                }

                SDOFlatObject sdoRow = FindSdoRow(store, mapRow.Index, mapRow.SubIndex);

                if (IsValueCandidate(mapRow, sdoRow) == false)
                {
                    continue;
                }

                int channelNo;

                if (channelNoByBaseIndex.TryGetValue(mapRow.Index, out channelNo) == false)
                {
                    channelNo = nextChannelNo;
                    nextChannelNo++;
                    channelNoByBaseIndex.Add(mapRow.Index, channelNo);
                }

                ValueRawType rawType = GetRawType(sdoRow.DataType, mapRow.BitLength);

                ValueChannelDefinition definition = new ValueChannelDefinition
                {
                    SlaveNo = _slaveNo,
                    ChannelNo = channelNo,

                    Direction = direction,

                    BaseIndex = mapRow.Index,

                    Index = mapRow.Index,
                    SubIndex = mapRow.SubIndex,

                    Name = sdoRow.DisplayName,
                    DataType = sdoRow.DataType,

                    BitLength = mapRow.BitLength,
                    ByteOffset = mapRow.ByteOffset,
                    BitInByte = mapRow.BitInByte,

                    RawType = rawType
                };

                _valueDefinitions.Add(definition);
            }
        }

        private bool TryReadValue(ValueChannelDefinition definition, out ValueChannelSnapshot snapshot)
        {
            snapshot = new ValueChannelSnapshot();

            if (definition == null)
            {
                return false;
            }

            if (definition.BitInByte != 0)
            {
                return false;
            }

            ReadOnlySpan<byte> data;

            if (definition.Direction == ValuePdoDirection.Input)
            {
                data = Input;
            }
            else
            {
                data = Output;
            }

            int offset = definition.ByteOffset;

            if (offset < 0 || offset >= data.Length)
            {
                return false;
            }

            snapshot.SlaveNo = definition.SlaveNo;
            snapshot.ChannelNo = definition.ChannelNo;
            snapshot.Direction = (int)definition.Direction;
            snapshot.Index = definition.Index;
            snapshot.SubIndex = definition.SubIndex;
            snapshot.RawType = (byte)definition.RawType;

            switch (definition.RawType)
            {
                case ValueRawType.Int8:
                    {
                        if (offset + 1 > data.Length)
                        {
                            return false;
                        }

                        snapshot.RawSigned = unchecked((sbyte)data[offset]);
                        return true;
                    }

                case ValueRawType.UInt8:
                    {
                        if (offset + 1 > data.Length)
                        {
                            return false;
                        }

                        snapshot.RawUnsigned = data[offset];
                        return true;
                    }

                case ValueRawType.Int16:
                    {
                        if (offset + 2 > data.Length)
                        {
                            return false;
                        }

                        short raw = BinaryPrimitives.ReadInt16LittleEndian(
                            data.Slice(offset, 2));

                        snapshot.RawSigned = raw;
                        return true;
                    }

                case ValueRawType.UInt16:
                    {
                        if (offset + 2 > data.Length)
                        {
                            return false;
                        }

                        ushort raw = BinaryPrimitives.ReadUInt16LittleEndian(
                            data.Slice(offset, 2));

                        snapshot.RawUnsigned = raw;
                        return true;
                    }

                case ValueRawType.Int32:
                    {
                        if (offset + 4 > data.Length)
                        {
                            return false;
                        }

                        int raw = BinaryPrimitives.ReadInt32LittleEndian(
                            data.Slice(offset, 4));

                        snapshot.RawSigned = raw;
                        return true;
                    }

                case ValueRawType.UInt32:
                    {
                        if (offset + 4 > data.Length)
                        {
                            return false;
                        }

                        uint raw = BinaryPrimitives.ReadUInt32LittleEndian(
                            data.Slice(offset, 4));

                        snapshot.RawUnsigned = raw;
                        return true;
                    }

                case ValueRawType.Int64:
                    {
                        if (offset + 8 > data.Length)
                        {
                            return false;
                        }

                        long raw = BinaryPrimitives.ReadInt64LittleEndian(
                            data.Slice(offset, 8));

                        snapshot.RawSigned = raw;
                        return true;
                    }

                case ValueRawType.UInt64:
                    {
                        if (offset + 8 > data.Length)
                        {
                            return false;
                        }

                        ulong raw = BinaryPrimitives.ReadUInt64LittleEndian(
                            data.Slice(offset, 8));

                        snapshot.RawUnsigned = raw;
                        return true;
                    }

                case ValueRawType.Real32:
                    {
                        if (offset + 4 > data.Length)
                        {
                            return false;
                        }

                        int bits = BinaryPrimitives.ReadInt32LittleEndian(
                            data.Slice(offset, 4));

                        snapshot.RawFloat = BitConverter.Int32BitsToSingle(bits);
                        return true;
                    }

                case ValueRawType.Real64:
                    {
                        if (offset + 8 > data.Length)
                        {
                            return false;
                        }

                        long bits = BinaryPrimitives.ReadInt64LittleEndian(
                            data.Slice(offset, 8));

                        snapshot.RawFloat = BitConverter.Int64BitsToDouble(bits);
                        return true;
                    }
            }

            return false;
        }

        public static bool HasValueCandidates(ushort slaveNo, List<uint> rxAllMap, List<uint> txAllMap)
        {
            SlaveStore store = Datamap.Instance.GetSlave(slaveNo);

            if (store == null)
            {
                return false;
            }

            if (HasValueCandidatesInMap(store, rxAllMap))
            {
                return true;
            }

            if (HasValueCandidatesInMap(store, txAllMap))
            {
                return true;
            }

            return false;
        }

        private static bool HasValueCandidatesInMap(SlaveStore store, List<uint> allMap)
        {
            if (store == null || allMap == null)
            {
                return false;
            }

            for (int i = 0; i < allMap.Count; i++)
            {
                uint raw = allMap[i];

                ushort index = (ushort)(raw >> 16);
                byte subIndex = (byte)((raw >> 8) & 0xFF);
                byte bitLength = (byte)(raw & 0xFF);

                PdoMapRow row = new PdoMapRow
                {
                    No = i + 1,
                    Raw = raw,
                    Index = index,
                    SubIndex = subIndex,
                    BitLength = bitLength,
                    BitOffset = 0
                };

                SDOFlatObject sdoRow = FindSdoRow(store, index, subIndex);

                if (IsValueCandidate(row, sdoRow))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsValueCandidate(PdoMapRow mapRow, SDOFlatObject sdoRow)
        {
            if (mapRow == null || sdoRow == null)
            {
                return false;
            }

            if (mapRow.Index == 0x0000)
            {
                return false;
            }

            if (mapRow.BitInByte != 0)
            {
                return false;
            }

            if (mapRow.BitLength != 8 &&
                mapRow.BitLength != 16 &&
                mapRow.BitLength != 32 &&
                mapRow.BitLength != 64)
            {
                return false;
            }

            ValueRawType kind = GetRawType(sdoRow.DataType, mapRow.BitLength);

            if (kind == ValueRawType.Unknown)
            {
                return false;
            }

            return true;
        }

        private static SDOFlatObject FindSdoRow(SlaveStore store, ushort index, byte subIndex)
        {
            if (store == null || store.SdoRows == null)
            {
                return null;
            }

            for (int i = 0; i < store.SdoRows.Count; i++)
            {
                SDOFlatObject row = store.SdoRows[i];

                if (row == null)
                {
                    continue;
                }

                if (row.HasSubIndex)
                {
                    continue;
                }

                if (row.Index == index && row.SubIndex == subIndex)
                {
                    return row;
                }
            }

            return null;
        }

        private static ValueRawType GetRawType(string dataType, int bitLength)
        {
            if (string.IsNullOrWhiteSpace(dataType))
            {
                return ValueRawType.Unknown;
            }

            string text = dataType.Trim().ToUpperInvariant();

            if ((text == "SINT" || text == "INT8" || text == "INTEGER8") && bitLength == 8)
            {
                return ValueRawType.Int8;
            }

            if ((text == "USINT" || text == "BYTE" || text == "UINT8" || text == "UNSIGNED8") && bitLength == 8)
            {
                return ValueRawType.UInt8;
            }

            if ((text == "INT" || text == "INT16" || text == "INTEGER16") && bitLength == 16)
            {
                return ValueRawType.Int16;
            }

            if ((text == "UINT" || text == "UINT16" || text == "UNSIGNED16") && bitLength == 16)
            {
                return ValueRawType.UInt16;
            }

            if ((text == "DINT" || text == "INT32" || text == "INTEGER32") && bitLength == 32)
            {
                return ValueRawType.Int32;
            }

            if ((text == "UDINT" || text == "UINT32" || text == "UNSIGNED32") && bitLength == 32)
            {
                return ValueRawType.UInt32;
            }

            if ((text == "LINT" || text == "INT64" || text == "INTEGER64") && bitLength == 64)
            {
                return ValueRawType.Int64;
            }

            if ((text == "ULINT" || text == "UINT64" || text == "UNSIGNED64") && bitLength == 64)
            {
                return ValueRawType.UInt64;
            }

            if ((text == "REAL" || text == "REAL32") && bitLength == 32)
            {
                return ValueRawType.Real32;
            }

            if ((text == "LREAL" || text == "REAL64") && bitLength == 64)
            {
                return ValueRawType.Real64;
            }

            return ValueRawType.Unknown;
        }
    }

}
