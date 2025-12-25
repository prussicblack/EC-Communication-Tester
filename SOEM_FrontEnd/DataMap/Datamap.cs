using SOEM_FrontEnd.Model;
using SOEM_FrontEnd.Util;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using static SOEM_FrontEnd.Ethercat.ESI.ESIXMLData;

namespace SOEM_FrontEnd.DataMap
{
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
        private long _seq;

        public void UpdateOk(SDOKey key, byte[] raw)
        {
            if (raw == null) return;

            var copy = new byte[raw.Length];
            Buffer.BlockCopy(raw, 0, copy, 0, raw.Length);

            lock (_lock)
            {
                SDOPoint p;
                if (!_dic.TryGetValue(key, out p))
                {
                    p = new SDOPoint();
                    _dic.Add(key, p);
                }

                _seq++;
                p.Seq = _seq;
                p.LastUpdateUtc = DateTime.UtcNow;
                p.LastRaw = copy;
                p.Status = SDOReadStatus.Ok;
                p.AbortCode = 0;
                p.Error = null;
            }
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
        public ESIDevice ESIDeviceInfo
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


        public SlaveStore(int slaveNo)
        {
            SlaveNo = slaveNo;
        }

        private readonly SDOStore _sdo = new SDOStore();

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

        public void Init(List<SoemSlaveInfo> slavwInfos)
        {
            lock (_lock)
            {
                if (_initialized)
                {
                    throw new InvalidOperationException("Already initialized.");
                }

                _slaves = new SlaveStore[slavwInfos.Count];

                for (int i = 0; i < slavwInfos.Count; i++)
                {
                    _slaves[i] = new SlaveStore(i);

                    //ESI에서 긁어와야됨.
                    //데이터 구조 정리해서 메모해놓을것.



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
