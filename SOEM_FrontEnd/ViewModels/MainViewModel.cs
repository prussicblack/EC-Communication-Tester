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
using System.IO;
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
        //foreach (var (ifname, desc) in PcapIfEnumerator.GetAll())
        //{
        //    Console.WriteLine($"{ifname}  |  {desc}");
        //}

        // SOEM 열 때 그대로 사용:
        //string ifnameToUse = @"\\Device\\NPF_{YOUR_GUID}"; // 위 출력 중 하나 선택
        //SOEMNative.soem_open(ifnameToUse);

        //if (string.IsNullOrEmpty(NICSelect))
        //{
        //    return;
        //}


        //
        /*
        // 0x1C12 sub0 = RxPDO 개수
        ushort slaveadd = 1;
        ushort index = 0x1c12;
        byte sub = 0;
        uint len = sizeof(byte);
        //IntPtr p = Marshal.AllocHGlobal(1);
        byte[] buf = new byte[len];

        int ret = SOEMNative.soem_sdo_read(slaveadd, index, sub, buf, ref len);
        if (ret != 0)
        {
            Console.WriteLine($"soem_sdo_read failed");
        }

        // 0x1600 sub1..n = 매핑 엔트리 (32bit)
        index = 0x1600;
        sub = 0;
        len = sizeof(byte);
        buf = new byte[len];
        SOEMNative.soem_sdo_read(slaveadd, index, sub, buf, ref len);
        byte entryCount = buf[0];

        for (byte subindex = 1; subindex <= entryCount; subindex++)
        {
            len = 4;
            buf = new byte[len];
            SOEMNative.soem_sdo_read(slaveadd, index, subindex, buf, ref len);

            // EtherCAT/CoE는 little-endian, Windows도 little-endian이니까
            // 그냥 BitConverter.ToUInt32(buf, 0) 쓰면 됨.
            uint mapword = BitConverter.ToUInt32(buf, 0);


            ushort idx = (ushort)(mapword >> 16);
            byte subIdx = (byte)((mapword >> 8) & 0xFF);
            byte bitLen = (byte)(mapword & 0xFF);

            Console.WriteLine($"RxPDO List - 0x{idx:X4}:{subIdx:X2} ({bitLen} bit)");
        }

        // 0x1C13 sub0 = TxPDO 개수
        slaveadd = 1;
        index = 0x1c13;
        sub = 0;
        len = sizeof(byte);
        //IntPtr p = Marshal.AllocHGlobal(1);
        buf = new byte[len];

        ret = SOEMNative.soem_sdo_read(slaveadd, index, sub, buf, ref len);
        if (ret != 0)
        {
            Console.WriteLine($"soem_sdo_read failed");
        }

        // 0x1A00 sub1..n = 매핑 엔트리 (32bit)
        index = 0x1A00;
        sub = 0;
        len = sizeof(byte);
        buf = new byte[len];
        SOEMNative.soem_sdo_read(slaveadd, index, sub, buf, ref len);
        entryCount = buf[0];

        for (byte subindex = 1; subindex <= entryCount; subindex++)
        {
            len = 4;
            buf = new byte[len];
            SOEMNative.soem_sdo_read(slaveadd, index, subindex, buf, ref len);

            // EtherCAT/CoE는 little-endian, Windows도 little-endian이니까
            // 그냥 BitConverter.ToUInt32(buf, 0) 쓰면 됨.
            uint mapword = BitConverter.ToUInt32(buf, 0);


            ushort idx = (ushort)(mapword >> 16);
            byte subIdx = (byte)((mapword >> 8) & 0xFF);
            byte bitLen = (byte)(mapword & 0xFF);

            Console.WriteLine($"TxPDO List - 0x{idx:X4}:{subIdx:X2} ({bitLen} bit)");
        }


    */

    }

    public void RemapTxPdoToPpInputs(ushort slave)
    {
        //테스트 필요.
        //ESI Catalog에서 해당 구성정보를 가져와서 사용할것.
        //PP 모드 구성 가져와서 사용할것.

        // 1) SM3(TxPDO) 비활성화: 0x1C13:00 = 0
        ECClient.SdoWriteU8(slave, 0x1C13, 0x00, 0);

        // 2) 0x1A00, 0x1A01 기존 매핑 제거 (sub0 = 0)
        //    (장비에 따라 1A00만 쓰고 있을 수도 있는데, ESI에 Exclude=1A00 나와 있으니 둘 다 정리)
        ECClient.SdoWriteU8(slave, 0x1A00, 0x00, 0);
        ECClient.SdoWriteU8(slave, 0x1A01, 0x00, 0);

        // 3) 0x1A01에 새 엔트리들 작성
        // sub1: 0x6041:00, 16bit
        uint m1 = ECClient.MakeMapWord(0x6041, 0x00, 16);
        ECClient.SdoWriteU32(slave, 0x1A01, 0x01, m1);

        // sub2: 0x6064:00, 32bit
        uint m2 = ECClient.MakeMapWord(0x6064, 0x00, 32);
        ECClient.SdoWriteU32(slave, 0x1A01, 0x02, m2);

        // sub3: 0x606C:00, 32bit
        uint m3 = ECClient.MakeMapWord(0x606C, 0x00, 32);
        ECClient.SdoWriteU32(slave, 0x1A01, 0x03, m3);

        // sub4: 0x60FD:00, 32bit
        uint m4 = ECClient.MakeMapWord(0x60FD, 0x00, 32);
        ECClient.SdoWriteU32(slave, 0x1A01, 0x04, m4);

        // sub5: 0x603F:00, 16bit
        uint m5 = ECClient.MakeMapWord(0x603F, 0x00, 16);
        ECClient.SdoWriteU32(slave, 0x1A01, 0x05, m5);

        // sub6: 0x6061:00, 8bit
        uint m6 = ECClient.MakeMapWord(0x6061, 0x00, 8);
        ECClient.SdoWriteU32(slave, 0x1A01, 0x06, m6);

        // 엔트리 개수 = 6
        ECClient.SdoWriteU8(slave, 0x1A01, 0x00, 6);

        // 4) SM3(TxPDO assign)에 1A01만 다시 연결
        ECClient.SdoWriteU8(slave, 0x1C13, 0x00, 1);        // PDO 개수 = 1
        ECClient.SdoWriteU16(slave, 0x1C13, 0x01, 0x1A01);  // 1번째 PDO = 0x1A01
    }


}
