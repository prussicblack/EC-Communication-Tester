using SOEM_FrontEnd.Model;
using System;
using SOEM_FrontEnd.Ethercat;

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

        public StateMachine(EcClient EC)
        {
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

            ret = _ECClient.EnsureState(EcClient.EC_STATE_SAFE_OP, 2000);
            if (ret == false)
            {
                return false;
            }

            m_eCurrentSequence = eStateSequenceName.SafeOp;

            return true;
        }

        public bool MoveToOperate()
        {
            //1회 processData run 후 Operate이동.
            _ECClient.SendProcessData();
            _ECClient.ReceiveProcessData();

            try
            {
                //PP모드 전환.
                //ECClient.SetModePP(slaveno);
                //초기 프로파일 입력.
                //ECClient.SetProfile(slaveno, 10000, 500, 500); // 예: vel/acc/dec
                //초기 알람 클리어.
                //ECClient.SdoWriteI16(slaveno, 0x6040, 00, 0x0080);  //slave alarm reset. SDO로 써도 먹네..

                bool ret = _ECClient.EnsureState(EcClient.EC_STATE_OPERATIONAL, 5000);
                if (ret == false)
                {
                    return false;
                }

                m_eCurrentSequence = eStateSequenceName.Op;

                //Worker시작.
                //var worker = new PDORTWorker(_ECClient);
                //worker.Start();

            }

            catch (InvalidOperationException ex)
            {
                Console.WriteLine(ex.Message);

                // 전체 슬레이브 상태 갱신
                SOEMNative.soem_readstate();
                int count = SOEMNative.soem_slave_count();

                for (int i = 1; i <= count; i++)
                {
                    ushort st = SOEMNative.soem_slave_state(i);
                    ushort al = SOEMNative.soem_slave_al_status(i);
                    Console.WriteLine($"Slave {i}: state=0x{st:X}, AL=0x{al:X4}");
                }

                throw; // 디버깅 끝나면 다시 던지거나, 여기서만 처리
            }

            return true;
        }




    }
}
