using SOEM_FrontEnd.DataMap;
using SOEM_FrontEnd.Model;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace SOEM_FrontEnd.Ethercat
{
    /// <summary>
    /// 단일 SDO 전용 스레드에서만 SDO를 읽고, SDOStore(Map)에 기록한다.
    /// - 외부(요청자)는 EnqueueRead로 요청만 넣는다.
    /// - SOEM SDO 호출은 절대 다른 스레드에서 실행되지 않는다.
    /// </summary>
    public sealed class SDOSubWorker : IDisposable
    {
        private readonly EcClient _ec;
        private readonly SDOStore _store;

        private readonly BlockingCollection<WorkItem> _queue;
        private readonly CancellationTokenSource _cts;

        private Thread _thread;
        private volatile bool _running;
        private bool _disposed;


        private sealed class WorkItem
        {
            public SDOKey Key;
            public int MaxLen;
            public TaskCompletionSource<bool> Tcs; // true=success
        }


        public SDOSubWorker(EcClient ec, SDOStore store, int boundedCapacity = 4096)
        {
            if (ec == null) throw new ArgumentNullException(nameof(ec));
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (boundedCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(boundedCapacity));

            _ec = ec;
            _store = store;

            _queue = new BlockingCollection<WorkItem>(new ConcurrentQueue<WorkItem>(), boundedCapacity);
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

            try
            {
                _cts.Cancel();
            }
            catch { }

            try
            {
                _queue.CompleteAdding();
            }
            catch { }

            try
            {
                if (_thread != null && _thread.IsAlive)
                    _thread.Join();
            }
            catch { }
        }

        /// <summary>
        /// SDO 읽기 요청. (단일 SDO 스레드에서 수행됨)
        /// </summary>
        public Task<bool> EnqueueReadAsync(int slaveNo, ushort index, byte subIndex, int maxLen = 64)
        {
            ThrowIfDisposed();
            if (!_running) throw new InvalidOperationException("SDOSubWorker is not running. Call Start() first.");
            if (maxLen <= 0) throw new ArgumentOutOfRangeException(nameof(maxLen));

            var key = new SDOKey(slaveNo, index, subIndex);
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            // 큐가 가득 찬 경우: 호출자 정책에 따라 Drop/Block 선택 가능.
            // 여기서는 Add(블로킹 가능)로 둔다. 필요하면 TryAdd + 실패 처리로 변경.
            _queue.Add(new WorkItem
            {
                Key = key,
                MaxLen = maxLen,
                Tcs = tcs
            });

            return tcs.Task;
        }

        /// <summary>
        /// 동기식 enqueue (fire-and-forget). 큐가 가득 차면 예외 가능.
        /// </summary>
        public void EnqueueRead(int slaveNo, ushort index, byte subIndex, int maxLen = 64)
        {
            ThrowIfDisposed();
            if (!_running) throw new InvalidOperationException("SDOSubWorker is not running. Call Start() first.");
            if (maxLen <= 0) throw new ArgumentOutOfRangeException(nameof(maxLen));

            _queue.Add(new WorkItem
            {
                Key = new SDOKey(slaveNo, index, subIndex),
                MaxLen = maxLen,
                Tcs = null
            });
        }

        private void ThreadMain()
        {
            try
            {
                foreach (var item in _queue.GetConsumingEnumerable(_cts.Token))
                {
                    bool ok = false;
                    try
                    {
                        ok = ExecuteReadAndWriteMap(item.Key, item.MaxLen);
                    }
                    catch (Exception ex)
                    {
                        // 치명적 예외가 아니면 item 단위로 실패만 처리
                        SafeUpdateError(item.Key, "SDO worker exception: " + ex.Message);
                        ok = false;
                    }

                    if (item.Tcs != null)
                        item.Tcs.TrySetResult(ok);
                }
            }
            catch (OperationCanceledException)
            {
                // 정상 종료
            }
            catch (Exception ex)
            {
                // 워커 스레드 자체가 깨진 경우: 남은 작업들 실패 처리
                DrainQueueWithFatalError(ex);
            }
        }

        /// <summary>
        /// 실제 SDO read 수행 + Map(SDOStore)에 결과 기록
        /// </summary>
        private bool ExecuteReadAndWriteMap(SDOKey key, int maxLen)
        {
            // SOEMNative.soem_sdo_read 시그니처: (ushort slv, ushort idx, byte sub, byte[] buf, ref uint inoutLen)
            // return: 0이면 성공, 그 외 실패(프로젝트 내 EcClient.SdoReadRaw의 관례)
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
                    _store.UpdateOk(key, trimmed);
                }
                else
                {
                    _store.UpdateOk(key, buf);
                }

                return true;
            }

            // 실패: soem_get_last_error_info + elist2string로 상세 확보
            string msg = BuildSoemErrorMessage(key, rc);
            SafeUpdateError(key, msg);
            return false;
        }

        private string BuildSoemErrorMessage(SDOKey key, int rc)
        {
            // GetLastErrorInfo는 실패 직후 호출할수록 정확
            var info = _ec.GetLastErrorInfo();
            string elist = EcClient.GetSoemErrorString();

            if (info.HasValue)
            {
                var e = info.Value;
                // ErrorCode는 래퍼 구현에 따라 의미가 다를 수 있음(Abort code가 아닐 수도 있음)
                // 그래도 원인 추적에 유용하니 포함
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
            // SDOStore에 에러 업데이트 메서드를 추가하는 것을 권장(아래 2) 참고)
            // 일단 해당 메서드가 없으면 컴파일이 안 되므로, 아래 2) 패치도 같이 적용하세요.
            _store.UpdateError(key, error, abortCode: 0);
        }

        private void DrainQueueWithFatalError(Exception ex)
        {
            try
            {
                WorkItem item;
                while (_queue.TryTake(out item))
                {
                    SafeUpdateError(item.Key, "SDO worker fatal error: " + ex.Message);
                    if (item.Tcs != null) item.Tcs.TrySetResult(false);
                }
            }
            catch { }
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
