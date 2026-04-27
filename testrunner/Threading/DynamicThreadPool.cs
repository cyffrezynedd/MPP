using System;
using System.Collections.Generic;
using System.Threading;

namespace TestRunner.Threading
{
    public sealed class DynamicThreadPool : IDisposable
    {
        private readonly object _gate = new();
        private readonly Queue<QueuedWork> _queue = new();
        private readonly DynamicThreadPoolOptions _options;
        private readonly List<Thread> _threads = new();

        private int _liveWorkers;
        private int _executing;
        private volatile bool _disposed;
        private Thread? _watchdog;

        public DynamicThreadPool(DynamicThreadPoolOptions? options = null)
        {
            _options = options ?? new DynamicThreadPoolOptions();
            if (_options.MinThreads < 1)
                throw new ArgumentOutOfRangeException(nameof(options), "MinThreads must be >= 1.");
            if (_options.MaxThreads < _options.MinThreads)
                throw new ArgumentOutOfRangeException(nameof(options), "MaxThreads must be >= MinThreads.");
        }

        public int MinThreads => _options.MinThreads;
        public int MaxThreads => _options.MaxThreads;

        public int QueueLength
        {
            get
            {
                lock (_gate) return _queue.Count;
            }
        }

        public int LiveWorkers => Volatile.Read(ref _liveWorkers);
        public int ExecutingWorkItems => Volatile.Read(ref _executing);

        public event EventHandler<ThreadPoolThreadEventArgs>? ThreadStarted;
        public event EventHandler<ThreadPoolThreadEventArgs>? ThreadStopped;
        public event EventHandler<ThreadPoolQueueEventArgs>? QueueChanged;
        public event EventHandler<ThreadPoolWorkEventArgs>? WorkItemStarted;
        public event EventHandler<ThreadPoolWorkEventArgs>? WorkItemCompleted;
        public event EventHandler<ThreadPoolErrorEventArgs>? Error;

        public void Start()
        {
            ThrowIfDisposed();
            lock (_gate)
            {
                EnsureWatchdogUnlocked();
                while (_threads.Count < _options.MinThreads)
                    SpawnWorkerLocked("initial-min");
            }
        }

        public void Enqueue(Action work)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            ThrowIfDisposed();

            var item = new QueuedWork(work, DateTime.UtcNow);

            lock (_gate)
            {
                _queue.Enqueue(item);
                MaybeScaleUpLocked();
                Monitor.PulseAll(_gate);
                QueueChanged?.Invoke(this, new ThreadPoolQueueEventArgs(_queue.Count, _threads.Count));
            }
        }

        public void WaitUntilIdle(TimeSpan? deadlineTimeout = null)
        {
            var deadline = DateTime.UtcNow + (deadlineTimeout ?? TimeSpan.FromMinutes(2));
            lock (_gate)
            {
                while ((_queue.Count > 0 || Volatile.Read(ref _executing) > 0) &&
                       DateTime.UtcNow < deadline)
                {
                    Monitor.Wait(_gate, 100);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            lock (_gate)
            {
                Monitor.PulseAll(_gate);
            }

            _watchdog?.Join(TimeSpan.FromSeconds(2));

            Thread[] snapshot;
            lock (_gate)
            {
                snapshot = _threads.ToArray();
            }

            foreach (var t in snapshot)
                t.Join(TimeSpan.FromSeconds(5));
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DynamicThreadPool));
        }

        private void EnsureWatchdogUnlocked()
        {
            if (!_options.EnableStuckWorkReplacement || _watchdog != null)
                return;

            _watchdog = new Thread(WatchdogLoop)
            {
                IsBackground = true,
                Name = "DynamicThreadPool-Watchdog"
            };
            _watchdog.Start();
        }

        private void WatchdogLoop()
        {
            int stuckTicks = 0;
            while (!_disposed)
            {
                Thread.Sleep(_options.StuckDetectionInterval);
                if (_disposed) break;

                lock (_gate)
                {
                    int q = _queue.Count;
                    int ex = Volatile.Read(ref _executing);
                    int workers = _threads.Count;

                    if (q > 0 && ex == 0 && workers < _options.MaxThreads)
                        stuckTicks++;
                    else
                        stuckTicks = 0;

                    if (stuckTicks >= _options.StuckIterationsBeforeReplacement)
                    {
                        SpawnWorkerLocked("watchdog-stuck-replacement");
                        stuckTicks = 0;
                    }
                }
            }
        }

        private void MaybeScaleUpLocked()
        {
            if (_threads.Count >= _options.MaxThreads)
                return;

            bool deepQueue = _queue.Count >= _options.ScaleQueueThreshold;
            DateTime oldest = _queue.Count > 0 ? PeekOldestEnqueueTime() : DateTime.UtcNow;
            bool longWait = _queue.Count > 0 && (DateTime.UtcNow - oldest) > TimeSpan.FromSeconds(2);

            if (deepQueue || longWait)
                SpawnWorkerLocked(deepQueue ? "queue-depth" : "queue-wait");
        }

        private DateTime PeekOldestEnqueueTime()
        {
            return _queue.Count > 0 ? _queue.Peek().EnqueuedUtc : DateTime.UtcNow;
        }

        private void SpawnWorkerLocked(string reason)
        {
            if (_threads.Count >= _options.MaxThreads)
                return;

            var thread = new Thread(() => WorkerLoop(reason))
            {
                IsBackground = false,
                Name = $"DynamicThreadPool-{_threads.Count + 1}"
            };

            _threads.Add(thread);
            thread.Start();
        }

        private void WorkerLoop(string spawnReason)
        {
            Interlocked.Increment(ref _liveWorkers);
            int tid = Environment.CurrentManagedThreadId;
            ThreadStarted?.Invoke(this, new ThreadPoolThreadEventArgs(tid, LiveWorkers, spawnReason));

            var idleAccumulator = TimeSpan.Zero;
            var exitReason = "exit";

            try
            {
                while (!_disposed)
                {
                    QueuedWork? item = null;

                    lock (_gate)
                    {
                        while (_queue.Count == 0 && !_disposed)
                        {
                            if (!Monitor.Wait(_gate, 100))
                            {
                                idleAccumulator += TimeSpan.FromMilliseconds(100);

                                bool overMin = _threads.Count > _options.MinThreads;
                                if (_queue.Count == 0 && overMin &&
                                    idleAccumulator >= _options.WorkerIdleShutdown)
                                {
                                    exitReason = "idle-shutdown";
                                    goto LeaveLoop;
                                }
                            }
                            else
                            {
                                idleAccumulator = TimeSpan.Zero;
                            }
                        }

                        if (_disposed && _queue.Count == 0)
                            break;

                        if (_queue.Count > 0)
                            item = _queue.Dequeue();

                        QueueChanged?.Invoke(this, new ThreadPoolQueueEventArgs(_queue.Count, _threads.Count));
                        Monitor.PulseAll(_gate);
                    }

                    if (item == null)
                        continue;

                    var waited = DateTime.UtcNow - item.Value.EnqueuedUtc;
                    WorkItemStarted?.Invoke(this, new ThreadPoolWorkEventArgs(tid, waited));

                    Interlocked.Increment(ref _executing);
                    try
                    {
                        item.Value.Work();
                    }
                    catch (Exception ex)
                    {
                        Error?.Invoke(this, new ThreadPoolErrorEventArgs(ex, "work-item", tid));

                        lock (_gate)
                        {
                            if (_threads.Count < _options.MaxThreads)
                                SpawnWorkerLocked("recover-after-work-failure");
                        }
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _executing);
                        WorkItemCompleted?.Invoke(this, new ThreadPoolWorkEventArgs(tid, waited));

                        lock (_gate)
                        {
                            if (_queue.Count == 0 && Volatile.Read(ref _executing) == 0)
                                Monitor.PulseAll(_gate);
                        }
                    }
                }

            LeaveLoop: ;
            }
            finally
            {
                lock (_gate)
                {
                    RemoveCurrentThreadLocked(Thread.CurrentThread);
                }

                Interlocked.Decrement(ref _liveWorkers);
                ThreadStopped?.Invoke(this, new ThreadPoolThreadEventArgs(tid, LiveWorkers, exitReason));
            }
        }

        private void RemoveCurrentThreadLocked(Thread current)
        {
            _threads.RemoveAll(t => t == current);
            Monitor.PulseAll(_gate);
        }

        private readonly struct QueuedWork
        {
            public Action Work { get; }
            public DateTime EnqueuedUtc { get; }

            public QueuedWork(Action work, DateTime enqueuedUtc)
            {
                Work = work;
                EnqueuedUtc = enqueuedUtc;
            }
        }
    }
}
