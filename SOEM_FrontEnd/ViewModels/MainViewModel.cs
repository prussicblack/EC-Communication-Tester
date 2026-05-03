using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SOEM_FrontEnd.Automation;
using SOEM_FrontEnd.DataMap;
using SOEM_FrontEnd.Ethercat;
using SOEM_FrontEnd.Ethercat.ESI;
using SOEM_FrontEnd.Ethercat.EthercatProfile.Interfaces;
using SOEM_FrontEnd.Ethercat.MiniENI;
using SOEM_FrontEnd.Model;
using SOEM_FrontEnd.Util;
using SOEM_FrontEnd.Util.Logging;
using SOEM_FrontEnd.Util.Logging.UI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Input;

//#nullable enable

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

    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class MainViewModel : ViewModelBase, IDisposable
{
    //public string Greeting => "Welcome to Avalonia!";

    private EcClient ECClient;

    private StateMachine StateMachine;

    public ICommand CMD_Test { get; private set; }

    public ICommand CMD_SelectNIC { get; private set; }

    private double _nicComboWidth = 180;
    public double NicComboWidth
    {
        get => _nicComboWidth;
        set => SetProperty(ref _nicComboWidth, value);
    }

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

    public ObservableCollection<string> SlavesListUI { get; } = new();

    private int _SlaveCountUI;

    public int SlaveCountUI
    {
        get
        {
            return _SlaveCountUI;
        }
        set
        {
            _SlaveCountUI = value;
            OnPropertyChanged(nameof(SlaveCountUI));
        }
    }

    List<SoemSlaveInfo> SlaveInfoData = new List<SoemSlaveInfo>();


    public List<ESIXMLData.ESIDevice> DevicesData = new List<ESIXMLData.ESIDevice>();

    private ObservableCollection<ESIXMLData.ESISDOObject> _SDOObjects;

    public ObservableCollection<ESIXMLData.ESISDOObject> SDOObjects
    {
        get
        {
            return _SDOObjects;
        }

        private set
        {
            if (!object.ReferenceEquals(_SDOObjects, value))
            {
                _SDOObjects = value;
                OnPropertyChanged(nameof(SDOObjects));
            }
        }

    }

    private SDOFlatObject _selectedSDO;
    public SDOFlatObject SelectedSDO
    {
        get { return _selectedSDO; }
        set
        {
            if (object.ReferenceEquals(_selectedSDO, value))
                return;

            _selectedSDO = value;
            OnPropertyChanged(nameof(SelectedSDO));

            UpdateSdoSelectionState();

            //1010 1011일때 저장문구 기록용.
            SelectedSdoTextBoxWrite(SelectedSDO);
        }
    }


    //Slave데이터를 보여주기 위한 프로퍼티들.
    //SlaveStore 에 대한 프로퍼티 노출.
    //참조 변경으로 갈아끼우는 방식임.
    public SlaveStore SelectedSlaveData
    {
        get
        {
            if (Datamap.Instance.IsInit() == true)
                return Datamap.Instance.GetSlave(_SelectedSlave);

            return null;
        }
    }

    private int _SelectedSlave;

    public int SelectedSlave
    {
        get
        {
            return _SelectedSlave;
        }
        set
        {
            if (_SelectedSlave == value)
                return;
            _SelectedSlave = value;
            OnPropertyChanged(nameof(SelectedSlave));
            OnPropertyChanged(nameof(SelectedSlaveData));           // 중요

            OnPropertyChanged(nameof(IsMasterSelected));
            OnPropertyChanged(nameof(IsSlaveSelected));

            UpdateControlProfile();

        }
    }

    public bool IsMasterSelected => SelectedSlave == 0;
    public bool IsSlaveSelected => SelectedSlave != 0;


    // Control 탭: SelectedSlaveData.BaseProfile이 IMotorCommands이면 MotorControl을 표시
    private MotorControlViewModel _motorControlVm;

    public MotorControlViewModel MotorControlVm
    {
        get { return _motorControlVm; }
        private set
        {
            if (SetProperty(ref _motorControlVm, value))
            {
                OnPropertyChanged(nameof(HasMotorControl));
                OnPropertyChanged(nameof(HasNoMotorControl));
                OnPropertyChanged(nameof(HasNoControl));
            }
        }
    }

    public bool HasMotorControl => MotorControlVm != null;
    public bool HasNoMotorControl => MotorControlVm == null;

    //IO컨트롤용 프로퍼티 추가.
    private IOControlViewModel _ioOutputVm;
    private IOControlViewModel _ioInputVm;
    private IPDOView _selectedPdoView;

    public IOControlViewModel IoOutputVm
    {
        get => _ioOutputVm;
        private set
        {
            if (SetProperty(ref _ioOutputVm, value))
            {
                OnPropertyChanged(nameof(HasIoControl));
                OnPropertyChanged(nameof(HasNoControl));
                OnPropertyChanged(nameof(HasIoOutput));
            }
        }
    }

    public IOControlViewModel IoInputVm
    {
        get => _ioInputVm;
        private set
        {
            if (SetProperty(ref _ioInputVm, value))
            {
                OnPropertyChanged(nameof(HasIoControl));
                OnPropertyChanged(nameof(HasNoControl));
                OnPropertyChanged(nameof(HasIoInput));
            }
        }
    }


    public bool HasIoControl
    {
        get { return HasIoOutput || HasIoInput; }
    }

    public bool HasNoControl
    {
        get
        {
            return IsSlaveSelected && MotorControlVm == null && !HasIoControl && !HasValueControl;
        }
    }

    public bool HasIoOutput
    {
        get { return IoOutputVm != null; }
    }

    public bool HasIoInput
    {
        get { return IoInputVm != null; }
    }

    //Value컨트롤용 프로퍼티 추가
    private ValueControlViewModel _valueControlVm;

    public ValueControlViewModel ValueControlVm
    {
        get { return _valueControlVm; }
        private set
        {
            if (SetProperty(ref _valueControlVm, value))
            {
                OnPropertyChanged(nameof(HasValueControl));
                OnPropertyChanged(nameof(HasNoControl));
            }
        }
    }

    public bool HasValueControl
    {
        get { return ValueControlVm != null; }
    }

    //마스터 통계용 UI
    private bool _pdoIsRunning;
    public bool PdoIsRunning
    {
        get { return _pdoIsRunning; }
        set { SetProperty(ref _pdoIsRunning, value); }
    }

    private long _pdoLoopCount;
    public long PdoLoopCount
    {
        get { return _pdoLoopCount; }
        set { SetProperty(ref _pdoLoopCount, value); }
    }

    private long _pdoLastPeriodUs;
    public long PdoLastPeriodUs
    {
        get { return _pdoLastPeriodUs; }
        set { SetProperty(ref _pdoLastPeriodUs, value); }
    }

    private long _pdoTargetPeriodUs;
    public long PdoTargetPeriodUs
    {
        get { return _pdoTargetPeriodUs; }
        set { SetProperty(ref _pdoTargetPeriodUs, value); }
    }

    private long _pdoMinPeriodUs;
    public long PdoMinPeriodUs
    {
        get { return _pdoMinPeriodUs; }
        set { SetProperty(ref _pdoMinPeriodUs, value); }
    }

    private long _pdoMaxPeriodUs;
    public long PdoMaxPeriodUs
    {
        get { return _pdoMaxPeriodUs; }
        set { SetProperty(ref _pdoMaxPeriodUs, value); }
    }

    private long _pdoAvgPeriodUs;
    public long PdoAvgPeriodUs
    {
        get { return _pdoAvgPeriodUs; }
        set { SetProperty(ref _pdoAvgPeriodUs, value); }
    }

    private int _pdoLastWkc;
    public int PdoLastWkc
    {
        get { return _pdoLastWkc; }
        set
        {
            if (SetProperty(ref _pdoLastWkc, value))
            {
                OnPropertyChanged(nameof(PdoWkcText));
            }
        }
    }

    private int _pdoExpectedWkc;
    public int PdoExpectedWkc
    {
        get { return _pdoExpectedWkc; }
        set
        {
            if (SetProperty(ref _pdoExpectedWkc, value))
            {
                OnPropertyChanged(nameof(PdoWkcText));
            }
        }
    }

    public string PdoWkcText
    {
        get
        {
            if (PdoExpectedWkc > 0)
            {
                return PdoLastWkc.ToString() + " / " + PdoExpectedWkc.ToString();
            }

            return PdoLastWkc.ToString();
        }
    }

    private long _pdoWkcErrorCount;
    public long PdoWkcErrorCount
    {
        get { return _pdoWkcErrorCount; }
        set { SetProperty(ref _pdoWkcErrorCount, value); }
    }

    private long _pdoOverrunCount;
    public long PdoOverrunCount
    {
        get { return _pdoOverrunCount; }
        set { SetProperty(ref _pdoOverrunCount, value); }
    }

    private string _pdoLastUpdateText = "";
    public string PdoLastUpdateText
    {
        get { return _pdoLastUpdateText; }
        set { SetProperty(ref _pdoLastUpdateText, value); }
    }

    private long _pdoMaxWaitUs;
    public long PdoMaxWaitUs
    {
        get { return _pdoMaxWaitUs; }
        set { SetProperty(ref _pdoMaxWaitUs, value); }
    }

    private long _pdoMaxBodyUs;
    public long PdoMaxBodyUs
    {
        get { return _pdoMaxBodyUs; }
        set { SetProperty(ref _pdoMaxBodyUs, value); }
    }

    private long _pdoMaxTxSendUs;
    public long PdoMaxTxSendUs
    {
        get { return _pdoMaxTxSendUs; }
        set { SetProperty(ref _pdoMaxTxSendUs, value); }
    }

    private long _pdoMaxRecvUs;
    public long PdoMaxRecvUs
    {
        get { return _pdoMaxRecvUs; }
        set { SetProperty(ref _pdoMaxRecvUs, value); }
    }

    private long _pdoMaxPostUs;
    public long PdoMaxPostUs
    {
        get { return _pdoMaxPostUs; }
        set { SetProperty(ref _pdoMaxPostUs, value); }
    }

    private long _pdoMaxHousekeepingUs;
    public long PdoMaxHousekeepingUs
    {
        get { return _pdoMaxHousekeepingUs; }
        set { SetProperty(ref _pdoMaxHousekeepingUs, value); }
    }



    private bool _isPdoStatsUiLogEnabled;
    public bool IsPdoStatsUiLogEnabled
    {
        get { return _isPdoStatsUiLogEnabled; }
        set { SetProperty(ref _isPdoStatsUiLogEnabled, value); }
    }

    //public ObservableCollection<string> PdoStatsUiLogs { get; } = new ObservableCollection<string>();

    public ICommand ResetPdoStatsCommand { get; }

    private long _lastPdoStatsUiLogTimestamp;
    private const double PdoStatsLogIntervalSeconds = 600.0;

    //PDO Map View
    //public ObservableCollection<PdoMapRow> RxPdoMapRows { get; } = new ObservableCollection<PdoMapRow>();

    //public ObservableCollection<PdoMapRow> TxPdoMapRows { get; } = new ObservableCollection<PdoMapRow>();
    public ObservableCollection<PdoMapUiRow> RxPdoMapRows { get; } = new ObservableCollection<PdoMapUiRow>();
    public ObservableCollection<PdoMapUiRow> TxPdoMapRows { get; } = new ObservableCollection<PdoMapUiRow>();

    public bool HasRxPdoMapRows
    {
        get { return RxPdoMapRows.Count > 0; }
    }

    public bool HasTxPdoMapRows
    {
        get { return TxPdoMapRows.Count > 0; }
    }
    public bool HasNoPdoMapRows
    {
        get { return RxPdoMapRows.Count == 0 && TxPdoMapRows.Count == 0; }
    }

    public GridLength RxPdoMapRowsHeight
    {
        get
        {
            if (HasRxPdoMapRows)
            {
                return new GridLength(1, GridUnitType.Star);
            }

            return new GridLength(0);
        }
    }

    public GridLength TxPdoMapRowsHeight
    {
        get
        {
            if (HasTxPdoMapRows)
            {
                return new GridLength(1, GridUnitType.Star);
            }

            return new GridLength(0);
        }
    }



    private void HandleResetPdoStats()
    {
        if (StateMachine != null)
        {
            StateMachine.ResetStats();
        }

        PdoLoopCount = 0;
        PdoLastPeriodUs = 0;
        PdoMinPeriodUs = 0;
        PdoMaxPeriodUs = 0;
        PdoAvgPeriodUs = 0;
        PdoLastWkc = 0;
        PdoExpectedWkc = 0;
        PdoWkcErrorCount = 0;
        PdoOverrunCount = 0;
        PdoLastUpdateText = "";
        PdoMaxWaitUs = 0;
        PdoMaxBodyUs = 0;
        PdoMaxTxSendUs = 0;
        PdoMaxRecvUs = 0;
        PdoMaxPostUs = 0;
        PdoMaxHousekeepingUs = 0;
        _lastPdoStatsUiLogTimestamp = 0;
    }

    private void UpdateControlProfile()
    {
        var store = SelectedSlaveData;
        if (store == null)
        {
            MotorControlVm = null;
            ValueControlVm = null;
            IoOutputVm = null;
            IoInputVm = null;
            _selectedPdoView = null;
            PdoHexDumpVm.Attach(null);

            RxPdoMapRows.Clear();
            TxPdoMapRows.Clear();

            return;
        }

        IPDOView pdoView = store.BaseProfile as IPDOView;
        _selectedPdoView = pdoView;
        PdoHexDumpVm.Attach(pdoView);

        UpdatePdoMapRows(pdoView);

        //모터 우선확인.
        IMotorCommands motor = store.BaseProfile as IMotorCommands;

        if (motor != null)
        {
            if (MotorControlVm == null)
            {
                MotorControlVm = new MotorControlViewModel();
            }

            MotorControlVm.Attach(motor);

            ValueControlVm = null;
            IoOutputVm = null;
            IoInputVm = null;
            return;
        }

        //모터 아니면...
        MotorControlVm = null;

        if (pdoView == null)
        {
            IoOutputVm = null;
            IoInputVm = null;
            return;
        }

        //Value프로파일일때.
        IValuePdoView valueView = store.BaseProfile as IValuePdoView;
        if (valueView != null)
        {
            if (ValueControlVm == null)
            {
                ValueControlVm = new ValueControlViewModel();
            }

            ValueControlVm.Attach(valueView);

            IoOutputVm = null;
            IoInputVm = null;
            return;
        }

        ValueControlVm = null;

        //IO프로파일일때.
        int outBytes = pdoView.OutputSnapshot.Length;
        int inBytes = pdoView.InputSnapshot.Length;

        const int columns = 16;

        if (outBytes > 0)
        {
            int bits = outBytes * 8;
            if (IoOutputVm == null) IoOutputVm = new IOControlViewModel(bits, columns, "Outputs (RxPDO)", true);
            else IoOutputVm.Reset(bits, columns);
        }
        else IoOutputVm = null;

        if (inBytes > 0)
        {
            int bits = inBytes * 8;
            if (IoInputVm == null) IoInputVm = new IOControlViewModel(bits, columns, "Inputs (TxPDO)", false);
            else IoInputVm.Reset(bits, columns);
        }

        else IoInputVm = null;

        IPDOAccess pdoAccess = store.BaseProfile as IPDOAccess;

        // ... IoOutputVm / IoInputVm 생성/Reset 끝난 다음에

        if (IoOutputVm != null)
        {
            IoOutputVm.Attach(pdoAccess);
        }

        // Input은 써야 하는 게 아니면 굳이 안 붙여도 됨(원하면 null로 detach)
        if (IoInputVm != null)
            IoInputVm.Attach(null);

        //if (MotorControlVm == null)
        //    MotorControlVm = new MotorControlViewModel();

        //MotorControlVm.Attach(motor);

        //PdoHexDumpVm.Attach(SelectedSlaveData != null ? (SelectedSlaveData.BaseProfile as IPDOView) : null);

    }

    private void UpdatePdoMapRows(IPDOView pdoView)
    {

        RxPdoMapRows.Clear();
        TxPdoMapRows.Clear();

        if (pdoView == null)
        {
            return;
        }

        if (pdoView.RxPdoMapRows != null)
        {
            for (int i = 0; i < pdoView.RxPdoMapRows.Count; i++)
            {
                PdoMapRow mapRow = pdoView.RxPdoMapRows[i];
                SDOFlatObject sdoRow = FindSdoRowForCurrentSlave(mapRow.Index, mapRow.SubIndex);

                RxPdoMapRows.Add(new PdoMapUiRow(mapRow, sdoRow));
            }
        }

        if (pdoView.TxPdoMapRows != null)
        {
            for (int i = 0; i < pdoView.TxPdoMapRows.Count; i++)
            {
                PdoMapRow mapRow = pdoView.TxPdoMapRows[i];
                SDOFlatObject sdoRow = FindSdoRowForCurrentSlave(mapRow.Index, mapRow.SubIndex);

                TxPdoMapRows.Add(new PdoMapUiRow(mapRow, sdoRow));
            }
        }

        OnPropertyChanged(nameof(HasRxPdoMapRows));
        OnPropertyChanged(nameof(HasTxPdoMapRows));
        OnPropertyChanged(nameof(HasNoPdoMapRows));
        OnPropertyChanged(nameof(RxPdoMapRowsHeight));
        OnPropertyChanged(nameof(TxPdoMapRowsHeight));
    }

    //SDO 관련
    public ICommand CMD_ReadAllSdoCommand { get; private set; }
    public ICommand CMD_ReadSelectedSdoCommand { get; private set; }
    public ICommand CMD_WriteSelectedSdoCommand { get; private set; }

    private string _WriteValueText;
    public string WriteValueText
    {
        get => _WriteValueText;
        set
        {
            if (_WriteValueText == value)
                return;
            _WriteValueText = value;
            OnPropertyChanged(nameof(WriteValueText));
        }
    }

    private bool _CanReadSelectedSdo;
    public bool CanReadSelectedSdo
    {
        get => _CanReadSelectedSdo;
        set
        {
            if (_CanReadSelectedSdo == value)
                return;
            _CanReadSelectedSdo = value;
            OnPropertyChanged(nameof(CanReadSelectedSdo));
        }
    }

    private bool _CanWriteSelectedSdo;
    public bool CanWriteSelectedSdo
    {
        get => _CanWriteSelectedSdo;
        set
        {
            if (_CanWriteSelectedSdo == value)
                return;
            _CanWriteSelectedSdo = value;
            OnPropertyChanged(nameof(CanWriteSelectedSdo));
        }
    }

    private string _MasterEcStateText = "NONE";
    public string MasterEcStateText
    {
        get
        {
            return _MasterEcStateText;
        }
        set
        {
            if (_MasterEcStateText == value)
                return;

            _MasterEcStateText = value;
            OnPropertyChanged(nameof(MasterEcStateText));
        }
    }

    private string _MasterEcStateColor = "Gray";
    public string MasterEcStateColor
    {
        get
        {
            return _MasterEcStateColor;
        }
        set
        {
            if (_MasterEcStateColor == value)
                return;

            _MasterEcStateColor = value;
            OnPropertyChanged(nameof(MasterEcStateColor));
        }
    }





    public SDOSubWorker SdoWorker { get; private set; }

    public ICommand CMD_MoveToInit { get; private set; }
    public ICommand CMD_MoveToPreOp { get; private set; }
    public ICommand CMD_MoveToSafeOp { get; private set; }
    public ICommand CMD_MoveToOp { get; private set; }

    //public ICommand CMD_ResetStat { get; private set; }


    //UI단에 표기되는 로그.
    public ObservableCollection<string> UiLogs { get; } = new();
    private readonly AvaloniaUiLogSink _sink;

    //로그 베이스.
    private readonly ILogger _log;

    //PDO RawView에 대한 프로퍼티.
    public PdoHexDumpViewModel PdoHexDumpVm { get; } = new PdoHexDumpViewModel();

    //UI갱신용 DispatcherTimer.
    private DispatcherTimer _uiTimer;

    private DispatcherTimer _uiTimerLow;

    //MiniEni용 뷰모델.
    private MiniEniViewModel _miniEniVm;

    public MiniEniViewModel MiniEniVm
    {
        get { return _miniEniVm; }
        private set { SetProperty(ref _miniEniVm, value); }
    }

    public MainViewModel()
    {

        //로그 초기화
        _log = OPLogger.CreateLogger("SOEM_FrontEnd");

        ECClient = new EcClient();

        StateMachine = new StateMachine(ECClient);

        MiniEniVm = new MiniEniViewModel(CurrentENIConfig);

        CMD_Test = new RelayCommand(HandleTest);

        CMD_SelectNIC = new RelayCommand(HandleNIC);


        UpdateNicList();



        CMD_ReadAllSdoCommand = new RelayCommand(HandleReadAllSDO);
        CMD_ReadSelectedSdoCommand = new RelayCommand(HandleReadSelectedSDO);
        CMD_WriteSelectedSdoCommand = new RelayCommand(HandleSelectedWriteSDO);


        CMD_MoveToInit = new RelayCommand(HandleMoveToInit);
        CMD_MoveToPreOp = new RelayCommand(HandleMoveToPreOp);
        CMD_MoveToSafeOp = new RelayCommand(HandleMoveToSafeOp);
        CMD_MoveToOp = new RelayCommand(HandleMoveToOp);

        //CMD_ResetStat = new RelayCommand(HandleResetStat);

        ResetPdoStatsCommand = new RelayCommand(HandleResetPdoStats);

        //UI로그 연결을 위한 코드.
        _sink = new AvaloniaUiLogSink(line =>
        {
            // AvaloniaUiLogSink가 UI thread로 flush하니까 여기선 Add만
            UiLogs.Add(line);

            //너무 길어지면 오래된 것 삭제
            const int max = 3000;
            if (UiLogs.Count > max)
                UiLogs.RemoveAt(0);
        });

        OPLogger.SetUiSink(_sink);

        //로그 기록
        _log.LogInformation("MainViewModel Created");

        //UI갱신용 UI타이머 생성.
        _uiTimer = new DispatcherTimer();
        _uiTimer.Interval = TimeSpan.FromMilliseconds(15); // 갱신주기.약 60Hz
        _uiTimer.Tick += (_, __) =>
        {
            // MotorControl이 떠있을 때만 돌려도 되고, 항상 돌려도 됨
            if (MotorControlVm != null)
            {
                MotorControlVm.UiTick();
            }
            
            if (ValueControlVm != null)
            {
                ValueControlVm.UiTick();
            }

            if (_selectedPdoView != null)
            {
                if (IoOutputVm != null)
                    IoOutputVm.UpdateFromBytes(_selectedPdoView.OutputSnapshot.Span);

                if (IoInputVm != null)
                    IoInputVm.UpdateFromBytes(_selectedPdoView.InputSnapshot.Span);
            }


        };
        _uiTimer.Start();


        //임시 로그타이머. 나중에 Automation으로 옮길것.
        _uiTimerLow = new DispatcherTimer();
        _uiTimerLow.Interval = TimeSpan.FromMilliseconds(1000); // 갱신주기. 1초
        _uiTimerLow.Tick += (_, __) =>
        {
            //StateMachine.PollPdoStats();
            UpdatePdoStatsUi();
        };
        _uiTimerLow.Start();

        StateMachine.CurrentSequenceChanged += OnCurrentSequenceChanged;
        UpdateMasterEcStateUi(StateMachine.CurrentSequence);


        //임시 ENI실행부. 귀찮아서...나중에 Automation으로 옮기기.
        MiniENI miniEni = MiniENICatalog.Current;
        if (miniEni == null)
        {
            _log.LogInformation("MiniENI is not loaded."); 
            return;
        }
        if (miniEni.AutoOpenAdapter == false)
        {
            _log.LogInformation("MiniENI loaded. AutoOpenAdapter is disabled.");
            return;
        }

        if (miniEni.Adapter == null)
        {
            _log.LogWarning("MiniENI adapter is empty.");
            return;
        }
        EniAdapterConfig adapterConfig = miniEni.Adapter;

        string matched = null;

        

        for (int i = 0; i < NICList.Count; i++)
        {
            string ifname = NICList[i].Substring(NICList[i].LastIndexOf(" - ") + (" - ".Length));

            if (string.IsNullOrWhiteSpace(adapterConfig.Name) == false && string.Equals(ifname, adapterConfig.Name, StringComparison.OrdinalIgnoreCase))
            {
                matched = NICList[i];
                break;
            }
        }
        if (matched == null)
        {
            _log.LogWarning("Saved adapter not found.");
            return;
        }
        
        NICSelect = matched;

        //Nic 선택
        HandleNIC();

        //슬레이브 체크
        if (miniEni.Slaves == null)
        {
            _log.LogWarning("MiniENI slaves are empty.");
            return;
        }
        if (miniEni.Slaves.Count + 1 != Datamap.Instance.SlaveCount)
        {
            _log.LogWarning("Slave count mismatch.");
            return;
        }

        for (int i = 0; i < miniEni.Slaves.Count; i++)
        {
            EniSlaveConfig expected = miniEni.Slaves[i];
            SoemSlaveInfo actual = SlaveInfoData[expected.SlaveNo];


            //벤더 ID체크.
            if (HexEquals(expected.VendorId, actual.vendor) == false)
            {
                _log.LogWarning(expected.SlaveNo, "VendorId", expected.VendorId, actual.vendor);
                return;
            }
            //product Code 체크.
            if (HexEquals(expected.ProductCode, actual.product) == false)
            {
                _log.LogWarning(expected.SlaveNo, "ProductCode", expected.ProductCode, actual.product);
                return;
            }
            //리비전넘버 체크.
            if (HexEquals(expected.RevisionNo, actual.revision) == false)
            {
                _log.LogWarning(expected.SlaveNo, "RevisionNo", expected.RevisionNo, actual.revision);
                return;
            }
        }

        //OP로 이동.
        StateMachine.MoveToOperate();


    }


    public void Dispose()
    {
        if (_uiTimer != null)
        {
            _uiTimer.Stop();
        }

        if (_uiTimerLow != null)
        {
            _uiTimerLow.Stop();
        }

        if (StateMachine != null)
        {
            StateMachine.CurrentSequenceChanged -= OnCurrentSequenceChanged;
            StateMachine.Shutdown();
        }

        if (SdoWorker != null)
        {
            SdoWorker.Dispose();
            SdoWorker = null;
        }

        if (ECClient != null)
        {
            ECClient.Dispose();
            ECClient = null;
        }

        // 창 닫힐 때 sink 해제/정리
        OPLogger.SetUiSink(null);
        _sink.Dispose();
    }

    private MiniENI CurrentENIConfig()
    {
        MiniENI current = MiniENICatalog.Current;

        MiniENI project = new MiniENI();

        if (current != null)
        {
            project.Version = current.Version;
            project.ProjectName = current.ProjectName;
            project.AutoOpenAdapter = current.AutoOpenAdapter;
            project.AutoMoveToOp = current.AutoMoveToOp;
        }
        else
        {
            project.Version = 1;
            project.ProjectName = "Current Project";
            project.AutoOpenAdapter = true;
            project.AutoMoveToOp = false;
        }

        if (string.IsNullOrWhiteSpace(project.ProjectName))
        {
            project.ProjectName = "Current Project";
        }

        if(StateMachine.CurrentSequence == eStateSequenceName.Op)
        {
            project.AutoMoveToOp = true;
        }


        project.Adapter = CurrentAdapterConfig();
        project.Slaves.Clear();

        for (int i = 1; i < SlaveInfoData.Count; i++)
        {
            SoemSlaveInfo info = SlaveInfoData[i];

            int inBytes = 0;
            int outBytes = 0;
            int inBits = 0;
            int outBits = 0;

            try
            {
                SOEMNative.soem_get_slave_inout_size(
                    i,
                    out inBytes,
                    out outBytes,
                    out inBits,
                    out outBits);
            }
            catch
            {
                inBits = 0;
                outBits = 0;
            }

            EniSlaveConfig slaveConfig = new EniSlaveConfig();

            slaveConfig.SlaveNo = (ushort)i;
            slaveConfig.Name = info.name ?? "";

            slaveConfig.VendorId = "0x" + info.vendor.ToString("X8");
            slaveConfig.ProductCode = "0x" + info.product.ToString("X8");
            slaveConfig.RevisionNo = "0x" + info.revision.ToString("X8");

            slaveConfig.InputBits = inBits;
            slaveConfig.OutputBits = outBits;

            slaveConfig.Profile = GetCurrentProfileName(i);

            if (current != null && current.Slaves != null)
            {
                EniSlaveConfig oldConfig = FindMiniEniSlave(current, (ushort)i);

                if (oldConfig != null)
                {
                    if (string.IsNullOrWhiteSpace(oldConfig.Profile) == false)
                    {
                        slaveConfig.Profile = oldConfig.Profile;
                    }

                    if (oldConfig.StartupSdos != null)
                    {
                        slaveConfig.StartupSdos.AddRange(oldConfig.StartupSdos);
                    }

                    if (oldConfig.PdoMapping != null)
                    {
                        slaveConfig.PdoMapping = oldConfig.PdoMapping;
                    }
                }
            }

            project.Slaves.Add(slaveConfig);
        }

        return project;
    }

    private static EniSlaveConfig FindMiniEniSlave(MiniENI project, ushort slaveNo)
    {
        if (project == null || project.Slaves == null)
        {
            return null;
        }

        for (int i = 0; i < project.Slaves.Count; i++)
        {
            EniSlaveConfig slave = project.Slaves[i];

            if (slave != null && slave.SlaveNo == slaveNo)
            {
                return slave;
            }
        }

        return null;
    }

    private string GetCurrentProfileName(int slaveNo)
    {
        if (Datamap.Instance.IsInit() == false)
        {
            return "Auto";
        }

        SlaveStore store = Datamap.Instance.GetSlave(slaveNo);

        if (store == null || store.BaseProfile == null)
        {
            return "Auto";
        }

        if (store.BaseProfile is IMotorCommands)
        {
            return "CiA402_PP";
        }

        if (store.BaseProfile is IValuePdoView)
        {
            return "NormalValue";
        }

        if (store.BaseProfile is IPDOView)
        {
            return "NormalIO";
        }

        return "Auto";
    }



    private EniAdapterConfig CurrentAdapterConfig()
    {
        EniAdapterConfig adapter = new EniAdapterConfig();

        string selected = NICSelect;

        if (string.IsNullOrWhiteSpace(selected))
        {
            return adapter;
        }

        string separator = " - ";
        int pos = selected.LastIndexOf(separator, StringComparison.Ordinal);

        if (pos >= 0)
        {
            adapter.Description = selected.Substring(0, pos);
            adapter.Name = selected.Substring(pos + separator.Length);
        }
        else
        {
            adapter.Name = selected;
            adapter.Description = "";
        }

        adapter.MacAddress = "";

        return adapter;
    }



    private void UpdateMasterEcStateUi(eStateSequenceName state)
    {
        switch (state)
        {
            case eStateSequenceName.Init:
                MasterEcStateText = "INIT";
                MasterEcStateColor = "Gray";
                break;

            case eStateSequenceName.PreOp:
                MasterEcStateText = "PRE-OP";
                MasterEcStateColor = "DarkOrange";
                break;

            case eStateSequenceName.SafeOp:
                MasterEcStateText = "SAFE-OP";
                MasterEcStateColor = "Goldenrod";
                break;

            case eStateSequenceName.Op:
                MasterEcStateText = "OP";
                MasterEcStateColor = "LimeGreen";
                break;

            case eStateSequenceName.None:
            default:
                MasterEcStateText = "NONE";
                MasterEcStateColor = "IndianRed";
                break;
        }
    }

    private static bool HexEquals(string expectedText, uint actualValue)
    {
        uint expectedValue;

        if (TryParseHexUInt32(expectedText, out expectedValue) == false)
        {
            return false;
        }

        return expectedValue == actualValue;
    }
    private static bool TryParseHexUInt32(string text, out uint value)
    {
        value = 0;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text.Substring(2);
        }

        return uint.TryParse(
            text,
            System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture,
            out value);
    }


    private void OnCurrentSequenceChanged(eStateSequenceName state)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            UpdateMasterEcStateUi(state);
            OnPropertyChanged(nameof(SelectedSlaveData));
            UpdateControlProfile();
        });
    }

    private void HandleMoveToOp()
    {
        bool ok = StateMachine.MoveToOperate();
        if (!ok)
            return;

    }

    private void HandleMoveToSafeOp()
    {
        bool ok = StateMachine.MoveToSafeOP();
        if (!ok)
            return;


        // BaseProfile들이 SafeOP 전환 과정에서 채워지므로, 선택된 Slave UI 갱신
        OnPropertyChanged(nameof(SelectedSlaveData));
        UpdateControlProfile();
    }

    private void HandleMoveToPreOp()
    {
        bool ok = StateMachine.MoveToPreOP();
        if (!ok)
            return;

    }

    private void HandleMoveToInit()
    {
        bool ok = StateMachine.MoveToInit();
        if (!ok)
            return;

    }

    //private void HandleResetStat()
    //{
    //    StateMachine.ResetStats();
    //}

    private void HandleReadSelectedSDO()
    {
        if (CanReadSelectedSdo == false)
            return;

        var row = _selectedSDO;
        if (row == null)
            return;

        // 선택된 row에 SlaveNo/Index/SubIndex가 이미 들어있음
        SdoWorker.EnqueueRead(row.SlaveNo, row.Index, row.SubIndex);
    }


    private void HandleSelectedWriteSDO()
    {
        if (!CanWriteSelectedSdo)
            return;

        var row = SelectedSDO;
        if (row == null)
            return;

        string err;
        byte[] payload = TryBuildWritePayload(row, WriteValueText, out err);
        if (payload == null)
        {
            _log.LogInformation("[SDO][WRITE] " + err);
            return;
        }

        SdoWorker.EnqueueWrite(row.SlaveNo, row.Index, row.SubIndex, payload);


    }

    private static byte[] TryBuildWritePayload(SDOFlatObject row, string text, out string error)
    {
        error = null;

        if (row == null) { error = "No selected SDO."; return null; }
        if (string.IsNullOrWhiteSpace(text)) { error = "WriteValueText is empty."; return null; }

        string dt = (row.DataType ?? "").Trim().ToUpperInvariant();
        ushort bs = row.BitSize;
        string s = text.Trim();

        // 규칙: 0x 붙으면 16진수, 없으면 10진수
        bool isHex = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase);

        try
        {
            // ---------- BOOL ----------
            if (dt == "BOOLEAN" || dt == "BOOL")
            {
                if (string.Equals(s, "TRUE", StringComparison.OrdinalIgnoreCase)) return new byte[] { 1 };
                if (string.Equals(s, "FALSE", StringComparison.OrdinalIgnoreCase)) return new byte[] { 0 };

                // 숫자도 허용: 0x1 / 1 / 0x0 / 0
                if (isHex)
                {
                    ulong hv = Convert.ToUInt64(s.Substring(2), 16);
                    return new byte[] { (byte)(hv != 0 ? 1 : 0) };
                }
                else
                {
                    long dv = long.Parse(s);
                    return new byte[] { (byte)(dv != 0 ? 1 : 0) };
                }
            }

            // --------- SINT -----------
            if (dt == "SINT" || (dt == "INT" && bs == 8))
            {
                sbyte v;

                if (isHex)
                    v = unchecked((sbyte)Convert.ToByte(s.Substring(2), 16)); // 0x80~0xFF도 음수로 매핑됨
                else
                    v = sbyte.Parse(s);

                return new byte[] { unchecked((byte)v) };
            }

            // -------- USINT ------------
            if (dt == "USINT" || (dt == "UINT" && bs == 8))
            {
                byte v;

                if (isHex)
                    v = Convert.ToByte(s.Substring(2), 16);
                else
                    v = byte.Parse(s);

                return new byte[] { v };
            }

            // ---------- INT16 ----------
            if (dt == "INT16" || dt == "INTEGER16" || (dt == "INT" && bs == 16))
            {
                short v;
                if (isHex) v = unchecked((short)Convert.ToUInt16(s.Substring(2), 16));
                else v = short.Parse(s);

                var b = new byte[2];
                System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(b, v);   // MSB
                return b;
            }

            // ---------- UINT16 ----------
            if (dt == "UINT16" || dt == "UNSIGNED16" || (dt == "UINT" && bs == 16))
            {
                ushort v;
                if (isHex) v = Convert.ToUInt16(s.Substring(2), 16);
                else v = ushort.Parse(s);

                var b = new byte[2];
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(b, v);  // MSB
                return b;
            }

            // ---------- INT32 ----------
            if (dt == "INT32" || dt == "INTEGER32" || (dt == "DINT" && bs == 32))
            {
                int v;
                if (isHex) v = unchecked((int)Convert.ToUInt32(s.Substring(2), 16));
                else v = int.Parse(s);

                var b = new byte[4];
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(b, v);   // MSB
                return b;
            }

            // ---------- UINT32 / UDINT ----------
            if (dt == "UINT32" || dt == "UNSIGNED32" || (dt == "UDINT" && bs == 32))
            {
                uint v;
                if (isHex) v = Convert.ToUInt32(s.Substring(2), 16);
                else v = uint.Parse(s);

                var b = new byte[4];
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(b, v);  // MSB
                return b;
            }

            // ---------- RAW BYTES (정의 안 된 타입은 raw로) ----------
            // 허용 형식:
            //  1) "0x11223344"          (연속 hex)
            //  2) "0x11 0x22 0x33"      (토큰별 hex)
            //  3) "17 34 255"           (토큰별 dec)
            //  4) "0x11 34 0xFF, 1"     (혼합)
            {
                bool hasDelim = s.IndexOf(' ') >= 0 || s.IndexOf('\t') >= 0 || s.IndexOf(',') >= 0;

                // (A) 토큰 리스트: 혼합 허용
                if (hasDelim)
                {
                    string[] parts = s.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    byte[] buf = new byte[parts.Length];

                    for (int i = 0; i < parts.Length; i++)
                    {
                        string p = parts[i].Trim();

                        if (p.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        {
                            string hx = p.Substring(2);
                            buf[i] = Convert.ToByte(hx, 16);
                        }
                        else
                        {
                            buf[i] = byte.Parse(p); // 0~255 아니면 예외
                        }
                    }

                    return buf;
                }

                // (B) 연속 hex는 0x... 일 때만
                if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    string hex = s.Substring(2);
                    if (hex.Length % 2 != 0)
                    {
                        error = "Hex string length must be even (e.g., 0x0A, 0x1122).";
                        return null;
                    }

                    byte[] raw = new byte[hex.Length / 2];
                    for (int i = 0; i < raw.Length; i++)
                        raw[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);

                    return raw;
                }

                // (C) 단일 10진 바이트
                return new byte[] { byte.Parse(s) };
            }
        }
        catch (Exception ex)
        {
            error = "Parse failed: " + ex.Message;
            return null;
        }
    }


    //Save/Load시 귀찮아서 해당 클릭시 대신 써주도록 만든 메소드.
    private void SelectedSdoTextBoxWrite(SDOFlatObject SelectedSDO)
    {
        if (SelectedSDO == null)
        {
            WriteValueText = "";
            return;
        }

        //1010인 경우(save)
        if (SelectedSDO.Index == 0x1010)
        {
            //SubIndex0은 하부 수량을 나타내므로 적용 금지.
            if (SelectedSDO.SubIndex != 0)
            {
                WriteValueText = "0x65766173";
                return;
            }

        }
        //1011의 경우(load)
        if (SelectedSDO.Index == 0x1011)
        {
            //SubIndex0은 하부 수량을 나타내므로 적용 금지.
            if (SelectedSDO.SubIndex != 0)
            {
                WriteValueText = "0x64616F6C";
                return;
            }
        }

        WriteValueText = "";
        return;
    }


    private void UpdateSdoSelectionState()
    {
        // 기본값
        CanReadSelectedSdo = false;
        CanWriteSelectedSdo = false;

        if (_selectedSDO == null)
            return;

        // 그룹행(HasSubIndex=true)은 읽기/쓰기 금지로 설계되어 있음
        if (_selectedSDO.HasSubIndex)
            return;

        // 워커 준비 + 슬레이브 선택 유효성
        if (SdoWorker == null || !SdoWorker.IsRunning)
            return;

        if (Datamap.Instance.IsInit() == false)
            return;

        // 읽기는 leaf면 허용
        CanReadSelectedSdo = true;

        // Write 권한 체크
        if (IsWritableByAccess(_selectedSDO) == false) return;

        CanWriteSelectedSdo = true;
    }

    private static bool IsWritableByAccess(SDOFlatObject row)
    {
        if (row == null) return false;

        // Flags가 없을 수도 있으니 방어적으로
        string acc = null;
        if (row.Flags != null) acc = row.Flags.Access;
        if (string.IsNullOrWhiteSpace(acc)) return false;

        acc = acc.Trim().ToLowerInvariant();

        // ro는 무조건 금지
        if (acc.Contains("ro")) return false;

        // rw/wo 같은 케이스: w 포함이면 허용
        return acc.Contains("w");
    }


    public void UpdateNicList()
    {
        NICList.Clear();

        string firstset = string.Empty;
        string lastset = string.Empty;

        foreach (var (ifname, desc) in PcapIfEnumerator.GetAll())
        {
            //Console.WriteLine($"{ifname}  |  {desc}");
            _log.LogInformation($"{ifname}  |  {desc}");

            string buf = $"{desc} - {ifname}";
            NICList.Add(buf);

            if (firstset == string.Empty)
            {
                firstset = buf;
            }

            lastset = buf;
        }

        NICSelect = lastset;

        NicComboWidth = MeasureMaxWidth(NICList);
    }

    private static double MeasureMaxWidth(IEnumerable<string> items)
    {
        // ComboBox 기본 폰트에 최대한 맞추기
        var typeface = new Typeface(FontFamily.Default);
        const double fontSize = 15.0;

        double maxText = 0;

        foreach (var s in items)
        {
            if (string.IsNullOrWhiteSpace(s))
                continue;

            var ft = new FormattedText(
                s,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                Brushes.Black);

            if (ft.Width > maxText)
                maxText = ft.Width;
        }

        // “콤보박스 크롬” (좌우 패딩 + 드롭다운 버튼 영역) 여유분 테마/스타일에 따라 달라서 보통 48~64px
        const double chrome = 56;

        var width = Math.Ceiling(maxText + chrome);

        // 너무 작아지는 것 방지
        if (width < 160) width = 160;
        // 너무 커지는 것 방지
        //if (width > 700) width = 700;

        return width;
    }


    private void HandleNIC()
    {

        string ifname = NICSelect.Substring(NICSelect.LastIndexOf(" - ") + (" - ".Length));

        ECClient.Open(ifname);

        int slaveCount = SlaveCountUI = ECClient.SlaveCount;

        SlaveInfoData.Clear();
        SlavesListUI.Clear();

        for (int i = 0; i <= slaveCount; i++)
        {
            if (i == 0) //0은 마스터로 사용.
            {
                SlaveInfoData.Add(new SoemSlaveInfo());
                SlavesListUI.Add($"{i} - Master - {ifname}");
            }
            else
            {
                if (ECClient.SlaveInfo(i, out SoemSlaveInfo info) != 0)
                {
                    SlaveInfoData.Add(info);
                    SlavesListUI.Add($"{i} - Slave - {info.name}, Alias = {info.alias}, StationAddress = 0x{info.configadr.ToString("X")}, VendorCode = 0x{info.vendor.ToString("X")}, ProductCode = 0x{info.product.ToString("X")}, Revision=0x{info.revision.ToString("X")}");
                    //Console.WriteLine($"{i} - Slave - {info.name}, Alias = {info.alias}, StationAddress = 0x{info.configadr.ToString("X")}, VendorCode = 0x{info.vendor.ToString("X")}, ProductCode = 0x{info.product.ToString("X")}, Revision=0x{info.revision.ToString("X")}");
                    _log.LogInformation($"{i} - Slave - {info.name}, Alias = {info.alias}, StationAddress = 0x{info.configadr.ToString("X")}, VendorCode = 0x{info.vendor.ToString("X")}, ProductCode = 0x{info.product.ToString("X")}, Revision=0x{info.revision.ToString("X")}");

                }
                else
                {
                    //Console.WriteLine($"#{i} -> failed to query");
                    _log.LogInformation($"#{i} -> failed to query");
                }
            }

        }

        Datamap.Instance.Init(SlaveInfoData);

        SdoWorker = new SDOSubWorker(ECClient, Datamap.Instance);
        SdoWorker.Start();

        StateMachine.AttachSdoWorker(SdoWorker);

        return;

        //성공시 랜카드 Nic 저장해서 ENI구성.

    }


    private void HandleTest()
    {
        //UI테스트용.
        List<SoemSlaveInfo> testInfoList = new List<SoemSlaveInfo>();

        //0번은 Master용. Dummy삽입.
        SoemSlaveInfo masterInfo = new SoemSlaveInfo();
        testInfoList.Add(masterInfo);

        //DM3E-556. dummy
        SoemSlaveInfo testInfo1 = new SoemSlaveInfo()
        {
            alias = 0,// Station Alias
            configadr = 0x1001,// Station Address
            vendor = 0x4321,// eep_man
            product = 0x8100,// eep_id
            revision = 0x1,// 리비전

            name = "DM3E-556"
        };

        testInfoList.Add(testInfo1);


        //DM3E-556. dummy
        SoemSlaveInfo testInfo2 = new SoemSlaveInfo()
        {
            alias = 0,// Station Alias
            configadr = 0x1002,// Station Address
            vendor = 0xfa00000,// eep_man
            product = 0x1002,// eep_id
            revision = 0x10001,// 리비전

            name = "Ezi-SERVO2 EtherCAT"
        };

        testInfoList.Add(testInfo2);

        for (int i = 0; i < testInfoList.Count; i++)
        {
            if (i == 0) //0은 마스터로 사용.
            {
                SlaveInfoData.Add(new SoemSlaveInfo());

                SlavesListUI.Add($"{i} - Master - {"UI Test IF"}");
            }
            else
            {
                //Dummy삽입.
                SoemSlaveInfo info = testInfoList[i];

                SlaveInfoData.Add(info);
                SlavesListUI.Add(
                    $"{i} - Slave - {info.name}, Alias = {info.alias}, StationAddress = 0x{info.configadr.ToString("X")}, VendorCode = 0x{info.vendor.ToString("X")}, ProductCode = 0x{info.product.ToString("X")}, Revision=0x{info.revision.ToString("X")}");
                //Console.WriteLine($"{i} - Slave - {info.name}, Alias = {info.alias}, StationAddress = 0x{info.configadr.ToString("X")}, VendorCode = 0x{info.vendor.ToString("X")}, ProductCode = 0x{info.product.ToString("X")}, Revision=0x{info.revision.ToString("X")}");
                _log.LogInformation(
                    $"{i} - Slave - {info.name}, Alias = {info.alias}, StationAddress = 0x{info.configadr.ToString("X")}, VendorCode = 0x{info.vendor.ToString("X")}, ProductCode = 0x{info.product.ToString("X")}, Revision=0x{info.revision.ToString("X")}");

            }
        }

        Datamap.Instance.Init(testInfoList);


        //SdoWorker = new SDOSubWorker(ECClient, Datamap.Instance);
        //SdoWorker.Start();


        _log.LogInformation($"Log Test.");
        return;

    }

    public void HandleReadAllSDO()
    {
        //해당 Slave의 SDO 오브젝트들을 모두 읽어서 SlaveStore에 기록.

        //SelectedSlave --> SlaveID 부터 시작.

        SlaveStore slave = Datamap.Instance.GetSlave(SelectedSlave);

        if (slave == null)
            return;

        IReadOnlyList<SDOKey> slavekeys = slave.SdoStore.GetAllSDOKeyList();

        foreach (var key in slavekeys)
            SdoWorker.EnqueueRead(key.SlaveNo, key.Index, key.SubIndex);
    }

    private void UpdatePdoStatsUi()
    {
        if (StateMachine == null)
        {
            PdoIsRunning = false;
            return;
        }

        PdoRtStats stats;

        if (StateMachine.TryGetPdoStats(out stats) == false)
        {
            PdoIsRunning = false;
            return;
        }

        PdoIsRunning = StateMachine.IsPdoRunning;
        PdoLoopCount = stats.LoopCount;

        PdoLastPeriodUs = (long)stats.LastDtUs;
        PdoTargetPeriodUs = (long)StateMachine.PdoTargetPeriodUs;
        PdoMinPeriodUs = (long)stats.MinDtUs;
        PdoMaxPeriodUs = (long)stats.MaxDtUs;
        PdoAvgPeriodUs = (long)stats.AvgDtUs;

        PdoLastWkc = stats.LastReceiveRc;
        PdoExpectedWkc = 0;

        PdoWkcErrorCount = stats.ReceiveErrorCount;
        PdoOverrunCount = stats.LateCycleCount;
        PdoMaxWaitUs = (long)stats.MaxWaitUs;
        PdoMaxBodyUs = (long)stats.MaxBodyUs;
        PdoMaxTxSendUs = (long)stats.MaxTxSendUs;
        PdoMaxRecvUs = (long)stats.MaxRecvUs;
        PdoMaxPostUs = (long)stats.MaxPostUs;
        PdoMaxHousekeepingUs = (long)stats.MaxHousekeepingUs;

        PdoLastUpdateText = DateTime.Now.ToString("HH:mm:ss");

        AppendPdoStatsUiLogIfNeeded(stats);
    }

    private void AppendPdoStatsUiLogIfNeeded(PdoRtStats stats)
    {
        if (!IsPdoStatsUiLogEnabled)
        {
            return;
        }

        long nowTimestamp = Stopwatch.GetTimestamp();

        if (_lastPdoStatsUiLogTimestamp != 0)
        {
            double elapsedSeconds = (double)(nowTimestamp - _lastPdoStatsUiLogTimestamp) / (double)Stopwatch.Frequency;

            if (elapsedSeconds < PdoStatsLogIntervalSeconds)
            {
                return;
            }
        }

        _lastPdoStatsUiLogTimestamp = nowTimestamp;

        string message =
            "[PDO] " +
            "Loop=" + stats.LoopCount.ToString() +
            ", Dt(us)=" +
            ((long)stats.LastDtUs).ToString() + "/" +
            ((long)stats.MinDtUs).ToString() + "/" +
            ((long)stats.AvgDtUs).ToString() + "/" +
            ((long)stats.MaxDtUs).ToString() +
            ", Jitter(us)=" +
            ((long)stats.LastJitterUs).ToString() + "/" +
            ((long)stats.MinJitterUs).ToString() + "/" +
            ((long)stats.AvgAbsJitterUs).ToString() + "/" +
            ((long)stats.MaxJitterUs).ToString() +
            ", RecvWKC=" + stats.LastReceiveRc.ToString() +
            ", RecvErr=" + stats.ReceiveErrorCount.ToString() +
            ", SendRc=" + stats.LastSendRc.ToString() +
            ", SendErr=" + stats.SendErrorCount.ToString() +
            ", Late=" + stats.LateCycleCount.ToString() +
            ", MaxPhase(us)=Wait:" + ((long)stats.MaxWaitUs).ToString() +
            "/Body:" + ((long)stats.MaxBodyUs).ToString() +
            "/TxSend:" + ((long)stats.MaxTxSendUs).ToString() +
            "/Recv:" + ((long)stats.MaxRecvUs).ToString() +
            "/Post:" + ((long)stats.MaxPostUs).ToString() +
            "/House:" + ((long)stats.MaxHousekeepingUs).ToString();

        _log.LogInformation(message);
    }

    //MAP은 PDO의 주소를 받아서 Datamap에서 설명이랑 관련 내용 다 가져오는 방식으로 적용.
    public sealed class PdoMapUiRow
    {
        private readonly PdoMapRow _mapRow;
        private readonly SDOFlatObject _sdoRow;

        public PdoMapUiRow(PdoMapRow mapRow, SDOFlatObject sdoRow)
        {
            _mapRow = mapRow;
            _sdoRow = sdoRow;
        }

        public int No
        {
            get { return _mapRow.No; }
        }

        public string AddressText
        {
            get { return _mapRow.AddressText; }
        }

        public string Name
        {
            get
            {
                if (_sdoRow == null)
                {
                    return "";
                }

                return _sdoRow.DisplayName;
            }
        }

        public string DataType
        {
            get
            {
                if (_sdoRow == null)
                {
                    return "";
                }

                return _sdoRow.DataType;
            }
        }

        public ushort SdoBitSize
        {
            get
            {
                if (_sdoRow == null)
                {
                    return 0;
                }

                return _sdoRow.BitSize;
            }
        }

        public byte BitLength
        {
            get { return _mapRow.BitLength; }
        }

        public int BitOffset
        {
            get { return _mapRow.BitOffset; }
        }

        public int ByteOffset
        {
            get { return _mapRow.ByteOffset; }
        }

        public int BitInByte
        {
            get { return _mapRow.BitInByte; }
        }

        public string RawText
        {
            get { return _mapRow.RawText; }
        }
    }

    private SDOFlatObject FindSdoRowForCurrentSlave(ushort index, byte subIndex)
    {
        SlaveStore store = SelectedSlaveData;

        if (store == null)
        {
            return null;
        }

        if (store.SdoStore == null)
        {
            return null;
        }

        if (store.SdoStore.Rows == null)
        {
            return null;
        }

        for (int i = 0; i < store.SdoStore.Rows.Count; i++)
        {
            SDOFlatObject row = store.SdoStore.Rows[i];

            if (row == null)
            {
                continue;
            }

            if (row.Index == index && row.SubIndex == subIndex)
            {
                return row;
            }
        }

        return null;
    }

}