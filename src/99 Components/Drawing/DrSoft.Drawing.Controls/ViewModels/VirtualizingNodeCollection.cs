using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Model;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;

namespace DrSoft.Drawing.Controls.ViewModels
{
    /// <summary>
    /// 虚拟化节点集合：按需创建 INodeViewModel，避免一次性创建百万级节点。
    /// 
    /// 工作原理：
    ///   - Count 报告模型的子节点总数（WPF 虚拟化据此计算滚动条）
    ///   - this[index] 仅在首次访问时创建对应 ViewModel 并缓存
    ///   - WPF TreeView 开启 VirtualizingPanel 后只会访问可视区域的索引
    ///   - 支持批量添加（AddRange）、移除、索引查找
    /// </summary>
    public class VirtualizingNodeCollection : IList<INodeViewModel>, INotifyCollectionChanged, INotifyPropertyChanged
    {
        private readonly IShape _owner;
        private readonly Func<IShape, INodeViewModel?> _parentGetter;
        private readonly IList<IShape> _modelChildren;          // 模型层子图形
        private readonly Dictionary<int, INodeViewModel> _cache; // 按索引缓存已创建的 ViewModel
        private readonly bool _ownsModelList;                  // 是否独占模型列表（IContainer 场景）
        private int _createdCount;                              // 已创建的 ViewModel 数量
        private Dictionary<int, int>? _uidIndex;               // UId → 索引的快速查找表（懒加载）

        // ── 标准 IList 事件 ──
        public event NotifyCollectionChangedEventHandler? CollectionChanged;
        public event PropertyChangedEventHandler? PropertyChanged;

        public VirtualizingNodeCollection(IShape owner, Func<IShape, INodeViewModel?> parentGetter)
        {
            _owner = owner;
            _parentGetter = parentGetter;
            _ownsModelList = true; // IContainer 场景：独占模型列表，Add/Insert 时同步模型层

            // 从 IContainer 获取子节点列表
            if (owner is Interface.IContainer container)
                _modelChildren = container.Children;
            else
                _modelChildren = new List<IShape>();

            _cache = new Dictionary<int, INodeViewModel>(_modelChildren.Count > 256 ? 256 : _modelChildren.Count);
            _createdCount = 0;
        }

        /// <summary>
        /// 直接指定模型子节点列表的构造函数（用于 LayerViewModel 等场景）
        /// </summary>
        /// <param name="modelChildren">模型子节点列表（集合独占管理）</param>
        /// <param name="parentGetter">父节点获取函数</param>
        /// <param name="ownsModelList">是否独占模型列表（默认 true，Add/Remove/Insert 时同步模型层）</param>
        public VirtualizingNodeCollection(IList<IShape> modelChildren, Func<IShape, INodeViewModel?> parentGetter, bool ownsModelList = true)
        {
            _owner = null!;
            _parentGetter = parentGetter;
            _ownsModelList = ownsModelList;
            _modelChildren = modelChildren;
            _cache = new Dictionary<int, INodeViewModel>(_modelChildren.Count > 256 ? 256 : _modelChildren.Count);
            _createdCount = 0;
        }

        // ══════════════════════════════════════════════════════
        //  核心按需创建
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 按索引获取或创建 ViewModel（虚拟化核心入口）
        /// </summary>
        public INodeViewModel this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_modelChildren.Count)
                    throw new ArgumentOutOfRangeException(nameof(index));

                if (_cache.TryGetValue(index, out var vm))
                    return vm;

                // 按需创建
                vm = NodeViewModelFactory.Create(_modelChildren[index], _parentGetter(null!), buildChildren: false);
                _cache[index] = vm;
                _createdCount++;

                return vm;
            }
            set => throw new NotSupportedException("VirtualizingNodeCollection 不支持设置索引");
        }

        public int Count => _modelChildren.Count;

        /// <summary>已实际创建的 ViewModel 数量（调试 / 性能监控用）</summary>
        public int CreatedCount => _createdCount;

        public bool IsReadOnly => false;

        // ══════════════════════════════════════════════════════
        //  集合操作
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 添加节点（同步模型层 + 缓存 + 通知 WPF）
        /// </summary>
        public void Add(INodeViewModel item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            // 检查模型是否已存在（如工厂 buildChildren 时，模型子节点已在构造函数中加载）
            var existIdx = IndexOfModelId(item.Id);
            if (existIdx >= 0)
            {
                // 模型已存在，仅缓存 ViewModel
                _cache[existIdx] = item;
                _createdCount++;
                return;
            }

            // 新增项：缓存 + 通知（模型层由调用者或 AddRangeModel 管理）
            var index = _modelChildren.Count;
            if (_ownsModelList)
            {
                var shape = ExtractModelShape(item);
                if (shape != null)
                    _modelChildren.Add(shape);
                index = _modelChildren.Count - 1;
            }

            _cache[index] = item;
            _createdCount++;

            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Add, item, index));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        }

        /// <summary>
        /// 移除指定节点（同步模型层 + 缓存 + 通知 WPF）
        /// </summary>
        public bool Remove(INodeViewModel item)
        {
            if (item == null) return false;

            var index = IndexOf(item);
            if (index < 0) return false;

            RemoveAt(index);
            return true;
        }

        /// <summary>
        /// 清空所有节点（同步模型层 + 缓存 + 通知 WPF）
        /// </summary>
        public void Clear()
        {
            // 同步模型层（仅独占模式）
            if (_ownsModelList)
                _modelChildren.Clear();

            // 清空缓存
            _cache.Clear();
            _createdCount = 0;

            // 通知 WPF
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Reset));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        }

        /// <summary>
        /// 批量添加模型图形（仅同步模型层，不创建 ViewModel，发送 Reset 通知）
        /// 适用于 Separate/Ungroup 等一次性追加大量子节点的场景
        /// </summary>
        public void AddRangeModel(IList<IShape> shapes)
        {
            if (_modelChildren is ChildCollection cc)
                cc.AddRange(shapes);
            else
                foreach (var s in shapes) _modelChildren.Add(s);
            RebuildUidIndex();

            // 不创建 ViewModel，仅通知 WPF 集合已重置
            // WPF TreeView 虚拟化会按需通过 this[index] 创建可见项
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Reset));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        }

        /// <summary>
        /// 批量按 UId 移除模型节点（O(n) 一次性完成，避免逐个 RemoveAt 的 O(n²) 和百万次通知）
        /// 适用于 Undo（还原为组合）等需要批量删除的场景
        /// </summary>
        public void RemoveRangeByModelIds(HashSet<int> uids)
        {
            if (uids.Count == 0) return;

            // 模型层批量移除
            if (_modelChildren is List<IShape> list)
                list.RemoveAll(s => uids.Contains(s.UId));
            else
            {
                for (int i = _modelChildren.Count - 1; i >= 0; i--)
                    if (uids.Contains(_modelChildren[i].UId))
                        _modelChildren.RemoveAt(i);
            }

            // 缓存清理：移除已被删除的项，并重建索引映射
            var newCache = new Dictionary<int, INodeViewModel>(_cache.Count);
            int newIndex = 0;
            for (int oldIndex = 0; oldIndex < _modelChildren.Count + uids.Count; oldIndex++)
            {
                if (_cache.TryGetValue(oldIndex, out var vm))
                {
                    // 该 ViewModel 对应的模型是否被移除？
                    if (!uids.Contains(vm.Id))
                        newCache[newIndex++] = vm;
                    else
                        _createdCount--;
                }
                // oldIndex 对应的模型如果未被移除，newIndex 自然递增
                // 注意：此处无法精确知道哪些旧索引被移除，但通过模型列表长度差可推断
                // 更简单的做法：直接清空缓存，让虚拟化重建
            }
            _cache.Clear();
            foreach (var kvp in newCache)
                _cache[kvp.Key] = kvp.Value;

            RebuildUidIndex();

            // 仅发送一次 Reset 通知
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Reset));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        }

        /// <summary>
        /// 批量按图形引用移除模型节点（O(n) 一次性完成）
        /// </summary>
        public void RemoveRangeByModels(IList<IShape> shapes)
        {
            if (shapes.Count == 0) return;

            var uidSet = new HashSet<int>(shapes.Count);
            foreach (var s in shapes)
                uidSet.Add(s.UId);

            RemoveRangeByModelIds(uidSet);
        }

        /// <summary>
        /// 在集合内移动元素（同步模型层 + 更新缓存）
        /// </summary>
        public void Move(int oldIndex, int newIndex)
        {
            if ((uint)oldIndex >= (uint)_modelChildren.Count || (uint)newIndex >= (uint)_modelChildren.Count)
                throw new ArgumentOutOfRangeException();

            // 同步模型层
            var modelItem = _modelChildren[oldIndex];
            _modelChildren.RemoveAt(oldIndex);
            _modelChildren.Insert(newIndex, modelItem);

            // 同步缓存：移动索引映射
            var cacheItem = _cache.TryGetValue(oldIndex, out var v) ? v : null;
            _cache.Remove(oldIndex);
            // 重建受影响索引的缓存映射
            ShiftCacheIndicesAfterMove(oldIndex, newIndex);
            if (cacheItem != null)
                _cache[newIndex] = cacheItem;

            // 通知 WPF
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Move, cacheItem, newIndex, oldIndex));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        }

        /// <summary>
        /// 移除指定索引的节点（同步模型层 + 更新缓存）
        /// </summary>
        public void RemoveAt(int index)
        {
            if ((uint)index >= (uint)_modelChildren.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            var removedItem = _cache.TryGetValue(index, out var v) ? v : null;

            // 同步模型层（仅独占模式）
            if (_ownsModelList)
                _modelChildren.RemoveAt(index);

            // 同步缓存：移除并后移索引
            _cache.Remove(index);
            ShiftCacheIndicesAfterRemove(index);

            if (removedItem != null) _createdCount--;

            // 通知 WPF
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Remove, removedItem, index));
        }

        /// <summary>
        /// 在指定索引处插入节点（同步模型层 + 更新缓存）
        /// </summary>
        public void Insert(int index, INodeViewModel item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (index < 0 || index > _modelChildren.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            // 检查模型是否已存在
            var existIdx = IndexOfModelId(item.Id);
            if (existIdx >= 0)
            {
                // 模型已存在，仅缓存 ViewModel
                _cache[existIdx] = item;
                _createdCount++;
                return;
            }

            // 同步模型层（仅独占模式；共享模式由调用者管理）
            if (_ownsModelList)
            {
                var shape = ExtractModelShape(item);
                if (shape != null)
                    _modelChildren.Insert(index, shape);
            }

            // 同步缓存：后移索引后插入
            ShiftCacheIndicesAfterInsert(index);
            _cache[index] = item;
            _createdCount++;

            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Add, item, index));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        }

        public bool Contains(INodeViewModel item) => IndexOf(item) >= 0;

        public void CopyTo(INodeViewModel[] array, int arrayIndex)
        {
            for (int i = 0; i < _modelChildren.Count; i++)
                array[arrayIndex + i] = this[i];
        }

        public int IndexOf(INodeViewModel item)
        {
            foreach (var kvp in _cache)
            {
                if (ReferenceEquals(kvp.Value, item))
                    return kvp.Key;
            }
            return -1;
        }

        // ══════════════════════════════════════════════════════
        //  查询辅助方法（基于模型层，不触发 ViewModel 创建）
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 根据图形 ID 查找并选中对应节点（返回 true 表示找到）
        /// </summary>
        public bool SelectNodeById(int uid)
        {
            var idx = IndexOfModelId(uid);
            if (idx >= 0)
            {
                this[idx].IsSelected = true;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 根据 UId 查找并移除节点（直接操作模型层，不枚举 ViewModel）
        /// 返回 true 表示找到并移除
        /// </summary>
        public bool RemoveByModelId(int uid)
        {
            var idx = IndexOfModelId(uid);
            if (idx < 0) return false;
            RemoveAt(idx);
            return true;
        }

        /// <summary>
        /// 判断模型层是否包含指定 UId 的图形（不触发 ViewModel 创建）
        /// </summary>
        public bool ContainsModelId(int uid) => IndexOfModelId(uid) >= 0;

        /// <summary>
        /// 判断模型层是否包含指定图形（不触发 ViewModel 创建）
        /// </summary>
        public bool ContainsModel(IShape shape) => _modelChildren.Contains(shape);

        // ══════════════════════════════════════════════════════
        //  模型提取辅助方法
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 从 INodeViewModel 提取关联的 IShape 模型
        /// 通过具体类型匹配获取 Model 属性（INodeViewModel 接口未暴露 Model）
        /// </summary>
        private static IShape? ExtractModelShape(INodeViewModel vm) => vm switch
        {
            NodeGroupViewModel g => g.Model,
            NodeCombinationViewModel c => c.Model,
            NodeHatchViewModel h => h.Model,
            NodeShapeViewModel s => s.Model,
            _ => null
        };

        // ══════════════════════════════════════════════════════
        //  缓存索引位移辅助方法
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// RemoveAt 后：将 index 之后的所有缓存键减 1
        /// </summary>
        private void ShiftCacheIndicesAfterRemove(int removedIndex)
        {
            // 收集需要位移的键（大于 removedIndex 的）
            var keysToShift = new List<int>();
            foreach (var key in _cache.Keys)
            {
                if (key > removedIndex)
                    keysToShift.Add(key);
            }
            keysToShift.Sort();
            foreach (var key in keysToShift)
            {
                _cache[key - 1] = _cache[key];
                _cache.Remove(key);
            }
        }

        /// <summary>
        /// Insert 后：将 index 及之后的缓存键加 1，为新项腾出位置
        /// </summary>
        private void ShiftCacheIndicesAfterInsert(int insertIndex)
        {
            // 收集需要位移的键（>= insertIndex 的），从大到小处理避免覆盖
            var keysToShift = new List<int>();
            foreach (var key in _cache.Keys)
            {
                if (key >= insertIndex)
                    keysToShift.Add(key);
            }
            keysToShift.Sort();
            keysToShift.Reverse(); // 从大到小
            foreach (var key in keysToShift)
            {
                _cache[key + 1] = _cache[key];
                _cache.Remove(key);
            }
        }

        /// <summary>
        /// Move 后：将 oldIndex 和 newIndex 之间的缓存键位移
        /// </summary>
        private void ShiftCacheIndicesAfterMove(int oldIndex, int newIndex)
        {
            if (oldIndex == newIndex) return;

            var keysToShift = new List<int>();
            if (oldIndex < newIndex)
            {
                // 向后移动：[oldIndex+1, newIndex] 的键减 1
                foreach (var key in _cache.Keys)
                {
                    if (key > oldIndex && key <= newIndex)
                        keysToShift.Add(key);
                }
                keysToShift.Sort();
                foreach (var key in keysToShift)
                {
                    _cache[key - 1] = _cache[key];
                    _cache.Remove(key);
                }
            }
            else
            {
                // 向前移动：[newIndex, oldIndex-1] 的键加 1
                foreach (var key in _cache.Keys)
                {
                    if (key >= newIndex && key < oldIndex)
                        keysToShift.Add(key);
                }
                keysToShift.Sort();
                keysToShift.Reverse();
                foreach (var key in keysToShift)
                {
                    _cache[key + 1] = _cache[key];
                    _cache.Remove(key);
                }
            }
        }

        /// <summary>
        /// 根据图形 ID 查找节点索引（O(1) 哈希查找，懒加载索引表）
        /// </summary>
        public int IndexOfModelId(int uid)
        {
            if (_uidIndex != null && _uidIndex.TryGetValue(uid, out var idx))
                return idx;

            // 首次查找或索引失效时线性扫描
            for (int i = 0; i < _modelChildren.Count; i++)
            {
                if (_modelChildren[i].UId == uid)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// 重建 UId → 索引的快速查找表
        /// </summary>
        private void RebuildUidIndex()
        {
            if (_modelChildren.Count > 100) // 仅在子节点较多时启用索引
            {
                _uidIndex = new Dictionary<int, int>(_modelChildren.Count);
                for (int i = 0; i < _modelChildren.Count; i++)
                    _uidIndex[_modelChildren[i].UId] = i;
            }
            else
            {
                _uidIndex = null;
            }
        }

        /// <summary>
        /// 根据图形 ID 查找已创建的 ViewModel（未创建则返回 null）
        /// </summary>
        public INodeViewModel? FindCreatedById(int uid)
        {
            foreach (var kvp in _cache)
            {
                if (_modelChildren[kvp.Key].UId == uid)
                    return kvp.Value;
            }
            return null;
        }

        /// <summary>
        /// 清除所有已创建节点的选中状态
        /// </summary>
        public void ClearAllSelection()
        {
            foreach (var kvp in _cache)
            {
                kvp.Value.ClearSelection();
            }
        }

        /// <summary>
        /// 递归收集已选中节点的图形
        /// </summary>
        public void CollectSelectedShapes(List<IShape> shapes, HashSet<NodeType> nodeTypes)
        {
            foreach (var kvp in _cache)
            {
                var vm = kvp.Value;

                if (vm.IsSelected)
                {
                    shapes.AddRange(vm.GetAllShapes());
                    nodeTypes.Add(vm.NodeType);
                    continue;
                }

                // 递归检查子节点（仅对已创建的容器节点）
                if (vm.Children is VirtualizingNodeCollection childVc)
                    childVc.CollectSelectedShapes(shapes, nodeTypes);
                else if (vm.Children.Count > 0)
                {
                    // 标准 ObservableCollection 子节点
                    CollectSelectedFromObservable(vm.Children, shapes, nodeTypes);
                }
            }
        }

        private static void CollectSelectedFromObservable(
            IList<INodeViewModel> children,
            List<IShape> shapes, HashSet<NodeType> nodeTypes)
        {
            foreach (var child in children)
            {
                if (child.IsSelected)
                {
                    shapes.AddRange(child.GetAllShapes());
                    nodeTypes.Add(child.NodeType);
                    continue;
                }
                CollectSelectedFromObservable(child.Children, shapes, nodeTypes);
            }
        }

        /// <summary>
        /// 获取所有模型子节点（不触发 ViewModel 创建）
        /// </summary>
        public IReadOnlyList<IShape> ModelChildren => (IReadOnlyList<IShape>)_modelChildren;

        /// <summary>
        /// 获取已缓存的 ViewModel 项（不触发 ViewModel 创建）
        /// 用于业务逻辑遍历已创建的节点，避免 GetEnumerator 导致全量创建
        /// </summary>
        public IEnumerable<KeyValuePair<int, INodeViewModel>> CachedItems => _cache;

        /// <summary>
        /// 通知集合已重置（模型层发生变化后调用）
        /// </summary>
        public void NotifyReset()
        {
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        }

        // ══════════════════════════════════════════════════════
        //  IEnumerable
        // ══════════════════════════════════════════════════════

        public IEnumerator<INodeViewModel> GetEnumerator()
        {
            for (int i = 0; i < _modelChildren.Count; i++)
                yield return this[i];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
