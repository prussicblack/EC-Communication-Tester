using SOEM_FrontEnd.DataMap;
using SOEM_FrontEnd.Ethercat.EthercatProfile.Interfaces;
using System;
using System.Buffers.Binary;

namespace SOEM_FrontEnd.ViewModels
{
    public sealed class MotorPDORowViewModel : ViewModelBase
    {
        private readonly PdoMapRow _mapRow;
        private readonly bool _isRx;

        private string _rawHex = "";
        private string _valueText = "";

        private string _designName = "";

        private SDOFlatObject _sdorow;


        public MotorPDORowViewModel(PdoMapRow mapRow, bool isRx, SDOFlatObject sdorow)
        {
            if (mapRow == null)
            {
                throw new ArgumentNullException(nameof(mapRow));
            }

            _mapRow = mapRow;
            _isRx = isRx;
            IsRx = isRx;
            _sdorow = sdorow;

            if (isRx)
            {
                Direction = "RxPDO";
            }
            else
            {
                Direction = "TxPDO";
            }

            No = mapRow.No;
            Index = mapRow.Index;
            SubIndex = mapRow.SubIndex;
            AddressText = mapRow.AddressText;
            BitLength = mapRow.BitLength;
            ByteOffset = mapRow.ByteOffset;
            BitInByte = mapRow.BitInByte;

            AccessText = ResolveAccessText(mapRow.Index, isRx);
        }

        private MotorPDORowViewModel()
        {
            //Design Time용
        }

        public bool IsRx { get; private set; }

        public string Direction { get; private set; } = "";
        public int No { get; private set; }
        public ushort Index { get; private set; }
        public byte SubIndex { get; private set; }
        public string AddressText { get; private set; } = "";
        public byte BitLength { get; private set; }
        public int ByteOffset { get; private set; }
        public int BitInByte { get; private set; }

        public string AccessText { get; private set; } = "";

        public string Name
        {
            get
            {
                if (_mapRow == null)
                {
                    return _designName;
                }

                // hardcoded known name 우선
                string known = ResolveKnownName(_mapRow.Index, _mapRow.SubIndex);

                if (string.IsNullOrWhiteSpace(known) == false)
                {
                    return known;
                }

                // SDO dictionary fallback
                if (_sdorow != null)
                {
                    return _sdorow.DisplayName ?? "";
                }

                return "";
            }
        }


        public string RawHex
        {
            get { return _rawHex; }
            private set { SetProperty(ref _rawHex, value); }
        }

        public string ValueText
        {
            get { return _valueText; }
            private set { SetProperty(ref _valueText, value); }
        }


        public static MotorPDORowViewModel CreateDesignRow(string direction, string addressText, string name, byte bitLength, 
            int byteOffset, string rawHex, string valueText, string accessText)
        {
            MotorPDORowViewModel row = new MotorPDORowViewModel();

            row.Direction = direction;
            row.AddressText = addressText;
            row._designName = name;
            row.BitLength = bitLength;
            row.ByteOffset = byteOffset;
            row.RawHex = rawHex;
            row.ValueText = valueText;
            row.AccessText = accessText;

            return row;
        }

        public string DataType
        {
            get
            {
                if (_sdorow == null)
                {
                    return "";
                }

                return _sdorow.DataType;
            }
        }


        public void Update(ReadOnlyMemory<byte> snapshot)
        {
            ReadOnlySpan<byte> span = snapshot.Span;

            if (_mapRow.BitInByte != 0)
            {
                RawHex = "bit";
                ValueText = "bit field";
                return;
            }

            int byteLength = (_mapRow.BitLength + 7) / 8;

            if (byteLength <= 0)
            {
                RawHex = "";
                ValueText = "";
                return;
            }

            if ((uint)_mapRow.ByteOffset + (uint)byteLength > (uint)span.Length)
            {
                RawHex = "";
                ValueText = "out of range";
                return;
            }

            ReadOnlySpan<byte> valueSpan = span.Slice(_mapRow.ByteOffset, byteLength);

            RawHex = ToHex(valueSpan);
            ValueText = FormatValue(valueSpan);
        }

        private string FormatValue(ReadOnlySpan<byte> valueSpan)
        {
            ushort index = _mapRow.Index;

            if (_mapRow.BitLength == 8)
            {
                byte raw = valueSpan[0];

                if (index == 0x6060 || index == 0x6061)
                {
                    return ((sbyte)raw).ToString();
                }

                return raw.ToString();
            }

            if (_mapRow.BitLength == 16)
            {
                ushort rawU16 = BinaryPrimitives.ReadUInt16LittleEndian(valueSpan);

                if (index == 0x6040 || index == 0x6041 || index == 0x603F)
                {
                    return "0x" + rawU16.ToString("X4");
                }

                return rawU16.ToString();
            }

            if (_mapRow.BitLength == 32)
            {
                if (index == 0x607A || index == 0x6064 || index == 0x60F4)
                {
                    int rawI32 = BinaryPrimitives.ReadInt32LittleEndian(valueSpan);
                    return rawI32.ToString();
                }

                uint rawU32 = BinaryPrimitives.ReadUInt32LittleEndian(valueSpan);

                if (index == 0x60FD)
                {
                    return "0x" + rawU32.ToString("X8");
                }

                return rawU32.ToString();
            }

            return "bits=" + _mapRow.BitLength.ToString();
        }

        public static string ResolveKnownName(ushort index, byte subIndex)
        {
            if (index == 0x603F)
                return "Error code";

            if (index == 0x6040)
                return "Controlword";

            if (index == 0x6041)
                return "Statusword";

            if (index == 0x6060)
                return "Mode command";

            if (index == 0x6061)
                return "Mode display";

            if (index == 0x6064)
                return "Actual position";

            if (index == 0x6072)
                return "Max torque";

            if (index == 0x607A)
                return "Target position";

            if (index == 0x6080)
                return "Max motor speed";

            if (index == 0x60B8)
                return "Touch probe function";

            if (index == 0x60B9)
                return "Touch probe status";

            if (index == 0x60BA)
                return "Touch probe position";

            if (index == 0x60FD)
                return "Digital inputs";

            return "";
        }

        private static string ResolveAccessText(ushort index, bool isRx)
        {
            if (isRx == false)
            {
                return "ReadOnly";
            }

            // 위험하거나 상태머신과 충돌하는 항목은 잠금
            switch (index)
            {
                case 0x6040: // Controlword
                case 0x607A: // Target Position
                case 0x6060: // Mode Of Operation
                    return "Locked";
            }

            return "Config";
        }

        private static string ToHex(ReadOnlySpan<byte> data)
        {
            if (data.Length == 0)
            {
                return "";
            }

            char[] chars = new char[data.Length * 3 - 1];

            for (int i = 0; i < data.Length; i++)
            {
                byte value = data[i];

                chars[i * 3] = GetHexChar(value >> 4);
                chars[i * 3 + 1] = GetHexChar(value & 0x0F);

                if (i < data.Length - 1)
                {
                    chars[i * 3 + 2] = ' ';
                }
            }

            return new string(chars);
        }

        private static char GetHexChar(int value)
        {
            if (value < 10)
            {
                return (char)('0' + value);
            }

            return (char)('A' + value - 10);
        }




    }
}
