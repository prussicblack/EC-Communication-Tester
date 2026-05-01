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

        public ObservableCollection<ValueChannelRowViewModel> Channels { get; private set; }

        public ValueControlViewModel()
        {
            Channels = new ObservableCollection<ValueChannelRowViewModel>();
        }

        public void Attach(IValuePdoView valueView)
        {
            _valueView = valueView;
            Channels.Clear();

            if (_valueView == null)
            {
                return;
            }

            IReadOnlyList<ValueChannelDefinition> definitions = _valueView.ValueDefinitions;

            if (definitions == null)
            {
                return;
            }

            for (int i = 0; i < definitions.Count; i++)
            {
                Channels.Add(new ValueChannelRowViewModel(definitions[i]));
            }

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

            int count = Channels.Count;

            if (frame.Channels.Length < count)
            {
                count = frame.Channels.Length;
            }

            for (int i = 0; i < count; i++)
            {
                Channels[i].UpdateSnapshot(frame.Channels[i]);
            }
        }
    }

    public sealed class ValueChannelRowViewModel : ViewModelBase
    {
        private readonly ValueChannelDefinition _definition;

        private ValueChannelSnapshot _snapshot;

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
            _snapshot = snapshot;
            RawText = FormatRawText(snapshot);
        }

        private static string FormatRawText(ValueChannelSnapshot snapshot)
        {
            ValueRawType rawType = (ValueRawType)snapshot.RawType;

            switch (rawType)
            {
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

                case ValueRawType.Unknown:
                default:
                    return "";
            }
        }
    }

}

