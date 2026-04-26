using Avalonia.Media;
using Microsoft.Extensions.Logging;
using SOEM_FrontEnd.DataMap;
using SOEM_FrontEnd.Ethercat;
using SOEM_FrontEnd.Ethercat.EthercatProfile;
using SOEM_FrontEnd.Ethercat.EthercatProfile.Interfaces;
using SOEM_FrontEnd.Model;
using SOEM_FrontEnd.Util.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace SOEM_FrontEnd.Automation
{
    //시퀀셜 상태머신 정의.
    //init -> PreOP -> SafeOP -> OP 상태 이동.
    //상태 이동간 Slave정의, 미리 설정된 값 입력, PDO Loop동작까지 진행.

    //상태머신이랑 스레드는 일단 놓지 말것.

    //이후 PDO작업 진행하기 위한 수동 메소드만 우선 작성.

    //기본적으로 전체 상태 변경은 여기에서만.
    //따라서 부분적 상태 변경은 일단은 지원 안하는걸로.

    //그럼 IO든 402든 여기서 new하는게 맞는데...?
    //그럼 여기서 Mapping해서 Byte계산할 수 있게 해줘야됨.

    public enum eStateSequenceName
    {
        None,
        Init,
        PreOp,
        SafeOp,
        Op
    }

    public sealed class StateMachine
    {

        private readonly EcClient _ECClient;

        private PDORTWorker worker;

        private readonly ILogger _log;

        private SDOSubWorker _sdoWorker;

        private readonly object _stateLock = new object();

        private Thread _automationThread;
        private AutoResetEvent _automationSignal;
        private bool _automationRunning;

        private eStateSequenceName _targetSequence;


        public eStateSequenceName m_eCurrentSequence = eStateSequenceName.None;

        public event Action<eStateSequenceName> CurrentSequenceChanged;


        public eStateSequenceName CurrentSequence
        {
            get
            {
                return m_eCurrentSequence;
            }
        }



        public StateMachine(EcClient EC)
        {
            //로그 초기화
            _log = OPLogger.CreateLogger("SOEM_FrontEnd");

            _ECClient = EC;

            m_eCurrentSequence = eStateSequenceName.Init;

            _targetSequence = eStateSequenceName.Init;

            StartAutomationThread();

        }

        private void StartAutomationThread()
        {
            if (_automationThread != null && _automationThread.IsAlive)
                return;

            _automationSignal = new AutoResetEvent(false);
            _automationRunning = true;

            _automationThread = new Thread(AutomationThreadMain);
            _automationThread.IsBackground = true;
            _automationThread.Name = "Automation-StateMachine";
            _automationThread.Start();
        }

        public void Shutdown()
        {
            try
            {
                // 종료 시에는 먼저 INIT으로 내려간다.
                // 기존 automation thread를 그대로 사용해서 상태 전이를 처리한다.
                RequestState(eStateSequenceName.Init);

                bool initOk = WaitForState(eStateSequenceName.Init, 5000);

                if (!initOk)
                {
                    _log.LogWarning("Shutdown: timeout while waiting for INIT state. Current={Current}", GetCurrentState());
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Shutdown: failed while moving to INIT");
            }
            finally
            {
                // 혹시 OP 상태에서 내려오지 못했더라도 PDO worker는 반드시 정리
                StopPdoWorker();

                _automationRunning = false;

                if (_automationSignal != null)
                {
                    _automationSignal.Set();
                }

                if (_automationThread != null && _automationThread.IsAlive)
                {
                    _automationThread.Join(2000);
                }

                if (_automationSignal != null)
                {
                    _automationSignal.Dispose();
                    _automationSignal = null;
                }

                _automationThread = null;
            }
        }

        private bool WaitForState(eStateSequenceName targetState, int timeoutMs)
        {
            Stopwatch sw = Stopwatch.StartNew();

            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (GetCurrentState() == targetState)
                {
                    return true;
                }

                Thread.Sleep(20);
            }

            return GetCurrentState() == targetState;
        }

        private bool RequestState(eStateSequenceName targetState)
        {
            lock (_stateLock)
            {
                _targetSequence = targetState;
            }

            if (_automationSignal != null)
                _automationSignal.Set();

            return true;
        }

        private void AutomationThreadMain()
        {
            while (_automationRunning)
            {
                _automationSignal.WaitOne();

                if (_automationRunning == false)
                    break;

                while (_automationRunning)
                {
                    eStateSequenceName currentState = GetCurrentState();
                    eStateSequenceName targetState = GetTargetState();

                    if (currentState == targetState)
                        break;

                    bool ok = ExecuteNextStep(currentState, targetState);
                    if (ok == false)
                    {
                        _log.LogWarning("Automation transition failed. Current={Current}, Target={Target}", currentState, targetState);
                        break;
                    }
                }
            }
        }

        private void DumpSlaveStates(string tag)
        {
            SOEMNative.soem_readstate();
            int count = SOEMNative.soem_slave_count();

            for (int i = 1; i <= count; i++)
            {
                ushort st = SOEMNative.soem_slave_state(i);
                ushort al = SOEMNative.soem_slave_al_status(i);
                _log.LogError($"{tag} - Slave {i}: state=0x{st:X}, AL=0x{al:X4}");
            }
        }


        private void StopPdoWorker()
        {
            if (worker == null)
                return;

            try
            {
                worker.Stop();
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "PDO worker stop failed");
            }
            finally
            {
                worker = null;
            }
        }
        

        private eStateSequenceName GetCurrentState()
        {
            lock (_stateLock)
            {
                return m_eCurrentSequence;
            }
        }

        private eStateSequenceName GetTargetState()
        {
            lock (_stateLock)
            {
                return _targetSequence;
            }
        }

        private void SetCurrentState(eStateSequenceName state)
        {
            bool changed = false;

            lock (_stateLock)
            {
                if (m_eCurrentSequence != state)
                {
                    m_eCurrentSequence = state;
                    changed = true;
                }
            }

            if (changed)
            {
                Action<eStateSequenceName> handler = CurrentSequenceChanged;
                if (handler != null)
                    handler(state);
            }
        }

        private bool ExecuteNextStep(eStateSequenceName currentState, eStateSequenceName targetState)
        {
            _log.LogInformation("ExecuteNextStep: Current={Current}, Target={Target}", currentState, targetState);


            switch (currentState)
            {
                case eStateSequenceName.Init:
                    switch (targetState)
                    {
                        case eStateSequenceName.Init:
                            return true;

                        case eStateSequenceName.PreOp:
                        case eStateSequenceName.SafeOp:
                        case eStateSequenceName.Op:
                            return MoveToPreOPCore();
                    }
                    break;

                case eStateSequenceName.PreOp:
                    switch (targetState)
                    {
                        case eStateSequenceName.Init:
                            return MoveToInitCore();

                        case eStateSequenceName.PreOp:
                            return true;

                        case eStateSequenceName.SafeOp:
                        case eStateSequenceName.Op:
                            return MoveToSafeOPCore();
                    }
                    break;

                case eStateSequenceName.SafeOp:
                    switch (targetState)
                    {
                        case eStateSequenceName.Init:
                        case eStateSequenceName.PreOp:
                            return MoveToPreOPCore();

                        case eStateSequenceName.SafeOp:
                            return true;

                        case eStateSequenceName.Op:
                            return MoveToOperateCore();
                    }
                    break;

                case eStateSequenceName.Op:
                    switch (targetState)
                    {
                        case eStateSequenceName.Init:
                        case eStateSequenceName.PreOp:
                        case eStateSequenceName.SafeOp:
                            StopPdoWorker();
                            return MoveDownToSafeOPCore();

                        case eStateSequenceName.Op:
                            return true;
                    }
                    break;
            }

            _log.LogWarning("ExecuteNextStep: No valid transition. Current={Current}, Target={Target}", currentState, targetState);

            return false;
        }



        public void AttachSdoWorker(SDOSubWorker sdoWorker)
        {
            _sdoWorker = sdoWorker;

            int count = Datamap.Instance.SlaveCount;
            for (int i = 1; i < count; i++)
            {
                SlaveStore store = Datamap.Instance.GetSlave(i);
                if (store == null)
                    continue;

                NormalMotorWithPPMode motor = store.BaseProfile as NormalMotorWithPPMode;
                if (motor != null)
                {
                    motor.AttachSdoWorker(sdoWorker);
                }
            }
        }

        public bool MoveToInit()
        {
            return RequestState(eStateSequenceName.Init);
        }

        public bool MoveToPreOP()
        {
            return RequestState(eStateSequenceName.PreOp);
        }

        public bool MoveToSafeOP()
        {
            return RequestState(eStateSequenceName.SafeOp);
        }

        public bool MoveToOperate()
        {
            return RequestState(eStateSequenceName.Op);
        }


        private bool MoveToInitCore()
        {
            bool ret = _ECClient.EnsureState(EcClient.EC_STATE_INIT, 2000);

            if (ret == false)
            {
                _log.LogError("Ensure Init failed");
                DumpSlaveStates("MoveToInitCore");
                
                return false;
            }

            SetCurrentState(eStateSequenceName.Init);

            _log.LogInformation("Device init Mode OK");

            return true;

        }


        private bool MoveToPreOPCore()
        {
            bool ret = _ECClient.EnsureState(EcClient.EC_STATE_PRE_OP, 2000);

            if (ret == false)
            {
                _log.LogError("Ensure PRE-OP failed");
                DumpSlaveStates("MoveToPreOPCore");
                
                return false;
            }

            //새로운 매핑 필요하면 여기서 진행되어야 함.


            SetCurrentState(eStateSequenceName.PreOp);

            _log.LogInformation("Device PreOP OK");


            return true; 
        }

        private bool MoveToSafeOPCore()
        {
            bool ret;

            
            ret = _ECClient.RebuildPdoMap();
            if (ret == false)
            {
                _log.LogError("Ensure SAFE-OP failed");
                DumpSlaveStates("MoveToSafeOPCore");

                return false;
            }

            //Datamap에서 Slave 생성지점.
            var map = Datamap.Instance;

            for (ushort i = 1; i <= _ECClient.SlaveCount; i++)
            {
                var store = map.GetSlave(i);

                // soem_get_slave_inout_size로 크기 확보 (config_map 이후라 가능)
                SOEMNative.soem_get_slave_inout_size(i, out int inBytes, out int outBytes, out int inBits, out int outBits);

                //프로파일 설정.
                switch (store.DeviceMode)
                {

                    case DeviceMode.NormalIO:
                    {
                        //
                        store.BaseProfile = new NormalOProfile(rxSize: outBytes, txSize: inBytes, i, _ECClient);
                        //Console.WriteLine($"Slave {i} - NormalIO");
                        _log.LogInformation("Slave {{i}} - NormalIO");
                        break;
                    }
                    case DeviceMode.NormalPPMode: //어쨋건 체크는 해야지...나중에 전용 방어코드가 들어가긴 하겠는데.
                    case DeviceMode.None:
                    default:
                    {
                        //없는경우 자동판단.
                        //402인 경우 PPmode를 기본모드로,
                        //없으면 Profile모드 다른거로 하면 되는데, 일단 여기서는 제외.

                        //402인 경우. PDO에 6040/6041존재시.
                        //Mapping은 존재한다면, PreOP에서 끝내야됨.

                        //Mapping구조는, 
                        //RX 0x1C12 -> 0x1600, 0x1601... -> 0x6040(Control Word)
                        //Tx 0x1C13 -> 0x1A00, 0x1A01... -> 0x6041(State Word)
                        bool is402 = DriveProfile402Check(i, out List<uint> txAllMapList, out List<uint> rxAllMapList);

                        if (is402 == true)
                        {
                            NormalMotorWithPPMode ppmode = new NormalMotorWithPPMode(rxSize: outBytes, txSize: inBytes, i, _ECClient);
                            
                            if (_sdoWorker != null)
                            {
                                ppmode.AttachSdoWorker(_sdoWorker);
                            }

                                
                            ppmode.SetPdoMapping(rxAllMapList, txAllMapList);
                            store.BaseProfile = ppmode;

                            //Console.WriteLine($"Slave {i} - 402Drive Profile. PPMode Set");
                            _log.LogInformation($"Slave {i} - 402Drive Profile. PPMode Set");
                        }
                        else
                        {
                             NormalOProfile normalioprofile = new NormalOProfile(rxSize: outBytes, txSize: inBytes, i, _ECClient);
                             //Console.WriteLine($"Slave {i} - 402Drive Profile Set Fail NormalIO set.");

                             normalioprofile.SetPdoMapping(txAllMapList, txAllMapList);

                             store.BaseProfile = normalioprofile;

                            _log.LogInformation($"Slave {i} - 402Drive Profile Set Fail NormalIO set.");

                        }

                        break;
                    }
                }

                //프로파일 결정되면 SafeOP로 올리기 위해 실행해야될거 처리.

                if (store.BaseProfile is IEthercatStateTransition)
                {
                    bool result = (store.BaseProfile as IEthercatStateTransition).PrepareSafeOp(5000);


                    if (result == false)
                    {
                        //로그기록.
                        //어차피 걍 SafeOP까지 올릴거임.
                        //Console.WriteLine($"{i} - PrepareSafeOp Fail");

                        _log.LogInformation($"{i} - PrepareSafeOp Fail");
                    }
                    else
                    {

                        _log.LogInformation($"{i} - PrepareSafeOp Success");

                    }
                }

            }

            ret = _ECClient.EnsureState(EcClient.EC_STATE_SAFE_OP, 2000); //safeop이행.

            if (ret == false)
            {
                //Console.WriteLine("EnsureSafeOp Fail");
                _log.LogInformation($"EnsureSafeOp Fail");

                return false;
            }

            _log.LogInformation($"EnsureSafeOp Success");
            SetCurrentState(eStateSequenceName.SafeOp);

            return true;
        }

        private bool MoveToOperateCore()
        {

            try
            {
                //워커 생성
                StopPdoWorker();
                
                worker = new PDORTWorker(_ECClient);

                var map = Datamap.Instance;

                for (int i = 1; i <= _ECClient.SlaveCount; i++)
                {
                    var store = map.GetSlave(i);

                    if (store.BaseProfile is IEthercatStateTransition)
                    {
                        bool result = (store.BaseProfile as IEthercatStateTransition).PrepareOp(5000);

                        if (result == false)
                        {
                            //로그기록.
                            //어차피 걍 OP까지 올릴거임.
                            //Console.WriteLine("PrepareOp Macro Fail");
                            _log.LogInformation($"PrepareOp Fail");
                        }
                        else
                        {
                            _log.LogInformation($"PrepareOp Success");
                        }
                    }

                }

                //최종 완성된 EC object를 이용하여 데이터 바인딩 처리.
                List<(ushort Slave, PDOBase Pdo)> binds = new List<(ushort Slave, PDOBase Pdo)>();

                for(int i = 1;i <= _ECClient.SlaveCount;i++)
                {
                    var pdo = map.GetSlave(i).BaseProfile as PDOBase;
                    if (pdo == null) continue; // IO/402 아닌 경우 등

                    binds.Add(((ushort)i, pdo));
                }

                //워커에 PDO주소 바인딩.
                worker.SetBinds(binds,(ushort)_ECClient.SlaveCount);

                //1회 processData run 후 Operate이동.
                _ECClient.SendProcessData();
                _ECClient.ReceiveProcessData();

                bool ret = _ECClient.EnsureState(EcClient.EC_STATE_OPERATIONAL, 5000);
                if (ret == false)
                {
                    //Console.WriteLine("EnsureOp Fail");
                    _log.LogError("Ensure OP failed");
                    DumpSlaveStates("MoveToOperateCore");


                    worker?.Stop();

                    worker = null;

                    return false;
                }

                //Worker시작.
                worker.Start();
                
                SetCurrentState(eStateSequenceName.Op);
                
                return true;

            }

            catch (InvalidOperationException ex)
            {
                //Console.WriteLine(ex.Message);
                _log.LogError(ex.Message);

                // 전체 슬레이브 상태 갱신
                SOEMNative.soem_readstate();
                int count = SOEMNative.soem_slave_count();

                for (int i = 1; i <= count; i++)
                {
                    ushort st = SOEMNative.soem_slave_state(i);
                    ushort al = SOEMNative.soem_slave_al_status(i);
                    //Console.WriteLine($"Slave {i}: state=0x{st:X}, AL=0x{al:X4}");
                    _log.LogError($"Slave {i}: state=0x{st:X}, AL=0x{al:X4}");

                }

                throw; // 디버깅 끝나면 다시 던지거나, 여기서만 처리
            }

        }

        private bool MoveDownToSafeOPCore()
        {
            StopPdoWorker();

            bool ret = _ECClient.EnsureState(EcClient.EC_STATE_SAFE_OP, 2000);
            if (ret == false)
            {
                _log.LogError("Ensure SAFE-OP failed");
                DumpSlaveStates("MoveDownToSafeOPCore");
                return false;
            }

            SetCurrentState(eStateSequenceName.SafeOp);
            _log.LogInformation("Device SafeOP OK (downward)");
            return true;
        }



        private SDOPoint ReadPointByWorker(ushort slave, ushort index, byte subIndex, int maxLen = 64)
        {
            if (_sdoWorker == null)
                return null;

            bool ok = _sdoWorker.EnqueueReadAsync(slave, index, subIndex, maxLen)
                .GetAwaiter()
                .GetResult();

            if (ok == false)
                return null;

            SlaveStore slaveStore = Datamap.Instance.GetSlave(slave);
            if (slaveStore == null)
                return null;

            SDOPoint point = slaveStore.TryGetSdo(index, subIndex);
            if (point == null)
                return null;

            if (point.ReadStatus != SDOReadStatus.Ok)
                return null;

            if (point.LastRaw == null)
                return null;

            return point;
        }



        private bool DriveProfile402Check(ushort slave, out List<uint> txAllMapList, out List<uint> rxAllMapList)
        {
            //없는경우 자동판단.
            //402인 경우 PPmode를 기본모드로,
            //없으면 Profile모드 다른거로 하면 되는데, 일단 여기서는 제외.

            //402인 경우. PDO에 6040/6041존재시 402로 간주.
            //Mapping은 존재한다면, PreOP에서 끝내야됨.

            //Mapping구조는, 
            //RX 0x1C12 -> 0x1600, 0x1601... -> 0x6040(Control Word)
            //Tx 0x1C13 -> 0x1A00, 0x1A01... -> 0x6041(State Word)

            //큐 없이 직접 읽어올것.

            //읽어온 김에 엔트리도 확정해서 내려줄 수 있겠는데?
            txAllMapList = new List<uint>();
            rxAllMapList = new List<uint>();

            //수정 포인트!! SDO 여기서 직접 읽어오지 말것. SDO Worker거쳐서 읽어오는게 맞을거 같아.
            //int rxmapcount = _ECClient.SdoReadI8(slave, 0x1C12, 0); //0번 서브인덱스, 총 엔트리 갯수.

            SDOPoint point = ReadPointByWorker(slave, 0x1C12, 0x00, 1);
            if (point == null)
                return false;
            int rxmapcount = point.LastRaw[0];
            if (rxmapcount <= 0)
                return false;


            List<ushort> rxentrylist = new List<ushort>();

            List<uint> rxallmap = new List<uint>();

            for (int i = 1; i <= rxmapcount; i++)
            {
                SDOPoint entryPoint = ReadPointByWorker(slave, 0x1C12, (byte)i, 2);
                if (entryPoint == null || entryPoint.LastRaw.Length < 2)
                    return false;

                ushort listElement = (ushort)(entryPoint.LastRaw[0] | (entryPoint.LastRaw[1] << 8));
                rxentrylist.Add(listElement);
            }


            for (int i = 0; i < rxentrylist.Count; i++)
            {
                SDOPoint mapCountPoint = ReadPointByWorker(slave, rxentrylist[i], 0x00, 1);
                if (mapCountPoint == null)
                    return false;

                int elementCount = mapCountPoint.LastRaw[0];

                for (byte j = 1; j <= elementCount; j++)
                {
                    SDOPoint mapPoint = ReadPointByWorker(slave, rxentrylist[i], j, 4);
                    if (mapPoint == null || mapPoint.LastRaw.Length < 4)
                        return false;

                    uint map = (uint)(
                        mapPoint.LastRaw[0] |
                        (mapPoint.LastRaw[1] << 8) |
                        (mapPoint.LastRaw[2] << 16) |
                        (mapPoint.LastRaw[3] << 24));

                    rxAllMapList.Add(map);
                }
            }
            //0번 서브인덱스, 총 엔트리 갯수.
            SDOPoint txCountPoint = ReadPointByWorker(slave, 0x1C13, 0x00, 1);
            if (txCountPoint == null)
                return false;

            int txmapcount = txCountPoint.LastRaw[0];
            if (txmapcount <= 0)
                return false;

            List<ushort> txentrylist = new List<ushort>();
            

            for (int i = 1; i <= txmapcount; i++)
            {
                SDOPoint entryPoint = ReadPointByWorker(slave, 0x1C13, (byte)i, 2);
                if (entryPoint == null || entryPoint.LastRaw.Length < 2)
                    return false;

                ushort listElement = (ushort)(entryPoint.LastRaw[0] | (entryPoint.LastRaw[1] << 8));
                txentrylist.Add(listElement);
            }

            for (int i = 0; i < txentrylist.Count; i++)
            {
                SDOPoint mapCountPoint = ReadPointByWorker(slave, txentrylist[i], 0x00, 1);
                if (mapCountPoint == null)
                    return false;

                int elementCount = mapCountPoint.LastRaw[0];

                for (byte j = 1; j <= elementCount; j++)
                {
                    SDOPoint mapPoint = ReadPointByWorker(slave, txentrylist[i], j, 4);
                    if (mapPoint == null || mapPoint.LastRaw.Length < 4)
                        return false;

                    uint map = (uint)(
                        mapPoint.LastRaw[0] |
                        (mapPoint.LastRaw[1] << 8) |
                        (mapPoint.LastRaw[2] << 16) |
                        (mapPoint.LastRaw[3] << 24));

                    txAllMapList.Add(map);
                }
            }



            //allmap을 분해해서 클래스로 만들어서 넣을것.
            bool has6040 = HasIndexSub(rxAllMapList, 0x6040, 0x00);

            bool has6041 = HasIndexSub(txAllMapList, 0x6041, 0x00);

            bool is402 = has6040 && has6041;

            return is402;
        }


        static bool HasIndexSub(List<uint> allmap, ushort index, byte sub)
        {
            for (int i = 0; i < allmap.Count; i++)
            {
                uint map = allmap[i];
                ushort idx = (ushort)(map >> 16);
                byte si = (byte)(map >> 8);
                if (idx == index && si == sub)
                    return true;
            }
            return false;
        }


        //통계 수집부.
        public void PollPdoStats()
        {
            if (worker == null)
            {
                return;
            }

            PdoRtStats stats = worker.GetStatsSnapshot();

            string line =
                $"[PDO] loop={stats.LoopCount} " +
                $"dt(us) last={stats.LastDtUs:F1} min={stats.MinDtUs:F1} max={stats.MaxDtUs:F1} avg={stats.AvgDtUs:F1} " +
                $" / jitter(us) last={stats.LastJitterUs:F1} min={stats.MinJitterUs:F1} max={stats.MaxJitterUs:F1} avgAbs={stats.AvgAbsJitterUs:F1} " +
                $" / late={stats.LateCycleCount} " +
                $" / send(last/min/max/err)={stats.LastSendRc}/{stats.MinSendRc}/{stats.MaxSendRc}/{stats.SendErrorCount} " +
                $" / recv(last/min/max/err)={stats.LastReceiveRc}/{stats.MinReceiveRc}/{stats.MaxReceiveRc}/{stats.ReceiveErrorCount}";

            _log.LogInformation(line);
            
        }

        public void ResetStats()
        {
            if (worker == null)
            {
                return;
            }
            
            worker.ResetStats();
            
        }
        public bool TryGetPdoStats(out PdoRtStats stats)
        {
            stats = default(PdoRtStats);

            if (worker == null)
            {
                return false;
            }

            stats = worker.GetStatsSnapshot();
            return true;
        }

        public bool IsPdoRunning
        {
            get
            {
                if (worker == null)
                {
                    return false;
                }

                return worker.IsRunning;
            }
        }

        public double PdoTargetPeriodUs
        {
            get
            {
                if (worker == null)
                {
                    return 0.0;
                }

                return worker.Period.TotalMilliseconds * 1000.0;
            }
        }

    }



}
