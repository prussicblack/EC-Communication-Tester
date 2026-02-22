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
        private enum SdoOp
        {
            Read = 0,
            Write = 1
        }


        private sealed class SdoJob
        {
            public SdoOp Op;
            public SDOKey Key;

            // Read용 (기존 maxLen/size)
            public int MaxLen;

            // Write용 payload (little-endian raw bytes)
            public byte[] Data;

            // 필요하면 완료 통지용 (옵션)
            public List<TaskCompletionSource<bool>> Waiters;
        }


        private readonly EcClient _ec;
        //private readonly SDOStore _store;
        private readonly Datamap _dataMap;

        private readonly BlockingCollection<SdoJob> _queue;
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

            _queue = new BlockingCollection<SdoJob>(boundedCapacity);
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
                    if (maxLen > wi.MaxLen) 
                        wi.MaxLen = maxLen;

                    if (wi.Waiters == null) 
                        wi.Waiters = new List<TaskCompletionSource<bool>>();
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
                _queue.Add(new SdoJob   // 여기서만 실제 큐 증가
                {
                    Op = SdoOp.Read,
                    Key = key,
                    MaxLen = maxLen,     // 네 코드에 있는 길이 변수명으로
                    Data = null,
                    Waiters = null       // async 대기자 묶고 있으면 여기 넣기
                }); 
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
                _queue.Add(new SdoJob
                {
                    Op = SdoOp.Read,
                    Key = key,
                    MaxLen = maxLen,     // 네 코드에 있는 길이 변수명으로
                    Data = null,
                    Waiters = null       // async 대기자 묶고 있으면 여기 넣기
                });
            }
        }

        public void EnqueueWrite(int slaveNo, ushort index, byte subIndex, byte[] data)
        {
            ThrowIfDisposed();
            if (!_running) throw new InvalidOperationException("SDOSubWorker is not running. Call Start() first.");
            if (data == null) throw new ArgumentNullException(nameof(data));

            var key = new SDOKey(slaveNo, index, subIndex);

            // Write는 보통 coalesce 하지 않는게 안전(사용자 의도 순서 보존)
            _queue.Add(new SdoJob
            {
                Op = SdoOp.Write,
                Key = key,
                MaxLen = 0,
                Data = (byte[])data.Clone(),
                Waiters = null
            });
        }
        public Task<bool> EnqueueWriteAsync(int slaveNo, ushort index, byte subIndex, byte[] data)
        {
            ThrowIfDisposed();
            if (!_running) throw new InvalidOperationException("SDOSubWorker is not running. Call Start() first.");
            if (data == null) throw new ArgumentNullException(nameof(data));

            var key = new SDOKey(slaveNo, index, subIndex);
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var waiters = new List<TaskCompletionSource<bool>>();
            waiters.Add(tcs);

            _queue.Add(new SdoJob
            {
                Op = SdoOp.Write,
                Key = key,
                MaxLen = 0,
                Data = (byte[])data.Clone(),
                Waiters = waiters
            });

            return tcs.Task;
        }


        private void ThreadMain()
        {
            try
            {
                foreach (var job in _queue.GetConsumingEnumerable(_cts.Token))
                {
                    if (job == null)
                        continue;

                    bool ok = false;

                    if (job.Op == SdoOp.Read)
                    {
                        HandleReadJob(job);
                        continue;
                    }
                    
                    //Write는 Job.Waiter를 여기서 완료 처리.
                    ok = HandleWriteJob(job);

                    //코얼레싱된 대기자 모두 완료 처리
                    if (job.Waiters != null)
                    {
                        for (int i = 0; i < job.Waiters.Count; i++)
                            job.Waiters[i].TrySetResult(ok);
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

        private bool HandleReadJob(SdoJob job)
        {
            WorkItem wi = null;

            //여기서 pending에서 꺼내면서 Remove (성공/실패와 무관)
            lock (_pendingLock)
            {
                if (_pending.TryGetValue(job.Key, out wi))
                {
                    _pending.Remove(job.Key);
                }
            }

            // pending이 없으면: 중복 트리거나 stop race 등. 그냥 무시
            if (wi == null)
                return false;

            bool ok = false;

            try
            {
                // wi.MaxLen이 coalesce 결과(최대)라서 job.MaxLen보다 신뢰도가 높음
                ok = ExecuteReadMap(wi.Key, wi.MaxLen);
            }
            catch (Exception ex)
            {
                SafeUpdateError(wi.Key, "SDO read worker exception: " + ex.Message, true);
                ok = false;
            }

            if (wi.Waiters != null)
            {
                for (int i = 0; i < wi.Waiters.Count; i++)
                    wi.Waiters[i].TrySetResult(ok);
            }


            return ok;
        }


        private bool HandleWriteJob(SdoJob job)
        {
            if (job.Data == null)
                return false;

            return ExecuteWriteMap(job.Key, job.Data);
        }

        private bool ExecuteWriteMap(SDOKey key, byte[] data)
        {
            // 예: soem_sdo_write(slave, index, sub, buf, len) 같은 wrapper
            int rc = SOEMNative.soem_sdo_write((ushort)key.SlaveNo, key.Index, key.SubIndex, data, (uint)data.Length);

            if (rc == 0)
            {
                // 즉시 UI에 "쓴 값" 반영(빠른 피드백)
                _dataMap.GetSlave(key.SlaveNo).SdoStore.UpdateOk(key, data, false);

                // 원하면: write 성공 후 실제 반영값 확인을 위해 read를 추가로 enqueue
                //EnqueueReadInternal(key, data.Length, null);

                return true;
            }

            SafeUpdateError(key, "SDO write failed (rc=" + rc + ")", false);
            return false;
        }



        /// <summary>
        /// 실제 SDO read 수행 + Map(SDOStore)에 결과 기록
        /// </summary>
        private bool ExecuteReadMap(SDOKey key, int maxLen)
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

                    _dataMap.GetSlave(key.SlaveNo).SdoStore.UpdateOk(key, trimmed, true);
                }
                else
                {
                    //_store.UpdateOk(key, buf);
                    _dataMap.GetSlave(key.SlaveNo).SdoStore.UpdateOk(key, buf, true);

                }

                return true;
            }

            string msg = BuildSoemErrorMessage(key, rc);
            SafeUpdateError(key, msg, true);
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

        private void SafeUpdateError(SDOKey key, string error, bool isRead)
        {

            //_store.UpdateError(key, error, abortCode: 0);
            _dataMap.GetSlave(key.SlaveNo).SdoStore.UpdateError(key, error, abortCode: 0, isRead);

        }

        private void SafePendingUpdateError(SDOKey key, string error)
        {
            _dataMap.GetSlave(key.SlaveNo).SdoStore.UpdateError(key, error, abortCode: 0, true);
            _dataMap.GetSlave(key.SlaveNo).SdoStore.UpdateError(key, error, abortCode: 0, false);
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
                SafePendingUpdateError(wi.Key, message);

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
