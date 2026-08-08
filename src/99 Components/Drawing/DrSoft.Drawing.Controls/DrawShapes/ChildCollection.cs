using System.Collections;
using DrSoft.Drawing.Model;

namespace DrSoft.Drawing.Controls.DrawShapes
{
    /// <summary>
    /// 容器图形的子图形集合，替代原始 List&lt;IShape&gt;。
    /// <para>集合内部负责：</para>
    /// <list type="bullet">
    ///   <item>FlattenCount 懒缓存（结构变化时自动失效，O(1) 读取）</item>
    ///   <item>子图形 BoundingBoxInvalidated 事件自动订阅/取消订阅</item>
    ///   <item>通过回调通知父容器使缓存失效</item>
    /// </list>
    /// <para>外部只需提供两个回调，无需手写 OnChildAdded/OnChildRemoved。</para>
    /// </summary>
    public sealed class ChildCollection : IList<IShape>, IReadOnlyList<IShape>
    {
        private readonly List<IShape> _items;
        private readonly Action _invalidateParentCaches;
        private readonly Func<bool> _isPropagationSuppressed;
        private int? _cachedFlattenCount;

        /// <param name="invalidateParentCaches">子级结构变化或包围盒变化时调用，使父容器缓存失效</param>
        /// <param name="isPropagationSuppressed">检查父容器是否正在抑制子级传播（对应 _suppressChildPropagation）</param>
        public ChildCollection(Action invalidateParentCaches, Func<bool>? isPropagationSuppressed = null)
        {
            _items = [];
            _invalidateParentCaches = invalidateParentCaches;
            _isPropagationSuppressed = isPropagationSuppressed ?? (() => false);
        }

        /// <summary>
        /// 用已有元素初始化集合。<b>不触发回调、不订阅事件</b>——
        /// 初始元素需通过 <see cref="SubscribeInitialItems"/> 统一订阅，
        /// 避免构造期间回调访问未就绪的状态。
        /// </summary>
        public ChildCollection(IEnumerable<IShape> items, Action invalidateParentCaches, Func<bool>? isPropagationSuppressed = null)
        {
            _items = new List<IShape>(items);
            _invalidateParentCaches = invalidateParentCaches;
            _isPropagationSuppressed = isPropagationSuppressed ?? (() => false);
        }

        // ── FlattenCount 懒缓存 ─────────────────────────────────────────

        /// <summary>
        /// 递归子级总数，O(1) 懒缓存。
        /// 结构变化（Add/Remove/Insert/RemoveAt/Clear/索引器赋值）时自动失效。
        /// </summary>
        public int FlattenCount
        {
            get
            {
                if (_cachedFlattenCount.HasValue)
                    return _cachedFlattenCount.Value;

                int count = 0;
                for (int i = 0; i < _items.Count; i++)
                    count += _items[i].FlattenCount;
                _cachedFlattenCount = count;
                return count;
            }
        }

        // ── 子图形事件订阅 ─────────────────────────────────────────────

        /// <summary>
        /// 订阅初始元素的 BoundingBoxInvalidated 事件。
        /// 仅在构造函数传入初始元素后调用一次，之后的 Add/Remove 自动管理。
        /// </summary>
        public void SubscribeInitialItems()
        {
            for (int i = 0; i < _items.Count; i++)
                SubscribeChild(_items[i]);
        }

        /// <summary>
        /// 取消订阅所有子图形的 BoundingBoxInvalidated 事件。
        /// </summary>
        public void UnsubscribeAll()
        {
            for (int i = 0; i < _items.Count; i++)
                UnsubscribeChild(_items[i]);
        }

        private void SubscribeChild(IShape child)
        {
            if (child is DrawObject drawObj)
                drawObj.BoundingBoxInvalidated += OnChildBoundingBoxInvalidated;
        }

        private void UnsubscribeChild(IShape child)
        {
            if (child is DrawObject drawObj)
                drawObj.BoundingBoxInvalidated -= OnChildBoundingBoxInvalidated;
        }

        private void OnChildBoundingBoxInvalidated(DrawObject child)
        {
            if (_isPropagationSuppressed()) return;
            _invalidateParentCaches();
        }

        // ── 结构变化辅助 ─────────────────────────────────────────────

        private void OnStructureChanged()
        {
            _cachedFlattenCount = null;
            _invalidateParentCaches();
        }

        // ── IList<IShape> ──────────────────────────────────────────────

        public IShape this[int index]
        {
            get => _items[index];
            set
            {
                var old = _items[index];
                _items[index] = value;
                UnsubscribeChild(old);
                SubscribeChild(value);
                OnStructureChanged();
            }
        }

        public int Count => _items.Count;
        public bool IsReadOnly => false;

        public void Add(IShape item)
        {
            _items.Add(item);
            SubscribeChild(item);
            OnStructureChanged();
        }

        public void AddRange(IEnumerable<IShape> items)
        {
            foreach (var item in items)
                Add(item);
        }

        public void Clear()
        {
            for (int i = _items.Count - 1; i >= 0; i--)
                UnsubscribeChild(_items[i]);
            _items.Clear();
            OnStructureChanged();
        }

        public bool Contains(IShape item) => _items.Contains(item);
        public void CopyTo(IShape[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public int IndexOf(IShape item) => _items.IndexOf(item);

        public void Insert(int index, IShape item)
        {
            _items.Insert(index, item);
            SubscribeChild(item);
            OnStructureChanged();
        }

        public bool Remove(IShape item)
        {
            var removed = _items.Remove(item);
            if (removed)
            {
                UnsubscribeChild(item);
                OnStructureChanged();
            }
            return removed;
        }

        public void RemoveAt(int index)
        {
            var item = _items[index];
            _items.RemoveAt(index);
            UnsubscribeChild(item);
            OnStructureChanged();
        }

        // ── 枚举 ──────────────────────────────────────────────────────

        public IEnumerator<IShape> GetEnumerator() => _items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
    }
}
