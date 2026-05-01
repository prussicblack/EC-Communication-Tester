using Avalonia.Controls;
using SOEM_FrontEnd.Ethercat.EthercatProfile.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SOEM_FrontEnd.ViewModels
{
    public sealed class ValueControlViewModel : ViewModelBase
    {
        private IValuePdoView _valueView;

        private readonly List<ValueChannelRowViewModel> _allRows =
            new List<ValueChannelRowViewModel>();

        public ObservableCollection<ValueChannelRowViewModel> InputChannels { get; private set; }

        public ObservableCollection<ValueChannelRowViewModel> OutputChannels { get; private set; }

        public bool HasInputChannels
        {
            get { return InputChannels.Count > 0; }
        }

        public bool HasOutputChannels
        {
            get { return OutputChannels.Count > 0; }
        }

        public bool HasNoChannels
        {
            get { return InputChannels.Count == 0 && OutputChannels.Count == 0; }
        }

        public GridLength InputRowsHeight
        {
            get
            {
                if (HasInputChannels)
                {
                    return new GridLength(1, GridUnitType.Star);
                }

                return new GridLength(0);
            }
        }

        public GridLength OutputRowsHeight
        {
            get
            {
                if (HasOutputChannels)
                {
                    return new GridLength(1, GridUnitType.Star);
                }

                return new GridLength(0);
            }
        }

        public ValueControlViewModel()
        {
            InputChannels = new ObservableCollection<ValueChannelRowViewModel>();
            OutputChannels = new ObservableCollection<ValueChannelRowViewModel>();
        }

        public void Attach(IValuePdoView valueView)
        {
            _valueView = valueView;

            _allRows.Clear();
            InputChannels.Clear();
            OutputChannels.Clear();

            if (_valueView == null)
            {
                RaiseLayoutChanged();
                return;
            }

            IReadOnlyList<ValueChannelDefinition> definitions = _valueView.ValueDefinitions;

            if (definitions == null)
            {
                RaiseLayoutChanged();
                return;
            }

            for (int i = 0; i < definitions.Count; i++)
            {
                ValueChannelDefinition definition = definitions[i];

                ValueChannelRowViewModel row = new ValueChannelRowViewModel(definition);
                _allRows.Add(row);

                if (definition.Direction == ValuePdoDirection.Input)
                {
                    InputChannels.Add(row);
                }
                else
                {
                    OutputChannels.Add(row);
                }
            }

            RaiseLayoutChanged();
            UiTick();
        }

        public void UiTick()
        {
            IValuePdoView valueView = _valueView;

            if (valueView == null)
            {
                return;
            }

            ValueSnapshotFrame frame = valueView.GetValueSnapshot();

            if (frame == null || frame.Channels == null)
            {
                return;
            }

            int count = _allRows.Count;

            if (frame.Channels.Length < count)
            {
                count = frame.Channels.Length;
            }

            for (int i = 0; i < count; i++)
            {
                _allRows[i].UpdateSnapshot(frame.Channels[i]);
            }
        }

        private void RaiseLayoutChanged()
        {
            OnPropertyChanged(nameof(HasInputChannels));
            OnPropertyChanged(nameof(HasOutputChannels));
            OnPropertyChanged(nameof(HasNoChannels));
            OnPropertyChanged(nameof(InputRowsHeight));
            OnPropertyChanged(nameof(OutputRowsHeight));
        }
    }

    public sealed class ValueChannelRowViewModel : ViewModelBase
    {
        private readonly ValueChannelDefinition _definition;

        private string _rawText = "";

        public ValueChannelRowViewModel(ValueChannelDefinition definition)
        {
            _definition = definition;
        }

        public int ChannelNo
        {
            get { return _definition.ChannelNo; }
        }

        public string DirectionText
        {
            get { return _definition.DirectionText; }
        }

        public string AddressText
        {
            get { return _definition.AddressText; }
        }

        public string Name
        {
            get { return _definition.Name; }
        }

        public string DataType
        {
            get { return _definition.DataType; }
        }

        public string RawTypeText
        {
            get { return _definition.RawType.ToString(); }
        }

        public int ByteOffset
        {
            get { return _definition.ByteOffset; }
        }

        public string RawText
        {
            get { return _rawText; }
            private set { SetProperty(ref _rawText, value); }
        }

        public void UpdateSnapshot(ValueChannelSnapshot snapshot)
        {
            RawText = FormatRawText(snapshot);
        }

        private static string FormatRawText(ValueChannelSnapshot snapshot)
        {
            ValueRawType rawType = (ValueRawType)snapshot.RawType;

            switch (rawType)
            {
                case ValueRawType.Bool:
                    return snapshot.RawUnsigned != 0 ? "ON" : "OFF";

                case ValueRawType.BitField:
                    return "0x" + snapshot.RawUnsigned.ToString("X");

                case ValueRawType.RawBits:
                    return "0x" + snapshot.RawUnsigned.ToString("X");

                case ValueRawType.Int8:
                case ValueRawType.Int16:
                case ValueRawType.Int32:
                case ValueRawType.Int64:
                    return snapshot.RawSigned.ToString();

                case ValueRawType.UInt8:
                case ValueRawType.UInt16:
                case ValueRawType.UInt32:
                case ValueRawType.UInt64:
                    return snapshot.RawUnsigned.ToString();

                case ValueRawType.Real32:
                case ValueRawType.Real64:
                    return snapshot.RawFloat.ToString("F6");

                default:
                    return "";
            }
        }
    }

}

