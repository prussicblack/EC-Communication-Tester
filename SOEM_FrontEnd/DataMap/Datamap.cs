using Avalonia;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Serilog;
using SOEM_FrontEnd.Ethercat.ESI;
using SOEM_FrontEnd.Model;
using SOEM_FrontEnd.Util;
using SOEM_FrontEnd.Util.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using static SOEM_FrontEnd.Ethercat.ESI.ESIXMLData;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace SOEM_FrontEnd.DataMap
{
    public enum DeviceMode
    {
        None = 0,
        NormalIO = 1,
        NormalPPMode = 2,

        //되는대로 추가.
    }


    //SlaveInfo 저장용 클래스.
    public sealed class SlaveInfo
    {
        //추가필요.

        public int SlaveNo { get; private set; }
        public string Name { get; set; }

        public uint VendorId { get; set; }
        public uint ProductCode { get; set; }
        public uint RevisionNo { get; set; }
        public uint SerialNo { get; set; }

        public string StateText { get; set; }       // "OP" 등
        public ushort AlStatusCode { get; set; }
        public string AlStatusText { get; set; }

        public byte CoEDetails { get; set; }
        public byte FoEDetails { get; set; }
        public byte EoEDetails { get; set; }
        public byte SoEDetails { get; set; }
        public bool BlockLRW { get; set; }

        public int IBytes { get; set; }
        public int OBytes { get; set; }

        public ushort EbusCurrentmA { get; set; }
        public DateTime LastUpdatedUtc { get; set; }

        public SlaveInfo(int slaveNo)
        {
            SlaveNo = slaveNo;
            Name = "";
            StateText = "";
            AlStatusText = "";
            LastUpdatedUtc = DateTime.MinValue;
        }


        public string GetSlaveInfo()
        {
            return "Pending implementation";
        }

    }




    //---UI표기 및 Flat클래스.
    public sealed class SDOFlatObject : INotifyPropertyChanged
    {
        public int SlaveNo { get; set; }
        public ushort Index { get; set; }
        public byte SubIndex { get; set; }

        public string IndexName { get; set; } = "";
        public string SubName { get; set; } = "";    //SubIndex가 있다면.
        public string DataType { get; set; } = "";   // SubIndex가 있다면 SubIndex의 타입이 우선.
        public ushort BitSize { get; set; }          // SubIndex에 있으면 SubIndex의 크기가 우선

        public bool HasSubIndex { get; set; } //서브 인덱스가 있을경우. 없으면 False, true의 경우 UI에서 읽고 쓰기 금지.

        public Flags Flags { get; set; }

        // 런타임 값(=SDOStore overlay)
        private string _currentValueText = "";
        public string CurrentValueText
        {
            get { return _currentValueText; }
            set
            {
                if (_currentValueText == value) return;
                _currentValueText = value;
                OnPropertyChanged(nameof(CurrentValueText));
            }
        }

        private string _currentValueRawHexText = "";

        public string CurrentValueRawHexText
        {
            get { return _currentValueRawHexText; }
            set
            {
                if (_currentValueRawHexText == value) return;
                _currentValueRawHexText = value;
                OnPropertyChanged(nameof(CurrentValueRawHexText));
            }

        }

        private SDOReadStatus _status;
        public SDOReadStatus Status
        {
            get { return _status; }
            set
            {
                if (_status == value) return;
                _status = value;
                OnPropertyChanged(nameof(Status));
            }
        }

        private string _lastErrorText = "";
        public string LastErrorText
        {
            get { return _lastErrorText; }
            set
            {
                if (_lastErrorText == value) return;
                _lastErrorText = value;
                OnPropertyChanged(nameof(LastErrorText));
            }
        }


        // ===== UI 편의 파생 =====
        public bool CanReadWrite
        {
            get { return HasSubIndex == false; } // 그룹행은 금지
        }

        public double RowOpacity
        {
            get { return HasSubIndex ? 0.45 : 1.0; } // “회색”을 opacity로 처리
        }

        public Thickness NameMargin
        {
            get { return HasSubIndex ? new Thickness(0) : new Thickness(16, 0, 0, 0); }
        }

        public string AddressText
        {
            get { return "0x" + Index.ToString("X4") + ":0x" + SubIndex.ToString("X2"); }
        }

        public string DisplayName
        {
            get
            {
                // 그룹행: IndexName만
                if (HasSubIndex) return IndexName;

                // 실제행: IndexName / SubName (SubName 없으면 IndexName만)
                if (!string.IsNullOrWhiteSpace(SubName)) return IndexName + " / " + SubName;
                return IndexName;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler == null) return;
            handler(this, new PropertyChangedEventArgs(name));
        }


    }


    public sealed class FlatIndex
    {
        private readonly Dictionary<SDOKey, SDOFlatObject> _byKey = new Dictionary<SDOKey, SDOFlatObject>();

        public void Rebuild(IEnumerable<SDOFlatObject> rows)
        {
            _byKey.Clear();

            foreach (var r in rows)
            {
                if (r.HasSubIndex) continue; // 그룹행은 store 연동 제외

                var key = new SDOKey(r.SlaveNo, r.Index, r.SubIndex);
                _byKey[key] = r;
            }
        }

        public bool TryGet(SDOKey key, out SDOFlatObject row)
        {
            return _byKey.TryGetValue(key, out row);
        }
    }


    /*
    public sealed class SDOUIBinder
    {
        private readonly FlatIndex _flatIndex;

        public SDOUIBinder(FlatIndex flatIndex)
        {
            _flatIndex = flatIndex;
        }

        //SDOPoint업데이트시 얘도 같이 업데이트 해야됨.
        public void OnPointUpdated(SDOKey key, SDOPoint p)
        {
            SDOFlatObject row;
            if (!_flatIndex.TryGet(key, out row)) return;

            // UI 갱신은 반드시 UIThread에서
            Dispatcher.UIThread.Post(() =>
            {
                row.Status = p.Status;
                row.LastErrorText = p.Error ?? "";

                if (p.Status == SDOReadStatus.Ok)
                {
                    row.CurrentValueText = FormatValueText(p, row);
                }
                else if (p.Status == SDOReadStatus.Abort)
                {
                    row.CurrentValueText = "ABORT 0x" + p.AbortCode.ToString("X8");
                }
                else if (p.Status == SDOReadStatus.Timeout)
                {
                    row.CurrentValueText = "TIMEOUT";
                }
                else if (p.Status == SDOReadStatus.Error)
                {
                    row.CurrentValueText = "ERROR: " + (p.Error ?? "");
                }
                else
                {
                    row.CurrentValueText = "";
                }
            });
        }

        private static string FormatValueText(SDOPoint p, SDOFlatObject row)
        {
            var raw = p.LastRaw;
            if (raw == null || raw.Length == 0) return "";

            // 타입 우선순위: “행 정의(DataType)”이 있으면 그걸 우선, 없으면 p.DataType
            string dt = row.DataType;
            if (string.IsNullOrWhiteSpace(dt)) dt = p.DataType;

            // EtherCAT CoE SDO는 리틀엔디안이 일반적
            // 여기서는 최소한의 실무 타입만 처리하고, 나머지는 HEX로 표시
            dt = (dt ?? "").Trim().ToUpperInvariant();

            try
            {
                if (dt == "BOOLEAN" || dt == "BOOL")
                    return (raw[0] != 0) ? "TRUE" : "FALSE";

                if (dt == "INT8" || dt == "INTEGER8" || dt == "SINT8")
                    return unchecked((sbyte)raw[0]).ToString();

                if (dt == "UINT8" || dt == "UNSIGNED8" || dt == "USINT8")
                    return raw[0].ToString();

                if (dt == "INT16" || dt == "INTEGER16" || dt == "SINT16")
                {
                    if (raw.Length < 2) return BytesToHex(raw);
                    short v = BitConverter.ToInt16(raw, 0);
                    return v.ToString();
                }

                if (dt == "UINT16" || dt == "UNSIGNED16" || dt == "USINT16")
                {
                    if (raw.Length < 2) return BytesToHex(raw);
                    ushort v = BitConverter.ToUInt16(raw, 0);
                    return v.ToString();
                }

                if (dt == "INT32" || dt == "INTEGER32" || dt == "SINT32")
                {
                    if (raw.Length < 4) return BytesToHex(raw);
                    int v = BitConverter.ToInt32(raw, 0);
                    return v.ToString();
                }

                if (dt == "UINT32" || dt == "UNSIGNED32")
                {
                    if (raw.Length < 4) return BytesToHex(raw);
                    uint v = BitConverter.ToUInt32(raw, 0);
                    return v.ToString();
                }

                // 문자열/가변은 장치마다 다르니 우선 HEX
                return BytesToHex(raw);
            }
            catch
            {
                return BytesToHex(raw);
            }
        }

        private static string BytesToHex(byte[] raw)
        {
            // 예: "01 02 0A"
            return BitConverter.ToString(raw).Replace("-", " ");
        }
    }
    */
    //여기가 끝.


    public struct SDOKey : IEquatable<SDOKey>
    {
        public int SlaveNo { get; }
        public ushort Index { get; }
        public byte SubIndex { get; }

        public SDOKey(int slaveNo, ushort index, byte subIndex)
        {
            SlaveNo = slaveNo;
            Index = index;
            SubIndex = subIndex;
        }

        public bool Equals(SDOKey other)
        {
            return SlaveNo == other.SlaveNo && Index == other.Index && SubIndex == other.SubIndex;
        }

        public override bool Equals(object obj)
        {
            if (!(obj is SDOKey)) return false;
            return Equals((SDOKey)obj);
        }

        public override int GetHashCode()
        {
            return SlaveNo ^ (Index << 8) ^ SubIndex;
        }
    }

    public enum SDOReadStatus
    {
        None,
        Ok,
        Abort,
        Timeout,
        Error
    }

    public sealed class SDOPoint
    {
        public byte[] LastRaw;

        public string DataType;

        public DateTime LastUpdateUtc;
        public long Seq;

        public SDOReadStatus Status;
        public uint AbortCode;

        public string Error;
    }

    public sealed class SDOStore
    {
        private readonly object _lock = new object();
        private readonly Dictionary<SDOKey, SDOPoint> _dic = new Dictionary<SDOKey, SDOPoint>();
        private readonly Dictionary<SDOKey, SDOFlatObject> _leafRowByKey = new Dictionary<SDOKey, SDOFlatObject>();

        private List<SDOKey> _cachedKeys;

        private long _seq;

        // DataGrid ItemsSource로 사용
        public ObservableCollection<SDOFlatObject> Rows { get; } = new ObservableCollection<SDOFlatObject>();

        // (선택) 업데이트를 UI단에서 처리하고 싶으면 이벤트로 넘김
        public event Action<SDOKey, SDOPoint, SDOFlatObject> PointUpdated;

        private readonly ILogger _log;


        public SDOStore(ESIDevice Device, int Slaveno) 
        {
            _log = OPLogger.CreateLogger("SOEM_FrontEnd");

            if (Device == null) throw new ArgumentNullException(nameof(Device));

            // 1) datatype name -> ESIDataType
            var dtByName = new Dictionary<string, ESIDataType>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in Device.Datatypes)
            {
                var dt = key.Value;
                if (dt == null) continue;
                if (string.IsNullOrWhiteSpace(dt.Name)) continue;

                if (!dtByName.ContainsKey(dt.Name))
                    dtByName.Add(dt.Name, dt);
            }

            // 2) SDO objects
            foreach (var kv in Device.SDOObjects)
            {
                var obj = kv.Value;
                if (obj == null) continue;

                ESIDataType dt;
                if (!dtByName.TryGetValue(obj.DataType, out dt))
                {
                    // 사용자 정책: datatype 없으면 ESI/파서 문제로 봄 (fail-fast)
                    throw new InvalidOperationException("Datatype not found: " + obj.DataType + " @ Index 0x" + obj.Index.ToString("X4"));
                }

                // scalar => sub0 leaf 1개
                if (dt.SubType == null || dt.SubType.Count == 0)
                {
                    ushort bitSize = dt.BitSize;
                    if (bitSize == 0) bitSize = obj.BitSize;

                    AddLeaf(Slaveno, obj.Index, 0, indexName: obj.Name, subName: "", dataType: obj.DataType, bitSize: bitSize, flags: obj.Flags);
                    continue;
                }

                // record/array => 그룹행 1개 + leaf N개
                AddGroupRow(Slaveno, obj.Index,
                    indexName: obj.Name,
                    dataType: obj.DataType,
                    bitSize: obj.BitSize,
                    flags: obj.Flags);

                for (int i = 0; i < dt.SubType.Count; i++)
                {
                    var sub = dt.SubType[i];

                    string subType = sub.Type;
                    if (string.IsNullOrWhiteSpace(subType))
                        subType = obj.DataType;

                    string subName = sub.Name;
                    if (string.IsNullOrWhiteSpace(subName))
                        subName = "SubIndex " + sub.SubIndex.ToString("D3");

                    Flags mergedFlags = MergeFlags(obj.Flags, sub.Flag);

                    AddLeaf(Slaveno, 
                        obj.Index, 
                        sub.SubIndex, 
                        indexName: obj.Name,
                        subName: subName,
                        dataType: subType,
                        bitSize: sub.BitSize,
                        flags: mergedFlags);
                }
            }


        }

        private void AddGroupRow(int slaveno, ushort index, string indexName, string dataType, ushort bitSize, Flags flags)
        {
            var row = new SDOFlatObject
            {
                SlaveNo = slaveno,
                Index = index,
                SubIndex = 0,
                HasSubIndex = true,      // 그룹행
                IndexName = indexName,
                SubName = "",
                DataType = dataType,
                BitSize = bitSize,
                Flags = flags
            };

            Rows.Add(row);
        }

        private void AddLeaf(int slaveno, ushort index, byte subIndex, string indexName, string subName, string dataType, ushort bitSize, Flags flags)
        {
            SDOKey key = new SDOKey(slaveno, index, subIndex);

            // 1) Point 등록(런타임)
            SDOPoint p = new SDOPoint
            {
                DataType = dataType,
                Status = SDOReadStatus.None,
                LastUpdateUtc = DateTime.MinValue,
                Seq = 0,
                AbortCode = 0,
                Error = null,
                LastRaw = null
            };

            // 중복 방지: 인덱서로 덮어쓰기(원하면 Add로 바꿔 fail-fast 가능)
            _dic[key] = p;

            // 2) Row 등록(UI)
            SDOFlatObject row = new SDOFlatObject
            {
                SlaveNo = slaveno,
                Index = index,
                SubIndex = subIndex,
                HasSubIndex = false,     // leaf
                IndexName = indexName,
                SubName = subName,
                DataType = dataType,
                BitSize = bitSize,
                Flags = flags
            };

            Rows.Add(row);
            _leafRowByKey[key] = row;
        }

        private static Flags MergeFlags(Flags parent, Flags sub)
        {
            if (sub == null) return parent;
            if (parent == null) return sub;

            Flags flags = new Flags();

            // sub 쪽이 값이 있으면 sub 우선
            flags.Access = !string.IsNullOrWhiteSpace(sub.Access) ? sub.Access : parent.Access;
            flags.WriteRestrictions = !string.IsNullOrWhiteSpace(sub.WriteRestrictions) ? sub.WriteRestrictions : parent.WriteRestrictions;
            flags.Category = !string.IsNullOrWhiteSpace(sub.Category) ? sub.Category : parent.Category;
            flags.PDOMapping = !string.IsNullOrWhiteSpace(sub.PDOMapping) ? sub.PDOMapping : parent.PDOMapping;

            return flags;
        }

        public void UpdateOk(SDOKey key, byte[] raw)
        {
            if (raw == null) return;

            var copy = new byte[raw.Length];
            Buffer.BlockCopy(raw, 0, copy, 0, raw.Length);

            SDOPoint p;
            lock (_lock)
            {
                if (!_dic.TryGetValue(key, out p))
                {
                    // 예상 밖 키면 새로 만들되, DataType은 나중에 채워도 됨
                    p = new SDOPoint();
                    _dic[key] = p;
                }

                _seq++;
                p.Seq = _seq;
                p.LastUpdateUtc = DateTime.UtcNow;
                p.LastRaw = copy;
                p.Status = SDOReadStatus.Ok;
                p.AbortCode = 0;
                p.Error = null;
            }

            // Row 갱신은 UIThread에서 하는 게 안전하므로, 이벤트로 던짐
            SDOFlatObject row;
            _leafRowByKey.TryGetValue(key, out row);

            var handler = PointUpdated;
            if (handler != null) handler(key, p, row);


            // Row 갱신(leaf만)
            if (row != null)
            {
                ApplyPointToRowOnUIThread(p, row);
            }

        }

        public void UpdateError(SDOKey key, string error, uint abortCode)
        {
            SDOPoint p;
            lock (_lock)
            {
                if (!_dic.TryGetValue(key, out p))
                {
                    p = new SDOPoint();
                    _dic[key] = p;
                }

                _seq++;
                p.Seq = _seq;
                p.LastUpdateUtc = DateTime.UtcNow;
                p.LastRaw = null;
                p.Status = SDOReadStatus.Error; // abortCode가 실제 Abort code로 들어오면 Abort로 바꿔도 됨
                p.AbortCode = abortCode;
                p.Error = error;
            }

            SDOFlatObject row;
            _leafRowByKey.TryGetValue(key, out row);

            var handler = PointUpdated;
            if (handler != null) handler(key, p, row);

            if (row != null)
            {
                ApplyPointToRowOnUIThread(p, row);
            }
        }

        public IReadOnlyList<SDOKey> GetAllSDOKeyList()
        {
            //가져가서 재할당 방지로 IReadOnlyList사용.
            if (_cachedKeys != null)
                return _cachedKeys;

            List<SDOKey> keylist = new List<SDOKey>(_dic.Count);
            foreach (var key in _dic.Keys)
            {
                keylist.Add(key);
            }

            _cachedKeys = keylist;

            return _cachedKeys;
        }

        public SDOPoint TryGetPoint(SDOKey key)
        {
            lock (_lock)
            {
                SDOPoint p;
                if (_dic.TryGetValue(key, out p)) return p;
                return null;
            }
        }

        private void ApplyPointToRowOnUIThread(SDOPoint p, SDOFlatObject row)
        {
            // UI 갱신은 반드시 UIThread에서
            Dispatcher.UIThread.Post(() =>
            {
                row.Status = p.Status;
                row.LastErrorText = p.Error ?? "";

                if (p.Status == SDOReadStatus.Ok)
                {
                    //Raw Hex값
                    row.CurrentValueRawHexText = FormatValueText(p, row);

                    //타입에 따라 변환된값.
                    row.CurrentValueText = FormatValueTextByType(p, row);
                }
                else if (p.Status == SDOReadStatus.Abort)
                {
                    //에러값 Value 에 기록금지.
                    //row.CurrentValueText = "ABORT 0x" + p.AbortCode.ToString("X8");
                    //Console.WriteLine(row.CurrentValueRawHexText);

                    _log.LogInformation(row.CurrentValueRawHexText);
                }
                else if (p.Status == SDOReadStatus.Timeout)
                {
                    //에러값 Value 에 기록금지.
                    //row.CurrentValueText = "TIMEOUT";
                    //Console.WriteLine(row.CurrentValueRawHexText);
                    _log.LogInformation(row.CurrentValueRawHexText);

                }
                else if (p.Status == SDOReadStatus.Error)
                {
                    //에러값 Value 에 기록금지.
                    //row.CurrentValueText = "ERROR: " + (p.Error ?? "");
                    //Console.WriteLine(row.CurrentValueRawHexText);
                    _log.LogInformation(row.CurrentValueRawHexText);

                }
                else
                {
                    row.CurrentValueRawHexText = "";
                }
            });
        }

        private static string FormatValueTextByType(SDOPoint p, SDOFlatObject row)
        {
            var raw = p.LastRaw;
            if (raw == null || raw.Length == 0) return "";

            string dt = row.DataType;
            if (string.IsNullOrWhiteSpace(dt)) dt = p.DataType;

            dt = NormalizeDataType(dt);


            try
            {
                if (dt == "BOOLEAN")
                {
                    bool b = raw[0] != 0;
                    return b ? "True" : "False";
                }

                if (dt == "INT8")
                {
                    sbyte v = unchecked((sbyte)raw[0]);
                    return v.ToString();
                }

                if (dt == "UINT8")
                {
                    byte v = raw[0];
                    return v.ToString();
                }

                if (dt == "INT16")
                {
                    if (raw.Length < 2) return BytesToHex(raw);
                    short v = ReadInt16LE(raw, 0);
                    return v.ToString(); // 필요하면 + HexSuffix16((ushort)v)
                }

                if (dt == "UINT16")
                {
                    if (raw.Length < 2) return BytesToHex(raw);
                    ushort v = ReadUInt16LE(raw, 0);
                    return v.ToString();
                }

                if (dt == "INT32")
                {
                    if (raw.Length < 4) return BytesToHex(raw);
                    int v = ReadInt32LE(raw, 0);
                    return v.ToString();
                }

                if (dt == "UINT32")
                {
                    if (raw.Length < 4) return BytesToHex(raw);
                    uint v = ReadUInt32LE(raw, 0);
                    return v.ToString();
                }

                if (dt == "INT64")
                {
                    if (raw.Length < 8) return BytesToHex(raw);
                    long v = ReadInt64LE(raw, 0);
                    return v.ToString();
                }

                if (dt == "UINT64")
                {
                    if (raw.Length < 8) return BytesToHex(raw);
                    ulong v = ReadUInt64LE(raw, 0);
                    return v.ToString();
                }

                if (dt == "REAL32")
                {
                    if (raw.Length < 4) return BytesToHex(raw);
                    float v = ReadSingleLE(raw, 0);
                    return v.ToString("G9");
                }

                if (dt == "REAL64")
                {
                    if (raw.Length < 8) return BytesToHex(raw);
                    double v = ReadDoubleLE(raw, 0);
                    return v.ToString("G17");
                }

                if (dt == "VISIBLE_STRING")
                {
                    int n = 0;
                    while (n < raw.Length && raw[n] != 0) n++;
                    return System.Text.Encoding.ASCII.GetString(raw, 0, n);
                }
            }
            catch
            {
                // fallthrough
            }

            return BytesToHex(raw);

        }

        private static string NormalizeDataType(string dt)
        {
            dt = (dt ?? "").Trim().ToUpperInvariant();

            // 흔한 alias 통일
            if (dt == "BOOL") return "BOOLEAN";

            if (dt == "SINT") return "INT8";
            if (dt == "USINT") return "UINT8";

            if (dt == "INT") return "INT16";
            if (dt == "UINT") return "UINT16";
            if (dt == "INTEGER16") return "INT16";
            if (dt == "UNSIGNED16") return "UINT16";

            if (dt == "DINT") return "INT32";
            if (dt == "UDINT") return "UINT32";
            if (dt == "INTEGER32") return "INT32";
            if (dt == "UNSIGNED32") return "UINT32";

            if (dt == "LINT") return "INT64";
            if (dt == "ULINT") return "UINT64";
            if (dt == "INTEGER64") return "INT64";
            if (dt == "UNSIGNED64") return "UINT64";

            if (dt == "REAL") return "REAL32";
            if (dt == "LREAL") return "REAL64";

            if (dt.Contains("STRING")) return "VISIBLE_STRING";

            //혹시 몰라서..
            if (dt == "USINT16") return "UINT16";
            if (dt == "SINT32") return "INT32";
            if (dt == "UDINT32") return "UINT32";

            return dt;
        }

        // 플랫폼 독립 little-endian reader들
        private static short ReadInt16LE(byte[] b, int o)
        {
            return unchecked((short)ReadUInt16LE(b, o));
        }
        private static ushort ReadUInt16LE(byte[] b, int o)
        {
            return (ushort)(b[o] | (b[o + 1] << 8));
        }

        private static int ReadInt32LE(byte[] b, int o)
        {
            return unchecked((int)ReadUInt32LE(b, o));
        }
        private static uint ReadUInt32LE(byte[] b, int o)
        {
            return (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
        }

        private static long ReadInt64LE(byte[] b, int o)
        {
            return unchecked((long)ReadUInt64LE(b, o));
        }
        private static ulong ReadUInt64LE(byte[] b, int o)
        {
            uint lo = ReadUInt32LE(b, o);
            uint hi = ReadUInt32LE(b, o + 4);
            return ((ulong)hi << 32) | lo;
        }

        private static float ReadSingleLE(byte[] b, int o)
        {
            // BitConverter는 엔디안 의존 → 필요시 swap
            if (BitConverter.IsLittleEndian)
                return BitConverter.ToSingle(b, o);

            var tmp = new byte[4];
            tmp[0] = b[o + 3]; tmp[1] = b[o + 2]; tmp[2] = b[o + 1]; tmp[3] = b[o + 0];
            return BitConverter.ToSingle(tmp, 0);
        }

        private static double ReadDoubleLE(byte[] b, int o)
        {
            if (BitConverter.IsLittleEndian)
                return BitConverter.ToDouble(b, o);

            var tmp = new byte[8];
            tmp[0] = b[o + 7]; tmp[1] = b[o + 6]; tmp[2] = b[o + 5]; tmp[3] = b[o + 4];
            tmp[4] = b[o + 3]; tmp[5] = b[o + 2]; tmp[6] = b[o + 1]; tmp[7] = b[o + 0];
            return BitConverter.ToDouble(tmp, 0);
        }



        private static string FormatValueText(SDOPoint p, SDOFlatObject row)
        {
            var raw = p.LastRaw;
            if (raw == null || raw.Length == 0) return "";

            string dt = row.DataType;
            if (string.IsNullOrWhiteSpace(dt)) dt = p.DataType;

            dt = (dt ?? "").Trim().ToUpperInvariant();


            return "0x"+BytesToHex(raw);
        }

        private static string BytesToHex(byte[] raw)
        {
            //정방향 MSB
            //if (raw == null || raw.Length == 0) return "";
            //var sb = new System.Text.StringBuilder(raw.Length * 2);
            //for (int i = 0; i < raw.Length; i++)
            //    sb.Append(raw[i].ToString("X2"));
            // return sb.ToString();

            //역방향 LSB
            if (raw == null || raw.Length == 0) return "";

            var sb = new System.Text.StringBuilder(raw.Length * 2);
            for (int i = raw.Length - 1; i >= 0; i--)
            {
                sb.Append(raw[i].ToString("X2"));
            }
            return sb.ToString();

        }


    }

    public sealed class DeviceESIInfo : INotifyPropertyChanged
    {
        private ESIDevice _ESIDeviceInfo;
        public ESIDevice? ESIDeviceInfo
        {
            get
            {
                return _ESIDeviceInfo;
            }

            set
            {
                if (_ESIDeviceInfo == value)
                    return;
                _ESIDeviceInfo = value;
                OnPropertyChanged(nameof(ESIDeviceInfo));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string name = null)
        {
            var h = PropertyChanged;
            if (h != null)
                h(this, new PropertyChangedEventArgs(name));
        }
    }




    public sealed class SlaveStore
    {
        public int SlaveNo { get; private set; }

        private readonly object _pdoLock = new object();
        private byte[] _lastPdoInputImage; // 최신 프레임

        private readonly SDOStore _sdo;

        private readonly DeviceESIInfo _deviceInfo;


        //나중에 SlaveInfo 처리하면서 같이 할것.
        private readonly SlaveInfo _SlaveInfo;

        //public string SlaveInfo
        //{
            //get
            //{
                //return _SlaveInfo.GetSlaveInfo();
            //}
        //}

        private object _BaseProfile;

        public object BaseProfile
        {
            get
            {
                return _BaseProfile;
            }
            set
            {

                if (_BaseProfile == value) 
                    return;
                _BaseProfile = value;
                //OnPropertyChanged(nameof(BaseProfile));
            }
        }

        public DeviceMode DeviceMode;

        public SDOStore SdoStore
        {
            get
            {
                return _sdo;
            }
        }

        // (권장) UI가 바로 쓰기 쉬운 Rows도 한 번 더 노출
        public ObservableCollection<SDOFlatObject> SdoRows
        {
            get
            {
                return _sdo.Rows;
            }
        }

        private readonly Microsoft.Extensions.Logging.ILogger _log;

        public SlaveStore(int slaveNo, SoemSlaveInfo SlaveInfo)
        {
            _log = OPLogger.CreateLogger("SOEM_FrontEnd");

            SlaveNo = slaveNo;

            //_deviceInfo 생성. 생성 전에 미리 ESI를 읽어둬야 함.
            ESIDevice? dev = ESICatalog.GetDeviceData(SlaveInfo.product, SlaveInfo.vendor, SlaveInfo.revision);
            if (dev == null)
            {
                if (_deviceInfo == null)
                {
                    //Console.WriteLine($"_deviceInfo is Null");
                    _log.LogInformation($"_deviceInfo is Null");

                    return;
                }
                _deviceInfo.ESIDeviceInfo = null;

                //Console.WriteLine($"{SlaveInfo.name} is ESI Nothing");
                _log.LogInformation($"{SlaveInfo.name} is ESI Nothing");

                return;
            }
            _deviceInfo = new DeviceESIInfo();

            _deviceInfo.ESIDeviceInfo = dev;

            _sdo = new SDOStore(dev, SlaveNo);
        }

        public void UpdateSdo(ushort index, byte sub, byte[] raw)
        {
            _sdo.UpdateOk(new SDOKey(SlaveNo, index, sub), raw);
        }

        public SDOPoint TryGetSdo(ushort index, byte sub)
        {
            return _sdo.TryGetPoint(new SDOKey(SlaveNo, index, sub));
        }

        //프로파일 reading용.
        public T GetProfile<T>() where T : class
        {
            return BaseProfile as T;
        }

        public bool TryGetProfile<T>(out T profile) where T : class
        {
            profile = BaseProfile as T;
            return profile != null;
        }

        //PDO(임시) 나중에 변경 및 제거.
        // 수집 스레드가 호출
        public void UpdatePdoFrame(byte[] inputImage)
        {
            if (inputImage == null) return;

            // 복사 여부는 정책에 따라: 여기서는 단순 복사
            var copy = new byte[inputImage.Length];

            Buffer.BlockCopy(inputImage, 0, copy, 0, inputImage.Length);

            lock (_pdoLock)
            {
                _lastPdoInputImage = copy;
            }
        }

        // UI/IPC가 호출: 특정 entry의 raw bytes만 복사해서 반환
        public byte[] TryReadPdoEntryBytes(int byteOffset, int byteLength)
        {
            lock (_pdoLock)
            {
                if (_lastPdoInputImage == null) return null;
                if (byteOffset < 0) return null;
                if (byteLength <= 0) return null;
                if (byteOffset + byteLength > _lastPdoInputImage.Length) return null;

                var buf = new byte[byteLength];
                Buffer.BlockCopy(_lastPdoInputImage, byteOffset, buf, 0, byteLength);
                return buf;
            }
        }
    }


    public class Datamap : Singleton<Datamap>
    {
        private SlaveStore[] _slaves;
        private bool _initialized;

        private readonly object _lock = new object();

        private readonly ILogger _log;

        public Datamap()
        {
            _log = OPLogger.CreateLogger("SOEM_FrontEnd");
        }


        public int SlaveCount
        {
            get 
            { 
                lock (_lock) 
                { 
                    return _slaves != null ? _slaves.Length : 0; 
                } 
            }
        }

        public bool IsInit()
        {
            return _initialized;
        }


        public void Init(List<SoemSlaveInfo> slaveInfos)
        {
            lock (_lock)
            {
                //재 이니셜 라이즈 가능하도록.
                if (_initialized)
                {
                    //throw new InvalidOperationException("Already initialized.");
                    //Console.WriteLine("Already initialized. ReInitializing"); //재 이니셜라이징 되었다고 콘솔로그만.
                    _log.LogInformation("Already initialized. ReInitializing");

                }

                _slaves = new SlaveStore[slaveInfos.Count];

                for (int i = 0; i < slaveInfos.Count; i++)
                {
                    if (i == 0)
                    {
                        _slaves[i] = null;
                    }
                    else
                    {
                        _slaves[i] = new SlaveStore(i, slaveInfos[i]);
                    }
                    //Slave는 1부터. 0은 전체라서 일단 없는걸로 처리.

                }

                _initialized = true;
            }
        }

        public SlaveStore GetSlave(int slaveNo)
        {
            lock (_lock)
            {
                if (!_initialized) 
                {
                    throw new InvalidOperationException("Not initialized."); //냅둬야되나?

                    //Console.WriteLine("Not initialized.");

                    _log.LogInformation("Not initialized.");
                }

                return _slaves[slaveNo];
            }
        }

    }






}
