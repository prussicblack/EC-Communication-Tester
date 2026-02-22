using SOEM_FrontEnd.DataMap;
using SOEM_FrontEnd.Ethercat;
using SOEM_FrontEnd.Ethercat.EthercatProfile;
using SOEM_FrontEnd.Ethercat.EthercatProfile.Interfaces;
using SOEM_FrontEnd.Model;
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using SOEM_FrontEnd.Util.Logging;

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
        public eStateSequenceName m_eCurrentSequence = eStateSequenceName.None;

        private readonly EcClient _ECClient;

        private PDORTWorker worker;

        private readonly ILogger _log;


        public StateMachine(EcClient EC)
        {
            //로그 초기화
            _log = OPLogger.CreateLogger("SOEM_FrontEnd");

            _ECClient = EC;

            m_eCurrentSequence = eStateSequenceName.Init;

        }



        public bool MoveToInit()
        {
            bool ret = _ECClient.EnsureState(EcClient.EC_STATE_INIT, 2000);

            if (ret == false)
            {
                return false;
            }

            m_eCurrentSequence = eStateSequenceName.Init;

            return true;

        }


        public bool MoveToPreOP()
        {
            bool ret = _ECClient.EnsureState(EcClient.EC_STATE_PRE_OP, 2000);

            if (ret == false)
            {
                return false;
            }

            //새로운 매핑 필요하면 여기서 진행되어야 함.


            m_eCurrentSequence = eStateSequenceName.PreOp;

            return true; 
        }

        public bool MoveToSafeOP()
        {
            bool ret;

            
            ret = _ECClient.RebuildPdoMap();
            if (ret == false)
            {
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
                            NormalMotorWithPPMode ppmode =
                                new NormalMotorWithPPMode(rxSize: outBytes, txSize: inBytes, i, _ECClient);
                            ppmode.SetPdoMapping(rxAllMapList, txAllMapList);
                            store.BaseProfile = ppmode;

                            //Console.WriteLine($"Slave {i} - 402Drive Profile. PPMode Set");
                            _log.LogInformation("Slave {{i}} - 402Drive Profile. PPMode Set");

                        }
                        else
                        {
                            store.BaseProfile = new NormalOProfile(rxSize: outBytes, txSize: inBytes, i, _ECClient);
                            //Console.WriteLine($"Slave {i} - 402Drive Profile Set Fail NormalIO set.");
                            _log.LogInformation("Slave {{i}} - 402Drive Profile Set Fail NormalIO set.");

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
            m_eCurrentSequence = eStateSequenceName.SafeOp;

            return true;
        }

        public bool MoveToOperate()
        {

            try
            {
                //워커 생성
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
                    _log.LogInformation($"EnsureOp Fail");

                    worker?.Stop();

                    worker = null;

                    return false;
                }

                m_eCurrentSequence = eStateSequenceName.Op;
                

                //Worker시작.
                worker.Start();

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

            return true;
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


            int rxmapcount = _ECClient.SdoReadI8(slave, 0x1C12, 0); //0번 서브인덱스, 총 엔트리 갯수.

            if (rxmapcount <= 0)
            {
                rxmapcount = _ECClient.SdoReadU16(slave, 0x1C12, 0); //설마 이걸 쓰진 않겠지..-0-
                if (rxmapcount == 0) 
                    return false;
            }

            List<ushort> rxentrylist = new List<ushort>();

            List<uint> rxallmap = new List<uint>();

            for (int i = 1; i <= rxmapcount; i++)
            {
                ushort listelement = _ECClient.SdoReadU16(slave, 0x1c12, (byte)i);
                rxentrylist.Add(listelement);
            }

            if (rxentrylist.Count <= 0 )
                return false;


            for (int i = 0; i < rxentrylist.Count; i++)
            {
                ushort elementcount = _ECClient.SdoReadU8(slave, rxentrylist[i], 0);

                for (byte j = 1; j <= elementcount; j++)
                {
                    uint map = _ECClient.SdoReadU32(slave, rxentrylist[i], j);
                    rxallmap.Add(map);
                }
            }

            int txmapcount = _ECClient.SdoReadI8(slave, 0x1C13, 0); //0번 서브인덱스, 총 엔트리 갯수.

            if (txmapcount <= 0)
            {
                txmapcount = _ECClient.SdoReadU16(slave, 0x1C13, 0); //설마 이걸 쓰진 않겠지..-0-
                if (txmapcount == 0)
                    return false;
            }

            List<ushort> txentrylist = new List<ushort>();

            List<uint> txallmap = new List<uint>();

            for (int i = 1; i <= txmapcount; i++)
            {
                ushort listelement = _ECClient.SdoReadU16(slave, 0x1c13,(byte)i);
                txentrylist.Add(listelement);
            }


            if (txentrylist.Count <= 0 )
                return false;

            for (int i = 0; i < txentrylist.Count; i++)
            {
                ushort elementcount = _ECClient.SdoReadU8(slave, txentrylist[i], 0);

                for (byte j = 1; j <= elementcount; j++)
                {
                    uint map = _ECClient.SdoReadU32(slave, txentrylist[i], j);
                    txallmap.Add(map);
                }
            }



            //allmap을 분해해서 클래스로 만들어서 넣을것.
            bool has6040 = HasIndexSub(rxallmap, 0x6040, 0x00);

            bool has6041 = HasIndexSub(txallmap, 0x6041, 0x00);

            bool is402 = has6040 && has6041;

            txAllMapList = txallmap;
            rxAllMapList = rxallmap;

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


    }



}
