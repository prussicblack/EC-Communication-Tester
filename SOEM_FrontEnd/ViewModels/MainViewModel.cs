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

    public ICommand CMD_Test { get; private set; }


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

    private int _SelectedSlave;
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


    public MainViewModel()
    {
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


    }

    private void DrawSlaves()
    {

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

        string ifname = NICSelect.Substring(NICSelect.LastIndexOf(" - ") + (" - ".Length));

        SOEMNative.soem_open(ifname);
        SOEMNative.soem_config_init(1);

        int slave = SOEMNative.soem_slave_count();

        //EC 슬레이브 조회.
        List<SoemSlaveInfo> slaves = new List<SoemSlaveInfo>();

        Slaves.Clear();
        
        for (int i = 1; i <= slave; i++)
        {
            if (SOEMNative.soem_get_slave_info(i, out SoemSlaveInfo info) != 0)
            {
                slaves.Add(info);
                Slaves.Add($" {i} - Name = {info.name}, Alias = {info.alias}, StationAddress = 0x{info.configadr.ToString("X")}, VendorCode = 0x{info.vendor.ToString("X")}, ProductCode = 0x{info.product.ToString("X")}, Revision=0x{info.revision.ToString("X")}");
            }
            else
            {
                Console.WriteLine($"#{i} -> failed to query");
            }
        }

        //EcClient test = new EcClient();


        //ESICatalog _esi = ESICatalog.LoadFolder("ESI");

        //ESIXMLData.EsiDevice testDevice = new ESIXMLData.EsiDevice();
        //_esi.TryGetDevice(slaves[0].vendor, slaves[0].product, slaves[0].revision, out ESIXMLData.EsiDevice testDevice);
        
        {
            //프로그램 로딩시 Splash Screen 과 함께 로딩.
            //의외로 시간이 좀 걸릴 수 있음.
            string path = AppDomain.CurrentDomain.BaseDirectory + "ESI";

            var devices = ESICatalog.LoadAllDevices(path);
        }
        //foreach (var dev in devices)
        //{
        //    Console.WriteLine($"Vendor=0x{dev.VendorId:X8}, Product=0x{dev.ProductCode:X8}, Rev=0x{dev.Revision:X8}, Name={dev.Name}");
        //}

    }

    //ESI XML 리딩.






}
