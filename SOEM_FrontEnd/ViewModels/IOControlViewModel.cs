using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace SOEM_FrontEnd.ViewModels
{
    public partial class IOControlViewModel : ViewModelBase
    {

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
        }


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


        public ObservableCollection<BitItem> Bits { get; private set; }

        // LSB-first: bit0 = data[0]의 bit0
        public void UpdateFromBytes(byte[] data)
        {
            if (data == null) return;

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
            private bool _isOn;

            public BitItem(int bitIndex)
            {
                BitIndex = bitIndex;
            }

            public int BitIndex { get; private set; }

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

