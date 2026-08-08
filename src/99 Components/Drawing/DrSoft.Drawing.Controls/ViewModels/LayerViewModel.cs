using CommunityToolkit.Mvvm.ComponentModel;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Model;

namespace DrSoft.Drawing.Controls.ViewModels
{
    // ─── 图层 ViewModel ───────────────────────────────────────
    public partial class LayerViewModel : ObservableObject, ILayerViewModel, INodeViewModel
    {
        private const int UniqueNameResolutionLimit = 10_000;

        [ObservableProperty] private int _id;
        [ObservableProperty] private string _name;
        [ObservableProperty] private string _color;
        [ObservableProperty] private bool _isVisible = true;
        [ObservableProperty] private bool _isLocked = false;
        [ObservableProperty] private bool _isExpanded = true;
        [ObservableProperty] private bool _isSelected;
        [ObservableProperty] private int _sortId;

        public NodeType NodeType => NodeType.Layer;

        public DrawingLayer Model { get; init; }

        /// <summary>
        /// 虚拟化子节点集合：按需创建 ViewModel，避免一次性创建百万级节点
        /// </summary>
        public VirtualizingNodeCollection Children { get; }

        // INodeViewModel 显式实现
        IList<INodeViewModel> INodeViewModel.Children => Children;

        public INodeViewModel? Parent { get; set; } = null;

        public string Icon => "⊞";
        public string ShapeTypeName => "图层";

        /// <summary>图形添加策略工厂</summary>
        private readonly ShapeAddStrategyFactory _shapeAddStrategy = new();

        /// <summary>图形移除策略工厂</summary>
        private readonly ShapeRemoveStrategyFactory _shapeRemoveStrategy = new();

        /// <summary>子节点数量（触发通知）</summary>
        public int ShapeCount => Children.Count;

        public LayerViewModel(DrawingLayer model)
        {
            Model = model;
            Id = model.UId;
            Name = model.Name;
            Color = model.Color;
            IsVisible = model.IsVisible;
            IsLocked = model.IsLocked;
            SortId = model.SortId;

            // 将基础图形打包成组合类型显示（复制图层时跳过，保持图形独立显示）
            if (!model.SkipBatchBasicShapes)
                BatchBasicShapes(model);

            // 创建虚拟化集合，拥有独立的模型列表，按需创建 ViewModel
            Children = new VirtualizingNodeCollection(
                model.AllShapesInternal.ToList(),  // 拷贝一份独立的列表，VirtualizingNodeCollection 独占管理
                _ => this);

            // 为图层中的图形分配画布级序号
            var serial = (DocumentContext.Instance.ActiveCanvas as DrawingCanvas)?.SerialNumber;
            foreach (var shape in model.Shapes)
                AssignSerialName(shape, serial);

            // 监听集合变化，触发 ShapeCount 通知
            Children.CollectionChanged += (s, e) => OnPropertyChanged(nameof(ShapeCount));
            Children.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(Children.Count))
                    OnPropertyChanged(nameof(ShapeCount));
            };

            // Add a command that requests the UI layer to open a color picker dialog.
            BorderClickCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<object>(param =>
            {
                // publish an event requesting the color picker; UI layer subscribes and shows dialog
                var result = EventBus.Instance.Publish<ColorPickerRequestEvent, string>(new ColorPickerRequestEvent { LayerId = Id, CurrentColor = Color });
                Color = result[0];
            });
        }

        public CommunityToolkit.Mvvm.Input.IRelayCommand<object> BorderClickCommand { get; }

        /// <summary>
        /// 将图层中的基础图形（非容器）合并为 DrawingGroup
        /// </summary>
        private static void BatchBasicShapes(DrawingLayer model)
        {
            var allShapes = model.AllShapesInternal;
            var containers = new List<IShape>();
            var basics = new List<IShape>();

            foreach (var shape in allShapes)
            {
                if (shape is IContainer)
                    containers.Add(shape);
                else
                    basics.Add(shape);
            }

            // 基础图形数量较少时不合并
            if (basics.Count <= 1) return;

            // 创建组合对象，将基础图形合并，标记为自动批量组合以便复制图层时展开
            var combination = new DrawCombination(basics);
            combination.IsBatchedBasicShapes = true;

            // 重建图层图形列表：先容器，再合并组合对象
            model.ClearShapes();
            foreach (var c in containers)
                model.AddShape(c);
            model.AddShape(combination);
        }

        partial void OnIsVisibleChanged(bool value)
        {
            Model.IsVisible = value;

            // 隐藏图层时清除选中状态
            if (!value)
            {
                ClearSelection();
                IsSelected = false;
            }
        }

        partial void OnIsLockedChanged(bool value)
        {
            Model.IsLocked = value;

            // 锁定图层时：清除选中状态 + 递归传播锁定到子节点
            if (value)
            {
                ClearSelection();
                IsSelected = false;
            }

            SetChildrenLocked(Children, value);
        }

        /// <summary>
        /// 递归设置子节点的锁定状态（ViewModel + Model 双层同步）
        /// </summary>
        private static void SetChildrenLocked(IList<INodeViewModel> children, bool locked)
        {
            foreach (var child in children)
            {
                child.IsLocked = locked;

                // 同步 Model 层锁定
                switch (child)
                {
                    case NodeShapeViewModel svm:
                        svm.Model.IsLocked = locked;
                        break;
                    case NodeGroupViewModel gvm:
                        gvm.Model.IsLocked = locked;
                        break;
                    case NodeHatchViewModel hvm:
                        hvm.Model.IsLocked = locked;
                        break;
                    case NodeCombinationViewModel cvm:
                        cvm.Model.IsLocked = locked;
                        break;
                }
                if (child.Children.Count > 0)
                    SetChildrenLocked(child.Children, locked);
            }
        }
        partial void OnNameChanged(string value) => Model.Name = value;
        partial void OnColorChanged(string value) => Model.Color = value;
        partial void OnSortIdChanged(int value) => Model.SortId = value;
        partial void OnIsSelectedChanged(bool value)
        {
            foreach (var shape in Model.Shapes)
            {
                shape.IsSelected = value;
            }
        }

        // ── 方法 ──────────────────────────────────────────
        /// <summary>
        /// 添加图形
        /// </summary>
        /// <param name="shapes">要添加的图形集合</param>
        public void AddNodes(IEnumerable<IShape> shapes)
        {
            var serial = (DocumentContext.Instance.ActiveCanvas as DrawingCanvas)?.SerialNumber;

            // 判断是否批量添加（如 Separate/Ungroup 产生的子节点）
            var shapeList = shapes as IList<IShape> ?? shapes.ToList();
            if (shapeList.Count == 0)
                return;

            foreach (var shape in shapeList)
                AssignSerialName(shape, serial);

            EnsureUniqueShapeNames(shapeList);

            if (shapeList.Count > 1000)
            {
                Model.AddShapes(shapeList);
                Children.AddRangeModel(shapeList);
            }
            else
            {
                // 少量：逐个创建 ViewModel
                foreach (var shape in shapeList)
                {
                    var strategy = _shapeAddStrategy.GetStrategy(shape);
                    strategy.Add(shape, this, Model, Children);
                }
            }
        }

        /// <summary>
        /// 在指定父容器（或图层顶层）的指定索引处插入图形。
        /// 用于解散群组/组合时把成员还原到原容器中的原位置。
        /// </summary>
        public void InsertNodes(IEnumerable<IShape> shapes, IShape? parentContainer, int startIndex)
        {
            var shapeList = shapes as IList<IShape> ?? shapes.ToList();
            if (shapeList.Count == 0)
                return;

            var serial = (DocumentContext.Instance.ActiveCanvas as DrawingCanvas)?.SerialNumber;
            foreach (var shape in shapeList)
                AssignSerialName(shape, serial);

            EnsureUniqueShapeNames(shapeList);

            INodeViewModel parentNode = this;
            if (parentContainer != null)
            {
                var found = FindNode(parentContainer);
                if (found != null)
                    parentNode = found;
            }

            for (int i = 0; i < shapeList.Count; i++)
            {
                var shape = shapeList[i];
                var node = NodeViewModelFactory.Create(shape, parentNode, buildChildren: false);
                int insertIndex = startIndex >= 0 ? startIndex + i : parentNode.Children.Count;

                // 防御：当 ViewModel 集合与模型层不同步时（例如测试直接操作 Model.AddShape），
                // 避免索引越界；模型层仍会按预期位置插入。
                if (insertIndex > parentNode.Children.Count)
                    insertIndex = parentNode.Children.Count;

                parentNode.Children.Insert(insertIndex, node);

                // 图层顶层时，VirtualizingNodeCollection 拥有的是 _shapes 的副本，
                // 因此还需要同步 DrawingLayer 模型。
                if (parentContainer == null)
                    Model.InsertShape(startIndex >= 0 ? startIndex + i : Model.AllShapesInternal.Count, shape);
            }
        }

        /// <summary>
        /// 为图形及其子节点分配画布级序号名称
        /// </summary>
        private static void AssignSerialName(IShape shape, SerialNumberGenerator? serial)
        {
            if (serial == null) return;
            if (string.IsNullOrEmpty(shape.Name))
                shape.Name = serial.NextId().ToString();

            // 递归为容器子节点分配序号
            if (shape is IContainer container)
            {
                foreach (var child in container.Children)
                {
                    AssignSerialName(child, serial);
                }
            }
        }

        /// <summary>
        /// 为当前批次新增图形分配图层内唯一名称。
        /// 图形量超过阈值时跳过，避免在超大图层上为命名去重引入额外遍历成本。
        /// </summary>
        private void EnsureUniqueShapeNames(IList<IShape> shapes)
        {
            if (!TryBuildNameResolutionContext(Model.AllShapesInternal, out var existingNames, out var nextSuffixByBase))
                return;

            foreach (var shape in shapes)
                EnsureUniqueShapeNameRecursive(shape, existingNames, nextSuffixByBase);
        }

        /// <summary>
        /// 扫描当前图层已有名称，并为每个基础名称预计算下一个可用后缀。
        /// 例如已有 1、1-1、1-3，则记录 1 的下一个候选后缀为 4。
        /// </summary>
        private static bool TryBuildNameResolutionContext(
            IEnumerable<IShape> existingShapes,
            out HashSet<string> existingNames,
            out Dictionary<string, int> nextSuffixByBase)
        {
            existingNames = new HashSet<string>(StringComparer.Ordinal);
            nextSuffixByBase = new Dictionary<string, int>(StringComparer.Ordinal);

            int visitedCount = 0;
            foreach (var shape in existingShapes)
            {
                if (!CollectExistingNames(shape, existingNames, nextSuffixByBase, ref visitedCount))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 递归收集图层中现有名称；超过数量阈值时返回 false，让调用方跳过去重逻辑。
        /// </summary>
        private static bool CollectExistingNames(
            IShape shape,
            HashSet<string> existingNames,
            Dictionary<string, int> nextSuffixByBase,
            ref int visitedCount)
        {
            visitedCount++;
            if (visitedCount > UniqueNameResolutionLimit)
                return false;

            if (!string.IsNullOrEmpty(shape.Name))
            {
                existingNames.Add(shape.Name);
                RegisterNextSuffix(shape.Name, nextSuffixByBase);
            }

            if (shape is not IContainer container)
                return true;

            foreach (var child in container.Children)
            {
                if (!CollectExistingNames(child, existingNames, nextSuffixByBase, ref visitedCount))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 递归处理容器及其子图形，保证整批新增对象在加入图层前已完成唯一命名。
        /// </summary>
        private static void EnsureUniqueShapeNameRecursive(
            IShape shape,
            HashSet<string> existingNames,
            Dictionary<string, int> nextSuffixByBase)
        {
            if (!string.IsNullOrEmpty(shape.Name))
                shape.Name = ResolveUniqueShapeName(shape.Name, existingNames, nextSuffixByBase);

            if (shape is not IContainer container)
                return;

            foreach (var child in container.Children)
                EnsureUniqueShapeNameRecursive(child, existingNames, nextSuffixByBase);
        }

        /// <summary>
        /// 若名称冲突，则按“原名-序号”继续向后分配，直到拿到当前图层内未占用的名称。
        /// </summary>
        private static string ResolveUniqueShapeName(
            string name,
            HashSet<string> existingNames,
            Dictionary<string, int> nextSuffixByBase)
        {
            if (existingNames.Add(name))
            {
                RegisterNextSuffix(name, nextSuffixByBase);
                return name;
            }

            int nextSuffix = nextSuffixByBase.TryGetValue(name, out var cachedNextSuffix)
                ? cachedNextSuffix
                : 1;

            string uniqueName;
            do
            {
                uniqueName = $"{name}-{nextSuffix}";
                nextSuffix++;
            }
            while (!existingNames.Add(uniqueName));

            nextSuffixByBase[name] = nextSuffix;
            RegisterNextSuffix(uniqueName, nextSuffixByBase);
            return uniqueName;
        }

        /// <summary>
        /// 从形如 name-3 的名称中反推出基础名称的下一个候选后缀。
        /// </summary>
        private static void RegisterNextSuffix(string name, Dictionary<string, int> nextSuffixByBase)
        {
            if (!TrySplitNumericSuffix(name, out var baseName, out var suffix))
                return;

            int nextSuffix = suffix + 1;
            if (nextSuffixByBase.TryGetValue(baseName, out var currentNextSuffix) && currentNextSuffix >= nextSuffix)
                return;

            nextSuffixByBase[baseName] = nextSuffix;
        }

        /// <summary>
        /// 尝试拆分末尾数字后缀，仅识别最后一个 '-' 后的纯数字片段。
        /// </summary>
        private static bool TrySplitNumericSuffix(string name, out string baseName, out int suffix)
        {
            baseName = string.Empty;
            suffix = 0;

            int separatorIndex = name.LastIndexOf('-');
            if (separatorIndex <= 0 || separatorIndex == name.Length - 1)
                return false;

            string suffixText = name[(separatorIndex + 1)..];
            if (!int.TryParse(suffixText, out suffix))
                return false;

            baseName = name[..separatorIndex];
            return !string.IsNullOrEmpty(baseName);
        }

        /// <summary>
        /// 删除图形
        /// </summary>
        /// <param name="shapes">要移除的图形集合</param>
        public void RemoveNodes(IEnumerable<IShape> shapes)
        {
            var shapeList = shapes as IList<IShape> ?? shapes.ToList();
            if (shapeList.Count > 100)
            {
                // 大批量：直接操作模型层 + VirtualizingNodeCollection，避免逐个 RemoveAt
                var uidSet = new HashSet<int>(shapeList.Count);
                foreach (var s in shapeList)
                    uidSet.Add(s.UId);

                // 同步 DrawingLayer 模型
                Model.RemoveShapes(shapeList);
                // 同步 VirtualizingNodeCollection
                Children.RemoveRangeByModelIds(uidSet);
            }
            else
            {
                // 少量：逐个通过策略移除
                foreach (var shape in shapeList)
                {
                    var strategy = _shapeRemoveStrategy.GetStrategy(shape);
                    strategy.Remove(shape, Model, Children);
                }
            }
        }

        /// <summary>
        /// 从指定父容器（或图层顶层）中移除图形。
        /// </summary>
        public void RemoveNodes(IEnumerable<IShape> shapes, IShape? parentContainer)
        {
            var shapeList = shapes as IList<IShape> ?? shapes.ToList();
            if (shapeList.Count == 0)
                return;

            if (parentContainer == null)
            {
                RemoveNodes(shapeList);
                return;
            }

            var parentNode = FindNode(parentContainer);
            if (parentNode == null)
            {
                // 找不到视图节点时退化为顶层移除（通常不会发生）
                RemoveNodes(shapeList);
                return;
            }

            if (parentNode.Children is VirtualizingNodeCollection vc)
            {
                foreach (var shape in shapeList)
                    vc.RemoveByModelId(shape.UId);
            }
            else
            {
                foreach (var shape in shapeList)
                {
                    var node = parentNode.Children.FirstOrDefault(n => n.Id == shape.UId);
                    if (node != null)
                        parentNode.Children.Remove(node);
                }
            }
        }

        /// <summary>
        /// 是否包含节点（直接子节点或嵌套子节点中包含指定图形）
        /// </summary>
        public bool Contains(IShape shape)
        {
            if (shape == null)
                return false;

            if (Children.ContainsModelId(shape.UId))
                return true;

            // 递归遍历容器子节点，支持嵌套组合/群组
            foreach (var topLevel in Model.Shapes)
            {
                if (ContainsRecursive(topLevel, shape))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 递归检查 container 及其子级中是否包含指定图形
        /// </summary>
        private static bool ContainsRecursive(IShape current, IShape target)
        {
            if (ReferenceEquals(current, target) || current.UId == target.UId)
                return true;

            if (current is IContainer container)
            {
                foreach (var child in container.Children)
                {
                    if (ContainsRecursive(child, target))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 在视图树中查找指定图形对应的节点（按需创建祖先节点）。
        /// </summary>
        internal INodeViewModel? FindNode(IShape target, INodeViewModel? node = null)
        {
            node ??= this;
            if (node.Id == target.UId)
                return node;

            foreach (var child in node.Children)
            {
                if (child.Id == target.UId)
                    return child;

                var found = FindNode(target, child);
                if (found != null)
                    return found;
            }

            return null;
        }

        /// <summary>
        /// 获取图层中被选中的顶级节点集合（仅遍历已创建的 ViewModel）
        /// </summary>
        public IEnumerable<INodeViewModel> GetSelectedNodes()
        {
            // 仅遍历已缓存的 ViewModel，不触发全量创建
            foreach (var kvp in Children.CachedItems)
            {
                if (kvp.Value.HasSelectedOrContainsSelected())
                    yield return kvp.Value;
            }
        }

        /// <summary>
        /// 清除图层中所有节点的选中状态（仅遍历已创建的 ViewModel）
        /// </summary>
        public void ClearSelection()
        {
            Children?.ClearAllSelection();
        }

        /// <summary>
        /// 判断该图层是否自身被选中，或包含被选中的子节点（仅检查已创建的 ViewModel）
        /// </summary>
        public bool HasSelectedOrContainsSelected()
        {
            if (IsSelected) return true;
            // 仅遍历已缓存的 ViewModel
            foreach (var kvp in Children.CachedItems)
            {
                if (kvp.Value.HasSelectedOrContainsSelected())
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 图层自身未被选中，但包含被选中的子节点（用于图层行背景高亮）
        /// </summary>
        public bool HasSelectedChild => HasSelectedOrContainsSelected() && !IsSelected;

        /// <summary>
        /// 通知 UI 刷新 HasSelectedChild 绑定状态
        /// </summary>
        public void NotifyHasSelectedChildChanged()
        {
            OnPropertyChanged(nameof(HasSelectedChild));
        }

        /// <summary>
        /// 根据ID查找节点并选中（基于模型层查找，不枚举 ViewModel）
        /// </summary>
        internal void SelectNodeById(int uid)
        {
            Children.SelectNodeById(uid);
        }

        /// <summary>
        /// 获取图层中所有图形（直接从模型层获取，不触发 ViewModel 创建）
        /// </summary>
        public IEnumerable<IShape> GetAllShapes()
        {
            return Children.ModelChildren;
        }
    }
}
