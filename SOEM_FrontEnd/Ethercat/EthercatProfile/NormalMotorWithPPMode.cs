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

        //서보 IO리미트 전체.
        private int _off60FDio = -1; //Tx: Servo IO Status.

        //모드가 있는경우 문제가 발생하는거 같음.
        private int _off6060mode = -1;        // Rx: Modes of operation
        private int _off6061modeDisplay = -1; // Tx: Modes of operation display

        private sbyte _pdoModeCommand = 1; // 1=PP, 6=Homing

        //max Torque가 PDO맵에 있는경우 문제가 발생...
        private int _off6072maxTorque = -1; // Rx: Max torque
        private const ushort TEMP_MAX_TORQUE_6072 = 0x1388; // 5000

        //max Speed도 PDO맵에 있는경우 문제가 되네...?
        private int _off6080maxMotorSpeed = -1; // Rx: Max motor speed, UDINT
        private const uint TEMP_MAX_SPEED_6080 = 3000000u;  // 임시값. 너무 크면 낮춰도 됨.


        //SDO 핫패스.
        private SDOPoint _sdo6060; // Mode of operation
        private SDOPoint _sdo6098; // Homing method 같은 것
        //SDO 핫패스. 홈기동용.
        private SDOPoint _sdo6099_01; // Homing speed: search switch
        private SDOPoint _sdo6099_02; // Homing speed: search zero
        private SDOPoint _sdo609A;    // Homing acceleration
        private SDOPoint _sdo607C;    // Home offset

        //SDO 핫패스. 일반기동용.
        private SDOPoint _sdo6081; //Profile Velocity
        private SDOPoint _sdo6083; //Profile Acceleration
        private SDOPoint _sdo6084; //Profile Deceleration

        private SDOSubWorker _sdoWorker;

        private readonly byte[] _u32WriteBuffer = new byte[4];

        private uint _profileVelocity = 0;
        private uint _profileAcceleration = 0;
        private uint _profileDeceleration = 0;

        //홈기동용 Write Buffer.
        private readonly byte[] _i8WriteBuffer = new byte[1];
        private readonly byte[] _i32WriteBuffer = new byte[4];

        //정확한 정지용으로.
        private bool _reqAnchorActualPosition;


        private readonly EcClient _ECClient;

        public void AttachSdoWorker(SDOSubWorker sdoWorker)
        {
            _sdoWorker = sdoWorker;
        }


        //IMotorCommands 구현부.
        //public int AxisID => throw new NotImplementedException();

        public bool IsServoOn => _isServoOn;

        public bool IsHome => _isHomed;

        public bool IsError => _isError;

        public bool IsInPosition => _isTargetReached;

        public int ActualPosition => getActualPosition();

        public bool IsNLimSensor => _isNlimOn;
        public bool IsHomeSensor => _isHomeOn;
        public bool IsPLimSensor => _isPlimOn;

        public void SetProfile(uint velocity, uint acceleration, uint deceleration)
        {
            if (_profileVelocity != velocity ||
                _profileAcceleration != acceleration ||
                _profileDeceleration != deceleration)
            {
                _profileVelocity = velocity;
                _profileAcceleration = acceleration;
                _profileDeceleration = deceleration;
                _profileDirty = true;
            }
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

        private bool WriteSdoByWorker(ushort index, byte subIndex, byte[] raw)
        {
            if (_sdoWorker == null)
                return false;

            if (_sdoWorker.IsRunning == false)
                return false;

            if (raw == null || raw.Length == 0)
                return false;

            bool ok = _sdoWorker.EnqueueWriteAsync(_SlaveNo, index, subIndex, raw)
                .GetAwaiter()
                .GetResult();

            return ok;
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

            //sdoworker로 변경.
            //_ECClient.SdoWriteI8(_SlaveNo, 0x6060, 0x00, 1); //PPMode 1

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

            //byte[] raw = new byte[1];
            //raw[0] = 1;

            //bool ok = WriteSdoByWorker(0x6060, 0x00, raw);
            //if (ok == false)
            //    return false;


            _pdoModeCommand = 1;

            // 0x6060이 RxPDO에 매핑되어 있으면 SDO write보다 PDO output image를 먼저 채운다.
            // 그렇지 않으면 기존처럼 SDO로 6060=1을 쓴다.
            if (_off6060mode >= 0 && (uint)_off6060mode < (uint)Output.Length)
            {
                Output[_off6060mode] = 1;
            }
            else
            {
                byte[] raw = new byte[1];
                raw[0] = 1;

                bool ok = WriteSdoByWorker(0x6060, 0x00, raw);
                if (ok == false)
                {
                    return false;
                }
            }

            // TEMP: 0x6072 Max torque가 RxPDO에 매핑된 경우,
            // SDO가 아니라 PDO Output image에 값을 넣어야 유지됨.
            WriteTemporaryMaxTorquePdo();

            WriteTemporaryMaxMotorSpeedPdo();


            SlaveStore slaveStore = Datamap.Instance.GetSlave(_SlaveNo);
            if (slaveStore == null)
                return false;

            BindSdoHotRefs(Datamap.Instance.GetSlave(_SlaveNo));

           

            return true;
        }

        public override void SetPdoMapping(List<uint> rxAllMap, List<uint> txAllMap)
        {
            Build(_rxMapTable, rxAllMap);
            Build(_txMapTable, txAllMap);

            SetCurrentPdoMapRows(rxAllMap, txAllMap);

            TryResolve402();

            TryServoIOMapping();
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
            if (slave == null)
                return;

            //Dic으로 구성된 데이터 구조라 RT에서 읽기가 안정적이지 않음.
            _sdo6060 = slave.TryGetSdo(0x6060, 0x00);
            _sdo6098 = slave.TryGetSdo(0x6098, 0x00);

            _sdo6081 = slave.TryGetSdo(0x6081, 0x00);
            _sdo6083 = slave.TryGetSdo(0x6083, 0x00);
            _sdo6084 = slave.TryGetSdo(0x6084, 0x00);


            //홈 기동용 핫패스 바인딩.
            _sdo6099_01 = slave.TryGetSdo(0x6099, 0x01);
            _sdo6099_02 = slave.TryGetSdo(0x6099, 0x02);
            _sdo609A = slave.TryGetSdo(0x609A, 0x00);
            _sdo607C = slave.TryGetSdo(0x607C, 0x00);

        }

        public bool TryResolve402()
        {
            _off6040cw = TryGetByteOffset(_rxMapTable, 0x6040, 0x00);
            _off607Atp = TryGetByteOffset(_rxMapTable, 0x607A, 0x00);

            _off6041sw = TryGetByteOffset(_txMapTable, 0x6041, 0x00);
            _off6064ap = TryGetByteOffset(_txMapTable, 0x6064, 0x00);

            _off6060mode = TryGetByteOffset(_rxMapTable, 0x6060, 0x00);
            _off6061modeDisplay = TryGetByteOffset(_txMapTable, 0x6061, 0x00);

            _off6072maxTorque = TryGetByteOffset(_rxMapTable, 0x6072, 0x00);

            _off6080maxMotorSpeed = TryGetByteOffset(_rxMapTable, 0x6080, 0x00);



            return _off6040cw >= 0 && _off607Atp >= 0 && _off6041sw >= 0;
        }

        public bool TryServoIOMapping()
        {
            _off60FDio = -1;

            PdoField field;

            if (_txMapTable.TryGetValue(new OdKey(0x60FD, 0x00), out field) == false)
            {
                return false;
            }

            if (field.BitInByte != 0)
            {
                return false;
            }

            if (field.BitLen < 32)
            {
                return false;
            }

            _off60FDio = field.ByteOffset;

            return true;
        }

        private void WriteTemporaryMaxTorquePdo()
        {
            if (_off6072maxTorque < 0)
            {
                return;
            }

            if ((uint)_off6072maxTorque + 2u > (uint)Output.Length)
            {
                return;
            }

            BinaryPrimitives.WriteUInt16LittleEndian(
                Output.Slice(_off6072maxTorque, 2),
                TEMP_MAX_TORQUE_6072);
        }

        private void WriteTemporaryMaxMotorSpeedPdo()
        {
            if (_off6080maxMotorSpeed < 0)
            {
                return;
            }

            if ((uint)_off6080maxMotorSpeed + 4u > (uint)Output.Length)
            {
                return;
            }

            BinaryPrimitives.WriteUInt32LittleEndian(
                Output.Slice(_off6080maxMotorSpeed, 4),
                TEMP_MAX_SPEED_6080);
        }



        private static int TryGetByteOffset(Dictionary<OdKey, PdoField> dict, ushort idx, byte sub)
        {
            if (dict.TryGetValue(new OdKey(idx, sub), out var f))
                return f.ByteOffset;
            return -1;
        }

        private void WritePdoModeCommand()
        {
            if (_off6060mode < 0)
            {
                return;
            }

            if ((uint)_off6060mode >= (uint)Output.Length)
            {
                return;
            }

            Output[_off6060mode] = unchecked((byte)_pdoModeCommand);
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

        private static void MarkWritePending(SDOPoint point)
        {
            if (point == null)
                return;

            point.WriteStatus = SDOWriteStatus.Pending;
            point.AbortCode = 0;
            point.Error = null;
        }

        private bool TryQueueWriteU32(ushort index, byte subIndex, uint value, SDOPoint point)
        {
            if (_sdoWorker == null)
                return false;

            if (point == null)
                return false;

            MarkWritePending(point);

            BinaryPrimitives.WriteUInt32LittleEndian(_u32WriteBuffer.AsSpan(0, 4), value);
            _sdoWorker.EnqueueWrite(_SlaveNo, index, subIndex, _u32WriteBuffer);
            return true;
        }

        private bool TryQueueWriteU32(ushort index, uint value, SDOPoint point)
        {
            return TryQueueWriteU32(index, 0x00, value, point);
        }

        private bool TryQueueWriteI8(ushort index, byte subIndex, sbyte value, SDOPoint point)
        {
            if (_sdoWorker == null)
                return false;

            if (point == null)
                return false;

            MarkWritePending(point);

            _i8WriteBuffer[0] = unchecked((byte)value);
            _sdoWorker.EnqueueWrite(_SlaveNo, index, subIndex, _i8WriteBuffer);
            return true;
        }

        private bool TryQueueWriteI32(ushort index, byte subIndex, int value, SDOPoint point)
        {
            if (_sdoWorker == null)
                return false;

            if (point == null)
                return false;

            MarkWritePending(point);

            BinaryPrimitives.WriteInt32LittleEndian(_i32WriteBuffer.AsSpan(0, 4), value);
            _sdoWorker.EnqueueWrite(_SlaveNo, index, subIndex, _i32WriteBuffer);
            return true;
        }



        private static bool TryReadHotU32(SDOPoint point, out uint value)
        {
            value = 0;

            if (point == null)
                return false;

            byte[] raw = point.LastRaw;
            if (raw == null)
                return false;

            if (raw.Length < 4)
                return false;

            value = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(0, 4));
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
        //PP모드 Bit12 = SetPointAcknowledge
        //Home모드 Bit12 = Homing attained

        //PP모드 Bit13 = Following error
        //Home모드 Bit13 = Homing error


        //비트 마스크 헬퍼. 여기서만 사용할것.
        private static bool HasSW(ushort sw, StatusWordBit mask) => (sw & (ushort)mask) != 0;
        private static ushort SetCW(ushort cw, ControlWordBit mask) => (ushort)(cw | (ushort)mask);
        private static ushort ClearCW(ushort cw, ControlWordBit mask) => (ushort)(cw & (ushort)~(ushort)mask);

        //홈 기동용 StatusWord Helper
        private static bool HasHomingAttained(ushort sw)
        {
            return (sw & (1 << 12)) != 0;
        }

        private static bool HasHomingError(ushort sw)
        {
            return (sw & (1 << 13)) != 0;
        }


        //402 전이 기본 CW 값
        private const ushort CW_SHUTDOWN = 0x0006;
        private const ushort CW_SWITCHON = 0x0007;
        private const ushort CW_ENABLEOP = 0x000F;

        //PDO Received에서 사용되는 필드.
        private bool _waitFaultClear;
        private int _faultResetHold;     // 안전용: 최대 몇 cycle까지만 1 유지 (예: 3)

        private bool _haltActive;

        private volatile bool _reqMove;
        private volatile int _moveTarget;

        private volatile bool _reqStop;

        private volatile bool _reqFaultReset;
        private volatile bool _reqEnable;
        private volatile bool _IsAbsMove;

        private MoveState _moveState = MoveState.Idle;

        private bool _isServoOn;
        private bool _isError;
        private bool _isTargetReached;
        private bool _isSetPointAck;
        private ushort _statusWordCache;

        //Jog용으로..
        private volatile bool _jogActive;
        private volatile int _jogDirection;   // +1 / -1
        private int _jogStepPulse;

        private volatile bool _profileDirty = true;
        private uint _appliedProfileVelocity;
        private uint _appliedProfileAcceleration;
        private uint _appliedProfileDeceleration;

        //ServoIO 표기용.
        private bool _isNlimOn;
        private bool _isPlimOn;
        private bool _isHomeOn;


        //홈 기동용 파라미터 필드.
        private sbyte _homeMethod = 35;
        private uint _homeSearchSwitchSpeed = 1000;
        private uint _homeSearchZeroSpeed = 500;
        private uint _homeAcceleration = 1000;
        private int _homeOffset = 0;

        private bool _isHomed;
        private bool _homeRestoreThenFault;

        private MotionCommand _motion = MotionCommand.None;

        private enum MotionCommand
        {
            None = 0,
            MoveAbs,
            MoveInc,
            Jog,
            Home
        }

        private enum MoveState
        {
            Idle = 0,

            QueueWrite6081,
            WaitWrite6081,

            QueueWrite6083,
            WaitWrite6083,

            QueueWrite6084,
            WaitWrite6084,

            QueuePdoStart,
            WaitSetPointAck,
            WaitSetPointAckClear,
            WaitTargetReached,

            // Jog Stop 시 현재 위치를 새 Absolute PP target으로 latch해서
            // 이전 relative jog target을 제거하기 위한 시퀀스.
            JogAnchorWaitAckClear,
            JogAnchorQueue,
            JogAnchorWaitAck,
            JogAnchorDone,

            //이하는 홈 기동용 시퀀스.
            QueueModeHoming,
            WaitModeHoming,

            QueueHomeMethod,
            WaitHomeMethod,

            QueueHomeSpeedSwitch,
            WaitHomeSpeedSwitch,

            QueueHomeSpeedZero,
            WaitHomeSpeedZero,

            QueueHomeAcceleration,
            WaitHomeAcceleration,

            QueueHomeOffset,
            WaitHomeOffset,

            QueuePdoHomeStart,
            WaitHomeComplete,

            QueueModePP,
            WaitModePP,

            Done,
            Fault
        }



        public override void OnAfterPdoReceived()
        {
            //SW보고 CW작업하기 위해사용.
            //alloc/Lock/Log금지.

            //servo io 추가. 
            //나중에 명령줄때 이거 기준으로 줘야 할 수 있으므로, 앞쪽으로 이동.
            UpdateServoIO();


            //좀 맘에 안드는데, 이전꺼 읽어와서 비트만 넣어주는 방식으로 나중에 다시 작성.

            //없으면 리턴.
            if (_off6040cw < 0 || _off6041sw < 0)
                return;

            //비트 오프셋 필터
            if ((uint)_off6041sw + 2u > (uint)Input.Length)
                return;

            if ((uint)_off6040cw + 2u > (uint)Output.Length)
                return;

            //1.입력(TxPDO) 직접 읽기
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
            //2.402 상태 비트
            bool swReadyToSwitchOn = HasSW(sw, StatusWordBit.ReadySwitchOn);
            bool swSwitchOn = HasSW(sw, StatusWordBit.SwitchedOn);
            bool swOperationEnabled = HasSW(sw, StatusWordBit.OperationEnabled);
            bool swFault = HasSW(sw, StatusWordBit.Fault);

            bool swSetPointAck = HasSW(sw, StatusWordBit.SetPointAcknowledge); 

            bool swTargetReached = HasSW(sw, StatusWordBit.TargetReached);

            bool swVoltageEnabled = HasSW(sw, StatusWordBit.VoltageEnabled);
            bool swQuickStop = HasSW(sw, StatusWordBit.QuickStop);
            bool swRemote = HasSW(sw, StatusWordBit.Remote);


            //_isServoOn = swOperationEnabled && !swFault;

            //조건 추가.
            _isServoOn = swReadyToSwitchOn && swSwitchOn && swVoltageEnabled && swOperationEnabled  && !swFault;

            _isError = swFault;
            _isSetPointAck = swSetPointAck;
            _isTargetReached = swTargetReached;


            //3.다음 cycle에 보낼 Controlword 계산
            ushort cw = Base402Controlword(sw, prevCw, _reqEnable);


            //4.Fault reset 펄스 처리(1 cycle)
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

            // Stop 요청 처리
            // Stop 요청 처리
            if (_reqStop)
            {
                _reqStop = false;

                bool reqAnchorActualPosition = _reqAnchorActualPosition;
                _reqAnchorActualPosition = false;

                _reqMove = false;
                _jogActive = false;
                _jogDirection = 0;

                cw = ClearCW(cw, ControlWordBit.NewSetPoint);
                cw = ClearCW(cw, ControlWordBit.ChangeSetImmediately);
                cw = ClearCW(cw, ControlWordBit.Relative);

                if (reqAnchorActualPosition)
                {
                    // Jog release에서는 단순 Halt만 걸면 이전 relative PP target이
                    // 드라이브 내부에 남을 수 있다.
                    // 따라서 현재 위치를 새 Absolute target으로 latch하는 상태로 보낸다.
                    _haltActive = true;
                    _motion = MotionCommand.None;
                    _moveState = MoveState.JogAnchorWaitAckClear;
                }
                else
                {
                    _haltActive = true;

                    if (_moveState != MoveState.Idle)
                    {
                        if (_motion == MotionCommand.Home)
                        {
                            _homeRestoreThenFault = false;
                            _moveState = MoveState.QueueModePP;
                        }
                        else
                        {
                            _motion = MotionCommand.None;
                            _moveState = MoveState.Idle;
                        }
                    }
                }
            }

            // 현재 Halt 상태 반영
            if (_haltActive)
            {
                cw = SetCW(cw, ControlWordBit.Halt);
            }
            else
            {
                cw = ClearCW(cw, ControlWordBit.Halt);
            }

            // Jog 요청 처리
            if (_jogActive)
            {
                if (_moveState == MoveState.Idle && _reqMove == false)
                {
                    int direction = _jogDirection;

                    if (direction != 0)
                    {
                        // 여기서 Halt를 풀면 이전 상대이동 명령이 먼저 재개될 수 있음.
                        // Halt 해제는 QueuePdoStart에서 새 target을 쓴 뒤에 한다.
                        //_haltActive = false;
                        _IsAbsMove = false;
                        _moveTarget = direction > 0 ? _jogStepPulse : -_jogStepPulse;
                        _reqMove = true;
                    }
                }
            }

            ProcessMoveAbsStateMachine(ref cw, swSetPointAck, swTargetReached, swFault, swOperationEnabled, sw);

            //5.최종 CW를 RxPDO(Output)에 기록
            if ((uint)_off6040cw + 2u > (uint)Output.Length)
                return;


            // 6060이 RxPDO에 매핑된 드라이브는 SDO로 쓴 mode가 PDO output에 의해 덮일 수 있다.
            // 따라서 매 cycle 현재 mode command를 명시적으로 써준다.
            WritePdoModeCommand();

            // TEMP: MINAS A6BE 테스트용.
            // 0x6072 Max torque가 RxPDO에 매핑되어 있으면,
            // Output image에 계속 써줘야 0으로 덮이지 않음.
            WriteTemporaryMaxTorquePdo();

            //MaxSpeed도 마찬가지...
            WriteTemporaryMaxMotorSpeedPdo();



            //6.최종 Controlword를 outputs(RxPDO)로 기록
            BinaryPrimitives.WriteUInt16LittleEndian(Output.Slice(_off6040cw, 2), cw);


        }

        private void UpdateServoIO()
        {
            if (_off60FDio < 0)
            {
                _isNlimOn = false;
                _isPlimOn = false;
                _isHomeOn = false;
                return;
            }

            if ((uint)_off60FDio + 4u > (uint)Input.Length)
            {
                _isNlimOn = false;
                _isPlimOn = false;
                _isHomeOn = false;
                return;
            }

            uint servoio = BinaryPrimitives.ReadUInt32LittleEndian(Input.Slice(_off60FDio, 4));

            // 0x60FD 기준: bit0 = N-Limit, bit1 = P-Limit, bit2 = Home
            _isNlimOn = (servoio & 0x00000001u) != 0;
            _isPlimOn = (servoio & 0x00000002u) != 0;
            _isHomeOn = (servoio & 0x00000004u) != 0;
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

            // 보통 PP에서는 Relative/ChangeNow/Halt 정도는 유지해도 OK.
            //ushort keepMask = (ushort)(ControlWordBit.Relative | ControlWordBit.ChangeSetImmediately);
            //ushort cw = (ushort)(cwBase | (prevCw & keepMask));

            // 펄스/핸드셰이크 비트는 prev에서 절대 이어받지 않게 "정규화"
            //cw = ClearCW(cw, ControlWordBit.NewSetPoint);
            //cw = ClearCW(cw, ControlWordBit.FaultReset);


            ushort cw = cwBase;

            cw = ClearCW(cw, ControlWordBit.NewSetPoint);
            cw = ClearCW(cw, ControlWordBit.FaultReset);
            cw = ClearCW(cw, ControlWordBit.Relative);
            cw = ClearCW(cw, ControlWordBit.ChangeSetImmediately);
            cw = ClearCW(cw, ControlWordBit.Halt);

            return cw;
        }

        private void ProcessMoveAbsStateMachine(ref ushort cw, bool swSetPointAck, bool swTargetReached, bool swFault, bool swOperationEnabled, ushort sw)
        {
            uint readValue;

            if (swFault && _moveState != MoveState.Idle)
            {
                _moveState = MoveState.Fault;
            }

            switch (_moveState)
            {
                case MoveState.Idle:
                    {
                        if (_reqMove == false)
                            break;

                        _reqMove = false;

                        if (_sdoWorker == null)
                        {
                            _moveState = MoveState.Fault;
                            break;
                        }

                        if (_isServoOn == false || _isError == true)
                        {
                            _moveState = MoveState.Fault;
                            break;
                        }

                        if (_motion == MotionCommand.Home)
                        {
                            if (_sdo6060 == null || _sdo6098 == null || _sdo6099_01 == null || _sdo6099_02 == null || _sdo609A == null || _sdo607C == null)
                            {
                                _moveState = MoveState.Fault;
                                break;
                            }

                            _isHomed = false;
                            _homeRestoreThenFault = false;
                            _moveState = MoveState.QueueModeHoming;
                            break;
                        }


                        if (_sdo6081 == null || _sdo6083 == null || _sdo6084 == null)
                        {
                            _moveState = MoveState.Fault;
                            break;
                        }

                        if (_profileDirty)
                        {
                            _moveState = MoveState.QueueWrite6081;
                        }
                        else
                        {
                            _moveState = MoveState.QueuePdoStart;
                        }

                        break;
                    }

                case MoveState.QueueWrite6081:
                    {
                        if (TryQueueWriteU32(0x6081, _profileVelocity, _sdo6081) == false)
                        {
                            _moveState = MoveState.Fault;
                            break;
                        }

                        _moveState = MoveState.WaitWrite6081;
                        break;
                    }

                case MoveState.WaitWrite6081:
                    {
                        if (_sdo6081.WriteStatus == SDOWriteStatus.Pending || _sdo6081.WriteStatus == SDOWriteStatus.None)
                            break;

                        if (_sdo6081.WriteStatus != SDOWriteStatus.Ok)
                        {
                            _moveState = MoveState.Fault;
                            break;
                        }

                        _moveState = MoveState.QueueWrite6083;
                        break;
                    }

                case MoveState.QueueWrite6083:
                    {
                        if (TryQueueWriteU32(0x6083, _profileAcceleration, _sdo6083) == false)
                        {
                            _moveState = MoveState.Fault;
                            break;
                        }

                        _moveState = MoveState.WaitWrite6083;
                        break;
                    }

                case MoveState.WaitWrite6083:
                    {
                        if (_sdo6083.WriteStatus == SDOWriteStatus.Pending || _sdo6083.WriteStatus == SDOWriteStatus.None)
                            break;

                        if (_sdo6083.WriteStatus != SDOWriteStatus.Ok)
                        {
                            _moveState = MoveState.Fault;
                            break;
                        }

                        _moveState = MoveState.QueueWrite6084;
                        break;
                    }

            

                case MoveState.QueueWrite6084:
                    {
                        if (TryQueueWriteU32(0x6084, _profileDeceleration, _sdo6084) == false)
                        {
                            _moveState = MoveState.Fault;
                            break;
                        }

                        _moveState = MoveState.WaitWrite6084;
                        break;
                        
                    }

                case MoveState.WaitWrite6084:
                    {
                        if (_sdo6084.WriteStatus == SDOWriteStatus.Pending || _sdo6084.WriteStatus == SDOWriteStatus.None)
                            break;

                        if (_sdo6084.WriteStatus != SDOWriteStatus.Ok)
                        {
                            _moveState = MoveState.Fault;
                            break;
                        }

                        _appliedProfileVelocity = _profileVelocity;
                        _appliedProfileAcceleration = _profileAcceleration;
                        _appliedProfileDeceleration = _profileDeceleration;
                        _profileDirty = false;

                        _moveState = MoveState.QueuePdoStart;
                        break;
                    }

                case MoveState.QueuePdoStart:
                    {
                        if (swFault)
                        {
                            _moveState = MoveState.Fault;
                            break;
                        }

                        if (swOperationEnabled == false)
                        {
                            break;
                        }

                        if (_off607Atp < 0 || (uint)_off607Atp + 4u > (uint)Output.Length)
                        {
                            _moveState = MoveState.Fault;
                            break;
                        }

                        BinaryPrimitives.WriteInt32LittleEndian(Output.Slice(_off607Atp, 4), _moveTarget);

                        // 새 target을 PDO image에 쓴 뒤에만 Halt를 해제한다.
                        // 그래야 이전 Jog 상대이동 명령이 재개되지 않음.
                        _haltActive = false;
                        cw = ClearCW(cw, ControlWordBit.Halt);


                        if (_IsAbsMove == true)
                        {
                            cw = ClearCW(cw, ControlWordBit.Relative);
                        }
                        else
                        {
                            cw = SetCW(cw, ControlWordBit.Relative);
                        }

                        if (_jogActive)
                        {
                            cw = SetCW(cw, ControlWordBit.ChangeSetImmediately);
                        }
                        else
                        {
                            cw = ClearCW(cw, ControlWordBit.ChangeSetImmediately);
                        }

                        cw = ClearCW(cw, ControlWordBit.Halt);
                        cw = SetCW(cw, ControlWordBit.NewSetPoint);

                        _moveState = MoveState.WaitSetPointAck;
                        break;
                    }

                case MoveState.WaitSetPointAck:
                    {

                        if (_IsAbsMove == true)
                        {
                            cw = ClearCW(cw, ControlWordBit.Relative);
                        }
                        else
                        {
                            cw = SetCW(cw, ControlWordBit.Relative);
                        }
                        cw = SetCW(cw, ControlWordBit.NewSetPoint);

                        if (swSetPointAck)
                        {
                            _moveState = MoveState.WaitSetPointAckClear;
                        }

                        break;
                    }

                case MoveState.WaitSetPointAckClear:
                    {

                        if (_IsAbsMove == true)
                        {
                            cw = ClearCW(cw, ControlWordBit.Relative);
                        }
                        else
                        {
                            cw = SetCW(cw, ControlWordBit.Relative);
                        }

                        if (_jogActive)
                        {
                            cw = SetCW(cw, ControlWordBit.ChangeSetImmediately);
                        }
                        else
                        {
                            cw = ClearCW(cw, ControlWordBit.ChangeSetImmediately);
                        }

                        cw = ClearCW(cw, ControlWordBit.NewSetPoint);

                        if (!swSetPointAck)
                        {
                            if (_jogActive)
                            {
                                _moveState = MoveState.Done;
                            }
                            else
                            {
                                _moveState = MoveState.WaitTargetReached;
                            }
                        }

                        break;
                    }

                case MoveState.WaitTargetReached:
                    {

                        if (_IsAbsMove == true)
                        {
                            cw = ClearCW(cw, ControlWordBit.Relative);
                        }
                        else
                        {
                            cw = SetCW(cw, ControlWordBit.Relative);
                        }
                        
                        cw = ClearCW(cw, ControlWordBit.NewSetPoint);

                        if (swTargetReached)
                        {
                            _moveState = MoveState.Done;
                        }

                        break;
                    }
                //Jog간의 버그 수정.
                case MoveState.JogAnchorWaitAckClear:
                    {
                        // 이전 PP command의 NewSetPoint/Ack가 내려간 뒤
                        // 현재 위치 anchor command를 넣는다.
                        cw = ClearCW(cw, ControlWordBit.NewSetPoint);
                        cw = ClearCW(cw, ControlWordBit.Relative);
                        cw = ClearCW(cw, ControlWordBit.ChangeSetImmediately);

                        // 이 구간에서는 Halt 유지.
                        // 이전 relative jog target이 재개되지 않게 막는다.
                        cw = SetCW(cw, ControlWordBit.Halt);

                        if (swSetPointAck == false)
                        {
                            _moveState = MoveState.JogAnchorQueue;
                        }

                        break;
                    }

                case MoveState.JogAnchorQueue:
                    {
                        if (swFault)
                        {
                            _moveState = MoveState.Fault;
                            break;
                        }

                        if (swOperationEnabled == false)
                        {
                            break;
                        }

                        if (_off6064ap < 0 || (uint)_off6064ap + 4u > (uint)Input.Length)
                        {
                            _moveState = MoveState.Fault;
                            break;
                        }

                        if (_off607Atp < 0 || (uint)_off607Atp + 4u > (uint)Output.Length)
                        {
                            _moveState = MoveState.Fault;
                            break;
                        }

                        int actualPosition = BinaryPrimitives.ReadInt32LittleEndian(
                            Input.Slice(_off6064ap, 4));

                        BinaryPrimitives.WriteInt32LittleEndian(
                            Output.Slice(_off607Atp, 4),
                            actualPosition);

                        _moveTarget = actualPosition;
                        _IsAbsMove = true;

                        // 현재 위치를 새 Absolute PP target으로 latch한다.
                        // 여기서만 Halt를 푼다.
                        _haltActive = false;

                        cw = ClearCW(cw, ControlWordBit.Halt);
                        cw = ClearCW(cw, ControlWordBit.Relative);
                        cw = SetCW(cw, ControlWordBit.ChangeSetImmediately);
                        cw = SetCW(cw, ControlWordBit.NewSetPoint);

                        _moveState = MoveState.JogAnchorWaitAck;
                        break;
                    }

                case MoveState.JogAnchorWaitAck:
                    {
                        cw = ClearCW(cw, ControlWordBit.Halt);
                        cw = ClearCW(cw, ControlWordBit.Relative);
                        cw = SetCW(cw, ControlWordBit.ChangeSetImmediately);
                        cw = SetCW(cw, ControlWordBit.NewSetPoint);

                        if (swSetPointAck)
                        {
                            _moveState = MoveState.JogAnchorDone;
                        }

                        break;
                    }

                case MoveState.JogAnchorDone:
                    {
                        cw = ClearCW(cw, ControlWordBit.Halt);
                        cw = ClearCW(cw, ControlWordBit.Relative);
                        cw = ClearCW(cw, ControlWordBit.ChangeSetImmediately);
                        cw = ClearCW(cw, ControlWordBit.NewSetPoint);

                        if (swSetPointAck == false)
                        {
                            _motion = MotionCommand.None;
                            _moveState = MoveState.Idle;
                        }

                        break;
                    }


                //홈 시퀀스 케이스 추가.
                case MoveState.QueueModeHoming:
                    {
                        _pdoModeCommand = 6;

                        if (_off6060mode >= 0 && (uint)_off6060mode < (uint)Output.Length)
                        {
                            Output[_off6060mode] = 6;
                            _moveState = MoveState.QueueHomeMethod;
                            break;
                        }

                        if (TryQueueWriteI8(0x6060, 0x00, 6, _sdo6060) == false)
                        {
                            _moveState = MoveState.Fault;
                            break;
                        }

                        _moveState = MoveState.WaitModeHoming;
                        break;
                    }

                case MoveState.WaitModeHoming:
                    {
                        if (_sdo6060.WriteStatus == SDOWriteStatus.Pending ||
                            _sdo6060.WriteStatus == SDOWriteStatus.None)
                        {
                            break;
                        }

                        if (_sdo6060.WriteStatus != SDOWriteStatus.Ok)
                        {
                            _moveState = MoveState.Fault;
                            break;
                        }

                        _moveState = MoveState.QueueHomeMethod;
                        break;
                    }

                case MoveState.QueueHomeMethod:
                    {
                        if (TryQueueWriteI8(0x6098, 0x00, _homeMethod, _sdo6098) == false)
                        {
                            _moveState = MoveState.Fault;
                            break;
                        }

                        _moveState = MoveState.WaitHomeMethod;
                        break;
                    }

                case MoveState.WaitHomeMethod:
                    {
                        if (_sdo6098.WriteStatus == SDOWriteStatus.Pending ||
                            _sdo6098.WriteStatus == SDOWriteStatus.None)
                        {
                            break;
                        }

                        if (_sdo6098.WriteStatus != SDOWriteStatus.Ok)
                        {
                            _moveState = MoveState.Fault;
                            break;
                        }

                        _moveState = MoveState.QueueHomeSpeedSwitch;
                        break;
                    }

                case MoveState.QueueHomeSpeedSwitch:
                    {
                        if (TryQueueWriteU32(0x6099, 0x01, _homeSearchSwitchSpeed, _sdo6099_01) == false)
                        {
                            _moveState = MoveState.Fault;
                            break;
                        }

                        _moveState = MoveState.WaitHomeSpeedSwitch;
                        break;
                    }

                case MoveState.WaitHomeSpeedSwitch:
                    {
                        if (_sdo6099_01.WriteStatus == SDOWriteStatus.Pending ||
                            _sdo6099_01.WriteStatus == SDOWriteStatus.None)
                        {
                            break;
                        }

                        if (_sdo6099_01.WriteStatus != SDOWriteStatus.Ok)
                        {
                            _moveState = MoveState.Fault;
                            break;
                        }

                        _moveState = MoveState.QueueHomeSpeedZero;
                        break;
                    }

                case MoveState.QueueHomeSpeedZero:
                    {
                        if (TryQueueWriteU32(0x6099, 0x02, _homeSearchZeroSpeed, _sdo6099_02) == false)
                        {
                            _moveState = MoveState.Fault;
                            break;
                        }

                        _moveState = MoveState.WaitHomeSpeedZero;
                        break;
                    }

                case MoveState.WaitHomeSpeedZero:
                    {
                        if (_sdo6099_02.WriteStatus == SDOWriteStatus.Pending ||
                            _sdo6099_02.WriteStatus == SDOWriteStatus.None)
                        {
                            break;
                        }

                        if (_sdo6099_02.WriteStatus != SDOWriteStatus.Ok)
                        {
                            _moveState = MoveState.Fault;
                            break;
                        }

                        _moveState = MoveState.QueueHomeAcceleration;
                        break;
                    }

                case MoveState.QueueHomeAcceleration:
                    {
                        if (TryQueueWriteU32(0x609A, 0x00, _homeAcceleration, _sdo609A) == false)
                        {
                            _moveState = MoveState.Fault;
                            break;
                        }

                        _moveState = MoveState.WaitHomeAcceleration;
                        break;
                    }

                case MoveState.WaitHomeAcceleration:
                    {
                        if (_sdo609A.WriteStatus == SDOWriteStatus.Pending ||
                            _sdo609A.WriteStatus == SDOWriteStatus.None)
                        {
                            break;
                        }

                        if (_sdo609A.WriteStatus != SDOWriteStatus.Ok)
                        {
                            _moveState = MoveState.Fault;
                            break;
                        }

                        _moveState = MoveState.QueueHomeOffset;
                        break;
                    }

                case MoveState.QueueHomeOffset:
                    {
                        if (TryQueueWriteI32(0x607C, 0x00, _homeOffset, _sdo607C) == false)
                        {
                            _moveState = MoveState.Fault;
                            break;
                        }

                        _moveState = MoveState.WaitHomeOffset;
                        break;
                    }

                case MoveState.WaitHomeOffset:
                    {
                        if (_sdo607C.WriteStatus == SDOWriteStatus.Pending ||
                            _sdo607C.WriteStatus == SDOWriteStatus.None)
                        {
                            break;
                        }

                        if (_sdo607C.WriteStatus != SDOWriteStatus.Ok)
                        {
                            _moveState = MoveState.Fault;
                            break;
                        }

                        _moveState = MoveState.QueuePdoHomeStart;
                        break;
                    }

                case MoveState.QueuePdoHomeStart:
                    {
                        if (swFault)
                        {
                            _moveState = MoveState.Fault;
                            break;
                        }

                        if (swOperationEnabled == false)
                        {
                            break;
                        }

                        cw = ClearCW(cw, ControlWordBit.Relative);
                        cw = ClearCW(cw, ControlWordBit.ChangeSetImmediately);
                        cw = ClearCW(cw, ControlWordBit.Halt);

                        // Homing mode에서 ControlWord bit4 = Homing operation start
                        cw = SetCW(cw, ControlWordBit.NewSetPoint);

                        _moveState = MoveState.WaitHomeComplete;
                        break;
                    }

                case MoveState.WaitHomeComplete:
                    {
                        bool homingAttained = HasHomingAttained(sw);
                        bool homingError = HasHomingError(sw);

                        // Homing 동작 중에는 bit4 유지
                        cw = SetCW(cw, ControlWordBit.NewSetPoint);

                        if (homingError || swFault)
                        {
                            cw = ClearCW(cw, ControlWordBit.NewSetPoint);

                            _isHomed = false;
                            _homeRestoreThenFault = true;
                            _moveState = MoveState.QueueModePP;
                            break;
                        }

                        if (homingAttained)
                        {
                            cw = ClearCW(cw, ControlWordBit.NewSetPoint);

                            _isHomed = true;
                            _homeRestoreThenFault = false;
                            _moveState = MoveState.QueueModePP;
                        }

                        break;
                    }

                case MoveState.QueueModePP:
                    {
                        _pdoModeCommand = 1;

                        if (_off6060mode >= 0 && (uint)_off6060mode < (uint)Output.Length)
                        {
                            Output[_off6060mode] = 1;
                            _moveState = MoveState.Done;
                            break;
                        }

                        if (TryQueueWriteI8(0x6060, 0x00, 1, _sdo6060) == false)
                        {
                            _moveState = MoveState.Fault;
                            break;
                        }

                        _moveState = MoveState.WaitModePP;
                        break;
                    }

                case MoveState.WaitModePP:
                    {
                        if (_sdo6060.WriteStatus == SDOWriteStatus.Pending ||
                            _sdo6060.WriteStatus == SDOWriteStatus.None)
                        {
                            break;
                        }

                        if (_sdo6060.WriteStatus != SDOWriteStatus.Ok)
                        {
                            _moveState = MoveState.Fault;
                            break;
                        }

                        if (_homeRestoreThenFault)
                        {
                            _homeRestoreThenFault = false;
                            _motion = MotionCommand.None;
                            _moveState = MoveState.Fault;
                            break;
                        }

                        _motion = MotionCommand.None;
                        _moveState = MoveState.Done;
                        break;
                    }


                case MoveState.Done:
                    {
                        cw = ClearCW(cw, ControlWordBit.NewSetPoint);
                        _motion = MotionCommand.None;
                        _homeRestoreThenFault = false;
                        _moveState = MoveState.Idle;
                        break;
                    }

                case MoveState.Fault:
                default:
                    {
                        cw = ClearCW(cw, ControlWordBit.NewSetPoint);
                        _motion = MotionCommand.None;
                        _homeRestoreThenFault = false;
                        _moveState = MoveState.Idle;
                        break;
                    }
            }
        }



        //IMotorCommand 구현부.
        public bool MoveABS(int position)
        {
            if (_profileVelocity == 0 || _profileAcceleration == 0 || _profileDeceleration == 0)
                return false;

            if (_moveState != MoveState.Idle)
                return false;

            if (_reqMove)
                return false;

            //_haltActive = false;   // Stop 상태 해제
            _moveTarget = position;
            _IsAbsMove = true;
            _motion = MotionCommand.MoveAbs;


            //마지막에 위에것이 확정되었다는 의미로...
            _reqMove = true;
            
            return true;
        }

        public bool MoveINC(int position)
        {
            if (_profileVelocity == 0 || _profileAcceleration == 0 || _profileDeceleration == 0)
                return false;

            if (_moveState != MoveState.Idle)
                return false;

            if (_reqMove)
                return false;

            //_haltActive = false;   // Stop 상태 해제
            _moveTarget = position;
            _IsAbsMove = false;

            _motion = MotionCommand.MoveInc;

            //마지막에 위에것이 확정되었다는 의미로...
            _reqMove = true;

            return true;
        }

        public bool QuickStop()
        {
            //나중에 빠른정지 추가.
            return Stop();
        }

        public bool Stop()
        {
            _jogActive = false;
            _jogDirection = 0;
            _reqStop = true;
            return true;
        }

        public bool JogPlus()
        {
            if (_profileVelocity == 0 || _profileAcceleration == 0 || _profileDeceleration == 0)
                return false;

            if (_isServoOn == false || _isError == true)
                return false;

            //stepPulse = speedPulsePerSec * (loopPeriodSec * leadLoopCount) 주의 발행주기가 8루프보단 길어야 되서 10으로 처리. 10/1000 은 10ms위치에 목적point발행.
            _jogStepPulse = (int)(_profileVelocity * 10 / 1000);

            if (_jogStepPulse < 1)
                _jogStepPulse = 1;

            _jogDirection = 1;
            _jogActive = true;
            //_haltActive = false;

            _motion = MotionCommand.Jog;

            return true;
        }

        public bool JogMinus()
        {
            if (_profileVelocity == 0 || _profileAcceleration == 0 || _profileDeceleration == 0)
                return false;

            if (_isServoOn == false || _isError == true)
                return false;

            _jogStepPulse = (int)(_profileVelocity * 10 / 1000);

            if (_jogStepPulse < 1)
                _jogStepPulse = 1;

            _jogDirection = -1;
            _jogActive = true;
            //_haltActive = false;

            _motion = MotionCommand.Jog;

            return true;
        }

        public bool JogStop()
        {
            _jogActive = false;
            _jogDirection = 0;

            _reqAnchorActualPosition = true;
            _reqStop = true;

            return true;
        }

        public bool AlarmClear()
        {
            _reqFaultReset = true;
            return true;
        }

        public bool ServoOn()
        {
            if (_isError == true)
            {
                return false;
            }

            _reqStop = false;
            _haltActive = false;

            _jogActive = false;
            _jogDirection = 0;

            _reqEnable = true;
            return true;

        }

        public bool ServoOff()
        {
            _jogActive = false;
            _jogDirection = 0;
            _reqEnable = false;
            return true;
        }

        public bool Home()
        {
            if (_isServoOn == false || _isError == true)
                return false;

            if (_moveState != MoveState.Idle)
                return false;

            if (_reqMove)
                return false;

            _jogActive = false;
            _jogDirection = 0;
            _haltActive = false;

            _motion = MotionCommand.Home;

            _reqMove = true;

            return true;
        }

        public void SetHomeProfile(sbyte method, uint searchSwitchSpeed, uint searchZeroSpeed, uint acceleration, int homeOffset)
        {
            _homeMethod = method;
            _homeSearchSwitchSpeed = searchSwitchSpeed;
            _homeSearchZeroSpeed = searchZeroSpeed;
            _homeAcceleration = acceleration;
            _homeOffset = homeOffset;
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
