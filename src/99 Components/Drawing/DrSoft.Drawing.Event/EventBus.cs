// ============================================================
// IEventBus.cs  —  接口定义
// ============================================================
namespace DrSoft.Drawing.Event
{
    public interface IEventBus
    {
        // ── 无返回值同步订阅 ──────────────────────────────
        void Subscribe<T>(Action<T> handler, bool replayLast = false) where T : IEvent;
        void Unsubscribe<T>(Action<T> handler) where T : IEvent;
        void Publish<T>(T eventData) where T : IEvent;

        // ── 带返回值同步订阅（Request / Response 模式）───
        void Subscribe<T, TResult>(Func<T, TResult> handler) where T : IEvent;
        void Unsubscribe<T, TResult>(Func<T, TResult> handler) where T : IEvent;

        /// <summary>收集所有处理器的返回值</summary>
        IReadOnlyList<TResult> Publish<T, TResult>(T eventData) where T : IEvent;

        // ── 无返回值异步订阅 ─────────────────────────────
        void Subscribe<T>(Func<T, CancellationToken, Task> handler) where T : IEvent;
        void Unsubscribe<T>(Func<T, CancellationToken, Task> handler) where T : IEvent;

        /// <summary>并行触发所有异步处理器，等待全部完成</summary>
        Task PublishAsync<T>(T eventData, CancellationToken ct = default) where T : IEvent;

        // ── 带返回值异步订阅 ─────────────────────────────
        void Subscribe<T, TResult>(Func<T, CancellationToken, Task<TResult>> handler) where T : IEvent;
        void Unsubscribe<T, TResult>(Func<T, CancellationToken, Task<TResult>> handler) where T : IEvent;

        /// <summary>并行触发，收集所有异步处理器的返回值</summary>
        Task<IReadOnlyList<TResult>> PublishAsync<T, TResult>(T eventData, CancellationToken ct = default) where T : IEvent;
    }


    // ============================================================
    // EventBus.cs  —  接口实现
    // ============================================================
    public sealed class EventBus : IEventBus
    {
        public static IEventBus Instance { get; } = new EventBus();

        private readonly Dictionary<(Type EventType, Type HandlerType), HashSet<Delegate>> _handlers = new();
        private readonly Dictionary<Type, object> _lastEvents = new();
        private readonly object _lock = new();

        // ── 私有辅助 ──────────────────────────────────────────────

        private HashSet<Delegate> GetOrCreate(Type eventType, Type handlerType)
        {
            var key = (eventType, handlerType);
            if (!_handlers.TryGetValue(key, out var set))
                _handlers[key] = set = new();
            return set;
        }

        private bool TryGet(Type eventType, Type handlerType, out HashSet<Delegate> set)
            => _handlers.TryGetValue((eventType, handlerType), out set!);

        private void AddHandler<THandler>(Type eventType, THandler handler) where THandler : Delegate
        {
            lock (_lock)
                GetOrCreate(eventType, typeof(THandler)).Add(handler); // 重复自动忽略
        }

        private void RemoveHandler<THandler>(Type eventType, THandler handler) where THandler : Delegate
        {
            lock (_lock)
            {
                if (TryGet(eventType, typeof(THandler), out var set))
                    set.Remove(handler);
            }
        }

        // Snapshot 把 HashSet 转成 List 再遍历，避免持锁期间执行 handler
        private List<Delegate> Snapshot(Type eventType, Type handlerType)
        {
            lock (_lock)
                return TryGet(eventType, handlerType, out var set)
                    ? new List<Delegate>(set)
                    : new List<Delegate>();
        }

        // ── 1. 无返回值同步 ───────────────────────────────────────

        public void Subscribe<T>(Action<T> handler, bool replayLast = false) where T : IEvent
        {
            AddHandler(typeof(T), handler);
            if (replayLast)
            {
                lock (_lock)
                {
                    if (_lastEvents.TryGetValue(typeof(T), out var lastEvent) && lastEvent is T evt)
                    {
                        handler(evt);
                    }
                }
            }
        }
        public void Unsubscribe<T>(Action<T> handler) where T : IEvent => RemoveHandler(typeof(T), handler);

        public void Publish<T>(T eventData) where T : IEvent
        {
            lock (_lock)
            {
                _lastEvents[typeof(T)] = eventData;
            }
            foreach (var h in Snapshot(typeof(T), typeof(Action<T>)))
                ((Action<T>)h)(eventData);
        }

        // ── 2. 带返回值同步 ───────────────────────────────────────

        public void Subscribe<T, TResult>(Func<T, TResult> handler) where T : IEvent => AddHandler(typeof(T), handler);
        public void Unsubscribe<T, TResult>(Func<T, TResult> handler) where T : IEvent => RemoveHandler(typeof(T), handler);

        public IReadOnlyList<TResult> Publish<T, TResult>(T eventData) where T : IEvent
        {
            var results = new List<TResult>();
            foreach (var h in Snapshot(typeof(T), typeof(Func<T, TResult>)))
                results.Add(((Func<T, TResult>)h)(eventData));
            return results;
        }

        // ── 3. 无返回值异步 ───────────────────────────────────────

        public void Subscribe<T>(Func<T, CancellationToken, Task> handler) where T : IEvent => AddHandler(typeof(T), handler);
        public void Unsubscribe<T>(Func<T, CancellationToken, Task> handler) where T : IEvent => RemoveHandler(typeof(T), handler);

        public async Task PublishAsync<T>(T eventData, CancellationToken ct = default) where T : IEvent
        {
            var tasks = Snapshot(typeof(T), typeof(Func<T, CancellationToken, Task>))
                .Select(h => ((Func<T, CancellationToken, Task>)h)(eventData, ct));
            await Task.WhenAll(tasks);
        }

        // ── 4. 带返回值异步 ───────────────────────────────────────

        public void Subscribe<T, TResult>(Func<T, CancellationToken, Task<TResult>> handler) where T : IEvent => AddHandler(typeof(T), handler);
        public void Unsubscribe<T, TResult>(Func<T, CancellationToken, Task<TResult>> handler) where T : IEvent => RemoveHandler(typeof(T), handler);

        public async Task<IReadOnlyList<TResult>> PublishAsync<T, TResult>(T eventData, CancellationToken ct = default) where T : IEvent
        {
            var tasks = Snapshot(typeof(T), typeof(Func<T, CancellationToken, Task<TResult>>))
                .Select(h => ((Func<T, CancellationToken, Task<TResult>>)h)(eventData, ct));
            return await Task.WhenAll(tasks);
        }
    }

    public interface IEvent
    {
    }
}