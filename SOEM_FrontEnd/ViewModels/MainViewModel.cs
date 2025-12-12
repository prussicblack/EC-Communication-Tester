using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using CommunityToolkit.Mvvm.Input;
using Microsoft.VisualBasic;
using SOEM_FrontEnd.Ethercat;
using SOEM_FrontEnd.Ethercat.ESI;
using SOEM_FrontEnd.Model;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Input;
using static System.Net.Mime.MediaTypeNames;

namespace SOEM_FrontEnd.ViewModels;

public class SlaveItem : INotifyPropertyChanged
{
    public int Index { get; init; }
    public string Name { get; init; } = "";
    public ushort Alias { get; init; }
    public ushort StationAddress { get; init; }
    public uint Vendor { get; init; }
    public uint Product { get; init; }
    public uint Revision { get; init; }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class MainViewModel : ViewModelBase
{
    //public string Greeting => "Welcome to Avalonia!";

    private EcClient ECClient;

    public ICommand CMD_Test { get; private set; }
    public ICommand CMD_SelectNIC { get; private set; }

    private ObservableCollection<string> _NICList = new ObservableCollection<string>();

    public ObservableCollection<string> NICList
    {
        get => _NICList;
        set
        {
            _NICList = value;
            OnPropertyChanged(nameof(NICList));
        }
    }

    private string _NICSelect = string.Empty;

    public string NICSelect
    {
        get => _NICSelect;
        set
        {
            _NICSelect = value;
            OnPropertyChanged(nameof(NICSelect));
        }
    }

    public ObservableCollection<string> Slaves { get;} = new();

    private int _SelectedSlave = 0;
    public int SelectedSlave
    {
        get => _SelectedSlave;
        set
        {
            if (_SelectedSlave != value)
            {
                _SelectedSlave = value;
                OnPropertyChanged();
            }
        }
    }

    public List<ESIXMLData.ESIDevice> DevicesData = new List<ESIXMLData.ESIDevice>();


    public MainViewModel()
    {

        ECClient = new EcClient();

        ////NativeBootstrap.EnsureLoaded(); //DLL로딩용.

        ////public static void LoadSoemWrap()
        ////{
        //string baseDir = @"C:\Users\ursae\Desktop\Git\SOEM\build\x64\Debug\";
        //// exe 옆에 둘 경우
        //string p = Path.Combine(baseDir, "soem_wrap.dll");
        //if (!File.Exists(p))
        //    throw new FileNotFoundException(p);

        //if (!NativeLibrary.TryLoad(p, out var h))
        //    throw new DllNotFoundException($"NativeLibrary.TryLoad failed: {p}");
        ////}

        //NativeLibrary.TryLoad(Path.Combine(AppContext.BaseDirectory, "soem_wrap.dll"), out _);
        //_ = SOEMNative.soem_slave_count();

        CMD_Test = new RelayCommand(HandleTest);

        CMD_SelectNIC = new RelayCommand(HandleNIC);

        string firstset = string.Empty;
        string lastset = string.Empty;
        foreach (var (ifname, desc) in PcapIfEnumerator.GetAll())
        {
            //Console.WriteLine($"{ifname}  |  {desc}");
            string buf = $"{desc} - {ifname}";
            NICList.Add(buf);
            if (firstset == string.Empty)
            {
                firstset = buf;
            }

            lastset = buf;
        }

        NICSelect = lastset;



        //나중에 프로그램 로딩시 Splash Screen 과 함께 로딩.
        //의외로 시간이 좀 걸릴 수 있음.
        string path = AppDomain.CurrentDomain.BaseDirectory + "ESI";

        var devices = ESICatalog.LoadAllDevices(path);
        DevicesData = devices;

    }

    private void DrawSlaves()
    {

    }

    private void HandleNIC()
    {
        string ifname = NICSelect.Substring(NICSelect.LastIndexOf(" - ") + (" - ".Length));

        ECClient.Open(ifname);

        int slave = ECClient.SlaveCount;

        List<SoemSlaveInfo> slaves = new List<SoemSlaveInfo>();
        
        Slaves.Clear();

        for (int i = 1; i <= slave; i++)
        {
            if (ECClient.SlaveInfo(i, out SoemSlaveInfo info) != 0)
            {
                slaves.Add(info);
                Slaves.Add($" {i} - Name = {info.name}, Alias = {info.alias}, StationAddress = 0x{info.configadr.ToString("X")}, VendorCode = 0x{info.vendor.ToString("X")}, ProductCode = 0x{info.product.ToString("X")}, Revision=0x{info.revision.ToString("X")}");
                Console.WriteLine($" {i} - Name = {info.name}, Alias = {info.alias}, StationAddress = 0x{info.configadr.ToString("X")}, VendorCode = 0x{info.vendor.ToString("X")}, ProductCode = 0x{info.product.ToString("X")}, Revision=0x{info.revision.ToString("X")}");
            }
            else
            {
                Console.WriteLine($"#{i} -> failed to query");
            }
        }




        //SOEMNative.soem_open(ifname);
        //SOEMNative.soem_config_init(1);

        //int slave = SOEMNative.soem_slave_count();

        ////EC 슬레이브 조회.
        //List<SoemSlaveInfo> slaves = new List<SoemSlaveInfo>();

        //Slaves.Clear();

        //for (int i = 1; i <= slave; i++)
        //{
        //    if (SOEMNative.soem_get_slave_info(i, out SoemSlaveInfo info) != 0)
        //    {
        //        slaves.Add(info);
        //        Slaves.Add($" {i} - Name = {info.name}, Alias = {info.alias}, StationAddress = 0x{info.configadr.ToString("X")}, VendorCode = 0x{info.vendor.ToString("X")}, ProductCode = 0x{info.product.ToString("X")}, Revision=0x{info.revision.ToString("X")}");
        //        Console.WriteLine($" {i} - Name = {info.name}, Alias = {info.alias}, StationAddress = 0x{info.configadr.ToString("X")}, VendorCode = 0x{info.vendor.ToString("X")}, ProductCode = 0x{info.product.ToString("X")}, Revision=0x{info.revision.ToString("X")}");
        //    }
        //    else
        //    {
        //        Console.WriteLine($"#{i} -> failed to query");
        //    }
        //}


    }


    private void HandleTest()
    {
        //uint productcode = 0x1002;
        //uint vendorcode = 0xFA00000;
        //uint revision = 0x10001;
        uint productcode = 0x8100;
        uint vendorcode = 0x4321;
        uint revision = 0x1;


        ESIXMLData.ESIDevice dev = DevicesData.FirstOrDefault(d => d.ProductCode == productcode && d.VendorId == vendorcode && d.Revision == revision);

        if (dev == null)
            return;

        //ESIXMLData.ESIPDO ppmodeRX = dev.RxPdos.FirstOrDefault(d => d.Name.Contains("PP"));

        

        ECClient.EnsureState(EcClient.EC_STATE_PRE_OP, 2000);


        //var test = ECClient.SdoReadU16(1, 0x2000, 0);


        //RemapRxPdo(1, ppmodeRX);
        //RemapRxPdo(1);

        //ESIXMLData.ESIPDO ppmodeTX = dev.TxPdos.FirstOrDefault(d => d.Name.Contains("PP"));

        //RemapTxPdo(1, ppmodeTX);

        //RemapTxPdo(1);

        //슬레이브 설정하고 한번만.
        ECClient.RebuildPdoMap();

        ECClient.EnsureState(EcClient.EC_STATE_SAFE_OP, 2000);

        // 보통 여기서 한 번 PDO 왕복해 줌
        ECClient.SendProcessData();
        ECClient.ReceiveProcessData();

        try
        {
            //Operational 상태로 전이
            ECClient.EnsureState(EcClient.EC_STATE_OPERATIONAL, 5000);

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




        ECClient.SetModePP(1);                  // 6060 = 1
        ECClient.SetProfile(1, 100000, 500, 500); // 예: vel/acc/dec

        var worker = new PDORTWorker(ECClient, 1);
        worker.Start();

        //// 우리가 리맵한 PDO 기준 오프셋 (테스트용 하드코딩)
        //const int RX_OFF_CW = 0; // 0x6040:00
        //const int RX_OFF_TPOS = 2; // 0x607A:00
        //const int RX_OFF_MODE = 14; // 예: 계산된 offset

        //const int TX_OFF_SW = 0; // 0x6041:00
        //const int TX_OFF_POS = 2; // 0x6064:00

        ////PDOTest
        //ushort slave = 1;           // SOEM 슬레이브 번호 (1부터 시작)

        //// Enable Operation까지 된 Controlword 기본값 (예: 0x000F ~ 0x001F 쪽 장비별로 맞춰야 함)
        //ushort cwBase = 0x000F;

        //int loop = 0;
        //int currentTarget = 0;
        //bool newSetPointBitHigh = false;   // 이번 사이클에 bit4=1로 보낼지
        //bool goingPositive = true;         // true면 0 -> 100000, false면 100000 -> 0

        ////ECClient.SetModePP(slave);                  // 6060 = 1 (PP 모드)
        ////ECClient.SetProfile(slave, 10000, 5000, 5000); // 예시 vel/acc/dec


        //// ===== 지터 측정용 stopwatch =====
        //Stopwatch jitterSw = Stopwatch.StartNew();
        //long lastTicks = jitterSw.ElapsedTicks;
        //double ticksPerMs = (double)Stopwatch.Frequency / 1000.0;

        //// 통계용
        //double maxAbsJitterUs = 0.0;
        //double lastPeriodMs = 0.0;
        //double lastJitterUs = 0.0;



        //HiResLoop.Run(TimeSpan.FromMilliseconds(1), () =>
        //{
        //    // ---------- 지터 측정 ----------
        //    long nowTicks = jitterSw.ElapsedTicks;
        //    double dtMs = (nowTicks - lastTicks) / ticksPerMs; // 실제 주기(ms)
        //    lastTicks = nowTicks;

        //    lastPeriodMs = dtMs;
        //    lastJitterUs = (dtMs - 1.0) * 1000.0;             // 목표 1ms 기준
        //    double absJitter = Math.Abs(lastJitterUs);
        //    if (absJitter > maxAbsJitterUs)
        //        maxAbsJitterUs = absJitter;

        //    // ---------- TxPDO 읽기 (이전 주기 결과) ----------
        //    ushort sw = ECClient.PdoReadU16(slave, TX_OFF_SW);
        //    int actPos = ECClient.PdoReadI32(slave, TX_OFF_POS);

        //    bool targetReached = (sw & (1 << 10)) != 0;  // Statusword bit10: Target reached

        //    // ---------- 새 타겟 & New set-point 펄스 결정 ----------
        //    // Target reached 상태이고, 대략 2000루프(2초) 마다 타겟 변경
        //    if (!newSetPointBitHigh) // 직전 사이클에서 bit4=1을 이미 보냈다면 이번엔 내릴 차례
        //    {
        //        if (targetReached && (loop % 2000 == 0))
        //        {
        //            goingPositive = !goingPositive;
        //            currentTarget = goingPositive ? 100000 : 0;

        //            // 이번 사이클에 New set-point(bit4)를 1로 보냄
        //            newSetPointBitHigh = true;
        //        }
        //    }

        //    // ---------- Controlword 구성 (PP 모드) ----------
        //    //  - bit4: New set-point
        //    //  - bit5: Change set immediately
        //    //  - bit6: 0 = Absolute
        //    ushort cw = cwBase;

        //    // 즉시 변경
        //    cw |= (1 << 5);  // bit5 = 1 (Change immediately)

        //    if (newSetPointBitHigh)
        //    {
        //        cw |= (1 << 4);  // bit4 = 1 (New set-point)
        //    }

        //    // ---------- RxPDO 쓰기 ----------
        //    ECClient.PdoWriteU16(slave, RX_OFF_CW, cw);
        //    ECClient.PdoWriteI32(slave, RX_OFF_TPOS, currentTarget);

        //    // ---------- PDO 전송/수신 ----------
        //    ECClient.SendProcessData();
        //    ECClient.ReceiveProcessData();

        //    // ---------- 로그 출력 ----------
        //    if (loop % 200 == 0)
        //    {
        //        Console.WriteLine(
        //            $"loop={loop}, period={lastPeriodMs:F3} ms, " +
        //            $"jitter={lastJitterUs:F1} us, max|jitter|={maxAbsJitterUs:F1} us, " +
        //            $"CW=0x{cw:X4}, SW=0x{sw:X4}, " +
        //            $"TR={(targetReached ? 1 : 0)}, TPOS={currentTarget}, ACT={actPos}");
        //    }

        //    // New set-point는 한 사이클만 1 → 다음 사이클에서 자동으로 0으로 내림
        //    if (newSetPointBitHigh)
        //    {
        //        newSetPointBitHigh = false;
        //    }

        //    loop++;

        //    // 테스트용: 약 10초(1ms * 10000) 돌리고 종료
        //    if (loop >= 10000)
        //        return false;

        //    return true;
        //});

    }

    public void RemapRxPdo(ushort slave)
    {
        // 1) SM2(RxPDO) 비활성화: 0x1C12:00 = 0
        ECClient.SdoWriteU8(slave, 0x1C12, 0x00, 0);
        Console.WriteLine("SM2(RxPDO) 비활성화");


        // 2) Index의 매핑 제거.
        ECClient.SdoWriteU8(slave, 0x1601, 0x00, 0);
        Console.WriteLine("Index 매핑 제거");

        // 3) RxMap의 Entry 를 써넣기.
        //byte sub = 1;
        //foreach (var entrie in RxMap.Entries)
        //{
        //    uint map = ECClient.MakeMapWord(entrie.Index, entrie.SubIndex, entrie.BitLength);
        //    ECClient.SdoWriteU32(slave, RxMap.Index, sub, map);

        //    Console.WriteLine($"RxMap Remapping {slave}-{RxMap.Index}-{sub}");

        //    sub++;
        //}

        // 6040:0 (16bit)
        uint m1 = ECClient.MakeMapWord(0x6040, 0x00, 16);
        ECClient.SdoWriteU32(slave, 0x1601, 0x01, m1);

        // 607A:0 (32bit)
        uint m2 = ECClient.MakeMapWord(0x607A, 0x00, 32);
        ECClient.SdoWriteU32(slave, 0x1601, 0x02, m2);

        // 6081:0 (32bit)
        uint m3 = ECClient.MakeMapWord(0x6081, 0x00, 32);
        ECClient.SdoWriteU32(slave, 0x1601, 0x03, m3);

        // 6060:0 (8bit)
        uint m4 = ECClient.MakeMapWord(0x6060, 0x00, 8);
        ECClient.SdoWriteU32(slave, 0x1601, 0x04, m4);

        // 엔트리 개수 = 4 (16+32+32+8 = 88bit)
        ECClient.SdoWriteU8(slave, 0x1601, 0x00, 4);

        // 이제 Assign
        ECClient.SdoWriteU8(slave, 0x1C12, 0x00, 0);     // SM2 비활성화
        ECClient.SdoWriteU16(slave, 0x1C12, 0x01, 0x1601);
        ECClient.SdoWriteU8(slave, 0x1C12, 0x00, 1);     // PDO 개수 = 1


        /*
        Console.WriteLine($"RxMap Remap Complete");


        //엔트리 갯수 기록
        byte count = (byte)(sub - 1);
        ECClient.SdoWriteU8(slave, RxMap.Index, 0x00, count);
        Console.WriteLine($"RxMap Entree Count Write");


        // 4) SM2(RxPDO assign)에 이 PDO 연결
        ECClient.SdoWriteU8(slave, 0x1C12, 0x00, 1);        // RxPDO 개수 = 1
        ECClient.SdoWriteU16(slave, 0x1C12, 0x01, RxMap.Index); // 1번째 PDO = rxMap.Index
        Console.WriteLine($"RxMap Connect");
        */

    }



    public void RemapRxPdo(ushort slave, ESIXMLData.ESIPDO RxMap)
    {
        // 1) SM2(RxPDO) 비활성화: 0x1C12:00 = 0
        ECClient.SdoWriteU8(slave, 0x1C12,0x00,0);
        Console.WriteLine("SM2(RxPDO) 비활성화");


        // 2) Index의 매핑 제거.
        ECClient.SdoWriteU8(slave, RxMap.Index, 0x00, 0);
        Console.WriteLine("Index 매핑 제거");

        // 3) RxMap의 Entry 를 써넣기.
        //byte sub = 1;
        //foreach (var entrie in RxMap.Entries)
        //{
        //    uint map = ECClient.MakeMapWord(entrie.Index, entrie.SubIndex, entrie.BitLength);
        //    ECClient.SdoWriteU32(slave, RxMap.Index, sub, map);

        //    Console.WriteLine($"RxMap Remapping {slave}-{RxMap.Index}-{sub}");

        //    sub++;
        //}

        // 6040:0 (16bit)
        uint m1 = ECClient.MakeMapWord(0x6040, 0x00, 16);
        ECClient.SdoWriteU32(slave, 0x1601, 0x01, m1);

        // 607A:0 (32bit)
        uint m2 = ECClient.MakeMapWord(0x607A, 0x00, 32);
        ECClient.SdoWriteU32(slave, 0x1601, 0x02, m2);

        // 6081:0 (32bit)
        uint m3 = ECClient.MakeMapWord(0x6081, 0x00, 32);
        ECClient.SdoWriteU32(slave, 0x1601, 0x03, m3);

        // 6060:0 (8bit)
        uint m4 = ECClient.MakeMapWord(0x6060, 0x00, 8);
        ECClient.SdoWriteU32(slave, 0x1601, 0x04, m4);

        // 엔트리 개수 = 4 (16+32+32+8 = 88bit)
        ECClient.SdoWriteU8(slave, 0x1601, 0x00, 4);

        // 이제 Assign
        ECClient.SdoWriteU8(slave, 0x1C12, 0x00, 0);     // SM2 비활성화
        ECClient.SdoWriteU16(slave, 0x1C12, 0x01, 0x1601);
        ECClient.SdoWriteU8(slave, 0x1C12, 0x00, 1);     // PDO 개수 = 1


        /*
        Console.WriteLine($"RxMap Remap Complete");


        //엔트리 갯수 기록
        byte count = (byte)(sub - 1);
        ECClient.SdoWriteU8(slave, RxMap.Index, 0x00, count);
        Console.WriteLine($"RxMap Entree Count Write");


        // 4) SM2(RxPDO assign)에 이 PDO 연결
        ECClient.SdoWriteU8(slave, 0x1C12, 0x00, 1);        // RxPDO 개수 = 1
        ECClient.SdoWriteU16(slave, 0x1C12, 0x01, RxMap.Index); // 1번째 PDO = rxMap.Index
        Console.WriteLine($"RxMap Connect");
        */
    }

    public void RemapTxPdo(ushort slave)
    {


    }

    public void RemapTxPdo(ushort slave, ESIXMLData.ESIPDO TxMap)
    {
        //테스트 필요.
        //ESI Catalog에서 해당 구성정보를 가져와서 사용할것.
        //PP 모드 구성 가져와서 사용할것.

        // 1) SM3(TxPDO) 비활성화: 0x1C13:00 = 0
        ECClient.SdoWriteU8(slave, 0x1C13, 0x00, 0);
        Console.WriteLine("SM3(TxPDO) 비활성화");

        // 2) 0x1A00, 0x1A01 기존 매핑 제거 (sub0 = 0)
        //    (장비에 따라 1A00만 쓰고 있을 수도 있는데, ESI에 Exclude=1A00 나와 있으니 둘 다 정리)
        ECClient.SdoWriteU8(slave, TxMap.Index, 0x00, 0); //ESI상 0x1A01 0x1A00 둘중 하나.
        Console.WriteLine("Index 매핑 제거");

        // 3) 0x1A01에 새 엔트리들 작성
        // sub1: 0x6041:00, 16bit
        byte sub = 1;
        foreach (var entrie in TxMap.Entries)
        {
            uint map = ECClient.MakeMapWord(entrie.Index, entrie.SubIndex, entrie.BitLength);
            ECClient.SdoWriteU32(slave, TxMap.Index, sub, map);

            Console.WriteLine($"TxMap Remapping {slave}-{TxMap.Index}-{sub}");

            sub++;
        }

        Console.WriteLine($"TxMap Remap Complete");

        //엔트리 갯수 기록
        byte count = (byte)(sub - 1);
        ECClient.SdoWriteU8(slave, TxMap.Index, 0x00, count);
        Console.WriteLine($"TxMap Entree Count Write");


        // 4) SM3(TxPDO assign)에 1A01만 다시 연결
        ECClient.SdoWriteU8(slave, 0x1C13, 0x00, 1);        // PDO 개수 = 1
        ECClient.SdoWriteU16(slave, 0x1C13, 0x01, TxMap.Index);  // 1번째 PDO = 0x1A01
        Console.WriteLine($"TxMap Connect");

    }


}
