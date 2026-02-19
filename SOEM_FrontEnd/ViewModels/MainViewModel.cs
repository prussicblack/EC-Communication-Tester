using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using SOEM_FrontEnd.DataMap;
using SOEM_FrontEnd.Ethercat;
using SOEM_FrontEnd.Ethercat.ESI;
using SOEM_FrontEnd.Model;
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
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using SOEM_FrontEnd.Automation;
using static System.Net.Mime.MediaTypeNames;
using SOEM_FrontEnd.Util;
using SOEM_FrontEnd.Util.Logging;
using SOEM_FrontEnd.Util.Logging.UI;

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

    public ObservableCollection<string> SlavesListUI { get;} = new();

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



    public ObservableCollection<string> LogLines { get; } = new ObservableCollection<string>();

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
        }
    }


    //Slave데이터를 보여주기 위한 프로퍼티들.
    //SlaveStore 에 대한 프로퍼티 노출.
    //참조 변경으로 갈아끼우는 방식임.
    public SlaveStore SelectedSlaveData
    {
        get
        {
            if(Datamap.Instance.IsInit() == true)
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
            //OnPropertyChanged(nameof(SdoRows));                     // (선택) 별도 프로퍼티 쓰면

            OnPropertyChanged(nameof(IsMasterSelected));
            OnPropertyChanged(nameof(IsSlaveSelected));

        }
    }

    public bool IsMasterSelected => SelectedSlave == 0;
    public bool IsSlaveSelected => SelectedSlave != 0;


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

    public SDOSubWorker SdoWorker { get; private set; }

    public ICommand CMD_MoveToInit { get; private set; }
    public ICommand CMD_MoveToPreOp { get; private set; }
    public ICommand CMD_MoveToSafeOp { get; private set; }
    public ICommand CMD_MoveToOp { get; private set; }

    
    //UI단에 표기되는 로그.
    public ObservableCollection<string> UiLogs { get; } = new();
    private readonly AvaloniaUiLogSink _sink;

    //로그 베이스.
    private readonly ILogger<StateMachine> _log;


    public MainViewModel()
    {

        ECClient = new EcClient();

        StateMachine = new StateMachine(ECClient);

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

        //UI로그 연결을 위한 코드.
        _sink = new AvaloniaUiLogSink(line =>
        {
            // AvaloniaUiLogSink가 UI thread로 flush하니까 여기선 Add만
            UiLogs.Add(line);

            // (선택) 너무 길어지면 오래된 것 삭제
            const int max = 3000;
            if (UiLogs.Count > max)
                UiLogs.RemoveAt(0);
        });

        OPLogger.SetUiSink(_sink);

        //로그 초기화
        _log = OPLogger.CreateLogger<StateMachine>();
        //로그 기록
        _log.LogInformation("MainViewModel Created");
    }

    public void Dispose()
    {
        // 창 닫힐 때 sink 해제/정리
        OPLogger.SetUiSink(null);
        _sink.Dispose();
    }



    private void HandleMoveToOp()
    {
        StateMachine.MoveToOperate();
    }

    private void HandleMoveToSafeOp()
    {
        StateMachine.MoveToSafeOP();

    }

    private void HandleMoveToPreOp()
    {
        StateMachine.MoveToPreOP();
    }

    private void HandleMoveToInit()
    {
        StateMachine.MoveToInit();

    }

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
            LogLines.Add("[SDO][WRITE] " + err);
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
        // ComboBox 기본 폰트에 최대한 맞추기 (원하면 VM에 FontSize/FontFamily를 넘겨서 더 정확히)
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

        // “콤보박스 크롬” (좌우 패딩 + 드롭다운 버튼 영역) 여유분
        // 테마/스타일에 따라 달라서 보통 48~64px에서 맞춥니다.
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
                    Console.WriteLine($"{i} - Slave - {info.name}, Alias = {info.alias}, StationAddress = 0x{info.configadr.ToString("X")}, VendorCode = 0x{info.vendor.ToString("X")}, ProductCode = 0x{info.product.ToString("X")}, Revision=0x{info.revision.ToString("X")}");
                }
                else
                {
                    Console.WriteLine($"#{i} -> failed to query");
                }
            }

        }

        Datamap.Instance.Init(SlaveInfoData);

        SdoWorker = new SDOSubWorker(ECClient, Datamap.Instance);
        SdoWorker.Start();

        return;

        //성공시 랜카드 Nic 저장.

    }


    private void HandleTest()
    {

        return;

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

    public void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        LogLines.Add(line);

        // 너무 많아지면 앞에서 잘라내기 (예: 2000줄 유지)
        if (LogLines.Count > 2000)
            LogLines.RemoveAt(0);
    }
}
