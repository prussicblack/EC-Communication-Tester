using Avalonia;
using Avalonia.Threading;
using SOEM_FrontEnd.Ethercat.ESI;
using SOEM_FrontEnd.Model;
using SOEM_FrontEnd.Util;
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

namespace SOEM_FrontEnd.DataMap
{
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

        private long _seq;

        // DataGrid ItemsSource로 사용
        public ObservableCollection<SDOFlatObject> Rows { get; } = new ObservableCollection<SDOFlatObject>();

        // (선택) 업데이트를 UI단에서 처리하고 싶으면 이벤트로 넘김
        public event Action<SDOKey, SDOPoint, SDOFlatObject> PointUpdated;


        public SDOStore(ESIDevice Device, int Slaveno) 
        {
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

                    AddLeaf(Slaveno, obj.Index, sub.SubIndex,
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




    }

    public sealed class DeviceInfo : INotifyPropertyChanged
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

        private readonly DeviceInfo _deviceInfo;

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

        public SlaveStore(int slaveNo, SoemSlaveInfo SlaveInfo)
        {
            SlaveNo = slaveNo;
            //_deviceInfo 생성. 생성 전에 미리 ESI를 읽어둬야 함.
            ESIDevice? dev = ESICatalog.GetDeviceData(SlaveInfo.product, SlaveInfo.vendor, SlaveInfo.revision);
            if (dev == null)
            {
                _deviceInfo.ESIDeviceInfo = null;

                Console.WriteLine($"{SlaveInfo.name} is ESI Nothing");
                return;
            }
            _deviceInfo = new DeviceInfo();

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

        public void Init(List<SoemSlaveInfo> slaveInfos)
        {
            lock (_lock)
            {
                if (_initialized)
                {
                    throw new InvalidOperationException("Already initialized.");
                }

                _slaves = new SlaveStore[slaveInfos.Count];

                for (int i = 0; i < slaveInfos.Count; i++)
                {
                    if (i == 0)
                    {
                        _slaves[i] = null;
                        
                    }

                    _slaves[i] = new SlaveStore(i, slaveInfos[i]);
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
                    throw new InvalidOperationException("Not initialized."); 
                }

                return _slaves[slaveNo];
            }
        }

    }






}
