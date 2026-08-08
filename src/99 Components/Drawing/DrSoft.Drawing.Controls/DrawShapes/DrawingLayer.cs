using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Model;
using SkiaSharp;
using System.Collections.ObjectModel;

namespace DrSoft.Drawing.Controls.DrawShapes
{
    /// <summary>
    /// 图层，同时实现 <see cref="ILayerData"/> 只读数据契约，供打标卡直接访问图层数据。
    /// </summary>
    public class DrawingLayer : ILayerData
    {
        public int UId { get; set; }

        public string Name { get; set; }
        public bool IsVisible { get; set; } = true;

        private string _color = "#000000";
        /// <summary>
        /// 图层颜色（十六进制），设置时更新图层共享画笔颜色，无需遍历图形。
        /// </summary>
        public string Color
        {
            get => _color;
            set
            {
                if (_color == value) return;
                _color = value;
                // 只更新图层级画笔，所有引用此画笔的图形自动生效
                LayerPen.Color = string.IsNullOrEmpty(_color) ? SKColors.Black : SKColor.Parse(_color);
                RedrawCallback?.Invoke();
            }
        }

        /// <summary>
        /// 图层级共享画笔，同一图层所有图形共用此画笔（零额外分配）。
        /// 图形自定义画笔（_pen != null）时覆盖此画笔。
        /// </summary>
        internal readonly SKPaint LayerPen = new() { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 0.15f };

        /// <summary>
        /// 图层排序ID，用于拖拽排序后记录顺序，默认按此值排序
        /// </summary>
        public int SortId { get; set; }

        private bool _isLocked = false;
        public bool IsLocked { get => _isLocked; set => _isLocked = value; }

        /// <summary>
        /// 是否跳过 BatchBasicShapes（将基础图形合并为 DrawCombination 显示）。
        /// 复制图层时设为 true，使图层树中每个图形独立显示而非合并为组合。
        /// </summary>
        internal bool SkipBatchBasicShapes { get; set; }

        private readonly ObservableCollection<IShape> _shapes = new();

        // ── ILayerData 实现：将图形暴露为只读 IShapeData 序列（零拷贝）──────────
        IReadOnlyList<IShapeData> ILayerData.Shapes =>
            _shapes.OfType<IShapeData>().ToList().AsReadOnly();

        // ── 选中图形缓存 ──
        // 由 DrawObject.IsSelected setter 主动注册/注销，避免每次 SetSelectedShapes 全量遍历
        private readonly HashSet<IShape> _selectedShapes = new();
        private readonly List<IShape> _selectedShapesSnapshot = new(); // 用于对外暴露快照
        private bool _selectedSnapshotDirty = true;

        /// <summary>
        /// 所属画布的选中注册回调，由 DrawingCanvas 在构造时注入。
        /// 用于在 AddShape 时设置 DrawObject 的画布级选中通知。
        /// </summary>
        internal Action<IShape>? OnShapeSelectedCallback { get; set; }
        internal Action<IShape>? OnShapeDeselectedCallback { get; set; }

        /// <summary>
        /// 画布重绘回调，由 DrawingCanvas 注入，图层属性变更时通知画布刷新。
        /// </summary>
        internal Action? RedrawCallback { get; set; }

        /// <summary>
        /// 当前图层中被选中的图形（顶层选中：如果父级选中则只返回父级，不返回其子级）。
        /// 由 DrawObject.IsSelected setter 增量维护，O(1) 注册/注销。
        /// </summary>
        public IReadOnlyList<IShape> SelectedShapes
        {
            get
            {
                if (_selectedSnapshotDirty)
                {
                    _selectedShapesSnapshot.Clear();
                    _selectedShapesSnapshot.AddRange(_selectedShapes);
                    _selectedSnapshotDirty = false;
                }
                return _selectedShapesSnapshot;
            }
        }

        /// <summary>
        /// 当前图层中被选中的图形（末级选中：展开所有容器，只返回叶子节点图形）。
        /// 用于需要逐个操作实际图形的场景（如填充线编辑、打标指令生成）。
        /// </summary>
        public IReadOnlyList<IShape> SelectedShapesLeafLevel
        {
            get
            {
                var result = new List<IShape>(_selectedShapes.Count);
                foreach (var shape in _selectedShapes)
                {
                    if (shape is IContainer container && container.Children?.Count > 0)
                    {
                        // 容器被选中：展开到叶子节点
                        foreach (var child in container.Children)
                            if (child.IsVisible)
                                result.Add(child);
                    }
                    else
                    {
                        result.Add(shape);
                    }
                }
                return result;
            }
        }

        /// <summary>
        /// 注册一个图形为选中状态（由 DrawObject.IsSelected setter 调用）
        /// </summary>
        internal void RegisterSelected(IShape shape)
        {
            if (_selectedShapes.Add(shape))
                _selectedSnapshotDirty = true;
        }

        /// <summary>
        /// 注销一个图形的选中状态（由 DrawObject.IsSelected setter 调用）
        /// </summary>
        internal void UnregisterSelected(IShape shape)
        {
            if (_selectedShapes.Remove(shape))
                _selectedSnapshotDirty = true;
        }

        /// <summary>
        /// 清除所有选中状态（仅清空缓存，不修改 DrawObject.IsSelected）
        /// </summary>
        internal void ClearSelectedCache()
        {
            _selectedShapes.Clear();
            _selectedSnapshotDirty = true;
        }

        /// <summary>
        /// 选中图形数量
        /// </summary>
        public int SelectedCount => _selectedShapes.Count;

        /// <summary>
        /// 对外只读访问图形集合（受保护，不允许直接修改）
        /// </summary>
        public IEnumerable<IShape> Shapes => (_shapes ?? Enumerable.Empty<IShape>()).Where(e => e.IsVisible);

        /// <summary>
        /// 获取所有图形（含不可见），用于内部操作
        /// </summary>
        internal IReadOnlyList<IShape> AllShapesInternal => _shapes;
        /// <summary>
        /// 向图层添加单个图形，并递归设置所有子图形的图层引用和回调。
        /// </summary>
        public void AddShape(IShape shape)
        {
            if (shape == null) return;
            _shapes.Add(shape);
            if (shape is DrawObject drawObj)
                PropagateOwningLayer(drawObj, this);
        }
        
        /// <summary>
        /// 在图层指定索引处插入单个图形，并递归设置所有子图形的图层引用和回调。
        /// </summary>
        public void InsertShape(int index, IShape shape)
        {
            if (shape == null) return;
            _shapes.Insert(index, shape);
            if (shape is DrawObject drawObj)
                PropagateOwningLayer(drawObj, this);
        }
        
        /// <summary>
        /// 向图层批量添加图形
        /// </summary>
        public void AddShapes(IEnumerable<IShape> shapes)
        {
            if (shapes == null) return;
            foreach (var s in shapes)
            {
                if (s != null)
                {
                    _shapes.Add(s);
                    if (s is DrawObject drawObj)
                        PropagateOwningLayer(drawObj, this);
                }
            }
        }

        /// <summary>
        /// 递归设置图形及其所有子图形的图层引用和回调。
        /// 若图形的 _pen 引用旧图层的 LayerPen（非自定义画笔），则清空以让 Pen getter 解析到新图层。
        /// </summary>
        private static void PropagateOwningLayer(DrawObject drawObj, DrawingLayer layer)
        {
            var oldLayer = drawObj.OwningLayer;
            drawObj.OwningLayer = layer;
            drawObj.OnShapeSelectedAction = layer.OnShapeSelectedCallback;
            drawObj.OnShapeDeselectedAction = layer.OnShapeDeselectedCallback;
            // 若 _pen 引用的是旧图层的共享画笔，清空以让 Pen getter 走新图层的 LayerPen
            drawObj.ClearLayerPenReference(oldLayer);
            if (drawObj is IContainer container && container.Children != null)
            {
                for (int i = 0; i < container.Children.Count; i++)
                {
                    if (container.Children[i] is DrawObject child)
                        PropagateOwningLayer(child, layer);
                }
            }
        }

        /// <summary>
        /// 从图层移除单个图形
        /// </summary>
        public bool RemoveShape(IShape shape)
        {
            if (shape == null) return false;
            var removed = _shapes.Remove(shape);
            if (removed)
            {
                if (shape is DrawObject drawObj)
                {
                    drawObj.OwningLayer = null;
                    if (drawObj.IsSelected)
                    {
                        UnregisterSelected(drawObj);
                        drawObj.SetIsSelectedSilent(false);
                        OnShapeDeselectedCallback?.Invoke(drawObj);
                    }
                }
            }
            return removed;
        }

        /// <summary>
        /// 从图层批量移除图形
        /// </summary>
        public void RemoveShapes(IEnumerable<IShape> shapes)
        {
            if (shapes == null) return;
            foreach (var s in shapes)
            {
                if (s != null && _shapes.Remove(s))
                {
                    if (s is DrawObject drawObj)
                    {
                        drawObj.OwningLayer = null;
                        if (drawObj.IsSelected)
                        {
                            UnregisterSelected(drawObj);
                            drawObj.SetIsSelectedSilent(false);
                            OnShapeDeselectedCallback?.Invoke(drawObj);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 清空图层所有图形（用于重建图形列表）
        /// </summary>
        internal void ClearShapes()
        {
            // 清除所有图形的 OwningLayer 和选中缓存
            foreach (var s in _shapes)
            {
                if (s is DrawObject drawObj)
                {
                    drawObj.OwningLayer = null;
                    if (drawObj.IsSelected)
                    {
                        drawObj.SetIsSelectedSilent(false);
                        OnShapeDeselectedCallback?.Invoke(drawObj);
                    }
                }
            }
            _shapes.Clear();
            ClearSelectedCache();
        }

        /// <summary>
        /// 将图形移动到指定索引位置（用于图层内排序）
        /// </summary>
        public void MoveShape(IShape shape, int newIndex)
        {
            if (shape == null) return;
            int oldIndex = _shapes.IndexOf(shape);
            if (oldIndex < 0 || oldIndex == newIndex) return;
            _shapes.RemoveAt(oldIndex);
            if (newIndex > oldIndex) newIndex--;
            _shapes.Insert(newIndex, shape);
        }

        /// <summary>
        /// 获取图形在图层中的索引
        /// </summary>
        public int IndexOf(IShape shape)
        {
            return shape == null ? -1 : _shapes.IndexOf(shape);
        }

        public DrawingLayer()
        {
            UId = UniqueIdGenerator.NextId();
        }

        public DrawingLayer(string name) : this()
        {
            Name = name;
        }
    }
}
