using SOEM_FrontEnd.DataMap;
using SOEM_FrontEnd.Ethercat.EthercatProfile;
using SOEM_FrontEnd.Ethercat.EthercatProfile.Interfaces;
using SOEM_FrontEnd.Model;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Reflection.Metadata.Ecma335;

namespace SOEM_FrontEnd.Ethercat
{

    //402Profile 기준의 모터구동을 위한 클래스. PPMode. 전용임.

    //상속구조.
    //각 StateMachine에서 사용될 인터페이스 Run코드 필요.
    //PDO접근을 위한 Byte코드 필요.(PDO(RX)에서 읽어와서 PDO(TX)에서 써주는.)

    //상속구조 정리.

    //PDOBase에서 PDO루프에서 통신담당.
    //IEthercatStateTransition에서 preop, safeop, op 간 이동시 매크로 작성.
    //IMotorCommands에서 ViewModel 통신담당. 

    public sealed class NormalMotorWithPPMode : PDOBase, IEthercatStateTransition, IMotorCommands
    {

        private Dictionary<OdKey, PdoField> _rxMapTable = new Dictionary<OdKey, PdoField>(); // outputs

        private Dictionary<OdKey, PdoField> _txMapTable = new Dictionary<OdKey, PdoField>(); // inputs
        //Ezi-Servo 기준으로, 
        //rxMapTable 0x6040(CW), 0x607a(Target Position) 존재.
        //txMapTable 0x6041(SW), 0x6064(Actual Position) 존재.


        private readonly ushort _SlaveNo;

        private int _off6040cw = -1; // Rx Offset.(매번 lookup 참조가 아니라 빠르게 접근용)
        private int _off6041sw = -1; // Tx Offset.

        private int _off6064ap = -1;   // Tx: Actual Position (마찬가지. 빠른 접근용)
        private int _off607Atp = -1;   // Rx: Target Position

        //SDO 핫패스.
        private SDOPoint _sdo6060; // Mode of operation
        private SDOPoint _sdo6098; // Homing method 같은 것



        private readonly EcClient _ECClient;

        //IMotorCommands 구현부.
        //public int AxisID => throw new NotImplementedException();

        public bool IsServoOn => getIsServoOn();

        public bool IsHome { get; private set; }

        public bool IsError => throw new NotImplementedException();

        public int ActualPosition => getActualPosition();

        private bool getIsServoOn()
        {
            return false;
        }

        private int getActualPosition()
        {
            bool ret = TryReadActualPosition6064(out int actualPos);
            if (ret == true)
            {
                return actualPos;
            }

            return 0;

        }

        public NormalMotorWithPPMode(int rxSize, int txSize, ushort slaveNo, EcClient ECClient) : base(rxSize, txSize)
        {
            _SlaveNo = slaveNo;
            _ECClient = ECClient;
        }


        bool IEthercatStateTransition.PrepareSafeOp(int timeoutMs)
        {
            //safeop로 넘어가기 전 실행될 코드.

            return true;
        }

        bool IEthercatStateTransition.PrepareOp(int timeoutMs)
        {
            //op로 넘어 가기 전 실행될 코드.
            //PP모드 전환 전 PP모드 변환.
            //_ECClient.SetModePP(_SlaveNo);

            _ECClient.SdoWriteI8(_SlaveNo, 0x6060, 0x00, 1); //PPMode 1

            //초기 프로파일 입력.
            //외부에서 설정 가능하도록 처리할것.
            //_ECClient.SetProfile(_SlaveNo, 1000000, 5000000, 5000000); // 예: vel/acc/dec
            //_ECClient.SdoWriteU32(_SlaveNo, 0x6081, 0x00, vel);
            //_ECClient.SdoWriteU32(_SlaveNo, 0x6083, 0x00, acc);
            //_ECClient.SdoWriteU32(_SlaveNo, 0x6084, 0x00, dec);

            //초기 알람 클리어. //따로 해줄것.
            //_ECClient.SdoWriteI16(_SlaveNo, 0x6040, 00, 0x0080);  //slave alarm reset. SDO로 써도 먹네..

            //핫패스 생성.
            //SlaveStore가 Dic으로 저장되어있기 때문에, 매번 호출하게되면 Dic을 조회해서 찾게됨.
            //이 경우 시간이 오래걸리는 문제로 미리 주소를 찍어놓고 호출하게됨.
            BindSdoHotRefs(Datamap.Instance.GetSlave(_SlaveNo));

            return true;
        }

        public void SetPdoMapping(List<uint> rxAllMap, List<uint> txAllMap)
        {
            Build(_rxMapTable, rxAllMap);
            Build(_txMapTable, txAllMap);

            TryResolve402();
        }

        private static void Build(Dictionary<OdKey, PdoField> dict, List<uint> allmap)
        {
            dict.Clear();

            int bitOffset = 0;
            for (int i = 0; i < allmap.Count; i++)
            {
                uint mapWord = allmap[i];

                ushort idx = (ushort)(mapWord >> 16);
                byte sub = (byte)(mapWord >> 8);
                byte bitLen = (byte)(mapWord & 0xFF);

                var key = new OdKey(idx, sub);
                if (dict.ContainsKey(key))
                {
                    bitOffset += bitLen;
                    continue;
                }

                dict.Add(key, new PdoField(bitOffset, bitLen));

                bitOffset += bitLen;
            }
        }


        public void BindSdoHotRefs(SlaveStore slave)
        {
            _sdo6060 = slave.TryGetSdo(0x6060, 0x00);
            _sdo6098 = slave.TryGetSdo(0x6098, 0x00);
        }

        public bool TryResolve402()
        {
            _off6040cw = TryGetByteOffset(_rxMapTable, 0x6040, 0x00);
            _off6041sw = TryGetByteOffset(_txMapTable, 0x6041, 0x00);

            _off6041sw = TryGetByteOffset(_txMapTable, 0x6041, 0x00);
            _off6064ap = TryGetByteOffset(_txMapTable, 0x6064, 0x00);

            return _off6040cw >= 0 && _off6041sw >= 0;
        }

        private static int TryGetByteOffset(Dictionary<OdKey, PdoField> dict, ushort idx, byte sub)
        {
            if (dict.TryGetValue(new OdKey(idx, sub), out var f))
                return f.ByteOffset;
            return -1;
        }


        //내부사용메소드
        private bool TryReadTxI32(ushort idx, byte sub, out int value)
        {
            value = 0;

            if (!_txMapTable.TryGetValue(new OdKey(idx, sub), out var f))
                return false;

            // PDO는 보통 byte-aligned로 매핑됨 (0x6064는 INT32=32bit)
            if (f.BitLen != 32 || f.BitInByte != 0)
                return false;

            var span = InputSnapshot.Span; // Slave→Master (TxPDO) snapshot
            int off = f.ByteOffset;
            if ((uint)off + 4u > (uint)span.Length)
                return false;

            value = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(off, 4));

            return true;
        }

        private bool TryReadTxU16(ushort idx, byte sub, out ushort value)
        {
            value = 0;

            if (!_txMapTable.TryGetValue(new OdKey(idx, sub), out var f))
                return false;

            if (f.BitLen != 16 || f.BitInByte != 0)
                return false;

            var span = InputSnapshot.Span;
            int off = f.ByteOffset;

            if ((uint)off + 2u > (uint)span.Length)
                return false;

            value = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(off, 2));
            return true;
        }
        //현재위치 파생.
        private bool TryReadActualPosition6064(out int actualPos)
        {
            return TryReadTxI32(0x6064, 0x00, out actualPos);
        }


        //PPMode ControlWord Bit Offset
        //만약 비트 마스크용이라면, [Flags]사용.
        [Flags]
        private enum ControlWordBit : ushort
        {
            //PPMode전용 맵.
            SwitchOn = 1 << 0,
            EnableVoltage = 1 << 1,
            QuickStop = 1 << 2,
            EnableOperation = 1 << 3,
            NewSetPoint = 1 << 4, //새로운 위치로 이동 수행.
            ChangeSetImmediately = 1 << 5, //0-위치 이동 중 새 명령이 들어왔을때 현재 명령 완료 후 재수행.(정지-이동) 1-현재명령 무시 후 새 명령실행. (정지없이 이동)
            Relative = 1 << 6, //0-abs move, 1-inc move
            FaultReset = 1 << 7,
            Halt = 1 << 8, //0-운전개시 및 계속수행, 1-위치이동 취소, Halt Option Code에 따라 운전 정지.
            PushMode = 1 << 12, //0-일반 위치 이동. 1-위치이동이 Push명령으로 동작.(모터를 목표 위치까지 토크모드로 이송)
            NonStopPush = 1 << 13 //0-Push명령 실행 시 작업물이 감지되면 멈추고 Push명령 해제. 1-Push명령시 작업물이 감지되면 멈추고 사라지면 다시 이동. Halt 명령 받으면 Push명령 해제됨.
        }


        //PPMode StatusWord Bit Offset
        [Flags]
        private enum StatusWordBit : ushort
        {
            //PPMode전용 맵.
            ReadySwitchOn = 1 << 0,
            SwitchedOn = 1 << 1,
            OperationEnabled = 1 << 2,
            Fault = 1 << 3,
            VoltageEnabled = 1 << 4,
            QuickStop = 1 << 5,
            SwitchOnDisabled = 1 << 6,
            PushState = 1 << 8, //0-TargetReached==0 모터가 일반 위치 이동 명령 수행중. 1-TargetReached==0 모터가 Push명령을 수행중
            Remote = 1 << 9, // 1-ControlWord 가 정상적으로 처리됨
            TargetReached = 1 << 10,
            //0-ControlWord의 Halt==0 목표 위치에 도달하지 못함. 
            //0-ControlWord의 Halt==1 제어기가 정지 중입니다.
            //1-ControlWord의 Halt==0 목표 위치에 도착했습니다.
            //1-ControlWord의 Halt==1 제어기가 정지했습니다.
            InternalLimitActive = 1 << 11, //0-SoftwareLimit이 감지되지 않았습니다. 1-SoftwareLimit이 감지되었습니다.
            SetPointAcknowledge = 1 << 12, //0-ControlWord의 NewSetPoint가 0이고 새 위치값의 입력이 가능합니다. 1-ControlWord의 NewSetPoint가 1이고 이전 위치값의 명령을 실행중입니다.
            FollowingError = 1 << 13, //1-위치편차 이상 발생.
            PushDetected = 1 << 14, //1-PushMode중 작업물 감지상태
            SafetyActivated = 1 << 15 //1-Safety기능이 활성화되어 정지상태.
        }
        // 비트 마스크 헬퍼(추가 클래스 없이, 이 클래스에서만 사용) ===

        private static bool HasSW(ushort sw, StatusWordBit mask) => (sw & (ushort)mask) != 0;
        private static ushort SetCW(ushort cw, ControlWordBit mask) => (ushort)(cw | (ushort)mask);
        private static ushort ClearCW(ushort cw, ControlWordBit mask) => (ushort)(cw & (ushort)~(ushort)mask);

        // === 402 전이 기본 CW 값(“값”이라 비트 enum이 아니라 const가 더 명확) ===
        private const ushort CW_SHUTDOWN = 0x0006;
        private const ushort CW_SWITCHON = 0x0007;
        private const ushort CW_ENABLEOP = 0x000F;

        //PDO Received에서 사용되는 필드.
        private bool _waitSpAck;
        private bool _waitSpAckClear;

        private bool _waitFaultClear;
        private int _faultResetHold;     // 안전용: 최대 몇 cycle까지만 1 유지 (예: 3)

        private volatile bool _reqMoveAbs;
        private volatile int _moveAbsTarget;

        private volatile bool _reqFaultReset;
        private volatile bool _reqEnable;


        public override void OnAfterPdoReceived()
        {
            //SW보고 CW작업하기 위해사용.
            //alloc/Lock/Log금지.

            //좀 맘에 안드는데, 이전꺼 읽어와서 비트만 넣어주는 방식으로 나중에 다시 작성.

            //없으면 리턴.
            if (_off6040cw < 0 || _off6041sw < 0)
                return;

            //비트 오프셋 필터
            if ((uint)_off6041sw + 2u > (uint)Input.Length)
                return;

            if ((uint)_off6040cw + 2u > (uint)Output.Length)
                return;

            // 1) 입력(TxPDO) 직접 읽기
            // Statusword 0x6041 (u16)
            ushort sw = BinaryPrimitives.ReadUInt16LittleEndian(Input.Slice(_off6041sw, 2));

            //기존 ControlWord.
            ushort prevCw = BinaryPrimitives.ReadUInt16LittleEndian(Output.Slice(_off6040cw, 2));


            // Actual Position 0x6064 (i32) - 있으면 캐시/상태 업데이트에 사용
            if (_off6064ap >= 0 && (uint)_off6064ap + 4u <= (uint)Input.Length)
            {
                int ap = BinaryPrimitives.ReadInt32LittleEndian(Input.Slice(_off6064ap, 4));
                // 필요하면 내부 캐시에 저장(alloc 없이)
                // _actualPosCache = ap;
            }
            // 2) 402 상태 비트
            bool swReadyToSwitchOn = HasSW(sw, StatusWordBit.ReadySwitchOn);
            bool swSwitchOn = HasSW(sw, StatusWordBit.SwitchedOn);
            bool swOperationEnabled = HasSW(sw, StatusWordBit.OperationEnabled);
            bool swFault = HasSW(sw, StatusWordBit.Fault);

            bool swSetPointAck = HasSW(sw, StatusWordBit.SetPointAcknowledge); // 네 enum 이름에 맞춰

            // 3) 다음 cycle에 보낼 Controlword 계산
            ushort cw = Base402Controlword(sw, prevCw, _reqEnable);


            // 4) Fault reset 펄스 처리(1 cycle)
            if (_reqFaultReset)
            {
                _reqFaultReset = false;

                // Fault가 아닐 때 reset은 의미 없을 수 있으니, Fault일 때만 시작
                if (swFault)
                {
                    _waitFaultClear = true;
                    _faultResetHold = 3; // 안전: 최대 3 cycle만 1 유지 (원하면 1로)
                }
            }

            if (_waitFaultClear)
            {
                if (swFault && _faultResetHold > 0)
                {
                    cw = SetCW(cw, ControlWordBit.FaultReset);
                    _faultResetHold--;
                }
                else
                {
                    // fault가 꺼졌거나(목표), 또는 hold 끝났으면 reset bit는 0으로 내림
                    cw = ClearCW(cw, ControlWordBit.FaultReset);

                    if (!swFault)
                    {
                        // fault가 실제로 사라졌으면 종료
                        _waitFaultClear = false;
                    }
                    else
                    {
                        // fault가 여전히 남아있으면: 원인 미해결/추가 절차 필요 상태
                        // (여기서 또 reset을 반복하고 싶으면 별도 정책으로)
                        _waitFaultClear = false;
                    }
                }
            }
            else
            {
                // 기본은 0 유지
                cw = ClearCW(cw, ControlWordBit.FaultReset);
            }


            // 5) MoveABS 요청 처리: TargetPosition 쓰고 NewSetPoint 펄스
            if (_reqMoveAbs)
            {
                _reqMoveAbs = false;

                if (_off607Atp >= 0 && (uint)_off607Atp + 4u <= (uint)Output.Length)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(Output.Slice(_off607Atp, 4), _moveAbsTarget);

                    // abs move
                    cw = ClearCW(cw, ControlWordBit.Relative); //ABS
                    _waitSpAck = true;
                    _waitSpAckClear = false;
                }
            }

            if (_waitSpAck)
            {
                cw = SetCW(cw, ControlWordBit.NewSetPoint);

                if (swSetPointAck)
                {
                    // Ack 확인 후 NSP는 다음 사이클부터 0으로 내림
                    _waitSpAck = false;
                    _waitSpAckClear = true;
                }
            }
            else
            {
                cw = ClearCW(cw, ControlWordBit.NewSetPoint);

                if (_waitSpAckClear && !swSetPointAck)
                {
                    // Ack가 0으로 내려오면 handshake 종료
                    _waitSpAckClear = false;
                }
            }


            // 6) 최종 CW를 RxPDO(Output)에 기록
            if ((uint)_off6040cw + 2u > (uint)Output.Length)
                return;

            // 7) 최종 Controlword를 outputs(RxPDO)로 기록
            BinaryPrimitives.WriteUInt16LittleEndian(Output.Slice(_off6040cw, 2), cw);

        }

        private ushort Base402Controlword(ushort sw, ushort prevCw, bool enableRequested)
        {
            bool fault = HasSW(sw, StatusWordBit.Fault);

            // Fault면 기본 전이는 의미 없고, Reset 로직은 별도(위에서 처리)
            if (fault)
                return 0x0000;

            // enable 요청이 없으면 "Shutdown(0x0006)"로 두는 게 보통 안정적(Ready 유지)
            // 완전 OFF로 떨어뜨리고 싶으면 0x0000으로 바꿔도 됨(정책)
            ushort cwBase;

            if (!enableRequested)
                return CW_SHUTDOWN; // 0x0006
            else
            {
                bool swReadySwitchOn = HasSW(sw, StatusWordBit.ReadySwitchOn);      // Ready to switch on
                bool swSwitchedOn = HasSW(sw, StatusWordBit.SwitchedOn);         // Switched on
                bool swOperationEnabled = HasSW(sw, StatusWordBit.OperationEnabled);   // Operation enabled

                // 402 기본 시퀀스:
                // Shutdown(0x0006) -> Switch on(0x0007) -> Enable op(0x000F)
                cwBase = CW_SHUTDOWN;     // 0x0006
                if (swReadySwitchOn) cwBase = CW_SWITCHON;   // 0x0007
                if (swSwitchedOn) cwBase = CW_ENABLEOP;   // 0x000F
                if (swOperationEnabled) cwBase = CW_ENABLEOP;   // 0x000F
            }

            // ===== 기존 CW에서 "유지해도 되는 비트"만 가져오기 =====
            // 여기 리스트는 네 프로젝트 정책에 따라 조절.
            // 보통 PP에서는 Relative/ChangeNow/Halt 정도는 유지해도 OK.
            ushort keepMask = (ushort)(ControlWordBit.Relative | ControlWordBit.ChangeSetImmediately);

            // keepMask |= CWMask(ControlWordBit.Halt); // 네 enum에 있으면(필요시)

            ushort cw = (ushort)(cwBase | (prevCw & keepMask));

            // 펄스/핸드셰이크 비트는 prev에서 절대 이어받지 않게 "정규화"
            cw = ClearCW(cw, ControlWordBit.NewSetPoint);
            cw = ClearCW(cw, ControlWordBit.FaultReset);

            return cw;
        }


        //IMotorCommand 구현부.
        public bool MoveABS(int position)
        {
            _moveAbsTarget = position;
            _reqMoveAbs = true;
            return true;
        }

        public bool MoveINC(int position)
        {
            throw new NotImplementedException();
        }

        public bool Stop()
        {
            throw new NotImplementedException();
        }

        public bool JogPlus()
        {
            throw new NotImplementedException();
        }

        public bool JogMinus()
        {
            throw new NotImplementedException();
        }

        public bool JogStop()
        {
            throw new NotImplementedException();
        }

        public bool AlarmClear()
        {
            _reqFaultReset = true;
            return true;
        }

        public bool ServoOn()
        {
            _reqEnable = true; 
            return true;
        }

        public bool ServoOff()
        {
            _reqEnable = false; 
            return true;
        }

        public bool Home()
        {
            throw new NotImplementedException();
        }

        public readonly struct PdoMapEntry
        {
            public readonly ushort Index;
            public readonly byte SubIndex;
            public readonly byte BitLen;

            public readonly int BitOffset;   // 누적
            public int ByteOffset => BitOffset >> 3;  // BitOffset / 8
            public int BitInByte => BitOffset & 7;   // BitOffset % 8

            public PdoMapEntry(ushort index, byte subIndex, byte bitLen, int bitOffset)
            {
                Index = index;
                SubIndex = subIndex;
                BitLen = bitLen;
                BitOffset = bitOffset;
            }
            public static PdoMapEntry FromMapWord(uint mapWord, int bitOffset)
            {
                ushort idx = (ushort)(mapWord >> 16);
                byte sub = (byte)(mapWord >> 8);
                byte bitLen = (byte)(mapWord & 0xFF);
                return new PdoMapEntry(idx, sub, bitLen, bitOffset);
            }
        }


        private readonly struct OdKey : IEquatable<OdKey>
        {
            public readonly ushort Index;
            public readonly byte SubIndex;

            public OdKey(ushort index, byte subIndex) { Index = index; SubIndex = subIndex; }

            public bool Equals(OdKey other) => Index == other.Index && SubIndex == other.SubIndex;
            public override bool Equals(object obj) => obj is OdKey other && Equals(other);
            public override int GetHashCode() => (Index << 8) ^ SubIndex;
        }

        private readonly struct PdoField
        {
            public readonly int BitOffset;
            public readonly byte BitLen;
            public PdoField(int bitOffset, byte bitLen) { BitOffset = bitOffset; BitLen = bitLen; }

            public int ByteOffset => BitOffset >> 3;
            public int BitInByte => BitOffset & 7;
        }


    }
}
