using SOEM_FrontEnd.DataMap;
using SOEM_FrontEnd.Model;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace SOEM_FrontEnd.Ethercat
{
    /// <summary>
    /// 단일 SDO 전용 스레드에서만 SDO를 읽고, SDOStore(Map)에 기록한다.
    ///
    /// 옵션(1): 중복 Enqueue 코얼레싱
    /// - 같은 SDOKey가 이미 큐/대기 중이면, 새 작업을 넣지 않고 대기자만 합친다.
    /// - 실제 SOEM SDO 호출은 1회만 수행하고, 결과를 대기자 모두에게 전달한다.
    /// </summary>
    public sealed class SDOSubWorker : IDisposable
    {
        private readonly EcClient _ec;
        //private readonly SDOStore _store;
        private readonly Datamap _dataMap;

        private readonly BlockingCollection<SDOKey> _queue;
        private readonly CancellationTokenSource _cts;

        // pending: key -> workitem (대기자 합치기용)
        private readonly object _pendingLock = new object();
        private readonly Dictionary<SDOKey, WorkItem> _pending = new Dictionary<SDOKey, WorkItem>();

        private Thread _thread;
        private volatile bool _running;
        private bool _disposed;

        private sealed class WorkItem
        {
            public SDOKey Key;
            public int MaxLen; // 합쳐진 요청 중 최대
            public List<TaskCompletionSource<bool>> Waiters; // null 가능 (fire-and-forget만 들어온 경우)
        }

        public SDOSubWorker(EcClient ec, Datamap datamap, int boundedCapacity = 4096)
        {
            if (ec == null) throw new ArgumentNullException(nameof(ec));
            if (datamap == null) throw new ArgumentNullException(nameof(datamap));
            if (boundedCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(boundedCapacity));

            _ec = ec;
            _dataMap = datamap;

            _queue = new BlockingCollection<SDOKey>(new ConcurrentQueue<SDOKey>(), boundedCapacity);
            _cts = new CancellationTokenSource();
        }

        public bool IsRunning
        {
            get { return _running; }
        }

        public void Start()
        {
            ThrowIfDisposed();

            if (_thread != null && _thread.IsAlive)
                return;

            _running = true;
            _thread = new Thread(ThreadMain);
            _thread.IsBackground = true;
            _thread.Name = "SOEM-SDO-SubWorker";
            _thread.Start();
        }

        public void Stop()
        {
            if (!_running) return;

            _running = false;

            try { _cts.Cancel(); } catch { }
            try { _queue.CompleteAdding(); } catch { }

            try
            {
                if (_thread != null && _thread.IsAlive)
                    _thread.Join();
            }
            catch { }

            // 남아있는 pending waiters는 실패로 정리
            DrainPendingOnStop();
        }

        /// <summary>
        /// 코얼레싱 Enqueue (async)
        /// </summary>
        public Task<bool> EnqueueReadAsync(int slaveNo, ushort index, byte subIndex, int maxLen = 64)
        {
            ThrowIfDisposed();
            if (!_running) throw new InvalidOperationException("SDOSubWorker is not running. Call Start() first.");
            if (maxLen <= 0) throw new ArgumentOutOfRangeException(nameof(maxLen));

            var key = new SDOKey(slaveNo, index, subIndex);
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            bool needEnqueue = false;

            lock (_pendingLock)
            {
                WorkItem wi;
                if (_pending.TryGetValue(key, out wi))
                {
                    // 이미 대기 중: 대기자만 합치고 maxLen 승격
                    if (maxLen > wi.MaxLen) wi.MaxLen = maxLen;

                    if (wi.Waiters == null) wi.Waiters = new List<TaskCompletionSource<bool>>();
                    wi.Waiters.Add(tcs);
                }
                else
                {
                    // 신규: pending 등록 후 큐에 key 1번만 넣는다
                    wi = new WorkItem
                    {
                        Key = key,
                        MaxLen = maxLen,
                        Waiters = new List<TaskCompletionSource<bool>> { tcs }
                    };
                    _pending.Add(key, wi);
                    needEnqueue = true;
                }
            }

            if (needEnqueue)
            {
                _queue.Add(key); // 여기서만 실제 큐 증가
            }

            return tcs.Task;
        }

        /// <summary>
        /// 코얼레싱 Enqueue (fire-and-forget)
        /// </summary>
        public void EnqueueRead(int slaveNo, ushort index, byte subIndex, int maxLen = 64)
        {
            ThrowIfDisposed();
            if (!_running) throw new InvalidOperationException("SDOSubWorker is not running. Call Start() first.");
            if (maxLen <= 0) throw new ArgumentOutOfRangeException(nameof(maxLen));

            var key = new SDOKey(slaveNo, index, subIndex);

            bool needEnqueue = false;

            lock (_pendingLock)
            {
                WorkItem wi;
                if (_pending.TryGetValue(key, out wi))
                {
                    // 이미 대기 중: maxLen만 승격(대기자 없음)
                    if (maxLen > wi.MaxLen) wi.MaxLen = maxLen;
                }
                else
                {
                    wi = new WorkItem
                    {
                        Key = key,
                        MaxLen = maxLen,
                        Waiters = null
                    };
                    _pending.Add(key, wi);
                    needEnqueue = true;
                }
            }

            if (needEnqueue)
            {
                _queue.Add(key);
            }
        }

        private void ThreadMain()
        {
            try
            {
                foreach (var key in _queue.GetConsumingEnumerable(_cts.Token))
                {
                    WorkItem wi = null;

                    lock (_pendingLock)
                    {
                        // 큐에서 키를 받았으면 pending에서 제거 후 처리(한 번만 실행)
                        if (_pending.TryGetValue(key, out wi))
                            _pending.Remove(key);
                    }

                    // Stop 직전 경합 등으로 wi가 null일 수 있음
                    if (wi == null)
                        continue;

                    bool ok = false;
                    try
                    {
                        ok = ExecuteReadAndWriteMap(wi.Key, wi.MaxLen);
                    }
                    catch (Exception ex)
                    {
                        SafeUpdateError(wi.Key, "SDO worker exception: " + ex.Message);
                        ok = false;
                    }

                    // 코얼레싱된 대기자 모두 완료 처리
                    if (wi.Waiters != null)
                    {
                        for (int i = 0; i < wi.Waiters.Count; i++)
                        {
                            wi.Waiters[i].TrySetResult(ok);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 정상 종료
            }
            catch (Exception ex)
            {
                // 워커 스레드 자체가 깨진 경우: pending 대기자 실패 처리
                FailAllPending("SDO worker fatal error: " + ex.Message);
            }
        }

        /// <summary>
        /// 실제 SDO read 수행 + Map(SDOStore)에 결과 기록
        /// </summary>
        private bool ExecuteReadAndWriteMap(SDOKey key, int maxLen)
        {
            byte[] buf = new byte[maxLen];
            uint len = (uint)maxLen;

            int rc = SOEMNative.soem_sdo_read((ushort)key.SlaveNo, key.Index, key.SubIndex, buf, ref len);

            if (rc == 0)
            {
                int actual = (int)len;
                if (actual < 0) actual = 0;
                if (actual > buf.Length) actual = buf.Length;

                if (actual != buf.Length)
                {
                    var trimmed = new byte[actual];
                    Buffer.BlockCopy(buf, 0, trimmed, 0, actual);

                    //
                    //_store.UpdateOk(key, trimmed);

                    _dataMap.GetSlave(key.SlaveNo).SdoStore.UpdateOk(key, trimmed);
                }
                else
                {
                    //_store.UpdateOk(key, buf);
                    _dataMap.GetSlave(key.SlaveNo).SdoStore.UpdateOk(key, buf);

                }

                return true;
            }

            string msg = BuildSoemErrorMessage(key, rc);
            SafeUpdateError(key, msg);
            return false;
        }

        private string BuildSoemErrorMessage(SDOKey key, int rc)
        {
            // 실패 직후 호출할수록 정확
            var info = _ec.GetLastErrorInfo();
            var elist = EcClient.GetSoemErrorString();

            if (info.HasValue)
            {
                var e = info.Value;

                if (!string.IsNullOrWhiteSpace(elist))
                {
                    return string.Format(
                        "SDO read failed. rc={0}, key={1} | LastErr=0x{2:X8} (Slave={3},Idx=0x{4:X4},Sub=0x{5:X2}) | {6}",
                        rc, key.ToString(),
                        e.ErrorCode, e.Slave, e.Index, e.SubIndex,
                        elist.Trim());
                }

                return string.Format(
                    "SDO read failed. rc={0}, key={1} | LastErr=0x{2:X8} (Slave={3},Idx=0x{4:X4},Sub=0x{5:X2})",
                    rc, key.ToString(),
                    e.ErrorCode, e.Slave, e.Index, e.SubIndex);
            }

            if (!string.IsNullOrWhiteSpace(elist))
            {
                return string.Format("SDO read failed. rc={0}, key={1} | {2}", rc, key.ToString(), elist.Trim());
            }

            return string.Format("SDO read failed. rc={0}, key={1}", rc, key.ToString());
        }

        private void SafeUpdateError(SDOKey key, string error)
        {

            //_store.UpdateError(key, error, abortCode: 0);
            _dataMap.GetSlave(key.SlaveNo).SdoStore.UpdateError(key, error, abortCode: 0);

        }

        private void DrainPendingOnStop()
        {
            FailAllPending("SDO worker stopped.");
        }

        private void FailAllPending(string message)
        {
            List<WorkItem> items;

            lock (_pendingLock)
            {
                items = new List<WorkItem>(_pending.Values);
                _pending.Clear();
            }

            for (int i = 0; i < items.Count; i++)
            {
                var wi = items[i];
                SafeUpdateError(wi.Key, message);

                if (wi.Waiters != null)
                {
                    for (int j = 0; j < wi.Waiters.Count; j++)
                        wi.Waiters[j].TrySetResult(false);
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SDOSubWorker));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Stop();

            try { _cts.Dispose(); } catch { }
            try { _queue.Dispose(); } catch { }
        }
    }
}
