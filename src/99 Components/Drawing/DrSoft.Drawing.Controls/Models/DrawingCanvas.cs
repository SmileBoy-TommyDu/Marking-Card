using System.Collections.Concurrent;
using System.Numerics;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Controls.ViewModels;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Model;
using DrSoft.Drawing.Utility.AOP;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.Models
{
    /// <summary>
    /// 画布，同时实现 <see cref="ICanvasData"/> 只读数据契约，供打标卡零拷贝访问图层和图形数据。
    /// </summary>
    public partial class DrawingCanvas : ObservableObject, ICanvas, ICanvasData
    {
        private readonly DocumentContext _context;

        // TODO:计算图形变化和重算堆栈
        private int _undoRedoCoverageDiagnosticTicket;

        [ObservableProperty]
        private int _id;
        //private int _id = 0;
        //public int Id { get { return _id; } private set { _id = value; } }
        [ObservableProperty]
        private string _name = "";
        [ObservableProperty]
        private bool _isActive;

        // ── ICanvasData 实现 ─────────────────────────────────────────────────────
        int ICanvasData.Id => Id;
        string ICanvasData.Name => Name ?? string.Empty;
        IReadOnlyList<ILayerData> ICanvasData.Layers =>
            LayerViewViewModel?.LayerViewModels
                .Select(lvm => lvm.Model)
                .OfType<ILayerData>()
                .ToList()
                .AsReadOnly()
            ?? (IReadOnlyList<ILayerData>)Array.Empty<ILayerData>();

        public Viewport Viewport { get; } = new();

        public bool IsModified { get; set; }

        public int ShapeCount => Layers.Sum(it => it.Shapes.Count());

        public DocumentContext Context => _context;
        // 机台范围属性
        [ObservableProperty]
        public Rect2D _machineBounds = DocumentContext.Instance.DefaultMachineBounds; 

        public float InitZoomPercent { set; get; } = 1;

        /// <summary>
        /// 该画布专属的图层序号生成器（画布级，从1开始）
        /// </summary>
        public SerialNumberGenerator SerialNumber { get; } = new();

        /// <summary>
        /// 该画布专属的 LayerViewViewModel
        /// </summary>
        public LayerViewViewModel? LayerViewViewModel { get; }
        internal bool SuppressSelectionPublishFromLayerChange { get; set; }

        /// <summary>
        /// 仅供画布局部适配使用的选区同步旁路。
        /// 主领域广播统一通过 <see cref="DocumentContext.PublishSelectChanged"/> /
        /// <see cref="DocumentContext.PublishSelectSharpsChange"/> -> <see cref="CanvasChangedEvent"/> 发布。
        /// </summary>
        public event Action<IEnumerable<IShape>>? SelectionChanged;
        // TODO:计算图形变化和重算堆栈
        internal event Action<string>? UndoRedoCoverageGapDetected;
        private void RaiseSelectionChanged()
            => SelectionChanged?.Invoke(_selection);

        // TODO:计算图形变化和重算堆栈
        internal string? LastUndoRedoCoverageGapMessage { get; private set; }

        public DrawingCanvas() : this(null)
        {
        }

        public DrawingCanvas(IEnumerable<DrawingLayer>? layers)
        {
            _context = DocumentContext.Instance;
            _id = UniqueCanvasCnt.NextId();
            Name = $"画布{_id}";
            LayerViewViewModel = new LayerViewViewModel(this, layers);

            // 设置 CommandHistory 后处理钩子：统一刷新选区和填充
            CommandHistory.PostProcessCallback = _ =>
            {
                SetSelectedShapes();
                RegenerateHatchForShapes();
            };

            // 为所有图层设置画布级选中回调
            foreach (var layer in Layers)
            {
                layer.OnShapeSelectedCallback = OnShapeSelected;
                layer.OnShapeDeselectedCallback = OnShapeDeselected;
                layer.RedrawCallback = () => Context?.RequestRedraw();
                foreach (var shape in layer.AllShapesInternal)
                {
                    if (shape is DrawObject drawObj)
                    {
                        drawObj.OnShapeSelectedAction = OnShapeSelected;
                        drawObj.OnShapeDeselectedAction = OnShapeDeselected;
                    }
                }
            }

            // 从已有图形中找到最大序号，初始化画布级序号生成器
            if (layers != null)
            {
                int maxSerial = 0;
                foreach (var layer in layers)
                {
                    foreach (var shape in layer.Shapes)
                    {
                        UpdateMaxSerial(shape, ref maxSerial);
                    }
                }
                SerialNumber.ResetToAtLeast(maxSerial);
            }

            CommandHistory.CommandExecuted -= CommandExecuted;
            CommandHistory.CommandExecuted += CommandExecuted;
            LayerViewViewModel.OnLayerChanged -= LayerViewViewModel_LayersChanged;
            LayerViewViewModel.OnLayerChanged += LayerViewViewModel_LayersChanged;
        }

        private void LayerViewViewModel_LayersChanged(object? sender, EventArgs e)
        {
            InvalidateVisibleCache();
            Context?.RequestRedraw();
            if (SuppressSelectionPublishFromLayerChange)
                return;

            Context.PublishSelectChanged();
            Context.PublishSelectSharpsChange();
        }

        private void CommandExecuted(object? sender, EventArgs e)
        {
            InvalidateVisibleCache();
            Context?.RequestFullRedraw();
            Context.PublishCanvasChange(Context.ActiveCanvas, CanvasChangeType.Command, null);
        }

        public ILayerViewModel? ActiveLayer => LayerViewViewModel?.ActiveLayer;
        public IEnumerable<ILayerViewModel> LayerViewModels => LayerViewViewModel?.LayerViewModels ?? Enumerable.Empty<ILayerViewModel>();
        public IEnumerable<DrawingLayer> Layers => LayerViewViewModel?.LayerViewModels.Select(vm => vm.Model) ?? Enumerable.Empty<DrawingLayer>();

        IViewport ICanvas.Viewport => Viewport;

        private readonly SelectionSet _selection = new();

        // ── 选中统计缓存
        private readonly Dictionary<ShapeType, int> _selectedCountByType = new();

        public Model.ISelectionSet Selection => _selection;

        /// <summary>
        /// 按图形类型统计的选中数量
        /// </summary>
        public IReadOnlyDictionary<ShapeType, int> SelectedCountByType => _selectedCountByType;

        /// <summary>
        /// 最后一个被用户选中的图形对象（按选中时间顺序），用于"最后所选对象"对齐基准
        /// </summary>
        public IShape? LastSelectedShape { get; set; }

        public IEnumerable<IShape> AllShapes => Layers.Where(l => l != null && l.IsVisible).SelectMany(l => l.Shapes);

        public CommandHistory CommandHistory { get; set; } = new CommandHistory();
        // 兼容 main 分支仍通过 DrawingCanvas.CommandManager 访问撤销栈的旧调用点。
        // 当前与 CommandHistory 指向同一实例，待调用方完成迁移后可移除。
        public CommandHistory CommandManager => CommandHistory;

        public void Clear()
        {
            throw new NotImplementedException();
        }

        public IEnumerable<IShape> GetShapesInLayer(int layerId)
        {
            return Layers.Where(l => l.IsVisible).SelectMany(l => l.Shapes);
        }

        public void Transform(Matrix3x2 matrix)
        {
            throw new NotImplementedException();
        }

        public Rect2D GetBounds()
        {
            throw new NotImplementedException();
        }

        public void SetSelectedShapes()
            => SetSelectedShapesCore(publishSelectionChanged: true, publishCanvasSelectionChange: true);

        internal void SetSelectedShapes(IEnumerable<IShape>? shapes)
        {
            UpdateSelectedShapes(shapes, publishSelectionChanged: true, publishCanvasSelectionChange: true);
        }

        internal void RefreshSelectedShapesSilently(IEnumerable<IShape>? shapes = null, bool publishSelectionChanged = false, bool publishCanvasSelectionChange = false)
        {
            if (shapes == null)
            {
                SetSelectedShapesCore(publishSelectionChanged: publishSelectionChanged, publishCanvasSelectionChange: publishCanvasSelectionChange);
                return;
            }

            UpdateSelectedShapes(shapes, publishSelectionChanged: publishSelectionChanged, publishCanvasSelectionChange: publishCanvasSelectionChange);
        }

        public bool ClearSelectedShapes()
        {
            // 从各图层的增量缓存清除，避免全遍历
            foreach (var layer in Layers.Where(l => l != null && l.IsVisible))
            {
                var selected = layer.SelectedShapes;
                foreach (var s in selected)
                    s.IsSelected = false; // setter 会自动 UnregisterSelected + OnShapeDeselected
            }
            UpdateSelectedShapes(new List<IShape>(), publishSelectionChanged: true, publishCanvasSelectionChange: true);
            return true;
        }

        private void SetSelectedShapesCore(bool publishSelectionChanged, bool publishCanvasSelectionChange)
        {
            // 直接从各图层的增量缓存合并，无需全量遍历
            var result = new List<IShape>();
            foreach (var layer in Layers.Where(l => l != null && l.IsVisible))
            {
                result.AddRange(layer.SelectedShapes);
            }

            UpdateSelectedShapes(result, publishSelectionChanged, publishCanvasSelectionChange);
        }

        private void UpdateSelectedShapes(
            IEnumerable<IShape>? shapes,
            bool publishSelectionChanged,
            bool publishCanvasSelectionChange)
        {
            List<IShape> selected;
            if (shapes == null)
            {
                selected = new List<IShape>();
            }
            else
            {
                selected = new List<IShape>();
                var seenIds = new HashSet<int>();

                foreach (var shape in shapes)
                {
                    if (shape == null || !shape.IsSelected || !seenIds.Add(shape.UId))
                        continue;

                    selected.Add(shape);
                }
            }

            _selection.Reset(selected);
            _selectedCountByType.Clear();
            foreach (var shape in selected)
            {
                var type = shape.Type;
                _selectedCountByType.TryGetValue(type, out var count);
                _selectedCountByType[type] = count + 1;
            }

            if (selected.Count == 0)
            {
                LastSelectedShape = null;
                Context.SelectState = SelectState.None;
            }

            Context.SyncSelectionService(selected);

            if (publishSelectionChanged)
                RaiseSelectionChanged();

            if (!publishCanvasSelectionChange)
                return;

            Context.PublishSelectChanged();
            Context.PublishSelectSharpsChange();
        }

        /// <summary>
        /// 由 DrawObject.IsSelected setter 调用：图形被选中时更新画布级统计缓存。
        /// </summary>
        internal void OnShapeSelected(IShape shape)
        {
            var type = shape.Type;
            _selectedCountByType.TryGetValue(type, out var count);
            _selectedCountByType[type] = count + 1;
        }

        /// <summary>
        /// 由 DrawObject.IsSelected setter 调用：图形取消选中时更新画布级统计缓存。
        /// </summary>
        internal void OnShapeDeselected(IShape shape)
        {
            var type = shape.Type;
            if (_selectedCountByType.TryGetValue(type, out var count) && count > 1)
                _selectedCountByType[type] = count - 1;
            else
                _selectedCountByType.Remove(type);
        }

        private const int MaxViewportRenderCandidates = 20_000;

        // ── 可见图形原始缓存 ──
        private List<DrawObject>? _visibleDrawObjectsCache;

        // ── 空间索引：与 _visibleDrawObjectsCache 同步，支持 O(k) 视口裁剪查询 ──
        // 使用 volatile 引用交换保证线程安全（构建时创建新实例，原子替换）
        private volatile SpatialGrid? _spatialIndex;

        // ── 视口过滤结果帧间缓存 ──
        // 视口未变（缩放/平移/画布尺寸均相同）时直接返回，避免每帧重复 Query
        // 图形集合变化时由 InvalidateVisibleCache 清除
        private List<DrawObject>? _viewportFilteredCache;
        private float _cachedVpScale;
        private SKRect _cachedViewRect;

        // ── 跳扫虚线端点缓存（基于当前可见对象几何）──
        // 拖动预览阶段对象本体未真正移动，可直接复用，避免每帧全量重算端点。
        private List<(SKPoint Start, SKPoint End)>? _jumpLineEndpointsCache;

        // ── 几何变换过渡态：保留旧空间索引，同时叠加本次变换过的对象 ──
        private IReadOnlyList<DrawObject>? _geometryDirtyObjects;
        private int _geometryVersion;
        private int _spatialIndexBuiltVersion;
        private int _spatialIndexRebuildQueued;


        /// <summary>
        /// 使可见图形缓存失效，下次访问 <see cref="GetVisibleDrawObjects"/> 时将重新构建。
        /// 在图层结构变化、形状增删、Undo/Redo、图形可见性变化时调用。
        /// </summary>
        public void InvalidateVisibleCache()
        {
            _visibleDrawObjectsCache = null;
            _spatialIndex = null;
            _viewportFilteredCache = null;
            _jumpLineEndpointsCache = null;
            _geometryDirtyObjects = null;
            Interlocked.Increment(ref _geometryVersion);
        }

        internal void InvalidateGeometryCaches(IEnumerable<IShape>? changedShapes = null)
        {
            _jumpLineEndpointsCache = null;
            _geometryDirtyObjects = SnapshotChangedDrawObjects(changedShapes);
            Interlocked.Increment(ref _geometryVersion);

            // 几何变更且当前视口未变化时，保留上一帧视口缓存，
            // 下一帧直接用 dirty overlay 覆盖旧位置，避免鼠标抬起后立刻重跑整块 Query。
            if (_geometryDirtyObjects == null || _geometryDirtyObjects.Count == 0)
            {
                _viewportFilteredCache = null;
            }

            if (_visibleDrawObjectsCache == null)
            {
                _viewportFilteredCache = null;
                _spatialIndex = null;
                return;
            }

            RequestSpatialIndexRebuild();
        }

        private float _lastScale = 1.0f;


        /// <summary>
        /// 缩放时仅清除帧间视口缓存，空间索引保持不变。
        /// <para>
        /// 缩放只改变视口（哪些世界坐标映射到屏幕），不改变图形本身的世界坐标，
        /// 因此空间索引仍然有效，无需重建（O(n) 开销）。
        /// 图形移动后由 <see cref="InvalidateGeometryCaches"/> 标记几何变更并触发索引重建，
        /// 后续缩放帧复用最新可用索引，保证流畅。
        /// </para>
        /// </summary>
        public void ScaleChangeVisibleCache()
        {
            _lastScale = Viewport.Scale;
            // 视口参数已变，清除帧间视口缓存，下帧重新查询
            // 不清除 _spatialIndex：缩放不改变图形世界坐标，索引仍有效
            _viewportFilteredCache = null;
        }


        /// <summary>
        /// 获取所有可见的原始 DrawObject（懒加载缓存）。
        /// 首次调用或缓存失效后遍历所有图层构建原始对象列表，并异步重建空间索引。
        /// </summary>
        public IReadOnlyList<DrawObject> GetVisibleDrawObjects()
        {
            if (_visibleDrawObjectsCache != null)
            {
                if (_spatialIndex == null || !_spatialIndex.IsBuilt)
                {
                    RequestSpatialIndexRebuild();
                }

                return _visibleDrawObjectsCache;
            }

            //var list = new List<DrawObject>();
            _visibleDrawObjectsCache = new List<DrawObject>();
            foreach (var layer in Layers.Where(l => l.IsVisible))
            {
                _visibleDrawObjectsCache.AddRange(layer.Shapes.AsParallel().SelectMany(s => s.Flatten()).OfType<DrawObject>());
                /*Parallel.ForEach(layer.Shapes.Where(x => x.IsVisible),
                    new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                    el =>
                    {
                        foreach (var target in el.Flatten().Where(c => c.IsVisible).OfType<DrawObject>())
                            list.Add(target);
                    });*/
            }

            //_visibleDrawObjectsCache = list.ToList();
            RequestSpatialIndexRebuild();

            return _visibleDrawObjectsCache;
        }

        private void RequestSpatialIndexRebuild()
        {
            if (_visibleDrawObjectsCache == null)
                return;

            if (Interlocked.CompareExchange(ref _spatialIndexRebuildQueued, 1, 0) != 0)
                return;

            _ = Task.Run(RebuildSpatialIndexLoop);
        }

        private void RebuildSpatialIndexLoop()
        {
            try
            {
                while (true)
                {
                    var snapshot = _visibleDrawObjectsCache;
                    if (snapshot == null)
                    {
                        _spatialIndex = null;
                        Volatile.Write(ref _spatialIndexBuiltVersion, Volatile.Read(ref _geometryVersion));
                        return;
                    }

                    int requestedVersion = Volatile.Read(ref _geometryVersion);
                    var index = new SpatialGrid();
                    index.Build(snapshot);

                    if (!ReferenceEquals(snapshot, _visibleDrawObjectsCache))
                        continue;

                    if (ApplySpatialIndexRebuildResult(index, requestedVersion))
                        return;
                }
            }
            finally
            {
                Interlocked.Exchange(ref _spatialIndexRebuildQueued, 0);

                if (_visibleDrawObjectsCache != null &&
                    Volatile.Read(ref _spatialIndexBuiltVersion) != Volatile.Read(ref _geometryVersion))
                {
                    RequestSpatialIndexRebuild();
                }
            }
        }

        internal bool ApplySpatialIndexRebuildResult(SpatialGrid index, int requestedVersion)
        {
            int currentVersion = Volatile.Read(ref _geometryVersion);
            if (requestedVersion != currentVersion)
                return false;

            _spatialIndex = index;
            Volatile.Write(ref _spatialIndexBuiltVersion, requestedVersion);
            _geometryDirtyObjects = null;

            RequestRedrawAfterSpatialIndexRebuild();
            return true;
        }

        private void RequestRedrawAfterSpatialIndexRebuild()
        {
            if (!ReferenceEquals(Context.ActiveCanvas, this))
                return;

            void InvalidateAndRedraw()
            {
                if (!ReferenceEquals(Context.ActiveCanvas, this))
                    return;

                _viewportFilteredCache = null;
                Context.RequestFullRedraw();
                Context.RequestRedraw();
            }

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke((Action)InvalidateAndRedraw);
                return;
            }

            InvalidateAndRedraw();
        }

        private static IReadOnlyList<DrawObject>? SnapshotChangedDrawObjects(IEnumerable<IShape>? changedShapes)
        {
            if (changedShapes == null)
                return null;

            if (changedShapes is IList<IShape> shapeList)
            {
                var directObjects = new List<DrawObject>(shapeList.Count);
                bool requiresFlatten = false;

                for (int i = 0; i < shapeList.Count; i++)
                {
                    if (shapeList[i] is DrawObject obj)
                    {
                        if (obj is IContainer container && container.Children.Count > 0)
                        {
                            requiresFlatten = true;
                            break;
                        }

                        if (obj.IsVisible)
                        {
                            directObjects.Add(obj);
                        }

                        continue;
                    }

                    requiresFlatten = true;
                    break;
                }

                if (!requiresFlatten)
                {
                    return directObjects.Count == 0
                        ? Array.Empty<DrawObject>()
                        : directObjects;
                }
            }

            Dictionary<int, DrawObject>? drawObjectsById = null;
            foreach (var shape in changedShapes)
            {
                foreach (var target in shape.Flatten().Where(x => x.IsVisible).OfType<DrawObject>())
                {
                    drawObjectsById ??= new Dictionary<int, DrawObject>();
                    drawObjectsById.TryAdd(target.UId, target);
                }
            }

            if (drawObjectsById == null || drawObjectsById.Count == 0)
                return Array.Empty<DrawObject>();

            return drawObjectsById.Values.ToList();
        }

        private List<DrawObject> PrepareViewportCandidates(
            List<DrawObject> baseCandidates,
            SKRect worldViewport,
            float scale,
            float lodMinPixels,
            int maxCandidates)
        {
            var geometryDirtyObjects = _geometryDirtyObjects;
            int capacity = baseCandidates.Count + (geometryDirtyObjects?.Count ?? 0);
            var result = new List<DrawObject>(Math.Min(capacity, maxCandidates > 0 ? maxCandidates : 65536));
            var seen = new HashSet<int>(Math.Min(capacity, 65536));

            bool canContinue = AppendViewportCandidates(
                result,
                seen,
                baseCandidates,
                worldViewport,
                scale,
                lodMinPixels,
                maxCandidates);

            if (canContinue && geometryDirtyObjects != null && geometryDirtyObjects.Count > 0)
            {
                AppendViewportCandidates(
                    result,
                    seen,
                    geometryDirtyObjects,
                    worldViewport,
                    scale,
                    lodMinPixels,
                    maxCandidates);
            }

            return result;
        }

        private static List<DrawObject> MergeDirtyViewportCandidates(
            List<DrawObject> baseCandidates,
            IReadOnlyList<DrawObject> geometryDirtyObjects,
            SKRect worldViewport,
            float scale,
            float lodMinPixels)
        {
            if (geometryDirtyObjects.Count == 0)
                return baseCandidates;

            int capacity = baseCandidates.Count + geometryDirtyObjects.Count;
            var dirtyIds = new HashSet<int>(Math.Min(geometryDirtyObjects.Count, 65536));
            foreach (var obj in geometryDirtyObjects)
            {
                dirtyIds.Add(obj.UId);
            }

            var result = new List<DrawObject>(Math.Min(capacity, 65536));
            foreach (var obj in baseCandidates)
            {
                if (!dirtyIds.Contains(obj.UId))
                {
                    result.Add(obj);
                }
            }

            foreach (var obj in geometryDirtyObjects)
            {
                // dirty overlay 代表最新几何状态，必须覆盖旧索引里的同 UId 旧位置
                if (!dirtyIds.Remove(obj.UId))
                    continue;

                var bb = obj.GetAABB();
                if (!bb.IsEmpty && !bb.IntersectsWith(worldViewport))
                    continue;

                if (lodMinPixels > 0f && !bb.IsEmpty)
                {
                    float screenSize = Math.Max(bb.Width, bb.Height) * scale;
                    if (screenSize < lodMinPixels)
                        continue;
                }

                result.Add(obj);
            }

            return result;
        }

        private static bool AppendViewportCandidates(
            List<DrawObject> result,
            HashSet<int> seen,
            IEnumerable<DrawObject> source,
            SKRect worldViewport,
            float scale,
            float lodMinPixels,
            int maxCandidates)
        {
            foreach (var obj in source)
            {
                if (!seen.Add(obj.UId))
                    continue;

                var bb = obj.GetAABB();
                if (!bb.IsEmpty && !bb.IntersectsWith(worldViewport))
                    continue;

                if (lodMinPixels > 0f && !bb.IsEmpty)
                {
                    float screenSize = Math.Max(bb.Width, bb.Height) * scale;
                    if (screenSize < lodMinPixels)
                        continue;
                }

                result.Add(obj);

                if (maxCandidates > 0 && result.Count >= maxCandidates)
                {
                    return false;
                }
            }

            return true;
        }

        private static List<DrawObject> DeduplicateViewportCandidates(
            List<DrawObject> candidates,
            float scale,
            int maxResultCount)
        {
            if (candidates.Count <= 1)
            {
                return candidates;
            }

            var renderedKeys = new Dictionary<int, byte>(Math.Min(candidates.Count, 65536));
            var result = new List<DrawObject>(Math.Min(candidates.Count, maxResultCount > 0 ? maxResultCount : 4096));

            foreach (var obj in candidates)
            {
                int key = ComputeDedupKey(obj, scale);
                if (!renderedKeys.TryAdd(key, 0))
                {
                    continue;
                }

                result.Add(obj);
                if (maxResultCount > 0 && result.Count >= maxResultCount)
                {
                    break;
                }
            }

            return result;
        }

        /// <summary>
        /// 计算图形的视觉去重键：同一屏幕像素附近、视觉属性一致的图形只渲染一个代表。
        /// </summary>
        private static int ComputeDedupKey(DrawObject target, float scale)
        {
            var c = target.SharpCenter;
            int type = (int)target.Type;

            scale /= 2f;

            int cx = (int)Math.Round(c.X * scale);
            int cy = (int)Math.Round(c.Y * scale);
            int w = (int)Math.Round(target.Width * scale);
            int h = (int)Math.Round(target.Height * scale);
            int rot = (int)Math.Round(target.Rotation);
            int sx = (int)Math.Round(target.ScaleX * 100);
            int sy = (int)Math.Round(target.ScaleY * 100);
            int skx = (int)Math.Round(target.SkewX * 100);
            int sky = (int)Math.Round(target.SkewY * 100);

            int hash = HashCode.Combine(type, cx, cy, w, h, rot, sx, sy);
            hash = HashCode.Combine(hash, skx, sky);

            switch (target)
            {
                case DrawingHatch hatch:
                    {
                        // 多次对同一目标做填充时，多个 hatch 往往拥有相同的中心/尺寸。
                        // 这里不能按视觉近似去重，否则后加入的填充对象会在视口候选阶段被误丢弃。
                        hash = HashCode.Combine(hash, hatch.UId);
                        break;
                    }
                case DrawDot dot when dot.Points.Count > 0:
                    {
                        var p = dot.Points[0];
                        int px = (int)Math.Round(p.X * scale);
                        int py = (int)Math.Round(p.Y * scale);
                        int r = (int)Math.Round(dot.Radius * scale * 100);
                        hash = HashCode.Combine(hash, px, py, r);
                        break;
                    }
                case DrawCircle circle:
                    {
                        var cc = circle.SharpCenter;
                        int ccx = (int)Math.Round(cc.X * scale);
                        int ccy = (int)Math.Round(cc.Y * scale);
                        int rx = (int)Math.Round(circle.DrawingRadiusX * scale);
                        int ry = (int)Math.Round(circle.DrawingRadiusY * scale);
                        int circRot = (int)Math.Round(circle.Rotation);
                        hash = HashCode.Combine(hash, ccx, ccy, rx, ry, circRot);
                        break;
                    }
                case DrawRectangle rect:
                    {
                        if (rect.IsCornerRadiusRectangle())
                        {
                            int crtl = (int)Math.Round(rect.CornerRadiusTopLeft * scale);
                            int crtr = (int)Math.Round(rect.CornerRadiusTopRight * scale);
                            int crbr = (int)Math.Round(rect.CornerRadiusBottomRight * scale);
                            int crbl = (int)Math.Round(rect.CornerRadiusBottomLeft * scale);
                            hash = HashCode.Combine(hash, crtl, crtr, crbr, crbl);
                        }

                        else if (rect.IsChamferRadiusRectangle())
                        {
                            int crtl = (int)Math.Round(rect.ChamferTopLeft * scale);
                            int crtr = (int)Math.Round(rect.ChamferTopRight * scale);
                            int crbr = (int)Math.Round(rect.ChamferBottomLeft * scale);
                            int crbl = (int)Math.Round(rect.ChamferBottomRight * scale);
                            hash = HashCode.Combine(hash, crtl, crtr, crbr, crbl);
                        }
                        else
                        {
                            hash = HashCode.Combine(hash, 0, 0, 0, 0);
                        }

                        break;
                    }
                case DrawArc arc when arc.Points.Count >= 2:
                    {
                        int x1 = (int)Math.Round(arc.Points[0].X * scale);
                        int y1 = (int)Math.Round(arc.Points[0].Y * scale);
                        hash = HashCode.Combine(hash, x1, y1);
                        break;
                    }
                case DrawPolyLines poly when poly.Points.Count >= 2:
                    {
                        hash = HashCode.Combine(hash, ComputePointsHash(poly.Points, scale));
                        break;
                    }
                case DrawPolygon polygon when polygon.Points.Count >= 3:
                    {
                        hash = HashCode.Combine(hash, ComputePointsHash(polygon.Points, scale));
                        break;
                    }
                case DrawBezier bezier when bezier.Points.Count >= 2:
                    {
                        hash = HashCode.Combine(hash, ComputePointsHash(bezier.Points, scale), bezier.IsClosed);
                        break;
                    }
            }

            return hash;
        }

        private static int ComputePointsHash(List<SKPoint> points, float scale)
        {
            if (points == null || points.Count == 0)
            {
                return 0;
            }

            int hash = points.Count;
            int step = Math.Max(1, points.Count / 8);

            for (int i = 0; i < points.Count; i += step)
            {
                int px = (int)Math.Round(points[i].X * scale);
                int py = (int)Math.Round(points[i].Y * scale);
                hash = HashCode.Combine(hash, px, py);
            }

            int lx = (int)Math.Round(points[points.Count - 1].X * scale);
            int ly = (int)Math.Round(points[points.Count - 1].Y * scale);
            hash = HashCode.Combine(hash, lx, ly);

            return hash;
        }

        /// <summary>
        /// 使用空间索引获取当前视口内的可见图形，并应用 LOD 分级剔除和视觉去重。
        /// <para>
        /// 查询复杂度 O(k)，k 为视口内对象数量，远小于总图形数 n：
        /// <list type="bullet">
        ///   <item>空间索引：将 AABB 视口裁剪从 O(n) 降至 O(k)</item>
        ///   <item>LOD 剔除：跳过屏幕尺寸 &lt; <paramref name="lodMinPixels"/> 的对象</item>
        ///   <item>视觉去重：同一屏幕像素附近的图形只渲染一个代表</item>
        /// </list>
        /// </para>
        /// </summary>
        /// <param name="worldViewport">世界坐标视口矩形</param>
        /// <param name="scale">当前视口缩放比例</param>
        /// <param name="lodMinPixels">
        ///   LOD 最小屏幕尺寸（像素）。对象在屏幕上的 max(宽, 高) &lt; 此值时跳过。
        ///   传 0 禁用 LOD 剔除。
        /// </param>
        public List<DrawObject> GetVisibleDrawObjectsInViewport(
            SKRect worldViewport, float scale, float lodMinPixels = 0f)
        {
            // 确保原始缓存与空间索引已构建
            _ = GetVisibleDrawObjects();

            var geometryDirtyObjects = _geometryDirtyObjects;
            bool sameViewport =
                _viewportFilteredCache != null &&
                _cachedVpScale == scale &&
                _cachedViewRect == worldViewport;

            // 视口参数相同时直接返回帧间缓存（缩放、平移、画布尺寸均未变）
            //if (sameViewport &&
            //    (geometryDirtyObjects == null || geometryDirtyObjects.Count == 0))
            //{
            //    return _viewportFilteredCache!;
            //}

            List<DrawObject> candidates;
            if (sameViewport && geometryDirtyObjects != null && geometryDirtyObjects.Count > 0)
            {
                candidates = MergeDirtyViewportCandidates(
                    _viewportFilteredCache!,
                    geometryDirtyObjects,
                    worldViewport,
                    scale,
                    lodMinPixels);
            }
            else
            {
                var idx = _spatialIndex; // volatile 读，快照当前索引
                if (idx != null && idx.IsBuilt)
                {
                    candidates = idx.Query(worldViewport, scale, lodMinPixels, MaxViewportRenderCandidates);

                    if (geometryDirtyObjects != null && geometryDirtyObjects.Count > 0)
                    {
                        candidates = MergeDirtyViewportCandidates(
                            candidates,
                            geometryDirtyObjects,
                            worldViewport,
                            scale,
                            lodMinPixels);
                    }
                }
                else
                {
                    candidates = PrepareViewportCandidates(
                        _visibleDrawObjectsCache ?? new List<DrawObject>(),
                        worldViewport,
                        scale,
                        lodMinPixels,
                        MaxViewportRenderCandidates);
                }
            }

            var result = DeduplicateViewportCandidates(candidates, scale, MaxViewportRenderCandidates);

            if (result.Count > 1)
            {
                result.Sort((x, y) => x.UId.CompareTo(y.UId));
            }

            // 缓存本帧结果，下帧视口不变时直接返回（局部刷新、选择框等静态帧受益）
            _viewportFilteredCache = result;
            _cachedVpScale = scale;
            _cachedViewRect = worldViewport;
            return result;
        }

        internal IReadOnlyList<(SKPoint Start, SKPoint End)> GetJumpLineEndpoints(
            Func<IEnumerable<DrawObject>, List<(SKPoint Start, SKPoint End)>> builder)
        {
            if (_jumpLineEndpointsCache != null)
                return _jumpLineEndpointsCache;

            var visibleDrawObjects = GetVisibleDrawObjects();
            _jumpLineEndpointsCache = visibleDrawObjects.Count >= 2
                ? builder(visibleDrawObjects)
                : new List<(SKPoint Start, SKPoint End)>();

            return _jumpLineEndpointsCache;
        }

        /// <summary>
        /// 计算图形变化和撤销栈是否能对上
        /// </summary>
        internal void ScheduleUndoRedoCoverageCheck(
            CommandHistory.HistoryStateSnapshot historySnapshot,
            string changeSource)
        {
            int diagnosticTicket = Interlocked.Increment(ref _undoRedoCoverageDiagnosticTicket);

            _ = Task.Run(async () =>
            {
                await Task.Yield();

                int latestTicket = Volatile.Read(ref _undoRedoCoverageDiagnosticTicket);
                if (diagnosticTicket != latestTicket)
                {
                    return;
                }

                var currentSnapshot = CommandHistory.CaptureStateSnapshot();
                bool historyChanged = currentSnapshot.MutationVersion != historySnapshot.MutationVersion;
                if (historyChanged)
                {
                    return;
                }

                string message =
                    $"[UndoRedoCoverage] 检测到图形已提交变化但撤销/重做历史未变化。Source={changeSource}, " +
                    $"Undo={historySnapshot.UndoCount}->{currentSnapshot.UndoCount}, " +
                    $"Redo={historySnapshot.RedoCount}->{currentSnapshot.RedoCount}.";

                LastUndoRedoCoverageGapMessage = message;
                DebugLogHub.Append(message);
                UndoRedoCoverageGapDetected?.Invoke(message);
            });
        }

        public void Dispose()
        {
            CommandHistory.CommandExecuted -= CommandExecuted;
            CommandHistory.Clear(); // 清空撤销/重做栈，释放命令对象引用

            // 取消订阅 LayerViewViewModel 事件
            if (LayerViewViewModel != null)
            {
                LayerViewViewModel.OnLayerChanged -= LayerViewViewModel_LayersChanged;
                LayerViewViewModel.Dispose();
            }

            // 清除各图层的回调引用，断开 DrawingLayer -> DrawingCanvas 的委托链
            foreach (var layer in Layers)
            {
                layer.OnShapeSelectedCallback = null;
                layer.OnShapeDeselectedCallback = null;
                // 清除各 DrawObject 的画布级回调，断开 DrawObject -> DrawingCanvas 的委托链
                foreach (var shape in layer.AllShapesInternal.OfType<DrawObject>())
                {
                    shape.OnShapeSelectedAction = null;
                    shape.OnShapeDeselectedAction = null;
                    shape.OwningLayer = null;
                }
                layer.ClearShapes();
            }

            // 清空选中状态
            _selection.Reset(new List<IShape>());
            _selectedCountByType.Clear();
            LastSelectedShape = null;

            // 清空所有缓存，释放内存
            InvalidateVisibleCache();
            _visibleDrawObjectsCache = null;
            _spatialIndex = null;
            _viewportFilteredCache = null;
            _jumpLineEndpointsCache = null;
            _geometryDirtyObjects = null;
        }

        public class UniqueCanvasCnt
        {
            private static int _current = 0;

            public static int NextId()
                => Interlocked.Increment(ref _current);
        }

        public void SetFontSettings(FontSettings fontSettings, string text = null, FontSettingsFields updatedFields = FontSettingsFields.All)
        {
            var drawTextList = Selection.OfType<DrawText>().ToList();
            if (drawTextList == null || drawTextList.Count == 0) return;

            // 创建命令，捕获 Before 快照
            var command = new DrSoft.Drawing.Controls.Commands.CommandFontSettings(drawTextList, "修改字体");

            foreach (var item in drawTextList)
            {
                if (text != null)
                {
                    item.TextModel.Text = text;
                }

                item.TextModel.FontSettings ??= new FontSettings();
                ApplyFontSettings(item.TextModel.FontSettings, fontSettings, updatedFields);
                // 缩放/旋转/倾斜全部保留在 Matrix 中，字体变化只重建局部路径；
                // 对齐方式变化时保持视觉中心不动，其余情况保持锚点不动。
                bool preserveVisualPosition = updatedFields == FontSettingsFields.HorizontalAlign;
                item.UpdateTextPath(item.TextModel, preserveVisualPosition);
            }

            // 捕获 After 快照并推入 CommandHistory
            command.CaptureAfterState();
            CommandHistory.PushExecutedCommand(command);
            Context.PublishTransformChange();
        }

        private static void ApplyFontSettings(FontSettings target, FontSettings source, FontSettingsFields updatedFields)
        {
            if (updatedFields == FontSettingsFields.None)
            {
                return;
            }

            if (updatedFields.HasFlag(FontSettingsFields.FontFamily))
            {
                target.FontFamily = source.FontFamily;
            }

            if (updatedFields.HasFlag(FontSettingsFields.FontSize))
            {
                target.FontSize = source.FontSize;
            }

            if (updatedFields.HasFlag(FontSettingsFields.IsBold))
            {
                target.IsBold = source.IsBold;
            }

            if (updatedFields.HasFlag(FontSettingsFields.IsItalic))
            {
                target.IsItalic = source.IsItalic;
            }

            if (updatedFields.HasFlag(FontSettingsFields.IsUnderline))
            {
                target.IsUnderline = source.IsUnderline;
            }

            if (updatedFields.HasFlag(FontSettingsFields.IsVerticalLayout))
            {
                target.IsVerticalLayout = source.IsVerticalLayout;
            }

            if (updatedFields.HasFlag(FontSettingsFields.HorizontalAlign))
            {
                target.HorizontalAlign = source.HorizontalAlign;
            }

            if (updatedFields.HasFlag(FontSettingsFields.VerticalAlign))
            {
                target.VerticalAlign = source.VerticalAlign;
            }

            if (updatedFields.HasFlag(FontSettingsFields.LineHeight))
            {
                target.LineHeight = source.LineHeight;
            }

            if (updatedFields.HasFlag(FontSettingsFields.CharacterSpacing))
            {
                target.CharacterSpacing = source.CharacterSpacing;
            }

            if (updatedFields.HasFlag(FontSettingsFields.TextColor))
            {
                target.TextColor = source.TextColor;
            }
        }

        /// <summary>
        /// 递归查找图形及其子节点中的最大序号
        /// </summary>
        private static void UpdateMaxSerial(IShape shape, ref int maxSerial)
        {
            if (int.TryParse(shape.Name, out int serial) && serial > maxSerial)
                maxSerial = serial;

            if (shape is IContainer container)
            {
                foreach (var child in container.Children)
                    UpdateMaxSerial(child, ref maxSerial);
            }
        }
    }
}
