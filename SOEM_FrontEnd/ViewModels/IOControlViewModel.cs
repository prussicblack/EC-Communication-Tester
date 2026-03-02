using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using SOEM_FrontEnd.Ethercat.EthercatProfile.Interfaces;
using System.Windows.Input;

namespace SOEM_FrontEnd.ViewModels
{
    public partial class IOControlViewModel : ViewModelBase
    {

        private int _columns;
        public int Columns
        {
            get { return _columns; }
            set
            {
                if (_columns == value) return;
                if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));

                _columns = value;
                OnPropertyChanged();
            }
        }
        private int _bitCount;
        public int BitCount
        {
            get { return _bitCount; }
            private set
            {
                if (_bitCount == value) return;
                _bitCount = value;
                OnPropertyChanged();
            }
        }

        private bool _IsOutput;

        public bool IsOutput
        {
            // 출력이면 클릭/쓰기 허용
            get
            {
                return _IsOutput;
            }
            private set
            {
                if (_IsOutput == value) return;
                _IsOutput = value;
                OnPropertyChanged();
            }
        }

        private string _Title;

        public string Title
        {
            //상단 라벨
            get
            {
                return _Title;
            }
            private set
            {
                if (_Title == value) return;
                _Title = value;
                OnPropertyChanged();
            }
        }


        private IPDOAccess _pdoAccess;

        public ICommand CmdToggleBit { get; private set; }

        public void Attach(IPDOAccess pdoAccess)
        {
            _pdoAccess = pdoAccess;
        }


        public ObservableCollection<BitItem> Bits { get; private set; }

        public IOControlViewModel(int bitCount, int columns, string title, bool isOutput)
        {
            if (bitCount <= 0) throw new ArgumentOutOfRangeException(nameof(bitCount));
            if (columns <= 0) throw new ArgumentOutOfRangeException(nameof(columns));

            _bitCount = bitCount;
            _columns = columns;

            _Title = title;
            _IsOutput = isOutput;

            Bits = new ObservableCollection<BitItem>();

            for (int i = 0; i < bitCount; i++)
            {
                Bits.Add(new BitItem(i));
            }

            CmdToggleBit = new RelayCommand<BitItem>(HandleToggleBit);

            //생성시 확인해서 On/Off 구현.
            if (isOutput == true)
            {
                foreach (var bit in Bits)
                {
                    bit.IsWritable = true;
                    bit.ToggleCommand = CmdToggleBit;
                }
            }
            else
            {
                foreach (var bit in Bits)
                {
                    bit.IsWritable = false;
                    bit.ToggleCommand = null;
                }
            }
        }

        private void HandleToggleBit(BitItem item)
        {
            if (item == null) return;
            if (!IsOutput) return;

            IPDOAccess pdoAccess = _pdoAccess;
            if (pdoAccess == null) return;

            Span<byte> output = pdoAccess.Output;

            int bit = item.BitIndex;
            int byteIndex = bit / 8;
            int bitIndexInByte = bit % 8;

            if (byteIndex < 0 || byteIndex >= output.Length) return;

            byte mask = (byte)(1 << bitIndexInByte);

            byte oldValue = output[byteIndex];
            bool newOn = (oldValue & mask) == 0; // toggle 결과(기존 off면 on)

            byte newValue;
            if (newOn)
                newValue = (byte)(oldValue | mask);
            else
                newValue = (byte)(oldValue & (byte)~mask);

            output[byteIndex] = newValue;

            // UI 즉시 반영(스냅샷 publish 기다리지 않게)
            item.IsOn = newOn;
        }


        // LSB-first: bit0 = data[0]의 bit0
        public void UpdateFromBytes(byte[] data)
        {
            if (data == null) return;

            UpdateFromBytes(data.AsSpan());
        }
        public void UpdateFromBytes(ReadOnlySpan<byte> data)
        {
            int bitCount = Bits.Count;

            for (int bit = 0; bit < bitCount; bit++)
            {
                int byteIndex = bit / 8;
                int bitIndexInByte = bit % 8;

                bool on = false;
                if (byteIndex < data.Length)
                {
                    byte mask = (byte)(1 << bitIndexInByte);
                    on = (data[byteIndex] & mask) != 0;
                }

                Bits[bit].IsOn = on;
            }
        }

        // 필요하면 데이터 길이에 맞춰 재구성 (선택)
        public void Reset(int bitCount, int columns)
        {
            if (bitCount <= 0) throw new ArgumentOutOfRangeException(nameof(bitCount));
            if (columns <= 0) throw new ArgumentOutOfRangeException(nameof(columns));

            Columns = columns;

            if (Bits.Count != bitCount)
            {
                Bits.Clear();
                for (int i = 0; i < bitCount; i++)
                {
                    Bits.Add(new BitItem(i));
                }
                BitCount = bitCount;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }

        public sealed class BitItem : INotifyPropertyChanged
        {

            public BitItem(int bitIndex)
            {
                BitIndex = bitIndex;
            }

            public int DisplayIndex
            {
                get { return BitIndex; } // 0-base. 1-base 원하면 BitIndex + 1
            }

            public int BitIndex { get; private set; }

            private bool _isOn;
            public bool IsOn
            {
                get { return _isOn; }
                set
                {
                    if (_isOn == value) return;
                    _isOn = value;
                    OnPropertyChanged();
                }
            }

            public bool IsWritable { get; set; }
            public ICommand ToggleCommand { get; set; }


            public event PropertyChangedEventHandler PropertyChanged;

            private void OnPropertyChanged([CallerMemberName] string propertyName = null)
            {
                PropertyChangedEventHandler handler = PropertyChanged;
                if (handler != null)
                    handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}

