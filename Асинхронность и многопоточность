using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ThreadSafeStructures;

namespace ThreadSafeStructures
{
    // 3.1: ПОТОКОБЕЗОПАСНЫЙ КЭШ С РАЗЛИЧНЫМИ ПОЛИТИКАМИ

    public enum CachePolicy { LRU, LFU, TTL, ARC }

    public interface IThreadSafeCache<K, V> where K : notnull
    {
        bool TryGet(K key, out V value);
        void Put(K key, V value, TimeSpan? ttl = null);
        bool Remove(K key);
        int Count { get; }
        void Clear();
        CacheStats GetStats();
    }

    public class CacheStats
    {
        private long _hits, _misses, _evictions;

        public long Hits => _hits;
        public long Misses => _misses;
        public long Evictions => _evictions;

        public void IncrementHits() => Interlocked.Increment(ref _hits);
        public void IncrementMisses() => Interlocked.Increment(ref _misses);
        public void IncrementEvictions() => Interlocked.Increment(ref _evictions);

        public double HitRatio => _hits + _misses > 0 ? (double)_hits / (_hits + _misses) : 0;

        public CacheStatsSnapshot GetSnapshot() => new CacheStatsSnapshot
        {
            Hits = _hits,
            Misses = _misses,
            Evictions = _evictions
        };
    }

    public class CacheStatsSnapshot
    {
        public long Hits { get; set; }
        public long Misses { get; set; }
        public long Evictions { get; set; }
        public double HitRatio => Hits + Misses > 0 ? (double)Hits / (Hits + Misses) : 0;
    }

    public class CacheEntry<V>
    {
        public V Value { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastAccessed { get; set; }
        public long AccessCount { get; set; }
        public TimeSpan? TimeToLive { get; set; }

        public CacheEntry(V value, TimeSpan? ttl = null)
        {
            Value = value;
            CreatedAt = DateTime.UtcNow;
            LastAccessed = DateTime.UtcNow;
            AccessCount = 1;
            TimeToLive = ttl;
        }

        public bool IsExpired => TimeToLive.HasValue && DateTime.UtcNow - CreatedAt > TimeToLive.Value;
        public void Touch() { LastAccessed = DateTime.UtcNow; AccessCount++; }
    }

    public class ThreadSafeCache<K, V> : IThreadSafeCache<K, V>, IDisposable where K : notnull
    {
        private readonly ConcurrentDictionary<K, CacheEntry<V>> _cache;
        private readonly int _capacity;
        private readonly CachePolicy _policy;
        private readonly TimeSpan? _defaultTTL;
        private readonly ReaderWriterLockSlim _evictionLock = new ReaderWriterLockSlim();
        private readonly CacheStats _stats = new CacheStats();
        private readonly Timer _cleanupTimer;

        public ThreadSafeCache(int capacity, CachePolicy policy, TimeSpan? defaultTTL = null)
        {
            _capacity = capacity;
            _policy = policy;
            _defaultTTL = defaultTTL;
            _cache = new ConcurrentDictionary<K, CacheEntry<V>>();
            _cleanupTimer = new Timer(_ => CleanupExpired(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        public bool TryGet(K key, out V value)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                if (entry.IsExpired)
                {
                    _cache.TryRemove(key, out _);
                    _stats.IncrementMisses();
                    value = default(V);
                    return false;
                }

                entry.Touch();
                _stats.IncrementHits();
                value = entry.Value;
                return true;
            }

            _stats.IncrementMisses();
            value = default(V);
            return false;
        }

        public void Put(K key, V value, TimeSpan? ttl = null)
        {
            var entry = new CacheEntry<V>(value, ttl ?? _defaultTTL);
            _cache.AddOrUpdate(key, entry, (k, old) => entry);

            if (_cache.Count > _capacity)
                Evict();
        }

        public bool Remove(K key) => _cache.TryRemove(key, out _);
        public int Count => _cache.Count;
        public void Clear() => _cache.Clear();

        public CacheStats GetStats() => _stats;

        private void Evict()
        {
            _evictionLock.EnterWriteLock();
            try
            {
                if (_cache.Count <= _capacity) return;

                var candidates = _cache.ToArray();
                K keyToRemove = default(K);

                switch (_policy)
                {
                    case CachePolicy.LRU:
                        keyToRemove = candidates.OrderBy(x => x.Value.LastAccessed).First().Key;
                        break;
                    case CachePolicy.LFU:
                        keyToRemove = candidates.OrderBy(x => x.Value.AccessCount).First().Key;
                        break;
                    case CachePolicy.TTL:
                        var expired = candidates.FirstOrDefault(x => x.Value.IsExpired);
                        if (!expired.Equals(default(KeyValuePair<K, CacheEntry<V>>)))
                            keyToRemove = expired.Key;
                        else
                            keyToRemove = candidates.OrderBy(x => x.Value.CreatedAt).First().Key;
                        break;
                }

                if (keyToRemove != null && !keyToRemove.Equals(default(K)) && _cache.TryRemove(keyToRemove, out _))
                    _stats.IncrementEvictions();
            }
            finally { _evictionLock.ExitWriteLock(); }
        }

        private void CleanupExpired()
        {
            var expiredKeys = _cache.Where(x => x.Value.IsExpired).Select(x => x.Key).ToList();
            foreach (var key in expiredKeys)
            {
                if (_cache.TryRemove(key, out _))
                    _stats.IncrementEvictions();
            }
        }

        public void Dispose() => _cleanupTimer?.Dispose();
    }

    public class MultiLevelCache<K, V> : IThreadSafeCache<K, V> where K : notnull
    {
        private readonly IThreadSafeCache<K, V> _l1Cache;
        private readonly IThreadSafeCache<K, V> _l2Cache;

        public MultiLevelCache(int l1Capacity, int l2Capacity, CachePolicy policy)
        {
            _l1Cache = new ThreadSafeCache<K, V>(l1Capacity, policy);
            _l2Cache = new ThreadSafeCache<K, V>(l2Capacity, policy);
        }

        public bool TryGet(K key, out V value)
        {
            if (_l1Cache.TryGet(key, out value)) return true;
            if (_l2Cache.TryGet(key, out value))
            {
                _l1Cache.Put(key, value);
                return true;
            }
            return false;
        }

        public void Put(K key, V value, TimeSpan? ttl = null)
        {
            _l1Cache.Put(key, value, ttl);
            _l2Cache.Put(key, value, ttl);
        }

        public bool Remove(K key) => _l1Cache.Remove(key) || _l2Cache.Remove(key);
        public int Count => _l1Cache.Count + _l2Cache.Count;
        public void Clear() { _l1Cache.Clear(); _l2Cache.Clear(); }

        public CacheStats GetStats()
        {
            var l1Stats = _l1Cache.GetStats();
            var l2Stats = _l2Cache.GetStats();
            return new CacheStats();
        }
    }

    // 3.2: ПУЛ РАБОЧИХ ПОТОКОВ (THREAD POOL)

    public enum Priority { High, Normal, Low }

    public class AdvancedThreadPool : IDisposable
    {
        private readonly int _minThreads, _maxThreads;
        private readonly List<WorkerThread> _workers;
        private readonly ConcurrentQueue<ThreadPoolTask> _normalQueue, _highQueue;
        private readonly CancellationTokenSource _cts;
        private readonly Timer _monitorTimer;
        private readonly object _balanceLock = new object();

        private long _tasksProcessed, _tasksFailed;
        private int _activeWorkers;

        public AdvancedThreadPool(int minThreads, int maxThreads)
        {
            _minThreads = minThreads;
            _maxThreads = maxThreads;
            _workers = new List<WorkerThread>();
            _normalQueue = new ConcurrentQueue<ThreadPoolTask>();
            _highQueue = new ConcurrentQueue<ThreadPoolTask>();
            _cts = new CancellationTokenSource();
            InitializeWorkers();
            _monitorTimer = new Timer(MonitorAndBalance, null, 1000, 1000);
        }

        private void InitializeWorkers()
        {
            for (int i = 0; i < _minThreads; i++) AddWorker();
        }

        private void AddWorker()
        {
            var worker = new WorkerThread(this, _workers.Count + 1, _cts.Token);
            _workers.Add(worker);
            worker.Start();
        }

        public Task<T> QueueTask<T>(Func<T> function, Priority priority = Priority.Normal)
        {
            var tcs = new TaskCompletionSource<T>();
            var task = new ThreadPoolTask(() =>
            {
                try { tcs.SetResult(function()); Interlocked.Increment(ref _tasksProcessed); }
                catch (Exception ex) { tcs.SetException(ex); Interlocked.Increment(ref _tasksFailed); }
            }, priority);

            if (priority == Priority.High) _highQueue.Enqueue(task);
            else _normalQueue.Enqueue(task);

            TryScaleUp();
            return tcs.Task;
        }

        private void TryScaleUp()
        {
            lock (_balanceLock)
            {
                var totalQueue = _highQueue.Count + _normalQueue.Count;
                if (totalQueue > _activeWorkers * 2 && _workers.Count < _maxThreads)
                    AddWorker();
            }
        }

        private void MonitorAndBalance(object state)
        {
            lock (_balanceLock)
            {
                if (_highQueue.IsEmpty && _normalQueue.IsEmpty && _workers.Count > _minThreads)
                {
                    var inactive = _workers.FirstOrDefault(w => !w.IsWorking && w.CanStop);
                    if (inactive != null) { _workers.Remove(inactive); inactive.Stop(); }
                }
            }
        }

        internal bool TryGetWork(out ThreadPoolTask task)
        {
            if (_highQueue.TryDequeue(out task) || _normalQueue.TryDequeue(out task))
            {
                Interlocked.Increment(ref _activeWorkers);
                return true;
            }
            task = null;
            return false;
        }

        internal void WorkCompleted() => Interlocked.Decrement(ref _activeWorkers);

        public ThreadPoolMetrics GetMetrics() => new ThreadPoolMetrics
        {
            ActiveThreads = _activeWorkers,
            TotalThreads = _workers.Count,
            HighPriorityQueue = _highQueue.Count,
            NormalPriorityQueue = _normalQueue.Count,
            TasksProcessed = Interlocked.Read(ref _tasksProcessed),
            TasksFailed = Interlocked.Read(ref _tasksFailed)
        };

        public void Shutdown(TimeSpan timeout)
        {
            _cts.Cancel();
            _monitorTimer?.Dispose();
            Task.Run(() => _workers.ForEach(w => w.Stop())).Wait(timeout);
        }

        public void Dispose() => Shutdown(TimeSpan.FromSeconds(30));
    }

    internal class WorkerThread
    {
        private readonly AdvancedThreadPool _pool;
        private readonly Thread _thread;
        private readonly CancellationToken _token;
        private volatile bool _running;

        public WorkerThread(AdvancedThreadPool pool, int id, CancellationToken token)
        {
            _pool = pool;
            _token = token;
            _thread = new Thread(Work) { Name = $"PoolWorker-{id}", IsBackground = true };
            _running = true;
        }

        public bool IsWorking { get; private set; }
        public bool CanStop => !IsWorking;
        public void Start() => _thread.Start();
        public void Stop() => _running = false;

        private void Work()
        {
            while (_running && !_token.IsCancellationRequested)
            {
                if (_pool.TryGetWork(out var task))
                {
                    IsWorking = true;
                    try { task.Execute(); }
                    finally { IsWorking = false; _pool.WorkCompleted(); }
                }
                else Thread.Sleep(100);
            }
        }
    }

    public class ThreadPoolTask
    {
        public Action Work { get; }
        public Priority Priority { get; }
        public DateTime QueuedTime { get; }

        public ThreadPoolTask(Action work, Priority priority)
        {
            Work = work; Priority = priority; QueuedTime = DateTime.UtcNow;
        }

        public void Execute() => Work();
    }

    public class ThreadPoolMetrics
    {
        public int ActiveThreads { get; set; }
        public int TotalThreads { get; set; }
        public int HighPriorityQueue { get; set; }
        public int NormalPriorityQueue { get; set; }
        public long TasksProcessed { get; set; }
        public long TasksFailed { get; set; }
    }

    // 3.3: АСИНХРОННАЯ ОБРАБОТКА ДАННЫХ ИЗ ПОТОКА

    public class AsyncStreamProcessor<T> : IAsyncDisposable
    {
        private readonly Channel<T> _channel;
        private readonly Func<IReadOnlyList<T>, CancellationToken, Task> _processor;
        private readonly int _batchSize;
        private readonly TimeSpan _batchTimeout;
        private readonly CancellationTokenSource _cts;
        private readonly Task[] _processingTasks;
        private readonly SemaphoreSlim _throttleSemaphore;
        private readonly int _maxConcurrentBatches;
        private readonly RetryPolicy _retryPolicy;

        private long _totalProcessed, _totalFailed, _totalBatches;

        public AsyncStreamProcessor(
            Func<IReadOnlyList<T>, CancellationToken, Task> processor,
            int batchSize = 100, TimeSpan? batchTimeout = null, int bufferCapacity = 1000,
            int maxConcurrentBatches = 4, RetryPolicy retryPolicy = null)
        {
            _processor = processor;
            _batchSize = batchSize;
            _batchTimeout = batchTimeout ?? TimeSpan.FromMilliseconds(500);
            _maxConcurrentBatches = maxConcurrentBatches;
            _retryPolicy = retryPolicy ?? new RetryPolicy(3, TimeSpan.FromMilliseconds(100));

            _channel = Channel.CreateBounded<T>(new BoundedChannelOptions(bufferCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false
            });

            _cts = new CancellationTokenSource();
            _throttleSemaphore = new SemaphoreSlim(maxConcurrentBatches, maxConcurrentBatches);

            // Создаем несколько задач для обработки
            _processingTasks = new Task[4];
            for (int i = 0; i < _processingTasks.Length; i++)
            {
                _processingTasks[i] = StartBatchProcessor();
            }
        }

        private async Task StartBatchProcessor()
        {
            var batch = new List<T>(_batchSize);
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var item = await ReadWithTimeout(_batchTimeout, _cts.Token);
                    if (item != null) batch.Add(item);

                    while (batch.Count < _batchSize && _channel.Reader.TryRead(out var nextItem))
                        batch.Add(nextItem);

                    if (batch.Count > 0)
                    {
                        await ProcessBatchWithRetry(batch);
                        batch.Clear();
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { Console.WriteLine($"Batch processor error: {ex.Message}"); }
            }

            if (batch.Count > 0) await ProcessBatchWithRetry(batch);
        }

        private async Task<T> ReadWithTimeout(TimeSpan timeout, CancellationToken ct)
        {
            try
            {
                var readTask = _channel.Reader.ReadAsync(ct).AsTask();
                var timeoutTask = Task.Delay(timeout, ct);

                var completedTask = await Task.WhenAny(readTask, timeoutTask);
                if (completedTask == readTask)
                    return await readTask;
                else
                    return default(T);
            }
            catch (OperationCanceledException) { return default(T); }
        }

        private async Task ProcessBatchWithRetry(List<T> batch)
        {
            await _throttleSemaphore.WaitAsync(_cts.Token);
            try
            {
                for (int attempts = 0; attempts <= _retryPolicy.MaxRetries; attempts++)
                {
                    try
                    {
                        await _processor(batch, _cts.Token);
                        Interlocked.Add(ref _totalProcessed, batch.Count);
                        Interlocked.Increment(ref _totalBatches);
                        break;
                    }
                    catch (Exception) when (attempts < _retryPolicy.MaxRetries)
                    {
                        await Task.Delay(_retryPolicy.Delay.Multiply(attempts), _cts.Token);
                    }
                    catch (Exception)
                    {
                        Interlocked.Add(ref _totalFailed, batch.Count);
                        throw;
                    }
                }
            }
            finally { _throttleSemaphore.Release(); }
        }

        public ValueTask PushAsync(T item, CancellationToken ct = default) =>
            _channel.Writer.WriteAsync(item, ct);

        public void Complete() => _channel.Writer.Complete();

        public StreamProcessorMetrics GetMetrics() => new StreamProcessorMetrics
        {
            TotalProcessed = Interlocked.Read(ref _totalProcessed),
            TotalFailed = Interlocked.Read(ref _totalFailed),
            TotalBatches = Interlocked.Read(ref _totalBatches),
            QueueLength = _channel.Reader.Count
        };

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            Complete();
            try { await Task.WhenAll(_processingTasks); }
            catch (OperationCanceledException) { }
            finally { _cts.Dispose(); _throttleSemaphore.Dispose(); }
        }
    }

    public class RetryPolicy
    {
        public int MaxRetries { get; }
        public TimeSpan Delay { get; }
        public RetryPolicy(int maxRetries, TimeSpan delay) { MaxRetries = maxRetries; Delay = delay; }
    }

    public class StreamProcessorMetrics
    {
        public long TotalProcessed { get; set; }
        public long TotalFailed { get; set; }
        public long TotalBatches { get; set; }
        public int QueueLength { get; set; }
    }

    // 3.4: PRODUCER-CONSUMER ПАТТЕРН

    public class ThrottledProducerConsumer<T> : IDisposable
    {
        private readonly BlockingCollection<Message<T>> _queue;
        private readonly List<Task> _consumerTasks;
        private readonly Func<T, CancellationToken, Task> _consumerAction;
        private readonly CancellationTokenSource _cts;
        private readonly SemaphoreSlim _throttleSemaphore;
        private readonly RateLimiter _rateLimiter;
        private readonly int _maxConsumers;

        private long _processedItems, _failedItems, _throttledItems;

        public ThrottledProducerConsumer(
            Func<T, CancellationToken, Task> consumerAction,
            int maxQueueSize = 1000, int maxConsumers = 4, RateLimit rateLimit = null)
        {
            _consumerAction = consumerAction;
            _maxConsumers = maxConsumers;
            _rateLimiter = new RateLimiter(rateLimit ?? new RateLimit(100, TimeSpan.FromSeconds(1)));
            _queue = new BlockingCollection<Message<T>>(new ConcurrentQueue<Message<T>>(), maxQueueSize);
            _consumerTasks = new List<Task>();
            _cts = new CancellationTokenSource();
            _throttleSemaphore = new SemaphoreSlim(maxConsumers, maxConsumers);
            StartConsumers();
        }

        public async Task<bool> ProduceAsync(T item, TimeSpan timeout, CancellationToken ct = default)
        {
            if (_cts.Token.IsCancellationRequested) return false;
            if (!_rateLimiter.TryAcquire()) { Interlocked.Increment(ref _throttledItems); return false; }

            var message = new Message<T>(item, DateTime.UtcNow);
            try { return _queue.TryAdd(message, (int)timeout.TotalMilliseconds, ct); }
            catch (OperationCanceledException) { return false; }
        }

        public void Produce(T item)
        {
            if (_cts.Token.IsCancellationRequested) throw new InvalidOperationException("Shutting down");
            if (!_rateLimiter.TryAcquire()) { Interlocked.Increment(ref _throttledItems); throw new RateLimitExceededException("Rate limit exceeded"); }
            _queue.Add(new Message<T>(item, DateTime.UtcNow), _cts.Token);
        }

        private void StartConsumers()
        {
            for (int i = 0; i < _maxConsumers; i++)
                _consumerTasks.Add(Task.Run(ConsumeLoop, _cts.Token));
        }

        private async Task ConsumeLoop()
        {
            while (!_cts.Token.IsCancellationRequested || !_queue.IsCompleted)
            {
                try
                {
                    if (_queue.TryTake(out var message, 100, _cts.Token))
                    {
                        await _throttleSemaphore.WaitAsync(_cts.Token);
                        try
                        {
                            await _consumerAction(message.Data, _cts.Token);
                            Interlocked.Increment(ref _processedItems);
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref _failedItems);
                            Console.WriteLine($"Consumption failed: {ex.Message}");
                            await HandleFailedMessage(message, ex);
                        }
                        finally { _throttleSemaphore.Release(); }
                    }
                }
                catch (OperationCanceledException) { break; }
            }
        }

        private async Task HandleFailedMessage(Message<T> message, Exception ex) =>
            await Task.Delay(1000);

        public void CompleteAdding() => _queue.CompleteAdding();

        public void Shutdown(TimeSpan timeout)
        {
            _cts.Cancel(); CompleteAdding();
            try { Task.WaitAll(_consumerTasks.ToArray(), timeout); }
            catch (AggregateException ae) { ae.Handle(e => e is OperationCanceledException); }
        }

        public ProducerConsumerMetrics GetMetrics() => new ProducerConsumerMetrics
        {
            ProcessedItems = Interlocked.Read(ref _processedItems),
            FailedItems = Interlocked.Read(ref _failedItems),
            ThrottledItems = Interlocked.Read(ref _throttledItems),
            QueueLength = _queue.Count,
            ActiveConsumers = _maxConsumers - _throttleSemaphore.CurrentCount
        };

        public void Dispose() => Shutdown(TimeSpan.FromSeconds(5));
    }

    public class Message<T>
    {
        public T Data { get; }
        public DateTime CreatedAt { get; }
        public int Attempts { get; set; }
        public Message(T data, DateTime createdAt) { Data = data; CreatedAt = createdAt; Attempts = 0; }
    }

    public class RateLimit
    {
        public int MaxRequests { get; }
        public TimeSpan TimeWindow { get; }
        public RateLimit(int maxRequests, TimeSpan timeWindow) { MaxRequests = maxRequests; TimeWindow = timeWindow; }
    }

    public class RateLimiter
    {
        private readonly RateLimit _limit;
        private readonly Queue<DateTime> _requestTimes;
        private readonly object _lock = new object();

        public RateLimiter(RateLimit limit) { _limit = limit; _requestTimes = new Queue<DateTime>(); }

        public bool TryAcquire()
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                while (_requestTimes.Count > 0 && now - _requestTimes.Peek() > _limit.TimeWindow)
                    _requestTimes.Dequeue();

                if (_requestTimes.Count < _limit.MaxRequests)
                {
                    _requestTimes.Enqueue(now);
                    return true;
                }
                return false;
            }
        }
    }

    public class RateLimitExceededException : Exception
    {
        public RateLimitExceededException(string message) : base(message) { }
    }

    public class ProducerConsumerMetrics
    {
        public long ProcessedItems { get; set; }
        public long FailedItems { get; set; }
        public long ThrottledItems { get; set; }
        public int QueueLength { get; set; }
        public int ActiveConsumers { get; set; }
    }

    // 3.5: БАРЬЕР СИНХРОНИЗАЦИИ ДЛЯ КООРДИНАЦИИ ПОТОКОВ

    public class AdvancedBarrier : IDisposable
    {
        private readonly int _initialParticipantCount;
        private readonly Action<AdvancedBarrier> _postPhaseAction;
        private readonly SemaphoreSlim _phaseLock = new SemaphoreSlim(1, 1);
        private readonly ManualResetEventSlim _phaseCompletedEvent = new ManualResetEventSlim(true);

        private int _currentParticipants, _remainingParticipants;
        private long _currentPhase;
        private bool _isDisposed;
        private List<Exception> _phaseExceptions = new List<Exception>();
        private TaskCompletionSource<bool> _phaseCompletionSource;

        public AdvancedBarrier(int participantCount, Action<AdvancedBarrier> postPhaseAction = null)
        {
            if (participantCount <= 0) throw new ArgumentOutOfRangeException(nameof(participantCount));
            _initialParticipantCount = participantCount;
            _currentParticipants = _remainingParticipants = participantCount;
            _postPhaseAction = postPhaseAction;
            _currentPhase = 0;
            _phaseCompletionSource = new TaskCompletionSource<bool>();
        }

        public async Task<bool> SignalAndWaitAsync(TimeSpan timeout = default, CancellationToken cancellationToken = default)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(AdvancedBarrier));

            using var timeoutCts = timeout == default ? null : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (timeoutCts != null) { timeoutCts.CancelAfter(timeout); cancellationToken = timeoutCts.Token; }

            try { return await SignalAndWaitCoreAsync(cancellationToken); }
            catch (OperationCanceledException) when (timeoutCts?.IsCancellationRequested == true) { throw new TimeoutException("Barrier operation timed out"); }
        }

        private async Task<bool> SignalAndWaitCoreAsync(CancellationToken cancellationToken)
        {
            int phase; bool shouldRunPostPhaseAction = false;

            await _phaseLock.WaitAsync(cancellationToken);
            try
            {
                phase = (int)_currentPhase;
                if (_remainingParticipants == 0) throw new InvalidOperationException("Barrier is full");

                _remainingParticipants--;
                if (_remainingParticipants == 0)
                {
                    _currentPhase++; _remainingParticipants = _currentParticipants;
                    shouldRunPostPhaseAction = true; _phaseCompletedEvent.Reset();
                    var oldCompletion = _phaseCompletionSource;
                    _phaseCompletionSource = new TaskCompletionSource<bool>();
                    oldCompletion?.SetResult(true);
                }
                else
                {
                    var completionSource = _phaseCompletionSource;
                    _phaseLock.Release();
                    try { await completionSource.Task.WaitAsync(cancellationToken); }
                    finally { await _phaseLock.WaitAsync(cancellationToken); }
                }
            }
            finally { _phaseLock.Release(); }

            if (shouldRunPostPhaseAction)
            {
                await ExecutePostPhaseActionAsync();
                _phaseCompletedEvent.Set();
            }

            return shouldRunPostPhaseAction;
        }

        private async Task ExecutePostPhaseActionAsync()
        {
            if (_postPhaseAction != null)
            {
                try { await Task.Run(() => _postPhaseAction(this)); }
                catch (Exception ex) { lock (_phaseExceptions) _phaseExceptions.Add(ex); }
            }
        }

        public void AddParticipants(int participantCount)
        {
            _phaseLock.Wait();
            try
            {
                if (_remainingParticipants != _currentParticipants) throw new InvalidOperationException("Cannot add participants during phase");
                _currentParticipants += participantCount; _remainingParticipants += participantCount;
            }
            finally { _phaseLock.Release(); }
        }

        public void RemoveParticipants(int participantCount)
        {
            _phaseLock.Wait();
            try
            {
                if (_remainingParticipants != _currentParticipants) throw new InvalidOperationException("Cannot remove participants during phase");
                if (participantCount > _currentParticipants) throw new ArgumentOutOfRangeException(nameof(participantCount));
                _currentParticipants -= participantCount; _remainingParticipants -= participantCount;
            }
            finally { _phaseLock.Release(); }
        }

        public int ParticipantsRemaining => _remainingParticipants;
        public int TotalParticipants => _currentParticipants;
        public long CurrentPhaseNumber => _currentPhase;

        public IReadOnlyList<Exception> GetPhaseExceptions()
        {
            lock (_phaseExceptions) { return new List<Exception>(_phaseExceptions); }
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                _phaseCompletedEvent?.Dispose();
                _phaseCompletionSource?.TrySetCanceled();
                _phaseLock?.Dispose();
            }
        }
    }

    // 3.6: СЕМАФОР С СПРАВЕДЛИВОСТЬЮ (FAIR SEMAPHORE)

    public class FairSemaphore : IDisposable
    {
        private readonly Queue<TaskCompletionSource<bool>> _waiters;
        private readonly object _lock = new object();
        private readonly int _maxCount;
        private int _currentCount;
        private bool _isDisposed;

        public FairSemaphore(int initialCount, int maxCount)
        {
            if (initialCount < 0 || maxCount <= 0 || initialCount > maxCount) throw new ArgumentException("Invalid semaphore count");
            _currentCount = initialCount; _maxCount = maxCount; _waiters = new Queue<TaskCompletionSource<bool>>();
        }

        public Task WaitAsync() => WaitAsync(TimeSpan.Zero, CancellationToken.None);

        public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                if (_isDisposed) throw new ObjectDisposedException(nameof(FairSemaphore));
                if (_currentCount > 0) { _currentCount--; return Task.FromResult(true); }

                var tcs = new TaskCompletionSource<bool>();
                _waiters.Enqueue(tcs);

                if (timeout != TimeSpan.Zero)
                {
                    var timeoutCts = new CancellationTokenSource(timeout);
                    timeoutCts.Token.Register(() =>
                    {
                        if (tcs.TrySetResult(false)) RemoveFromQueue(tcs);
                    });
                }

                cancellationToken.Register(() =>
                {
                    if (tcs.TrySetCanceled(cancellationToken)) RemoveFromQueue(tcs);
                });

                return tcs.Task;
            }
        }

        private void RemoveFromQueue(TaskCompletionSource<bool> tcsToRemove)
        {
            lock (_lock)
            {
                var newQueue = new Queue<TaskCompletionSource<bool>>();
                while (_waiters.Count > 0)
                {
                    var waiter = _waiters.Dequeue();
                    if (waiter != tcsToRemove) newQueue.Enqueue(waiter);
                }
                while (newQueue.Count > 0) _waiters.Enqueue(newQueue.Dequeue());
            }
        }

        public void Release() => Release(1);

        public void Release(int releaseCount)
        {
            if (releaseCount < 1) throw new ArgumentOutOfRangeException(nameof(releaseCount));

            lock (_lock)
            {
                if (_isDisposed) throw new ObjectDisposedException(nameof(FairSemaphore));
                if (_currentCount + releaseCount > _maxCount) throw new SemaphoreFullException();

                while (_waiters.Count > 0 && releaseCount > 0)
                {
                    var waiter = _waiters.Dequeue();
                    if (waiter.TrySetResult(true)) releaseCount--;
                }

                _currentCount += releaseCount;
            }
        }

        public int CurrentCount { get { lock (_lock) return _currentCount; } }
        public int WaitQueueLength { get { lock (_lock) return _waiters.Count; } }

        public void Dispose()
        {
            lock (_lock)
            {
                if (!_isDisposed)
                {
                    _isDisposed = true;
                    while (_waiters.Count > 0)
                        _waiters.Dequeue().TrySetException(new ObjectDisposedException(nameof(FairSemaphore)));
                }
            }
        }
    }

    // 3.7: READWRITELOCK ДЛЯ ОПТИМИЗАЦИИ ЧТЕНИЯ

    public class PriorityReadWriteLock : IDisposable
    {
        private readonly object _lock = new object();
        private readonly Queue<WriterWaiter> _writerQueue;
        private readonly Dictionary<Priority, Queue<ReaderWaiter>> _readerQueues;
        private int _activeReaders, _waitingWriters;
        private bool _activeWriter, _isDisposed;

        public PriorityReadWriteLock()
        {
            _writerQueue = new Queue<WriterWaiter>();
            _readerQueues = new Dictionary<Priority, Queue<ReaderWaiter>>();
            foreach (Priority priority in Enum.GetValues(typeof(Priority)))
                _readerQueues[priority] = new Queue<ReaderWaiter>();
        }

        public async Task<IDisposable> EnterReadLockAsync(TimeSpan timeout = default, Priority priority = Priority.Normal, CancellationToken cancellationToken = default)
        {
            var waiter = new ReaderWaiter();
            lock (_lock)
            {
                if (_isDisposed) throw new ObjectDisposedException(nameof(PriorityReadWriteLock));
                if (!_activeWriter && _waitingWriters == 0) { _activeReaders++; return new ReadLockToken(this); }
                _readerQueues[priority].Enqueue(waiter);
            }

            try { await waiter.WaitAsync(timeout, cancellationToken); return new ReadLockToken(this); }
            catch { RemoveFromQueue(waiter, priority); throw; }
        }

        public async Task<IDisposable> EnterWriteLockAsync(TimeSpan timeout = default, CancellationToken cancellationToken = default)
        {
            var waiter = new WriterWaiter();
            lock (_lock)
            {
                if (_isDisposed) throw new ObjectDisposedException(nameof(PriorityReadWriteLock));
                if (!_activeWriter && _activeReaders == 0) { _activeWriter = true; return new WriteLockToken(this); }
                _writerQueue.Enqueue(waiter); _waitingWriters++;
            }

            try { await waiter.WaitAsync(timeout, cancellationToken); return new WriteLockToken(this); }
            catch { RemoveFromQueue(waiter); throw; }
        }

        private void RemoveFromQueue(ReaderWaiter waiter, Priority priority)
        {
            lock (_lock)
            {
                var queue = _readerQueues[priority];
                var newQueue = new Queue<ReaderWaiter>();
                while (queue.Count > 0) { var item = queue.Dequeue(); if (item != waiter) newQueue.Enqueue(item); }
                while (newQueue.Count > 0) queue.Enqueue(newQueue.Dequeue());
            }
        }

        private void RemoveFromQueue(WriterWaiter waiter)
        {
            lock (_lock)
            {
                var newQueue = new Queue<WriterWaiter>();
                while (_writerQueue.Count > 0) { var item = _writerQueue.Dequeue(); if (item != waiter) newQueue.Enqueue(item); }
                while (newQueue.Count > 0) _writerQueue.Enqueue(newQueue.Dequeue());
                _waitingWriters--;
            }
        }

        private void ExitReadLock()
        {
            lock (_lock) { _activeReaders--; WakeUpWaiters(); }
        }

        private void ExitWriteLock()
        {
            lock (_lock) { _activeWriter = false; WakeUpWaiters(); }
        }

        private void WakeUpWaiters()
        {
            if (!_activeWriter && _activeReaders == 0)
            {
                if (_writerQueue.Count > 0)
                {
                    var writer = _writerQueue.Dequeue(); _waitingWriters--; _activeWriter = true;
                    writer.Complete();
                }
                else
                {
                    foreach (var priority in new[] { Priority.High, Priority.Normal, Priority.Low })
                    {
                        var queue = _readerQueues[priority];
                        while (queue.Count > 0 && !_activeWriter)
                        {
                            var reader = queue.Dequeue(); _activeReaders++;
                            reader.Complete();
                        }
                    }
                }
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _isDisposed = true;
                foreach (var queue in _readerQueues.Values)
                    while (queue.Count > 0) queue.Dequeue().Fail(new ObjectDisposedException(nameof(PriorityReadWriteLock)));
                while (_writerQueue.Count > 0)
                    _writerQueue.Dequeue().Fail(new ObjectDisposedException(nameof(PriorityReadWriteLock)));
            }
        }

        private class ReadLockToken : IDisposable
        {
            private readonly PriorityReadWriteLock _lock; private bool _disposed;
            public ReadLockToken(PriorityReadWriteLock lockObj) { _lock = lockObj; }
            public void Dispose() { if (!_disposed) { _lock.ExitReadLock(); _disposed = true; } }
        }

        private class WriteLockToken : IDisposable
        {
            private readonly PriorityReadWriteLock _lock; private bool _disposed;
            public WriteLockToken(PriorityReadWriteLock lockObj) { _lock = lockObj; }
            public void Dispose() { if (!_disposed) { _lock.ExitWriteLock(); _disposed = true; } }
        }

        private class ReaderWaiter
        {
            private readonly TaskCompletionSource<bool> _tcs = new TaskCompletionSource<bool>();
            public Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
            {
                if (timeout != default)
                {
                    var timeoutCts = new CancellationTokenSource(timeout);
                    timeoutCts.Token.Register(() => _tcs.TrySetException(new TimeoutException()));
                }
                cancellationToken.Register(() => _tcs.TrySetCanceled(cancellationToken));
                return _tcs.Task;
            }
            public void Complete() => _tcs.TrySetResult(true);
            public void Fail(Exception ex) => _tcs.TrySetException(ex);
        }

        private class WriterWaiter
        {
            private readonly TaskCompletionSource<bool> _tcs = new TaskCompletionSource<bool>();
            public Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
            {
                if (timeout != default)
                {
                    var timeoutCts = new CancellationTokenSource(timeout);
                    timeoutCts.Token.Register(() => _tcs.TrySetException(new TimeoutException()));
                }
                cancellationToken.Register(() => _tcs.TrySetCanceled(cancellationToken));
                return _tcs.Task;
            }
            public void Complete() => _tcs.TrySetResult(true);
            public void Fail(Exception ex) => _tcs.TrySetException(ex);
        }
    }

    // 3.8: АСИНХРОННЫЕ ЦЕПОЧКИ ОПЕРАЦИЙ

    public class AsyncPipeline<TInput, TOutput>
    {
        private readonly List<IPipelineStage> _stages;
        private readonly CancellationTokenSource _cts;
        private readonly bool _continueOnFailure;

        public AsyncPipeline(bool continueOnFailure = false)
        {
            _stages = new List<IPipelineStage>(); _cts = new CancellationTokenSource(); _continueOnFailure = continueOnFailure;
        }

        public AsyncPipeline<TInput, TOutput> AddStage<TIn, TOut>(Func<TIn, CancellationToken, Task<TOut>> stage, string name = null)
        {
            var pipelineStage = new PipelineStage<TIn, TOut>(stage, name);
            if (_stages.Count > 0)
            {
                var lastStage = _stages[_stages.Count - 1];
                if (lastStage is PipelineStage<object, object> last)
                {
                    last.IsLast = false;
                }
            }
            pipelineStage.IsLast = true;
            _stages.Add(pipelineStage);
            return this;
        }

        public async Task<TOutput> ExecuteAsync(TInput input, CancellationToken cancellationToken = default)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
            object current = input;
            PipelineContext context = new PipelineContext();

            foreach (var stage in _stages)
            {
                try
                {
                    current = await stage.ExecuteAsync(current, linkedCts.Token);
                    context.MarkStageSuccess(stage.Name);
                }
                catch (Exception ex) when (_continueOnFailure)
                {
                    context.MarkStageFailure(stage.Name, ex);
                    if (stage.IsLast) break;
                }
                catch (Exception ex)
                {
                    context.MarkStageFailure(stage.Name, ex);
                    throw new PipelineException($"Stage '{stage.Name}' failed", ex, context);
                }
            }

            return (TOutput)current;
        }

        public void Cancel() => _cts.Cancel();
        public void Dispose() => _cts.Dispose();
    }

    public interface IPipelineStage
    {
        string Name { get; }
        bool IsLast { get; set; }
        Task<object> ExecuteAsync(object input, CancellationToken cancellationToken);
    }

    public class PipelineStage<TIn, TOut> : IPipelineStage
    {
        private readonly Func<TIn, CancellationToken, Task<TOut>> _stage;
        public string Name { get; }
        public bool IsLast { get; set; }

        public PipelineStage(Func<TIn, CancellationToken, Task<TOut>> stage, string name = null)
        {
            _stage = stage; Name = name ?? $"Stage_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
        }

        public async Task<object> ExecuteAsync(object input, CancellationToken cancellationToken) =>
            await _stage((TIn)input, cancellationToken);
    }

    public class PipelineContext
    {
        public List<StageResult> StageResults { get; } = new List<StageResult>();
        public bool IsSuccess { get; private set; } = true;

        public void MarkStageSuccess(string stageName) => StageResults.Add(new StageResult(stageName, true, null));
        public void MarkStageFailure(string stageName, Exception error) { StageResults.Add(new StageResult(stageName, false, error)); IsSuccess = false; }
    }

    public class StageResult
    {
        public string StageName { get; }
        public bool Success { get; }
        public Exception Error { get; }
        public DateTime Timestamp { get; }

        public StageResult(string stageName, bool success, Exception error)
        {
            StageName = stageName; Success = success; Error = error; Timestamp = DateTime.UtcNow;
        }
    }

    public class PipelineException : Exception
    {
        public PipelineContext Context { get; }
        public PipelineException(string message, Exception innerException, PipelineContext context) : base(message, innerException) { Context = context; }
    }

    // 3.9: ПАРАЛЛЕЛЬНАЯ ОБРАБОТКА КОЛЛЕКЦИЙ С PLINQ

    public static class ParallelProcessor
    {
        public static ParallelQuery<TOut> ProcessWithPLINQ<TIn, TOut>(
            this IEnumerable<TIn> source,
            Func<TIn, TOut> processor,
            ParallelOptions options = null)
        {
            options = options ?? new ParallelOptions();

            return source.AsParallel()
                .WithCancellation(options.CancellationToken)
                .WithDegreeOfParallelism(options.MaxDegreeOfParallelism)
                .WithExecutionMode(ParallelExecutionMode.ForceParallelism)
                .Select(processor);
        }

        public static async Task<List<TOut>> ProcessWithPLINQAsync<TIn, TOut>(
            this IAsyncEnumerable<TIn> source,
            Func<TIn, TOut> processor,
            ParallelOptions options = null,
            int? batchSize = null)
        {
            options = options ?? new ParallelOptions();
            var results = new ConcurrentBag<TOut>();
            var exceptions = new ConcurrentQueue<Exception>();

            await foreach (var batch in source.BatchAsync(batchSize ?? 1000))
            {
                if (options.CancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    var batchResults = batch.ProcessWithPLINQ(processor, options);
                    foreach (var result in batchResults)
                    {
                        results.Add(result);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (AggregateException ae)
                {
                    foreach (var ex in ae.InnerExceptions)
                    {
                        exceptions.Enqueue(ex);
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Enqueue(ex);
                }
            }

            if (exceptions.Count > 0)
            {
                throw new AggregateException(exceptions);
            }

            return results.ToList();
        }

        public static ParallelQuery<TOut> ProcessWithPLINQ<TIn, TOut>(
            this IEnumerable<TIn> source,
            Func<TIn, int, TOut> processor,
            ParallelOptions options = null)
        {
            options = options ?? new ParallelOptions();

            return source.AsParallel()
                .WithCancellation(options.CancellationToken)
                .WithDegreeOfParallelism(options.MaxDegreeOfParallelism)
                .Select((item, index) => processor(item, index));
        }

        public static async IAsyncEnumerable<List<T>> BatchAsync<T>(
            this IAsyncEnumerable<T> source, int batchSize)
        {
            var batch = new List<T>(batchSize);

            await foreach (var item in source)
            {
                batch.Add(item);
                if (batch.Count >= batchSize)
                {
                    yield return batch;
                    batch = new List<T>(batchSize);
                }
            }

            if (batch.Count > 0)
            {
                yield return batch;
            }
        }

        public static IEnumerable<List<T>> Batch<T>(this IEnumerable<T> source, int batchSize)
        {
            var batch = new List<T>(batchSize);

            foreach (var item in source)
            {
                batch.Add(item);
                if (batch.Count >= batchSize)
                {
                    yield return batch;
                    batch = new List<T>(batchSize);
                }
            }

            if (batch.Count > 0)
            {
                yield return batch;
            }
        }
    }

    public class ParallelProcessor<TInput, TOutput>
    {
        private readonly Func<TInput, TOutput> _processor;
        private readonly ParallelOptions _options;

        public ParallelProcessor(Func<TInput, TOutput> processor, ParallelOptions options = null)
        {
            _processor = processor;
            _options = options ?? new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
        }

        public List<TOutput> Process(IEnumerable<TInput> inputs)
        {
            var results = new ConcurrentBag<TOutput>();
            var exceptions = new ConcurrentQueue<Exception>();

            try
            {
                Parallel.ForEach(inputs, _options, (input, state) =>
                {
                    if (_options.CancellationToken.IsCancellationRequested)
                    {
                        state.Stop();
                        return;
                    }

                    try
                    {
                        var result = _processor(input);
                        results.Add(result);
                    }
                    catch (Exception ex)
                    {
                        exceptions.Enqueue(ex);
                        if (!_options.CancellationToken.IsCancellationRequested)
                        {
                            state.Stop();
                        }
                    }
                });
            }
            catch (OperationCanceledException)
            {
            }

            if (exceptions.Count > 0)
            {
                throw new AggregateException(exceptions);
            }

            return results.ToList();
        }

        public async Task<List<TOutput>> ProcessAsync(IAsyncEnumerable<TInput> inputs)
        {
            var results = new ConcurrentBag<TOutput>();
            var exceptions = new ConcurrentQueue<Exception>();

            await foreach (var batch in inputs.BatchAsync(1000))
            {
                if (_options.CancellationToken.IsCancellationRequested)
                    break;

                var batchResults = Process(batch);
                foreach (var result in batchResults)
                {
                    results.Add(result);
                }
            }

            if (exceptions.Count > 0)
            {
                throw new AggregateException(exceptions);
            }

            return results.ToList();
        }
    }

    // 3.10: РЕАКТИВНОЕ ПРОГРАММИРОВАНИЕ С RX

    public interface IObservable<T>
    {
        IDisposable Subscribe(IObserver<T> observer);
    }

    public interface IObserver<T>
    {
        void OnNext(T value);
        void OnError(Exception error);
        void OnCompleted();
    }

    public class Observable<T> : IObservable<T>, IDisposable
    {
        private readonly List<IObserver<T>> _observers = new List<IObserver<T>>();
        private readonly object _observersLock = new object();
        private bool _isCompleted;
        private Exception _error;

        public IDisposable Subscribe(IObserver<T> observer)
        {
            if (observer == null)
                throw new ArgumentNullException(nameof(observer));

            lock (_observersLock)
            {
                if (_isCompleted)
                {
                    observer.OnCompleted();
                    return new EmptyDisposable();
                }

                if (_error != null)
                {
                    observer.OnError(_error);
                    return new EmptyDisposable();
                }

                _observers.Add(observer);
            }

            return new Subscription(this, observer);
        }

        public void OnNext(T value)
        {
            List<IObserver<T>> observersCopy;
            lock (_observersLock)
            {
                if (_isCompleted || _error != null)
                    return;

                observersCopy = new List<IObserver<T>>(_observers);
            }

            foreach (var observer in observersCopy)
            {
                try
                {
                    observer.OnNext(value);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Observer error: {ex.Message}");
                }
            }
        }

        public void OnError(Exception error)
        {
            List<IObserver<T>> observersCopy;
            lock (_observersLock)
            {
                if (_isCompleted || _error != null)
                    return;

                _error = error;
                observersCopy = new List<IObserver<T>>(_observers);
                _observers.Clear();
            }

            foreach (var observer in observersCopy)
            {
                try
                {
                    observer.OnError(error);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Observer error handler failed: {ex.Message}");
                }
            }
        }

        public void OnCompleted()
        {
            List<IObserver<T>> observersCopy;
            lock (_observersLock)
            {
                if (_isCompleted || _error != null)
                    return;

                _isCompleted = true;
                observersCopy = new List<IObserver<T>>(_observers);
                _observers.Clear();
            }

            foreach (var observer in observersCopy)
            {
                try
                {
                    observer.OnCompleted();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Observer completion handler failed: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            OnCompleted();
        }

        private class Subscription : IDisposable
        {
            private readonly Observable<T> _observable;
            private readonly IObserver<T> _observer;
            private bool _isDisposed;

            public Subscription(Observable<T> observable, IObserver<T> observer)
            {
                _observable = observable;
                _observer = observer;
            }

            public void Dispose()
            {
                if (!_isDisposed)
                {
                    lock (_observable._observersLock)
                    {
                        _observable._observers.Remove(_observer);
                    }
                    _isDisposed = true;
                }
            }
        }

        private class EmptyDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }

    public static class ObservableExtensions
    {
        public static IObservable<T> Where<T>(this IObservable<T> source, Func<T, bool> predicate)
        {
            var observable = new Observable<T>();

            source.Subscribe(new Observer<T>(
                onNext: value =>
                {
                    if (predicate(value))
                    {
                        observable.OnNext(value);
                    }
                },
                onError: observable.OnError,
                onCompleted: observable.OnCompleted
            ));

            return observable;
        }

        public static IObservable<TResult> Select<TSource, TResult>(
            this IObservable<TSource> source, Func<TSource, TResult> selector)
        {
            var observable = new Observable<TResult>();

            source.Subscribe(new Observer<TSource>(
                onNext: value =>
                {
                    var result = selector(value);
                    observable.OnNext(result);
                },
                onError: observable.OnError,
                onCompleted: observable.OnCompleted
            ));

            return observable;
        }

        public static IObservable<T> Buffer<T>(this IObservable<T> source, TimeSpan timeSpan, int count)
        {
            var observable = new Observable<T>();
            var buffer = new List<T>();
            var timer = new Timer(_ =>
            {
                if (buffer.Count > 0)
                {
                    foreach (var item in buffer)
                    {
                        observable.OnNext(item);
                    }
                    buffer.Clear();
                }
            }, null, timeSpan, timeSpan);

            source.Subscribe(new Observer<T>(
                onNext: value =>
                {
                    buffer.Add(value);
                    if (buffer.Count >= count)
                    {
                        foreach (var item in buffer)
                        {
                            observable.OnNext(item);
                        }
                        buffer.Clear();
                    }
                },
                onError: error =>
                {
                    timer.Dispose();
                    observable.OnError(error);
                },
                onCompleted: () =>
                {
                    timer.Dispose();
                    if (buffer.Count > 0)
                    {
                        foreach (var item in buffer)
                        {
                            observable.OnNext(item);
                        }
                    }
                    observable.OnCompleted();
                }
            ));

            return observable;
        }
    }

    public class Observer<T> : IObserver<T>
    {
        private readonly Action<T> _onNext;
        private readonly Action<Exception> _onError;
        private readonly Action _onCompleted;

        public Observer(Action<T> onNext, Action<Exception> onError = null, Action onCompleted = null)
        {
            _onNext = onNext ?? throw new ArgumentNullException(nameof(onNext));
            _onError = onError ?? (ex => { });
            _onCompleted = onCompleted ?? (() => { });
        }

        public void OnNext(T value) => _onNext(value);
        public void OnError(Exception error) => _onError(error);
        public void OnCompleted() => _onCompleted();
    }

    // 3.11: РАСПРЕДЕЛЕННАЯ ОБРАБОТКА (MAP-REDUCE)

    public class MapReduce<TInput, TKey, TValue, TResult>
    {
        private readonly Func<TInput, IEnumerable<KeyValuePair<TKey, TValue>>> _mapper;
        private readonly Func<TKey, IEnumerable<TValue>, TResult> _reducer;
        private readonly IEqualityComparer<TKey> _keyComparer;
        private readonly int _maxDegreeOfParallelism;

        public MapReduce(
            Func<TInput, IEnumerable<KeyValuePair<TKey, TValue>>> mapper,
            Func<TKey, IEnumerable<TValue>, TResult> reducer,
            IEqualityComparer<TKey> keyComparer = null,
            int maxDegreeOfParallelism = -1)
        {
            _mapper = mapper;
            _reducer = reducer;
            _keyComparer = keyComparer ?? EqualityComparer<TKey>.Default;
            _maxDegreeOfParallelism = maxDegreeOfParallelism == -1 ?
                Environment.ProcessorCount : maxDegreeOfParallelism;
        }

        public async Task<Dictionary<TKey, TResult>> ExecuteAsync(
            IEnumerable<TInput> inputs,
            CancellationToken cancellationToken = default)
        {
            // Map phase
            var mapResults = new ConcurrentBag<KeyValuePair<TKey, TValue>>();

            await Parallel.ForEachAsync(inputs,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = _maxDegreeOfParallelism,
                    CancellationToken = cancellationToken
                },
                async (input, ct) =>
                {
                    var mapped = _mapper(input);
                    foreach (var item in mapped)
                    {
                        mapResults.Add(item);
                    }
                });

            // Shuffle phase
            var grouped = mapResults
                .GroupBy(x => x.Key, _keyComparer)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Value).ToList());

            // Reduce phase
            var reduceResults = new ConcurrentDictionary<TKey, TResult>();

            await Parallel.ForEachAsync(grouped,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = _maxDegreeOfParallelism,
                    CancellationToken = cancellationToken
                },
                async (group, ct) =>
                {
                    var result = _reducer(group.Key, group.Value);
                    reduceResults[group.Key] = result;
                });

            return reduceResults.ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        public async Task<Dictionary<TKey, TResult>> ExecuteAsync(
            IAsyncEnumerable<TInput> inputs,
            CancellationToken cancellationToken = default)
        {
            var mapResults = new ConcurrentBag<KeyValuePair<TKey, TValue>>();

            await foreach (var batch in inputs.BatchAsync(1000))
            {
                await Parallel.ForEachAsync(batch,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = _maxDegreeOfParallelism,
                        CancellationToken = cancellationToken
                    },
                    async (input, ct) =>
                    {
                        var mapped = _mapper(input);
                        foreach (var item in mapped)
                        {
                            mapResults.Add(item);
                        }
                    });
            }

            var grouped = mapResults
                .GroupBy(x => x.Key, _keyComparer)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Value).ToList());

            var reduceResults = new ConcurrentDictionary<TKey, TResult>();

            await Parallel.ForEachAsync(grouped,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = _maxDegreeOfParallelism,
                    CancellationToken = cancellationToken
                },
                async (group, ct) =>
                {
                    var result = _reducer(group.Key, group.Value);
                    reduceResults[group.Key] = result;
                });

            return reduceResults.ToDictionary(kv => kv.Key, kv => kv.Value);
        }
    }

    // 3.12: ТАЙМЕР С ПРОИЗВОЛЬНОЙ ТОЧНОСТЬЮ

    public class HighPrecisionTimer : IDisposable
    {
        private readonly TimeSpan _interval;
        private readonly Action _callback;
        private readonly Thread _timerThread;
        private readonly Stopwatch _stopwatch;
        private readonly ManualResetEventSlim _stopEvent;
        private readonly bool _useHighResolution;

        private long _totalTicks;
        private long _missedTicks;
        private double _averageJitter;
        private volatile bool _isRunning;
        private volatile bool _isDisposed;

        public HighPrecisionTimer(TimeSpan interval, Action callback, bool useHighResolution = true)
        {
            _interval = interval;
            _callback = callback;
            _useHighResolution = useHighResolution;
            _stopwatch = new Stopwatch();
            _stopEvent = new ManualResetEventSlim();
            _timerThread = new Thread(TimerLoop)
            {
                Name = "HighPrecisionTimer",
                Priority = useHighResolution ? ThreadPriority.Highest : ThreadPriority.Normal,
                IsBackground = true
            };
        }

        public void Start()
        {
            if (_isRunning || _isDisposed)
                return;

            _isRunning = true;
            _timerThread.Start();
        }

        public void Stop()
        {
            _isRunning = false;
            if (!_isDisposed)
            {
                _stopEvent?.Set();
            }
        }

        private void TimerLoop()
        {
            try
            {
                _stopwatch.Start();
                var nextTick = _stopwatch.ElapsedTicks + (long)(_interval.TotalSeconds * Stopwatch.Frequency);

                while (_isRunning && !_isDisposed)
                {
                    var currentTicks = _stopwatch.ElapsedTicks;

                    if (currentTicks >= nextTick)
                    {
                        // Вычисляем jitter
                        var jitter = currentTicks - nextTick;
                        if (_totalTicks > 0)
                        {
                            _averageJitter = (_averageJitter * _totalTicks + jitter) / (_totalTicks + 1);
                        }
                        else
                        {
                            _averageJitter = jitter;
                        }

                        if (jitter > _interval.Ticks * 0.1) // >10% интервала
                        {
                            Interlocked.Increment(ref _missedTicks);
                        }

                        try
                        {
                            _callback();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Timer callback error: {ex.Message}");
                        }

                        _totalTicks++;
                        nextTick += (long)(_interval.TotalSeconds * Stopwatch.Frequency);
                    }
                    else
                    {
                        // Точное ожидание с адаптивной калибровкой
                        var sleepTime = (nextTick - currentTicks) * 1000 / Stopwatch.Frequency - 1;
                        if (sleepTime > 0)
                        {
                            if (_useHighResolution)
                            {
                                // Используем SpinWait для максимальной точности
                                var spinWait = new SpinWait();
                                var targetTicks = nextTick;
                                while (_stopwatch.ElapsedTicks < targetTicks && _isRunning && !_isDisposed)
                                {
                                    spinWait.SpinOnce();
                                }
                            }
                            else
                            {
                                Thread.Sleep(Math.Max(1, (int)sleepTime));
                            }
                        }
                    }

                    // Проверка остановки с безопасным использованием _stopEvent
                    if (!_isDisposed && _stopEvent.Wait(0))
                        break;
                }
            }
            catch (ObjectDisposedException)
            {
                // Игнорируем исключение при освобождении
            }
            finally
            {
                _stopwatch.Stop();
            }
        }

        public TimerMetrics GetMetrics()
        {
            return new TimerMetrics
            {
                TotalTicks = Interlocked.Read(ref _totalTicks),
                MissedTicks = Interlocked.Read(ref _missedTicks),
                AverageJitter = TimeSpan.FromTicks((long)_averageJitter),
                MissedRatio = _totalTicks > 0 ? (double)_missedTicks / _totalTicks : 0
            };
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _isRunning = false;

            try
            {
                _stopEvent?.Set();
            }
            catch (ObjectDisposedException)
            {
                // Игнорируем, если уже освобожден
            }

            try
            {
                if (_timerThread.IsAlive && !_timerThread.Join(1000))
                {
                    try
                    {
                        _timerThread.Interrupt();
                    }
                    catch (ThreadStateException)
                    {
                        // Игнорируем, если поток уже завершен
                    }
                }
            }
            finally
            {
                _stopEvent?.Dispose();
            }
        }
    }

    public class TimerMetrics
    {
        public long TotalTicks { get; set; }
        public long MissedTicks { get; set; }
        public TimeSpan AverageJitter { get; set; }
        public double MissedRatio { get; set; }
    }

    // Таймер с компенсацией дрейфа
    public class DriftCompensatingTimer : IDisposable
    {
        private readonly TimeSpan _interval;
        private readonly Action _callback;
        private readonly HighPrecisionTimer _timer;
        private long _executionCount;
        private TimeSpan _totalDrift;

        public DriftCompensatingTimer(TimeSpan interval, Action callback)
        {
            _interval = interval;
            _callback = callback;
            _timer = new HighPrecisionTimer(interval, CompensatedCallback, true);
        }

        private void CompensatedCallback()
        {
            var expectedTime = _interval * _executionCount;
            var actualTime = TimeSpan.FromTicks(_timer.GetMetrics().TotalTicks * _interval.Ticks);
            var drift = actualTime - expectedTime;

            _totalDrift += drift;

            // Компенсация дрейфа
            if (Math.Abs(_totalDrift.TotalMilliseconds) > _interval.TotalMilliseconds * 0.1)
            {
                // Пропускаем или добавляем тик для компенсации
                if (_totalDrift > TimeSpan.Zero)
                {
                    _totalDrift -= _interval;
                    _executionCount++;
                    return;
                }
            }

            try
            {
                _callback();
            }
            finally
            {
                _executionCount++;
            }
        }

        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();
        public void Dispose() => _timer.Dispose();

        public TimeSpan CurrentDrift => _totalDrift;
    }

    // 3.13: СИСТЕМА С TIMEOUT И DEADLINE

    public class DeadlineContext : IDisposable
    {
        private readonly CancellationTokenSource _deadlineCts;
        private readonly CancellationTokenSource _linkedCts;
        private readonly DateTime _deadline;

        public DeadlineContext(TimeSpan timeout)
        {
            _deadline = DateTime.UtcNow + timeout;
            _deadlineCts = new CancellationTokenSource(timeout);
            _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_deadlineCts.Token);
        }

        public DeadlineContext(DateTime deadline)
        {
            _deadline = deadline;
            var timeout = deadline - DateTime.UtcNow;
            if (timeout <= TimeSpan.Zero)
                throw new ArgumentException("Deadline must be in the future");

            _deadlineCts = new CancellationTokenSource(timeout);
            _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_deadlineCts.Token);
        }

        public CancellationToken Token => _linkedCts.Token;
        public DateTime Deadline => _deadline;
        public TimeSpan Remaining => _deadline - DateTime.UtcNow;

        public bool IsExpired => DateTime.UtcNow >= _deadline;

        public void ThrowIfExpired()
        {
            if (IsExpired)
                throw new TimeoutException($"Deadline expired at {_deadline}");
        }

        public CancellationToken CreateLinkedToken(CancellationToken externalToken)
        {
            return CancellationTokenSource.CreateLinkedTokenSource(
                Token, externalToken).Token;
        }

        public void Dispose()
        {
            _deadlineCts?.Dispose();
            _linkedCts?.Dispose();
        }
    }

    public static class DeadlineExtensions
    {
        public static async Task<T> WithDeadline<T>(
            this Task<T> task,
            DateTime deadline,
            CancellationToken cancellationToken = default)
        {
            using var deadlineContext = new DeadlineContext(deadline);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                deadlineContext.Token, cancellationToken);

            try
            {
                return await task.WithCancellation(linkedCts.Token);
            }
            catch (OperationCanceledException) when (deadlineContext.IsExpired)
            {
                throw new TimeoutException($"Operation timed out at deadline {deadline}");
            }
        }

        public static async Task<T> WithTimeout<T>(
            this Task<T> task,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            return await task.WithDeadline(DateTime.UtcNow + timeout, cancellationToken);
        }

        public static async Task WithDeadline(
            this Task task,
            DateTime deadline,
            CancellationToken cancellationToken = default)
        {
            using var deadlineContext = new DeadlineContext(deadline);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                deadlineContext.Token, cancellationToken);

            try
            {
                await task.WithCancellation(linkedCts.Token);
            }
            catch (OperationCanceledException) when (deadlineContext.IsExpired)
            {
                throw new TimeoutException($"Operation timed out at deadline {deadline}");
            }
        }

        private static async Task<T> WithCancellation<T>(this Task<T> task, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();

            using (cancellationToken.Register(s =>
                ((TaskCompletionSource<bool>)s).TrySetResult(true), tcs))
            {
                if (task != await Task.WhenAny(task, tcs.Task))
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }

            return await task;
        }

        private static async Task WithCancellation(this Task task, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();

            using (cancellationToken.Register(s =>
                ((TaskCompletionSource<bool>)s).TrySetResult(true), tcs))
            {
                if (task != await Task.WhenAny(task, tcs.Task))
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }

            await task;
        }
    }

    // 3.14: КОНВЕРТЕР CALLBACK-BASED КОДА В ASYNC/AWAIT

    public static class CallbackToAsyncConverter
    {
        public static Task<T> FromCallback<T>(
            Action<Action<T>> callbackMethod,
            CancellationToken cancellationToken = default)
        {
            var tcs = new TaskCompletionSource<T>();

            try
            {
                callbackMethod(result =>
                {
                    if (!tcs.Task.IsCompleted)
                    {
                        tcs.TrySetResult(result);
                    }
                });
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }

            return AttachCancellation(tcs, cancellationToken);
        }

        public static Task<T> FromCallback<T>(
            Action<Action<T, Exception>> callbackMethod,
            CancellationToken cancellationToken = default)
        {
            var tcs = new TaskCompletionSource<T>();

            try
            {
                callbackMethod((result, error) =>
                {
                    if (error != null)
                    {
                        tcs.TrySetException(error);
                    }
                    else
                    {
                        tcs.TrySetResult(result);
                    }
                });
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }

            return AttachCancellation(tcs, cancellationToken);
        }

        public static Task FromCallback(
            Action<Action> callbackMethod,
            CancellationToken cancellationToken = default)
        {
            var tcs = new TaskCompletionSource<bool>();

            try
            {
                callbackMethod(() =>
                {
                    tcs.TrySetResult(true);
                });
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }

            return AttachCancellation(tcs, cancellationToken);
        }

        public static Task FromCallback(
            Action<Action<Exception>> callbackMethod,
            CancellationToken cancellationToken = default)
        {
            var tcs = new TaskCompletionSource<bool>();

            try
            {
                callbackMethod(error =>
                {
                    if (error != null)
                    {
                        tcs.TrySetException(error);
                    }
                    else
                    {
                        tcs.TrySetResult(true);
                    }
                });
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }

            return AttachCancellation(tcs, cancellationToken);
        }

        private static Task<T> AttachCancellation<T>(
            TaskCompletionSource<T> tcs,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(() =>
                {
                    tcs.TrySetCanceled(cancellationToken);
                });
            }

            return tcs.Task;
        }
    }

    // Адаптер для событий
    public class EventToAsyncAdapter<TEventArgs>
    {
        private readonly Action<EventHandler<TEventArgs>> _subscribe;
        private readonly Action<EventHandler<TEventArgs>> _unsubscribe;
        private readonly Func<TEventArgs, bool> _completionCondition;

        public EventToAsyncAdapter(
            Action<EventHandler<TEventArgs>> subscribe,
            Action<EventHandler<TEventArgs>> unsubscribe,
            Func<TEventArgs, bool> completionCondition = null)
        {
            _subscribe = subscribe;
            _unsubscribe = unsubscribe;
            _completionCondition = completionCondition ?? (_ => true);
        }

        public Task<TEventArgs> WaitAsync(TimeSpan timeout = default)
        {
            return WaitAsync(timeout, CancellationToken.None);
        }

        public Task<TEventArgs> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<TEventArgs>();
            var cts = timeout == default ? null : new CancellationTokenSource(timeout);
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, cts?.Token ?? CancellationToken.None);

            EventHandler<TEventArgs> handler = null;
            handler = (sender, args) =>
            {
                if (_completionCondition(args))
                {
                    _unsubscribe(handler);
                    tcs.TrySetResult(args);
                }
            };

            _subscribe(handler);

            linkedCts.Token.Register(() =>
            {
                _unsubscribe(handler);
                tcs.TrySetCanceled(linkedCts.Token);
            });

            return tcs.Task;
        }
    }

    // 3.15: СИСТЕМА ПУЛА ОБЪЕКТОВ ДЛЯ ПЕРЕИСПОЛЬЗОВАНИЯ

    public interface IObjectPool<T> : IDisposable where T : class
    {
        T Get();
        void Return(T item);
        int Count { get; }
        int InUseCount { get; }
    }

    public class ObjectPool<T> : IObjectPool<T> where T : class
    {
        private readonly ConcurrentBag<T> _objects;
        private readonly Func<T> _objectGenerator;
        private readonly Action<T> _objectReset;
        private readonly int _maxSize;
        private readonly bool _trackUsage;

        private int _createdCount;
        private int _inUseCount;
        private bool _isDisposed;

        public ObjectPool(
            Func<T> objectGenerator,
            int maxSize = 100,
            Action<T> objectReset = null,
            bool trackUsage = false)
        {
            _objectGenerator = objectGenerator ?? throw new ArgumentNullException(nameof(objectGenerator));
            _objectReset = objectReset;
            _maxSize = maxSize;
            _trackUsage = trackUsage;
            _objects = new ConcurrentBag<T>();
        }

        public T Get()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(ObjectPool<T>));

            if (_objects.TryTake(out T item))
            {
                if (_trackUsage)
                    Interlocked.Increment(ref _inUseCount);

                return item;
            }

            if (_createdCount < _maxSize)
            {
                Interlocked.Increment(ref _createdCount);
                item = _objectGenerator();

                if (_trackUsage)
                    Interlocked.Increment(ref _inUseCount);

                return item;
            }

            // Достигнут лимит - ждем освобождения объекта
            return WaitForObject();
        }

        private T WaitForObject()
        {
            var spinWait = new SpinWait();
            T item;
            while (!_objects.TryTake(out item) && !_isDisposed)
            {
                spinWait.SpinOnce();
                if (spinWait.Count > 100) // Превышен лимит ожидания
                {
                    throw new InvalidOperationException("Object pool exhausted");
                }
            }

            if (_trackUsage && !_isDisposed && item != null)
                Interlocked.Increment(ref _inUseCount);

            return item ?? throw new ObjectDisposedException(nameof(ObjectPool<T>));
        }

        public void Return(T item)
        {
            if (_isDisposed)
            {
                (item as IDisposable)?.Dispose();
                return;
            }

            if (item == null)
                throw new ArgumentNullException(nameof(item));

            _objectReset?.Invoke(item);

            if (_trackUsage)
                Interlocked.Decrement(ref _inUseCount);

            _objects.Add(item);
        }

        public int Count => _objects.Count;
        public int InUseCount => _trackUsage ? _inUseCount : -1;
        public int CreatedCount => _createdCount;

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;

                while (_objects.TryTake(out T item))
                {
                    (item as IDisposable)?.Dispose();
                }
            }
        }
    }

    public class PooledObject<T> : IDisposable where T : class
    {
        private readonly IObjectPool<T> _pool;
        private T _value;

        public PooledObject(IObjectPool<T> pool)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _value = pool.Get();
        }

        public T Value => _value ?? throw new ObjectDisposedException(nameof(PooledObject<T>));

        public void Dispose()
        {
            if (_value != null)
            {
                _pool.Return(_value);
                _value = null;
            }
        }
    }

    // Пул с проверкой утечек
    public class LeakTrackingObjectPool<T> : IObjectPool<T> where T : class
    {
        private readonly IObjectPool<T> _innerPool;
        private readonly ConcurrentDictionary<T, string> _leasedObjects;
        private readonly Timer _leakChecker;

        public LeakTrackingObjectPool(IObjectPool<T> innerPool, TimeSpan leakCheckInterval)
        {
            _innerPool = innerPool;
            _leasedObjects = new ConcurrentDictionary<T, string>();

            _leakChecker = new Timer(CheckLeaks, null, leakCheckInterval, leakCheckInterval);
        }

        public T Get()
        {
            var item = _innerPool.Get();
            _leasedObjects[item] = Environment.StackTrace;
            return item;
        }

        public void Return(T item)
        {
            _leasedObjects.TryRemove(item, out _);
            _innerPool.Return(item);
        }

        private void CheckLeaks(object state)
        {
            if (_leasedObjects.IsEmpty)
                return;

            Console.WriteLine($"Potential memory leaks detected: {_leasedObjects.Count} objects not returned");

            foreach (var kvp in _leasedObjects)
            {
                Console.WriteLine($"Leaked object: {kvp.Key}");
                Console.WriteLine($"Allocation stack: {kvp.Value}");
            }
        }

        public int Count => _innerPool.Count;
        public int InUseCount => _innerPool.InUseCount;

        public void Dispose()
        {
            _leakChecker?.Dispose();
            _innerPool?.Dispose();
        }
    }
}

// Расширения для TimeSpan
public static class TimeSpanExtensions
{
    public static TimeSpan Multiply(this TimeSpan timeSpan, int multiplier) =>
        TimeSpan.FromTicks(timeSpan.Ticks * multiplier);
}

// Пример использования всех компонентов
public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== Testing All Thread-Safe Structures ===");

        // 1. Тестирование кэша
        await TestCache();

        // 2. Тестирование Map-Reduce
        await TestMapReduce();

        // 3. Тестирование реактивного программирования
        await TestReactiveProgramming();

        // 4. Тестирование таймера высокой точности
        await TestHighPrecisionTimer();

        // 5. Тестирование дедлайнов
        await TestDeadlines();

        // 6. Тестирование callback конвертера
        await TestCallbackConverter();

        // 7. Тестирование пула объектов
        await TestObjectPool();

        Console.WriteLine("All tests completed successfully!");
    }

    private static async Task TestCache()
    {
        Console.WriteLine("\n1. Testing Cache...");

        var cache = new ThreadSafeStructures.ThreadSafeCache<string, string>(3, ThreadSafeStructures.CachePolicy.LRU);

        cache.Put("key1", "value1");
        cache.Put("key2", "value2", TimeSpan.FromSeconds(1));
        cache.Put("key3", "value3");

        if (cache.TryGet("key1", out var value))
            Console.WriteLine($"Found: key1 = {value}");

        // Симуляция вытеснения
        cache.Put("key4", "value4");

        var stats = cache.GetStats();
        Console.WriteLine($"Cache stats: Hits={stats.Hits}, Misses={stats.Misses}, HitRatio={stats.HitRatio:P2}");
    }

    private static async Task TestMapReduce()
    {
        Console.WriteLine("\n2. Testing Map-Reduce...");

        var mapReduce = new ThreadSafeStructures.MapReduce<string, string, int, int>(
            // Mapper: разбиваем текст на слова и считаем каждое слово как 1
            text => text.Split(' ')
                       .Select(word => new KeyValuePair<string, int>(word.ToLower(), 1)),
            // Reducer: суммируем количество вхождений каждого слова
            (word, counts) => counts.Sum()
        );

        var texts = new[]
        {
            "hello world hello",
            "world test hello",
            "test test test"
        };

        var result = await mapReduce.ExecuteAsync(texts);

        foreach (var kvp in result)
        {
            Console.WriteLine($"Word: '{kvp.Key}', Count: {kvp.Value}");
        }
    }

    private static async Task TestReactiveProgramming()
    {
        Console.WriteLine("\n3. Testing Reactive Programming...");

        var observable = new ThreadSafeStructures.Observable<int>();

        observable.Subscribe(new ThreadSafeStructures.Observer<int>(
            onNext: value => Console.WriteLine($"Received: {value}"),
            onError: ex => Console.WriteLine($"Error: {ex.Message}"),
            onCompleted: () => Console.WriteLine("Completed")
        ));

        // Генерация событий
        for (int i = 1; i <= 5; i++)
        {
            observable.OnNext(i);
            await Task.Delay(100);
        }

        observable.OnCompleted();
    }

    private static async Task TestHighPrecisionTimer()
    {
        Console.WriteLine("\n4. Testing High Precision Timer...");

        int tickCount = 0;
        var timer = new ThreadSafeStructures.HighPrecisionTimer(
            TimeSpan.FromMilliseconds(100),
            () => {
                tickCount++;
                Console.WriteLine($"Timer tick: {tickCount}");
            },
            useHighResolution: true
        );

        timer.Start();
        await Task.Delay(1000);
        timer.Stop();

        var metrics = timer.GetMetrics();
        Console.WriteLine($"Timer metrics: TotalTicks={metrics.TotalTicks}, MissedTicks={metrics.MissedTicks}");

        timer.Dispose();
    }

    private static async Task TestDeadlines()
    {
        Console.WriteLine("\n5. Testing Deadlines...");

        try
        {
            // Задача, которая выполняется дольше дедлайна
            var longTask = Task.Delay(2000).ContinueWith(_ => "Completed");

            // Пытаемся выполнить с дедлайном 1 секунда
            var result = await longTask.WithTimeout(TimeSpan.FromSeconds(1));
            Console.WriteLine($"Result: {result}");
        }
        catch (TimeoutException ex)
        {
            Console.WriteLine($"Expected timeout: {ex.Message}");
        }
    }

    private static async Task TestCallbackConverter()
    {
        Console.WriteLine("\n6. Testing Callback Converter...");

        // Симуляция callback-based API
        void CallbackBasedMethod(Action<string> callback)
        {
            Task.Delay(100).ContinueWith(_ => callback("Callback result"));
        }

        // Конвертация в async/await
        var result = await ThreadSafeStructures.CallbackToAsyncConverter.FromCallback<string>(
            callback => CallbackBasedMethod(callback)
        );

        Console.WriteLine($"Converted result: {result}");
    }

    private static async Task TestObjectPool()
    {
        Console.WriteLine("\n7. Testing Object Pool...");

        var pool = new ThreadSafeStructures.ObjectPool<StringBuilder>(
            () => new StringBuilder(),
            5,
            sb => sb.Clear(),
            true);

        var tasks = new List<Task>();

        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                using var pooled = new ThreadSafeStructures.PooledObject<StringBuilder>(pool);
                pooled.Value.Append($"Task {Task.CurrentId}");
                Console.WriteLine(pooled.Value.ToString());
            }));
        }

        await Task.WhenAll(tasks);

        Console.WriteLine($"Pool stats: Count={pool.Count}, InUse={pool.InUseCount}, Created={pool.CreatedCount}");

        pool.Dispose();
    }
}
