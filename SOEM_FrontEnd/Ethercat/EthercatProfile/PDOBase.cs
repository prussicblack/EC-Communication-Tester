using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SOEM_FrontEnd.Ethercat.EthercatProfile.Interfaces;

namespace SOEM_FrontEnd.Ethercat.EthercatProfile
{
    public abstract class PDOBase : IPDOAccess, IPDOView
    {
        //PDO Interface기본 틀.
        private readonly byte[] _rx;
        private readonly byte[] _tx;

        private readonly byte[] _rxSnapA, _rxSnapB;
        private readonly byte[] _txSnapA, _txSnapB;

        private byte[] _rxSnapCurrent;
        private byte[] _txSnapCurrent;

        public Span<byte> Rx => _rx;               // 매번 새 Span 생성 (cheap)
        public ReadOnlySpan<byte> Tx => _tx;
        //여기까지 PDOInterface 기본틀.


        //PDO View
        public ReadOnlyMemory<byte> RxSnapshot => System.Threading.Volatile.Read(ref _rxSnapCurrent);
        public ReadOnlyMemory<byte> TxSnapshot => System.Threading.Volatile.Read(ref _txSnapCurrent);

        public void PublishSnapshots()
        {
            // 다음에 공개할 버퍼 선택
            var nextRx = ReferenceEquals(_rxSnapCurrent, _rxSnapA) ? _rxSnapB : _rxSnapA;
            var nextTx = ReferenceEquals(_txSnapCurrent, _txSnapA) ? _txSnapB : _txSnapA;

            Buffer.BlockCopy(_rx, 0, nextRx, 0, _rx.Length);
            Buffer.BlockCopy(_tx, 0, nextTx, 0, _tx.Length);

            // 원자적 스왑
            System.Threading.Volatile.Write(ref _rxSnapCurrent, nextRx);
            System.Threading.Volatile.Write(ref _txSnapCurrent, nextTx);
        }
        //PDO View

        public PDOBase(int rxSize, int txSize)
        {
            _rx = new byte[rxSize];
            _tx = new byte[txSize];

            _rxSnapA = new byte[rxSize];
            _rxSnapB = new byte[rxSize];
            _txSnapA = new byte[txSize];
            _txSnapB = new byte[txSize];

            _rxSnapCurrent = _rxSnapA;
            _txSnapCurrent = _txSnapA;
        }

    }
}
