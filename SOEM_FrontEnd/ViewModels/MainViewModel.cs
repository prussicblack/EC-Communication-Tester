using CommunityToolkit.Mvvm.Input;
using Microsoft.VisualBasic;
using SOEM_FrontEnd.Ethercat;
using SOEM_FrontEnd.Ethercat.ESI;
using SOEM_FrontEnd.Model;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Input;
using static System.Net.Mime.MediaTypeNames;

namespace SOEM_FrontEnd.ViewModels;

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

    private void HandleTest()
    {
        //foreach (var (ifname, desc) in PcapIfEnumerator.GetAll())
        //{
        //    Console.WriteLine($"{ifname}  |  {desc}");
        //}

        // SOEM 열 때 그대로 사용:
        //string ifnameToUse = @"\\Device\\NPF_{YOUR_GUID}"; // 위 출력 중 하나 선택
        // SoemNative.soem_open(ifnameToUse);

        //if (string.IsNullOrEmpty(NICSelect))
        //{
        //    return;
        //}

        //string ifname = NICSelect.Substring(NICSelect.LastIndexOf(" - ") + (" - ".Length));

        //EcClient test = new EcClient();
        //test.Open(ifname);

        ESICatalog _esi = ESICatalog.LoadFolder("ESI");
        //_esi.TryGetDevice()
        
        
    }

    //ESI XML 리딩.






}
