using System.Collections;
using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Shapes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrSoft.Drawing.Controls.Commands;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Controls.Mapping;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Model;
using DrSoft.Drawing.Utility;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.ViewModels
{
    // ─── 拖拽放置位置 ─────────────────────────────────────
    public enum DropPosition
    {
        Before,
        After,
        Inside
    }

    // ─── 面板主 ViewModel ─────────────────────────────────────
    public partial class LayerViewViewModel : ObservableObject
    {
        /// <summary>图层变更事件，用于通知画布重绘</summary>
        public event EventHandler? OnLayerChanged;

        [ObservableProperty]
        private LayerViewModel? _activeLayer;
        [ObservableProperty] private bool isSingle = true;
        [ObservableProperty] private bool isAllVisible = true;

        /// <summary>进入单一图层模式前，保存各图层的原始可见状态，退出时恢复</summary>
        private Dictionary<int, bool> _originalVisibility = new();


        private readonly DrawingCanvas _canvas;

        /// <summary>标志：正在处理图层树选中变化，不同步画布回调</summary>
        private bool _isProcessingLayerTreeSelection = false;

        public SerialNumberGenerator SerialNumber = new();

        public ObservableCollection<LayerViewModel> LayerViewModels { get; } = new ObservableCollection<LayerViewModel>();

        // 绑定到 UI 的图层数量
        public int LayerCount => LayerViewModels.Count;

        [RelayCommand]
        private void ToggleAllVisible()
        {
            if (LayerViewModels == null || LayerViewModels.Count == 0) return;
            bool anyInvisible = LayerViewModels.Any(l => !l.IsVisible);
            foreach (var l in LayerViewModels)
            {
                l.IsVisible = anyInvisible; // 如果有不可见则全部设为可见，否则全部设为不可见
            }
            // 同步更新总可视开关的图标状态
            IsAllVisible = anyInvisible;
            OnLayerChanged?.Invoke(this, EventArgs.Empty);
            OnPropertyChanged(nameof(LayerCount));
        }

        /// <summary>根据所有图层的可见状态同步 IsAllVisible 字段</summary>
        private void SyncIsAllVisible()
        {
            IsAllVisible = LayerViewModels.All(l => l.IsVisible);
        }

        /// <summary>Shift 范围选择的锚点节点</summary>
        private INodeViewModel? _anchorNode;

        internal LayerViewViewModel(DrawingCanvas canvas, IEnumerable<DrawingLayer>? layers)
        {
            _canvas = canvas;
            Initialize(layers);

            LayerViewModels.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                {
                    foreach (LayerViewModel vm in e.NewItems)
                    {
                        vm.PropertyChanged += LayerVm_PropertyChanged;
                        vm.Model.OnShapeSelectedCallback = _canvas.OnShapeSelected;
                        vm.Model.OnShapeDeselectedCallback = _canvas.OnShapeDeselected;
                        vm.Model.RedrawCallback = () => _canvas.Context?.RequestRedraw();
                        ActiveLayer = vm;
                    }
                }

                if (e.OldItems != null)
                {
                    foreach (LayerViewModel vm in e.OldItems)
                    {
                        vm.PropertyChanged -= LayerVm_PropertyChanged;
                    }

                    ActiveLayer = LayerViewModels.Count > 0
                        ? LayerViewModels[Math.Clamp(
                            e.OldStartingIndex > 0 ? e.OldStartingIndex - 1 : 0,
                            0, LayerViewModels.Count - 1)]
                        : null;

                    OnLayerChanged?.Invoke(this, EventArgs.Empty);
                }

                // Whenever collection changes, notify LayerCount binding
                OnPropertyChanged(nameof(LayerCount));
            };

            ActiveLayer = LayerViewModels.FirstOrDefault();

            // 订阅画布图形选中事件
            _canvas.SelectionChanged -= OnSelectionChanged;
            _canvas.SelectionChanged += OnSelectionChanged;
        }

        // ── 命令 ──────────────────────────────────────────────

        [RelayCommand]
        private void AddLayer()
        {
            var newLayer = new LayerViewModel(new DrawingLayer { Name = $"L{SerialNumber.NextId()}", SortId = LayerViewModels.Count });
            _canvas.CommandHistory.Execute(new CommandAddLayer(LayerViewModels, new LayerViewModel[] { newLayer }));

            // 清除所有选中，切换选中到新增图层
            ClearAllSelection();
            newLayer.IsSelected = true;
            ActiveLayer = newLayer;
            OnSelectionChanged();

            OnLayerChanged?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand(CanExecute = nameof(CanRemove))]
        private void RemoveLayer()
        {
            if (ActiveLayer == null) return;
            if (!CanRemoveLayer(new List<LayerViewModel> { ActiveLayer })) return;

            var command = new CommandRemoveLayer(LayerViewModels, new LayerViewModel[] { ActiveLayer });
            _canvas.CommandHistory.Execute(command);
            OnLayerChanged?.Invoke(this, EventArgs.Empty);
        }
        private bool CanRemove() => ActiveLayer != null;

        [RelayCommand]
        public void CopyLayer(LayerViewModel? targetLayer = null)
        {
            // 如果未指定图层，使用当前激活图层
            var sourceLayer = targetLayer ?? ActiveLayer;
            if (sourceLayer == null) return;

            // 通过 EventBus 请求 UI 层弹出输入对话框，获取复制数量
            var results = EventBus.Instance.Publish<CopyLayerCountRequestEvent, int?>(
                new CopyLayerCountRequestEvent { LayerName = sourceLayer.Name });

            int? copyCount = results.FirstOrDefault();
            if (copyCount == null || copyCount <= 0) return;

            // 收集原图层所有图形的 UId（含容器子图形），用于后续参数复制映射
            var allUidMappings = new Dictionary<int, int>();
            var newLayers = new List<LayerViewModel>();

            for (int i = 1; i <= copyCount.Value; i++)
            {
                // 创建新图层
                var newDrawingLayer = new DrawingLayer
                {
                    Name = $"{sourceLayer.Name}_{i}",
                    SortId = LayerViewModels.Count + i - 1,
                    IsVisible = sourceLayer.Model.IsVisible,
                    IsLocked = sourceLayer.Model.IsLocked,
                    Color = sourceLayer.Model.Color,
                    SkipBatchBasicShapes = true, // 复制图层跳过 BatchBasicShapes，保持图形独立显示
                };

                // 克隆原图层的所有图形（含不可见图形）
                // 仅展开由 BatchBasicShapes 自动创建的组合，用户手动创建的组合保持原样
                var sourceModel = sourceLayer.Model;
                var originalShapes = sourceModel.AllShapesInternal;
                foreach (var shape in originalShapes)
                {
                    if (shape is DrawCombination combination && combination.IsBatchedBasicShapes)
                    {
                        // 展开自动批量组合，将子图形单独克隆添加
                        foreach (var child in combination.Children)
                        {
                            var childClone = child.Clone();
                            UpdateShapeLayerId(childClone, newDrawingLayer.UId);
                            BuildUIdMapping(child, childClone, allUidMappings);
                            ClearLayerPenRefsRecursive(childClone, sourceModel);
                            newDrawingLayer.AddShape(childClone);
                        }
                    }
                    else
                    {
                        var clone = shape.Clone();
                        UpdateShapeLayerId(clone, newDrawingLayer.UId);
                        BuildUIdMapping(shape, clone, allUidMappings);
                        ClearLayerPenRefsRecursive(clone, sourceModel);
                        newDrawingLayer.AddShape(clone);
                    }
                }

                newLayers.Add(new LayerViewModel(newDrawingLayer));
            }

            // 通过 CommandManager 执行，支持撤销
            _canvas.CommandHistory.Execute(new CommandAddLayer(LayerViewModels, newLayers));
            OnLayerChanged?.Invoke(this, EventArgs.Empty);

            // 发布参数复制事件，由 UI 层处理加工参数的复制
            EventBus.Instance.Publish(new CopyLayerParametersEvent
            {
                CanvasId = _canvas.Id,
                OldToNewUIdMap = allUidMappings
            });
        }

        /// <summary>
        /// 递归更新图形及其子图形的 LayerId
        /// </summary>
        private static void UpdateShapeLayerId(IShape shape, int newLayerId)
        {
            shape.LayerId = newLayerId;
            if (shape is IContainer container)
            {
                foreach (var child in container.Children)
                    UpdateShapeLayerId(child, newLayerId);
            }
        }

        /// <summary>
        /// 递归建立原图形 → 克隆图形的 UId 映射（含容器子图形）
        /// </summary>
        private static void BuildUIdMapping(IShape original, IShape clone, Dictionary<int, int> mapping)
        {
            mapping[original.UId] = clone.UId;
            if (original is IContainer origContainer && clone is IContainer cloneContainer)
            {
                int count = Math.Min(origContainer.Children.Count, cloneContainer.Children.Count);
                for (int i = 0; i < count; i++)
                    BuildUIdMapping(origContainer.Children[i], cloneContainer.Children[i], mapping);
            }
        }

        [RelayCommand(CanExecute = nameof(CanMoveUp))]
        private void MoveUp() => MoveLayer(-1);
        private bool CanMoveUp() =>
            ActiveLayer != null && LayerViewModels.IndexOf(ActiveLayer) > 0;

        [RelayCommand(CanExecute = nameof(CanMoveDown))]
        private void MoveDown() => MoveLayer(+1);
        private bool CanMoveDown() =>
            ActiveLayer != null && LayerViewModels.IndexOf(ActiveLayer) < LayerViewModels.Count - 1;

        private bool CanRemoveLayer(List<LayerViewModel> selectedLayerNodes)
        {
            if (LayerViewModels.Count <= selectedLayerNodes.Count)
            {
                MessageBox.Show("至少需要一个图层！", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            return true;
        }

        private void MoveLayer(int delta)
        {
            if (ActiveLayer == null) return;
            int idx = LayerViewModels.IndexOf(ActiveLayer);
            int newIdx = idx + delta;
            if (newIdx < 0 || newIdx >= LayerViewModels.Count) return;
            //Layers.Move(idx, newIdx);
            LayerViewModels.Move(idx, newIdx);
            UpdateLayerSortIds();
            OnLayerChanged?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private void ToggleLayerVisible(INodeViewModel? node)
        {
            if (node == null) return;

            // If the node itself is a layer, toggle it directly
            if (node is LayerViewModel layer)
            {
                layer.IsVisible = !layer.IsVisible;
                SyncIsAllVisible();
                OnLayerChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            // Otherwise find its parent layer and toggle that
            var parent = node.Parent;
            while (parent != null && parent is not LayerViewModel)
                parent = parent.Parent;

            if (parent is LayerViewModel parentLayer)
            {
                parentLayer.IsVisible = !parentLayer.IsVisible;
                SyncIsAllVisible();
                OnLayerChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        [RelayCommand]
        private void ToggleLock(LayerViewModel? layer)
        {
            if (layer == null) return;
            layer.IsLocked = !layer.IsLocked;
            OnLayerChanged?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private void Notify(INodeViewModel vm)
        {
            OnSelectionChanged();
        }
        /// <summary>
        /// 切换单一图层显示模式：开启时仅显示当前激活图层，关闭时恢复各图层原始可见性
        /// </summary>
        [RelayCommand]
        private void SingleLayer()
        {
            IsSingle = !IsSingle;

            if (IsSingle)
            {
                // 进入单一模式：保存当前可见状态，仅保留激活图层可见
                _originalVisibility.Clear();
                foreach (var layer in LayerViewModels)
                {
                    _originalVisibility[layer.Id] = layer.IsVisible;
                    layer.IsVisible = layer == ActiveLayer;
                }
            }
            else
            {
                // 退出单一模式：恢复各图层原始可见性
                foreach (var layer in LayerViewModels)
                {
                    if (_originalVisibility.TryGetValue(layer.Id, out var wasVisible))
                        layer.IsVisible = wasVisible;
                    else
                        layer.IsVisible = true;
                }
                _originalVisibility.Clear();
            }

            SyncIsAllVisible();
            OnLayerChanged?.Invoke(this, EventArgs.Empty);
        }

        // ── 多选逻辑 ───────────────────────────────────────────

        /// <summary>
        /// 处理节点选择，支持 Ctrl / Shift 多选，锁定的图层/节点不可选中
        /// </summary>
        /// <param name="node">被点击的节点</param>
        /// <param name="addToSelection">Ctrl 按下：切换选中</param>
        /// <param name="rangeSelect">Shift 按下：范围选中</param>
        public void SelectNode(INodeViewModel node, bool addToSelection, bool rangeSelect)
        {
            try
            {
                _isProcessingLayerTreeSelection = true;

                // 当已选中的图形是群组的子级时，不允许选中群组外的图形
                if (addToSelection || rangeSelect)
                {
                    var scope = FindSelectionScope();
                    if (scope != null && !IsNodeInScope(node, scope))
                    {
                        // 节点不在当前群组作用域内，忽略此次选择
                        return;
                    }
                }

                if (rangeSelect)
                {
                    ClearAllSelection();
                    if (_anchorNode != null)
                    {
                        SelectRange(_anchorNode, node);
                    }
                    else
                    {
                        // 无锚点时降级为普通点击
                        node.IsSelected = true;
                        _anchorNode = node;
                    }
                }
                else if (addToSelection)
                {
                    // Ctrl+Click: 切换当前节点，不改变锚点
                    node.IsSelected = !node.IsSelected;
                }
                else
                {
                    // 普通点击: 清除其他，仅选中当前，更新锚点
                    ClearAllSelection();
                    node.IsSelected = true;
                    _anchorNode = node;
                }

                // 激活所属图层
                SetActiveLayerForNode(node);

                // 更新最后选中的图形（用于"最后所选对象"对齐基准）
                if (node.IsSelected && node.NodeType == NodeType.Shape)
                {
                    var lastShape = node.GetAllShapes().FirstOrDefault();
                    if (lastShape != null)
                    {
                        _canvas.LastSelectedShape = lastShape;
                    }
                }

                OnSelectionChanged();
            }
            finally
            {
                _isProcessingLayerTreeSelection = false;
            }
        }

        /// <summary>
        /// 查找当前选中的作用域：
        /// - 如果已选中的节点在某个群组内，返回该群组节点；
        /// - 如果已选中的节点在图层层级（不在群组内），返回该图层节点；
        /// - 如果没有任何选中节点，返回 null（表示不限制作用域）。
        /// </summary>
        private INodeViewModel? FindSelectionScope()
        {
            foreach (var layerVm in LayerViewModels)
            {
                if (layerVm.IsSelected)
                    return null; // 图层级选中，不限制

                var scope = FindSelectionScopeInChildren(layerVm.Children, layerVm);
                if (scope != null)
                    return scope;
            }
            return null;
        }

        private static INodeViewModel? FindSelectionScopeInChildren(
            IList<INodeViewModel> children,
            LayerViewModel layer)
        {
            foreach (var child in children)
            {
                if (child.IsSelected)
                {
                    // 已选中的节点的父群组就是作用域
                    if (child.Parent is NodeGroupViewModel group)
                        return group;
                    // 选中节点在图层层级（不在群组内），作用域为图层
                    return layer;
                }

                if (child.Children.Count > 0)
                {
                    var scope = FindSelectionScopeInChildren(child.Children, layer);
                    if (scope != null)
                        return scope;
                }
            }
            return null;
        }

        /// <summary>
        /// 判断节点是否在指定的作用域内。
        /// - 群组作用域：节点的父级链中包含该群组；
        /// - 图层作用域：节点必须是该图层的直接子级（不在任何群组内）。
        /// </summary>
        private static bool IsNodeInScope(INodeViewModel node, INodeViewModel scope)
        {
            // 节点本身就在作用域内
            if (ReferenceEquals(node, scope))
                return true;

            // 图层作用域：节点必须是图层的直接子级（不在群组内）
            if (scope is LayerViewModel)
                return node.Parent is LayerViewModel layerParent && ReferenceEquals(layerParent, scope);

            // 群组作用域：节点的父级链中包含该群组
            var parent = node.Parent;
            while (parent != null)
            {
                if (ReferenceEquals(parent, scope))
                    return true;
                parent = parent.Parent;
            }

            return false;
        }

        /// <summary>
        /// 清除所有节点的选中状态
        /// </summary>
        public void ClearAllSelection()
        {
            foreach (var layerVm in LayerViewModels)
            {
                layerVm.IsSelected = false;
                layerVm.ClearSelection();
            }
        }

        /// <summary>
        /// 范围选择：选中 from 到 to 之间的同级节点
        /// </summary>
        private void SelectRange(INodeViewModel from, INodeViewModel to)
        {
            // 同一父节点下的范围选择
            if (from.Parent != null && from.Parent == to.Parent)
            {
                SelectRangeByIndex(from.Parent.Children, from, to);
            }
            // 两个都是图层：在顶层列表中选择
            else if (from is LayerViewModel fromLayer && to is LayerViewModel toLayer)
            {
                int fromIdx = LayerViewModels.IndexOf(fromLayer);
                int toIdx = LayerViewModels.IndexOf(toLayer);
                SelectRangeByIndex(LayerViewModels, fromIdx, toIdx);
            }
        }

        /// <summary>
        /// 在集合中按索引范围选中节点
        /// </summary>
        private static void SelectRangeByIndex<T>(IList<T> collection, int fromIdx, int toIdx) where T : INodeViewModel
        {
            if (fromIdx < 0 || toIdx < 0) return;
            int start = Math.Min(fromIdx, toIdx);
            int end = Math.Max(fromIdx, toIdx);
            for (int i = start; i <= end; i++)
            {
                collection[i].IsSelected = true;
            }
        }

        /// <summary>
        /// 在 IList 中按引用查找并范围选中节点
        /// </summary>
        private static void SelectRangeByIndex(IList<INodeViewModel> children, INodeViewModel from, INodeViewModel to)
        {
            int fromIdx = -1, toIdx = -1;
            for (int i = 0; i < children.Count; i++)
            {
                if (ReferenceEquals(children[i], from)) fromIdx = i;
                if (ReferenceEquals(children[i], to)) toIdx = i;
            }
            SelectRangeByIndex(children, fromIdx, toIdx);
        }



        /// <summary>
        /// 根据节点找到所属图层并设为 ActiveLayer
        /// </summary>
        private void SetActiveLayerForNode(INodeViewModel node)
        {
            if (node is LayerViewModel layerVm)
            {
                ActiveLayer = layerVm;
                return;
            }

            // 向上遍历找到包含该节点的图层
            var parent = node.Parent;
            while (parent != null)
            {
                if (parent is LayerViewModel pLayerVm)
                {
                    ActiveLayer = pLayerVm;
                    return;
                }
                parent = parent.Parent;
            }
        }

        /// <summary>
        /// 收集当前所有选中节点的图形，发布选中事件
        /// </summary>
        private void OnSelectionChanged()
        {
            var selectedShapes = new List<IShape>();
            var nodeTypes = new HashSet<NodeType>();


            foreach (var layerVm in LayerViewModels)
            {
                if (layerVm.IsSelected)
                {
                    selectedShapes.AddRange(layerVm.Model.Shapes);
                    nodeTypes.Add(NodeType.Layer);
                    continue; // 图层选中时不再重复收集子节点
                }

                CollectSelectedShapesFromChildren(layerVm.Children, selectedShapes, nodeTypes);
            }

            ParallelExecutor.ForEach(selectedShapes, shape =>
            {
                if (shape != null) shape.IsSelected = true;
            });

            bool selectionChanged = _canvas.Context.CompareSelectedShapes(selectedShapes);
            _canvas.SetSelectedShapes(selectedShapes);

            if (selectionChanged)
            {
                _canvas.Context.SelectState = SelectState.None;
            }

            if (selectedShapes.Count > 0 && _canvas.Context.SelectState == SelectState.None)
            {
                _canvas.Context.SelectState = SelectState.FirstSelected;
            }

            _canvas.SuppressSelectionPublishFromLayerChange = true;
            try
            {
                OnLayerChanged?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                _canvas.SuppressSelectionPublishFromLayerChange = false;
            }
            PublishNodeSelectedEvent(selectedShapes, LayerViewModels.Count(x => x.IsSelected) > 0 ? NodeType.Layer : NodeType.Shape);
        }

        /// <summary>
        /// 递归收集子节点中已选中节点的图形
        /// </summary>
        private static void CollectSelectedShapesFromChildren(
            IList<INodeViewModel> children,
            List<IShape> shapes,
            HashSet<NodeType> nodeTypes)
        {
            foreach (var child in children)
            {
                if (child.IsSelected)
                {
                    shapes.AddRange(child.GetAllShapes());
                    nodeTypes.Add(child.NodeType);
                    continue; // GetAllShapes 已含子节点图形，跳过递归
                }

                CollectSelectedShapesFromChildren(child.Children, shapes, nodeTypes);
            }
        }

        /// <summary>
        /// 从选中图形列表中移除那些被包含在其它选中容器内的子级。
        /// 当某个容器（Group/Combination/Hatch）被选中时，其子图形不应单独出现在最终选择列表中。
        /// </summary>
        private static List<IShape> RemoveChildrenFromSelection(List<IShape> allShapes)
        {
            if (allShapes == null || allShapes.Count == 0) return new List<IShape>();

            var childIds = new HashSet<int>();

            // 收集所有被选中容器所包含的子级 ID
            foreach (var shape in allShapes)
            {
                if (shape is IContainer container && container.Children != null)
                {
                    foreach (var child in container.Children)
                    {
                        if (child != null)
                            childIds.Add(child.UId);
                    }
                }
            }

            // 过滤掉那些出现在 childIds 中的图形
            var result = new List<IShape>();
            foreach (var shape in allShapes)
            {
                if (!childIds.Contains(shape.UId))
                {
                    result.Add(shape);
                }
            }

            return result;
        }

        /// <summary>
        /// 移动节点参数
        /// </summary>
        public record MoveNodeArgs(INodeViewModel Source, INodeViewModel TargetParent);

        /// <summary>
        /// 判断是否允许拖放：群组成员只能在群组内部移动，不允许移出群组
        /// </summary>
        public bool CanDrop(INodeViewModel source, INodeViewModel target, DropPosition position)
        {
            if (source == null || target == null || source == target) return false;

            // 允许图层拖拽排序：仅允许 Before/After 位置
            if (source is LayerViewModel sourceLayer)
            {
                if (target is not LayerViewModel) return false;
                if (position == DropPosition.Inside) return false;
                return true;
            }

            // 群组子节点只能在同一群组内重排，不能移出
            if (source.Parent is NodeGroupViewModel sourceGroup)
            {
                if (ReferenceEquals(target, sourceGroup))
                    return position != DropPosition.Inside;

                // 只允许在同一群组内 Before/After
                if (target.Parent != sourceGroup) return false;
                if (position == DropPosition.Inside) return false;
                return true;
            }

            // 不允许将节点拖入群组内部（除非已在群组内）
            if (position == DropPosition.Inside && target is NodeGroupViewModel) return false;

            // 不允许拖入 Hatch 或 Combination 容器
            if (position == DropPosition.Inside && target is not LayerViewModel) return false;

            // 不允许拖到自己的后代节点上，避免形成循环层级
            if (IsDescendantOf(source, target)) return false;

            return true;
        }

        /// <summary>
        /// 图层拖拽排序：将 sourceLayer 移动到 targetLayer 的 Before/After 位置，并更新 SortId
        /// </summary>
        private void ReorderLayer(LayerViewModel sourceLayer, LayerViewModel targetLayer, DropPosition position)
        {
            int sourceIdx = LayerViewModels.IndexOf(sourceLayer);
            int targetIdx = LayerViewModels.IndexOf(targetLayer);
            if (sourceIdx < 0 || targetIdx < 0) return;

            // 计算目标索引
            int newIdx = position == DropPosition.Before ? targetIdx : targetIdx + 1;
            if (sourceIdx < newIdx) newIdx--;
            if (sourceIdx == newIdx) return;

            // 执行移动
            LayerViewModels.Move(sourceIdx, newIdx);

            // 更新所有图层的 SortId 以匹配当前顺序
            UpdateLayerSortIds();

            OnLayerChanged?.Invoke(this, EventArgs.Empty);
        }

        internal bool ReorderLayerToSlot(LayerViewModel sourceLayer, int slotIndex)
        {
            int sourceIdx = LayerViewModels.IndexOf(sourceLayer);
            if (sourceIdx < 0) return false;

            int clampedSlotIndex = Math.Clamp(slotIndex, 0, LayerViewModels.Count);
            int newIdx = clampedSlotIndex;
            if (sourceIdx < newIdx) newIdx--;
            if (newIdx < 0) newIdx = 0;
            if (newIdx >= LayerViewModels.Count) newIdx = LayerViewModels.Count - 1;
            if (sourceIdx == newIdx) return false;

            LayerViewModels.Move(sourceIdx, newIdx);
            UpdateLayerSortIds();
            OnLayerChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        /// <summary>
        /// 根据当前 LayerViewModels 集合顺序更新所有图层的 SortId
        /// </summary>
        private void UpdateLayerSortIds()
        {
            for (int i = 0; i < LayerViewModels.Count; i++)
            {
                LayerViewModels[i].SortId = i;
            }
        }

        /// <summary>
        /// 执行拖放排序：将 source 节点移动到 target 的 Before/After 位置
        /// </summary>
        public void ReorderNode(INodeViewModel source, INodeViewModel target, DropPosition position)
        {
            if (!CanDrop(source, target, position)) return;

            // 图层拖拽排序
            if (source is LayerViewModel sourceLayer && target is LayerViewModel targetLayer)
            {
                ReorderLayer(sourceLayer, targetLayer, position);
                return;
            }

            if (TryGetNodeReorderRequest(source, target, position, out var request))
            {
                if (ReorderNodeToSlot(request.Source, request.TargetContainer, request.SlotIndex))
                    return;
            }
        }

        internal bool ReorderNodeToSlot(
            INodeViewModel source,
            INodeViewModel targetContainer,
            int slotIndex)
        {
            if (source is LayerViewModel || targetContainer == null)
                return false;

            var oldParent = source.Parent;
            if (oldParent == null)
                return false;

            if (oldParent is NodeGroupViewModel sourceGroup && !ReferenceEquals(targetContainer, sourceGroup))
                return false;

            if (ReferenceEquals(oldParent, targetContainer))
            {
                var children = targetContainer.Children;
                int sourceIdx = children.IndexOf(source);
                if (sourceIdx < 0)
                    return false;

                int clampedSlotIndex = Math.Clamp(slotIndex, 0, children.Count);
                int newIdx = clampedSlotIndex;
                if (sourceIdx < newIdx)
                    newIdx--;
                if (newIdx < 0)
                    newIdx = 0;
                if (newIdx >= children.Count)
                    newIdx = children.Count - 1;
                if (sourceIdx == newIdx)
                    return false;

                if (children is VirtualizingNodeCollection vc)
                    vc.Move(sourceIdx, newIdx);
                else if (children is ObservableCollection<INodeViewModel> oc)
                    oc.Move(sourceIdx, newIdx);
                else
                {
                    children.RemoveAt(sourceIdx);
                    children.Insert(newIdx, source);
                }

                SyncModelReorder(targetContainer, source, children);
                OnLayerChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }

            int targetInsertIdx = Math.Clamp(slotIndex, 0, targetContainer.Children.Count);
            if (!CanMoveNodeToContainer(source, targetContainer))
                return false;

            MoveNodeToContainerAtIndex(source, oldParent, targetContainer, targetInsertIdx);
            OnLayerChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        private static bool TryGetNodeReorderRequest(
            INodeViewModel source,
            INodeViewModel target,
            DropPosition position,
            out NodeReorderRequest request)
        {
            request = default;

            if (source is LayerViewModel)
                return false;

            if (target is LayerViewModel targetLayer)
            {
                int slotIndex = position == DropPosition.Before
                    ? 0
                    : targetLayer.Children.Count;
                request = new NodeReorderRequest(source, targetLayer, slotIndex);
                return true;
            }

            if (ReferenceEquals(source.Parent, target) && IsContainerNode(target))
            {
                int slotIndex = position == DropPosition.Before
                    ? 0
                    : target.Children.Count;
                request = new NodeReorderRequest(source, target, slotIndex);
                return true;
            }

            if (source.Parent != null && ReferenceEquals(source.Parent, target.Parent))
            {
                int targetIdx = target.Parent.Children.IndexOf(target);
                if (targetIdx < 0)
                    return false;

                int slotIndex = position == DropPosition.Before
                    ? targetIdx
                    : targetIdx + 1;
                request = new NodeReorderRequest(source, target.Parent, slotIndex);
                return true;
            }

            return false;
        }

        private static bool CanMoveNodeToContainer(INodeViewModel source, INodeViewModel targetContainer)
        {
            if (source is LayerViewModel)
                return false;

            if (source.Parent is NodeGroupViewModel sourceGroup)
                return ReferenceEquals(targetContainer, sourceGroup);

            return targetContainer is LayerViewModel;
        }

        private static void MoveNodeToContainerAtIndex(
            INodeViewModel source,
            INodeViewModel oldParent,
            INodeViewModel targetContainer,
            int insertIdx)
        {
            int oldIdx = oldParent.Children.IndexOf(source);
            if (oldIdx < 0)
                return;

            if (oldParent.Children is VirtualizingNodeCollection oldVc)
                oldVc.RemoveAt(oldIdx);
            else
                oldParent.Children.RemoveAt(oldIdx);

            int targetIdx = Math.Clamp(insertIdx, 0, targetContainer.Children.Count);
            targetContainer.Children.Insert(targetIdx, source);

            RemoveShapeFromParentModel(source, oldParent);
            AddShapeToParentModel(source, targetContainer);
            SyncModelReorder(targetContainer, source, targetContainer.Children);
            source.Parent = targetContainer;
        }

        private static bool IsContainerNode(INodeViewModel node)
        {
            return node is LayerViewModel or NodeGroupViewModel or NodeHatchViewModel or NodeCombinationViewModel;
        }

        private readonly record struct NodeReorderRequest(
            INodeViewModel Source,
            INodeViewModel TargetContainer,
            int SlotIndex);

        /// <summary>
        /// 同步 ViewModel 顺序到 Model
        /// </summary>
        private static void SyncModelReorder(INodeViewModel? parent, INodeViewModel movedNode, IList<INodeViewModel> children)
        {
            var movedModel = GetShapeModel(movedNode);
            if (movedModel == null) return;

            if (parent is LayerViewModel layerVm)
            {
                // 找到 movedNode 在 children 中的新索引
                int newIdx = children.IndexOf(movedNode);
                if (newIdx >= 0)
                {
                    layerVm.Model.MoveShape(movedModel, newIdx);
                }
            }
            else if (parent is NodeGroupViewModel groupVm && groupVm.Model is IContainer gContainer)
            {
                int newIdx = children.IndexOf(movedNode);
                if (newIdx >= 0)
                {
                    gContainer.Children.Remove(movedModel);
                    if (newIdx > gContainer.Children.Count) newIdx = gContainer.Children.Count;
                    gContainer.Children.Insert(newIdx, movedModel);
                }
            }
            else if (parent is NodeHatchViewModel hatchVm && hatchVm.Model is IContainer hContainer)
            {
                int newIdx = children.IndexOf(movedNode);
                if (newIdx >= 0)
                {
                    hContainer.Children.Remove(movedModel);
                    if (newIdx > hContainer.Children.Count) newIdx = hContainer.Children.Count;
                    hContainer.Children.Insert(newIdx, movedModel);
                }
            }
            else if (parent is NodeCombinationViewModel combVm && combVm.Model is IContainer cContainer)
            {
                int newIdx = children.IndexOf(movedNode);
                if (newIdx >= 0)
                {
                    cContainer.Children.Remove(movedModel);
                    if (newIdx > cContainer.Children.Count) newIdx = cContainer.Children.Count;
                    cContainer.Children.Insert(newIdx, movedModel);
                }
            }
        }

        /// <summary>
        /// 移动节点到指定父节点下
        /// </summary>
        [RelayCommand]
        private void MoveNode(MoveNodeArgs args)
        {
            var source = args.Source;
            var target = args.TargetParent;

            if (source.Parent == null || target == null) return;
            if (source.Parent == target) return;
            if (IsDescendantOf(target, source)) return; // 防止循环引用

            // ViewModel 层面：从旧父节点移除，添加到新父节点
            int oldIdx = source.Parent.Children.IndexOf(source);
            if (oldIdx >= 0)
            {
                if (source.Parent.Children is VirtualizingNodeCollection oldVc)
                    oldVc.RemoveAt(oldIdx);
                else
                    source.Parent.Children.Remove(source);
            }
            target.Children.Insert(target.Children.Count, source);

            // Model 层面同步
            RemoveShapeFromParentModel(source, source.Parent);
            AddShapeToParentModel(source, target);

            source.Parent = target;
        }

        /// <summary>
        /// 判断 potentialDescendant 是否在 potentialAncestor 的子树中
        /// </summary>
        private static bool IsDescendantOf(INodeViewModel potentialAncestor, INodeViewModel potentialDescendant)
        {
            var current = potentialDescendant.Parent;
            while (current != null)
            {
                if (current == potentialAncestor) return true;
                current = current.Parent;
            }
            return false;
        }

        /// <summary>
        /// 从父节点的模型中移除图形
        /// </summary>
        private static void RemoveShapeFromParentModel(INodeViewModel node, INodeViewModel parent)
        {
            var model = GetShapeModel(node);
            if (model == null) return;

            if (parent is LayerViewModel layerVm)
            {
                layerVm.Model.RemoveShape(model);
            }
            else if (parent is NodeGroupViewModel groupVm && groupVm.Model is IContainer gContainer)
            {
                gContainer.Children.Remove(model);
            }
            else if (parent is NodeHatchViewModel hatchVm && hatchVm.Model is IContainer hContainer)
            {
                hContainer.Children.Remove(model);
            }
            else if (parent is NodeCombinationViewModel combVm && combVm.Model is IContainer cContainer)
            {
                cContainer.Children.Remove(model);
            }
        }

        /// <summary>
        /// 向父节点的模型中添加图形
        /// </summary>
        private static void AddShapeToParentModel(INodeViewModel node, INodeViewModel parent)
        {
            var model = GetShapeModel(node);
            if (model == null) return;

            if (parent is LayerViewModel layerVm)
            {
                layerVm.Model.AddShape(model);
            }
            else if (parent is NodeGroupViewModel groupVm && groupVm.Model is IContainer gContainer)
            {
                gContainer.Children.Add(model);
                // 将容器的图层引用传播到子图形
                if (groupVm.Model is DrawObject containerObj)
                    PropagateOwningLayer(model, containerObj.OwningLayer);
            }
            else if (parent is NodeHatchViewModel hatchVm && hatchVm.Model is IContainer hContainer)
            {
                hContainer.Children.Add(model);
                if (hatchVm.Model is DrawObject containerObj)
                    PropagateOwningLayer(model, containerObj.OwningLayer);
            }
            else if (parent is NodeCombinationViewModel combVm && combVm.Model is IContainer cContainer)
            {
                cContainer.Children.Add(model);
                if (combVm.Model is DrawObject containerObj)
                    PropagateOwningLayer(model, containerObj.OwningLayer);
            }
        }

        /// <summary>
        /// 将图层引用和回调递归传播到图形及其子图形
        /// </summary>
        private static void PropagateOwningLayer(IShape shape, DrawingLayer? layer)
        {
            if (layer == null || shape is not DrawObject drawObj) return;
            drawObj.OwningLayer = layer;
            drawObj.OnShapeSelectedAction = layer.OnShapeSelectedCallback;
            drawObj.OnShapeDeselectedAction = layer.OnShapeDeselectedCallback;
            // 递归传播到子容器
            if (shape is IContainer container && container.Children != null)
            {
                foreach (var child in container.Children)
                    PropagateOwningLayer(child, layer);
            }
        }

        /// <summary>
        /// 递归清除克隆图形中对源图层 LayerPen 的引用，让 Pen getter 解析到新图层的 LayerPen。
        /// 仅在 Clone() 后调用，因为 Clone() 会将源 LayerPen 引用存入 _pen。
        /// </summary>
        private static void ClearLayerPenRefsRecursive(IShape shape, DrawingLayer sourceLayer)
        {
            if (shape is not DrawObject drawObj) return;
            drawObj.ClearLayerPenReference(sourceLayer);
            if (shape is IContainer container && container.Children != null)
            {
                for (int i = 0; i < container.Children.Count; i++)
                    ClearLayerPenRefsRecursive(container.Children[i], sourceLayer);
            }
        }

        /// <summary>
        /// 获取节点关联的 IShape 模型
        /// </summary>
        private static IShape? GetShapeModel(INodeViewModel node)
        {
            return node switch
            {
                NodeShapeViewModel s => s.Model,
                NodeGroupViewModel g => g.Model,
                NodeHatchViewModel h => h.Model,
                NodeCombinationViewModel c => c.Model,
                _ => null
            };
        }

        // ── 初始化 ─────────────────────────────────────────────

        private void Initialize(IEnumerable<DrawingLayer>? layers)
        {
            LayerViewModels.Clear();
            if (layers == null)
            {
                AddLayer();
            }
            else
            {
                // 按 SortId 排序，如果 SortId 相同则保持原始顺序
                var sortedLayers = layers
                    .Select((layer, index) => new { Layer = layer, SortKey = layer.SortId > 0 ? layer.SortId : index })
                    .OrderBy(x => x.SortKey)
                    .Select(x => x.Layer)
                    .ToList();

                foreach (var layer in sortedLayers)
                {
                    LayerViewModels.Add(new LayerViewModel(layer));
                }

                // 初始化 SortId（确保所有图层都有正确的排序ID）
                UpdateLayerSortIds();
            }

            // 根据当前图层数量初始化序号生成器，使新图层序号接续已有图层
            SerialNumber = new SerialNumberGenerator(LayerCount);
        }

        private void LayerVm_PropertyChanged(object? sender,
            System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(LayerViewModel.IsVisible))
            {
                SyncIsAllVisible();
                OnSelectionChanged();
            }
            else if (e.PropertyName is nameof(LayerViewModel.IsLocked)
                               or nameof(LayerViewModel.IsSelected))
            {
                OnSelectionChanged();
            }
        }

        #region 处理画布选中事件
        /// <summary>
        /// 处理画布选中事件
        /// </summary>
        private void OnSelectionChanged(IEnumerable<IShape> shapes)
        {
            // 如果正在处理图层树选中变化，不同步画布的回调
            // 因为 OnSelectionChanged() 无参版本已经会调用 SetSelectedShapes()
            if (_isProcessingLayerTreeSelection)
            {
                return;
            }

            // 收集所有选中图形的ID
            var selectedIds = shapes.Where(x => x != null).Select(x => x.UId).ToList();

            // 同步子图形选中状态到图层树
            SelectNodesInLayerView(selectedIds);

            // 激活所属图层
            if (selectedIds.Count > 0)
            {
                ActiveLayer = LayerViewModels.FirstOrDefault(x => x.Model.UId == ((DrawObject)shapes.Last()).OwningLayer?.UId);
            }

            PublishNodeSelectedEvent(shapes, shapes.Count() > 0 ? NodeType.Shape : NodeType.Canvas);
        }

        private void PublishNodeSelectedEvent(IEnumerable<IShape> shapes, NodeType nodeType)
        {
            var selectedIds = shapes.Where(x => x != null).Select(x => x.UId).ToList();
            var uniformType = GetUniformType(shapes);
            var shape = uniformType != null ? shapes.OfType<DrawObject>().FirstOrDefault() : null;

            // 发布 NodeSelectedEvent 事件
            var nodeSelectedEvent = new NodeSelectedEvent
            {
                CanvasId = _canvas.Id,
                NodeType = nodeType,
                Summary = new NodeSelectedSummary
                {
                    EditingObject = shape == null ? null : DrawObjectMapper.MapWithoutChildren(shape),
                    UniformType = uniformType == null ? null : ShapeTypeMapper.Map(uniformType.Value),
                    TotalCount = selectedIds.Count,
                    TotalCountWithChildren = shapes.Sum(s => s.FlattenCount),
                    SelectionIds = selectedIds
                }
            };

            EventBus.Instance.Publish<NodeSelectedEvent>(nodeSelectedEvent);
        }

        private ShapeType? GetUniformType(IEnumerable<IShape> shapes)
        {
            ShapeType? first = null;

            DrawObject drawObject;
            foreach (var shape in shapes)
            {
                drawObject = shape as DrawObject;
                if (drawObject == null) continue;
                if (first == null)
                {
                    first = drawObject.Type;
                }
                else if (shape.Type != first)
                {
                    return null;
                }
            }

            return first;
        }

        /// <summary>
        /// 在图层控件中选中对应ID的节点
        /// </summary>
        private void SelectNodesInLayerView(List<int> selectedIds)
        {
            // 清除之前的选中状态（仅当此方法被正确调用时，即从画布同步而不是从图层树操作）
            ClearAllSelection();

            foreach (var id in selectedIds)
            {
                foreach (var layerVm in LayerViewModels)
                {
                    layerVm.SelectNodeById(id);
                }
            }

            // 更新锚点为最后一个选中节点（用于后续 Shift 范围选择）
            if (selectedIds.Count > 0)
            {
                _anchorNode = FindNodeById(LayerViewModels, selectedIds[^1]);
            }
            else
            {
                _anchorNode = null;
            }
        }

        private static INodeViewModel? FindNodeById(
            ObservableCollection<LayerViewModel> layers, int uid)
        {
            foreach (var layer in layers)
            {
                if (layer.Id == uid) return layer;
                var found = FindNodeByIdRecursive(layer.Children, uid);
                if (found != null) return found;
            }
            return null;
        }

        private static INodeViewModel? FindNodeByIdRecursive(
            IList<INodeViewModel> children, int uid)
        {
            // VirtualizingNodeCollection：基于模型层查找
            if (children is VirtualizingNodeCollection vc)
            {
                var idx = vc.IndexOfModelId(uid);
                if (idx >= 0) return vc[idx];
                return null;
            }

            foreach (var child in children)
            {
                if (child.Id == uid) return child;
                var found = FindNodeByIdRecursive(child.Children, uid);
                if (found != null) return found;
            }
            return null;
        }

        #endregion

        #region Delete 选中节点

        /// <summary>
        /// 删除当前选中的节点，根据节点类型执行对应层级的删除操作：
        /// <list type="bullet">
        ///   <item><b>Layer</b>：删除整个图层</item>
        ///   <item><b>Shape / Group / Hatch / Combination</b>：从所属图层中删除对应图形</item>
        /// </list>
        /// </summary>
        /// <returns>是否执行了删除操作</returns>
        public bool DeleteSelectedNodes()
        {
            // 1. 收集所有选中的顶层节点（跨图层）
            var selectedLayerNodes = new List<LayerViewModel>();
            var selectedShapes = new List<IShape>();
            foreach (var layerVm in LayerViewModels.ToList())
            {
                if (layerVm.IsSelected)
                {
                    // 图层被选中 → 删除整个图层
                    selectedLayerNodes.Add(layerVm);
                    continue;
                }

                // 收集该图层下被选中的子节点图形（仅遍历已缓存的 ViewModel，不触发全量创建）
                CollectSelectedShapesFromVirtualizing(layerVm.Children, selectedShapes);
            }

            // 构建命令列表，统一通过 CommandManager 执行以支持撤销
            var commands = new List<IDrawingCommand>();

            // 2. 删除选中的图层（CommandRemoveLayer 支持撤销）
            if (selectedLayerNodes.Count > 0)
            {
                if (!CanRemoveLayer(selectedLayerNodes))
                    return false;

                commands.Add(new CommandRemoveLayer(LayerViewModels, selectedLayerNodes));
            }

            // 3. 删除选中的图形（CommandRemove 支持撤销）
            if (selectedShapes.Count > 0 && _canvas != null)
            {
                commands.Add(new CommandRemove(_canvas.LayerViewModels, selectedShapes));
            }

            if (commands.Count == 0)
                return false;

            // 通过 CommandManager 执行，确保所有删除操作可通过一次 Ctrl+Z 整体撤销
            IDrawingCommand finalCommand = commands.Count == 1
                ? commands[0]
                : new CompositeCommand($"删除 {selectedLayerNodes.Count} 个图层和 {selectedShapes.Count} 个图形", commands);

            _canvas.CommandHistory.Execute(finalCommand);
            OnLayerChanged?.Invoke(this, EventArgs.Empty);

            return true;
        }

        /// <summary>
        /// 从 VirtualizingNodeCollection 中收集被选中的子节点图形（仅遍历已缓存的 ViewModel，避免触发全量创建）
        /// </summary>
        private static void CollectSelectedShapesFromVirtualizing(VirtualizingNodeCollection children, List<IShape> shapes)
        {
            foreach (var kvp in children.CachedItems)
            {
                var child = kvp.Value;
                if (child.IsSelected)
                {
                    var shape = child.GetAllShapes().FirstOrDefault();
                    if (shape != null)
                        shapes.Add(shape);
                    continue;
                }

                // 容器节点未选中但子节点可能被选中，递归检查其子节点
                if (child.Children.Count > 0)
                {
                    CollectSelectedShapes(child.Children, shapes);
                }
            }
        }

        /// <summary>
        /// 递归收集子节点中被选中的图形
        /// </summary>
        private static void CollectSelectedShapes(IList<INodeViewModel> children, List<IShape> shapes)
        {
            foreach (var child in children)
            {
                if (child.IsSelected)
                {
                    // 选中容器（Group/Hatch/Combination）时删除整个容器
                    var shape = child.GetAllShapes().FirstOrDefault();
                    if (shape != null)
                        shapes.Add(shape);
                    continue; // GetAllShapes 已包含子级，无需递归
                }

                // 容器节点未选中但子节点可能被选中，递归检查
                if (child.Children.Count > 0)
                {
                    CollectSelectedShapes(child.Children, shapes);
                }
            }
        }

        #endregion

        public void Dispose() // 或在适当位置调用
        {
            if (_canvas != null)
                _canvas.SelectionChanged -= OnSelectionChanged;

            // 取消所有 LayerViewModel 的事件订阅
            foreach (var vm in LayerViewModels)
            {
                vm.PropertyChanged -= LayerVm_PropertyChanged;
            }
            LayerViewModels.Clear();

            ActiveLayer = null;
            _anchorNode = null;
        }

        /// <summary>
        /// 激活图层切换时：单一图层模式下自动更新可见性
        /// </summary>
        partial void OnActiveLayerChanged(LayerViewModel? value)
        {
            if (IsSingle && value != null)
            {
                foreach (var layer in LayerViewModels)
                    layer.IsVisible = layer == value;
                SyncIsAllVisible();
                OnLayerChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
