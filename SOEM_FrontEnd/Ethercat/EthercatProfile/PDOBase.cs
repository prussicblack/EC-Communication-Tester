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

        //헷갈린다.
        //SOEM기준으로 output -> 마스터기준 출력 input -> 마스터기준 입력.

        private readonly byte[] _output; //_rx
        private readonly byte[] _input; //_tx

        private readonly byte[] _outputSnapA, _outputSnapB;
        private readonly byte[] _inputSnapA, _inputSnapB;

        private byte[] _outputSnapCurrent;
        private byte[] _inputSnapCurrent;

        public Span<byte> Output => _output;               // 매번 새 Span 생성 (cheap)
        public ReadOnlySpan<byte> Input => _input;
        //여기까지 PDOInterface 기본틀.


        //PDO View
        public ReadOnlyMemory<byte> OutputSnapshot => System.Threading.Volatile.Read(ref _outputSnapCurrent);
        public ReadOnlyMemory<byte> InputSnapshot => System.Threading.Volatile.Read(ref _inputSnapCurrent);

        public void PublishSnapshots()
        {
            // 다음에 공개할 버퍼 선택
            var nextRx = ReferenceEquals(_outputSnapCurrent, _outputSnapA) ? _outputSnapB : _outputSnapA;
            var nextTx = ReferenceEquals(_inputSnapCurrent, _inputSnapA) ? _inputSnapB : _inputSnapA;

            Buffer.BlockCopy(_output, 0, nextRx, 0, _output.Length);
            Buffer.BlockCopy(_input, 0, nextTx, 0, _input.Length);

            // 원자적 스왑
            System.Threading.Volatile.Write(ref _outputSnapCurrent, nextRx);
            System.Threading.Volatile.Write(ref _inputSnapCurrent, nextTx);
        }


        /// <summary>
        /// Called after input (TxPDO) has been received and snapshots are published.
        /// Implementations may compute next-cycle outputs (RxPDO), e.g., controlword updates.
        /// Must be RT-safe: no allocations, no locks, no logging.
        /// </summary>
        public virtual void OnAfterPdoReceived()
        {

        }

        //PDO 루프에서 Tx를 채워 넣을 수 있게
        internal Span<byte> InputWriteSpan
        {
            get
            {
                return _input;
            }
        }


        //PDO View

        public PDOBase(int outputSize, int inputSize)
        {
            _output = new byte[outputSize];
            _input = new byte[inputSize];

            _outputSnapA = new byte[outputSize];
            _outputSnapB = new byte[outputSize];
            _inputSnapA = new byte[inputSize];
            _inputSnapB = new byte[inputSize];

            _outputSnapCurrent = _outputSnapA;
            _inputSnapCurrent = _inputSnapA;
        }

    }
}
