using SOEM_FrontEnd.Ethercat.EthercatProfile.Interfaces;
using SOEM_FrontEnd.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SOEM_FrontEnd.Ethercat.EthercatProfile
{
    public sealed class NormalValueProfile : PDOBase, IPDOView, IValuePdoView
    {
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
            : base(rxSize, txSize, slaveNo, ecClient)
        {
        }

        public void BuildValueChannels()
        {
            _valueDefinitions.Clear();

            BuildFromMapRows(ValuePdoDirection.Output, RxPdoMapRows);
            BuildFromMapRows(ValuePdoDirection.Input, TxPdoMapRows);

            _snapshots = new ValueChannelSnapshot[_valueDefinitions.Count];
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
    }
}
