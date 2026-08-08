using System;
using System.Collections.Concurrent;
using System.Security.Policy;
using System.Windows.Controls;
using System.Windows.Shapes;
using DrSoft.Drawing.Contracts;
using DrSoft.Drawing.Controls.Clipboard;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Controls.Mapping;
using DrSoft.Drawing.Controls.Tools;
using DrSoft.Drawing.Controls.ViewModels;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Model;
using DrSoft.Drawing.Rendering;
using SkiaSharp;
using Cursor = System.Windows.Input.Cursor;

namespace DrSoft.Drawing.Controls.Models;

/// <summary>
/// 文档级状态：跨交互持久存在，描述“当前编辑的是什么”。
/// 新增文档语义字段时，优先归入这里。
/// </summary>
public sealed class CanvasDocumentState
{
    public FontSettings CurrentTextFontSettings { get; set; } = new();
    public ToolBase? ActiveTool { get; set; }
    public ICanvas? ActiveCanvas { get; set; }
    public IShape? CurrentShape { get; set; }
    public Rect2D DefaultMachineBounds { get; set; } = new(-50, -50, 100, 100);
    public float KeysMoveSharpsStepX { get; set; } = 0.1f;
    public float KeysMoveSharpsStepY { get; set; } = 0.1f;
}

/// <summary>
/// 视图级状态：只影响显示、脏区和渲染缓存，不表达用户交互流程。
/// 新增渲染/显示字段时，优先归入这里。
/// </summary>
public sealed class CanvasViewState
{
    public SKRect? CachedDragPreviewBounds { get; set; }
    public SKPoint[]? CachedDragPreviewCorners { get; set; }
    public bool ShowDirectionArrow { get; set; } = true;
    public bool ShowJumpLine { get; set; } = true;
    public bool IsSnapToStart { get; set; }
    public SKPoint SnapStartPoint { get; set; } = SKPoint.Empty;
    public bool IsPartialRender { get; set; }
    public float GridSizeX { get; set; } = 100.0f;
    public float GridSizeY { get; set; } = 100.0f;
    public SKRect? CachedSelectionBounds { get; set; }
    public SKRect? DirtyRect { get; set; }
}

/// <summary>
/// 交互级状态：短生命周期、鼠标手势、节点编辑、框选等临时流程状态。
/// 新增交互过程字段时，优先归入这里。
/// </summary>
public sealed class CanvasInteractionState
{
    public bool IsDragControlPoint { get; set; }
    public bool IsDrawing { get; set; }
    public bool IsApplyingDeferredDragCommit { get; set; }
    public object DeferredDragCommitSyncRoot { get; } = new();
    public BoxSelectionState BoxSelection { get; set; } = new();
    public bool IsNodeEditing { get; set; }
    public DrSoft.Drawing.Event.Tool.NodeEditSubMode NodeEditSubMode { get; set; }
    public SKPoint? SelectedMoveNodeWorldPosition { get; set; }
    public List<SKPoint> SelectedPathNodeWorldPositions { get; set; } = new();
    public SKPoint CurMouseDown { get; set; } = new();
    /// <summary>当前画布是否已接收过鼠标点击（用于粘贴时决定是否使用鼠标位置）</summary>
    public bool HasMousePosition { get; set; }
    public float SeparateNodeDistance { get; set; } = 2.0f;
    public SKPoint? SelectedSeparateNodeWorldPosition { get; set; }

    public bool IsScalePreview { get; set; }
    public SKPoint[]? RealScaleOBBCorners { get; set; }
    public SKRect RealScalePreviewAABB { get; set; }

    public bool IsRotationPreview { get; set; }
    public SKPoint[]? RealRotationCorners { get; set; }

    public bool IsSkewPreview { get; set; }
    public SKPoint[]? RealSkewOBBCorners { get; set; }
    public SKRect RealSkewPreviewAABB { get; set; }
}

/// <summary>
/// 单激活画布前提下的编辑器上下文单例。
/// 当前作为“文档状态 + 视图状态 + 交互状态 + 宿主桥接”的统一入口，但新职责应优先归入分层状态对象。
/// </summary>
public sealed class DocumentContext
{
    // 单例实例
    private static readonly object _lock = new object();

    private static Lazy<DocumentContext> lazyInstance = new Lazy<DocumentContext>(() => new DocumentContext());
    private readonly SelectionService _selectionService;
    /// <summary>
    /// 获取DocumentContext的单例实例
    /// </summary>
    public static DocumentContext Instance
    {
        get
        {
            return lazyInstance.Value;
        }
    }

    /// <summary>
    /// 私有构造函数，防止外部实例化
    /// </summary>
    private DocumentContext()
    {
        _selectionService = new SelectionService(this);
    }

    /// <summary>
    /// 新字段不要直接平铺到 DocumentContext。
    /// 优先判断其归属：DocumentState / ViewState / InteractionState。
    /// </summary>
    public CanvasDocumentState DocumentState { get; } = new();
    public CanvasViewState ViewState { get; } = new();
    public CanvasInteractionState InteractionState { get; } = new();

    #region Compatibility Wrappers

    public FontSettings CurrentTextFontSettings
    {
        get => DocumentState.CurrentTextFontSettings;
        set => DocumentState.CurrentTextFontSettings = value;
    }
    /// <summary>
    /// 重置实例（主要用于测试或特殊场景）
    /// </summary>
    public static void ResetInstance()
    {
        // 使用Lazy<T>时，可以通过重新创建Lazy实例来重置
        lock (_lock)
        {
            lazyInstance = new Lazy<DocumentContext>(() => new DocumentContext());
        }
    }

    public ToolBase? ActiveTool
    {
        get => DocumentState.ActiveTool;
        set => DocumentState.ActiveTool = value;
    }

    /// <summary>
    /// 缓存的 Select 工具实例，由 ViewModel 初始化，避免切换时丢失状态
    /// </summary>
    public ToolBase? SelectTool { get; set; }

    public ICanvas? ActiveCanvas
    {
        get => DocumentState.ActiveCanvas;
        set
        {
            if (DocumentState.ActiveCanvas != value)
            {
                DocumentState.ActiveCanvas = value;
                _selectionService.Reset();
                _selectionService.SyncSelection(value?.Selection);
                ActiveCanvasChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>活动画布切换事件（画布切换时触发，用于通知工具栏等组件更新状态）</summary>
    public event EventHandler? ActiveCanvasChanged;
    public IShape? CurrentShape
    {
        get => DocumentState.CurrentShape;
        set => DocumentState.CurrentShape = value;
    }

    public bool IsDragControlPoint
    {
        get => InteractionState.IsDragControlPoint;
        set => InteractionState.IsDragControlPoint = value;
    }

    public bool IsDrawing
    {
        get => InteractionState.IsDrawing;
        set => InteractionState.IsDrawing = value;
    }

    public bool IsApplyingDeferredDragCommit
    {
        get => InteractionState.IsApplyingDeferredDragCommit;
        set => InteractionState.IsApplyingDeferredDragCommit = value;
    }

    public bool IsNodeEditing
    {
        get => InteractionState.IsNodeEditing;
        set => InteractionState.IsNodeEditing = value;
    }

    public DrSoft.Drawing.Event.Tool.NodeEditSubMode NodeEditSubMode
    {
        get => InteractionState.NodeEditSubMode;
        set => InteractionState.NodeEditSubMode = value;
    }

    /// <summary>
    /// 延后拖拽提交会在后台真实改写图形对象；渲染若同时读取同一批图元，
    /// 可能读到半更新状态。这里用一个轻量串行化锁隔离提交窗口期的读写。
    /// </summary>
    public object DeferredDragCommitSyncRoot => InteractionState.DeferredDragCommitSyncRoot;

    public BoxSelectionState BoxSelection
    {
        get => InteractionState.BoxSelection;
        set => InteractionState.BoxSelection = value ?? new BoxSelectionState();
    }

    public BoxSelectionState BoxSelect
    {
        get => InteractionState.BoxSelection;
        set => InteractionState.BoxSelection = value ?? new BoxSelectionState();
    }

    private SKPoint _mergedRotationCenter = new SKPoint(float.PositiveInfinity, float.PositiveInfinity);
    public SKPoint MergedRotationCenter
    {
        get => _mergedRotationCenter;
        set => _mergedRotationCenter = value;
    }

    private SKPoint _anchorPosition = new SKPoint(float.PositiveInfinity, float.PositiveInfinity);
    public SKPoint AnchorPosition
    {
        get => _anchorPosition;
        set => _anchorPosition = value;
    }

    public bool IsAnchorPositionShow { get; set; }


    public bool IsScalePreview
    {
        get => InteractionState.IsScalePreview;
        set => InteractionState.IsScalePreview = value;
    }
    /// <summary>
    /// SecondSelected 缩放预览时缓存的红框 OBB 角点。
    /// 黑框仍然基于 AABB 计算，红框优先使用这组角点避免退回 AABB。
    /// </summary>
    public SKPoint[]? RealScaleOBBCorners
    {
        get => InteractionState.RealScaleOBBCorners;
        set => InteractionState.RealScaleOBBCorners = value;
    }
    public SKRect RealScalePreviewAABB
    {
        get => InteractionState.RealScalePreviewAABB;
        set => InteractionState.RealScalePreviewAABB = value;
    }
    /// <summary>是否处于旋转预览拖拽中（图形不动，仅渲染旋转后的 OBB 和 AABB 控制点）</summary>
    public bool IsRotationPreview
    {
        get => InteractionState.IsRotationPreview;
        set => InteractionState.IsRotationPreview = value;
    }

    public SKPoint[]? RealRotationCorners
    {
        get => InteractionState.RealRotationCorners;
        set => InteractionState.RealRotationCorners = value;
    }

    public bool IsSkewPreview
    {
        get => InteractionState.IsSkewPreview;
        set => InteractionState.IsSkewPreview = value;
    }
    public SKPoint[]? RealSkewOBBCorners
    {
        get => InteractionState.RealSkewOBBCorners;
        set => InteractionState.RealSkewOBBCorners = value;
    }
    public SKRect RealSkewPreviewAABB
    {
        get => InteractionState.RealSkewPreviewAABB;
        set => InteractionState.RealSkewPreviewAABB = value;
    }

    private SelectState _selectState = SelectState.None;
    public SelectState SelectState
    {
        get => _selectState;
        set
        {
            var oldSelectState = _selectState;
            _selectState = value;
            if (_selectState != oldSelectState)
            {
                if (value == SelectState.FirstSelected && ActiveCanvas != null)
                {
                    var shapes = ActiveCanvas!.Selection.Transformables;
                    if (shapes.Count() > 1)
                    {
                        var aabb = shapes.GetUnionAABB();
                        MergedRotationCenter = new SKPoint(aabb.MidX, aabb.MidY);
                    }
                    IsAnchorPositionShow = false;
                }

                PublishSelectStateChanged();
            }
        }
    }
    /// <summary>
    /// 拖拽预览时缓存的选中图形合并边界框。
    /// 拖拽期间图形不实际移动，bbox 不变，只需计算一次。
    /// 在拖拽开始时设置，拖拽结束时清除。
    /// </summary>
    public SKRect? CachedDragPreviewBounds
    {
        get => ViewState.CachedDragPreviewBounds;
        set => ViewState.CachedDragPreviewBounds = value;
    }

    /// <summary>
    /// 单选拖拽预览的原始选择框角点。
    /// 旋转组合/群组不能退回合并 AABB，拖动阶段只平移这组角点。
    /// </summary>
    public SKPoint[]? CachedDragPreviewCorners
    {
        get => ViewState.CachedDragPreviewCorners;
        set => ViewState.CachedDragPreviewCorners = value;
    }

    /// <summary>是否显示加工方向箭头（由配置控制，运行时可热切换）</summary>
    public bool ShowDirectionArrow
    {
        get => ViewState.ShowDirectionArrow;
        set => ViewState.ShowDirectionArrow = value;
    }

    /// <summary>是否显示加工路径虚线（由配置控制，运行时可热切换）</summary>
    public bool ShowJumpLine
    {
        get => ViewState.ShowJumpLine;
        set => ViewState.ShowJumpLine = value;
    }

    /// <summary>
    /// 多段线绘制时，鼠标是否靠近起始点（吸附状态）。
    /// 当为 true 时，渲染管线在起始点绘制吸附指示框。
    /// </summary>
    public bool IsSnapToStart
    {
        get => ViewState.IsSnapToStart;
        set => ViewState.IsSnapToStart = value;
    }

    /// <summary>
    /// 多段线绘制时的吸附指示点（起始点世界坐标）。
    /// 仅在 IsSnapToStart=true 时有效。
    /// </summary>
    public SKPoint SnapStartPoint
    {
        get => ViewState.SnapStartPoint;
        set => ViewState.SnapStartPoint = value;
    }

    //是否局部刷新
    public bool IsPartialRender
    {
        get => ViewState.IsPartialRender;
        set => ViewState.IsPartialRender = value;
    }
    //public NotifyingList<IShape> SelectedShapes = new NotifyingList<IShape>();

    /// <summary>
    /// UI 宿主桥接入口。工具和会话不应直接依赖具体 ViewModel/WPF 控件，而是通过该宿主请求状态栏/光标/对话框能力。
    /// </summary>
    public ICanvasInteractionHost? InteractionHost { get; set; }

    public Action<ICanvas?, CanvasChangeType, object?> PublishCanvasChange { get; set; }

    /// <summary>
    /// 图形变换完成后自动重新计算跳点的回调（由 ShapeService 注入）
    /// </summary>
    public Action? RecalculateJumpPointsAction { get; set; }

    /// <summary>
    /// 移动节点模式下被选中节点的世界坐标（由 ToolSelect 设置，由 SelectionRenderer 读取）。
    /// null 表示未选中任何节点。
    /// </summary>
    public SKPoint? SelectedMoveNodeWorldPosition
    {
        get => InteractionState.SelectedMoveNodeWorldPosition;
        set => InteractionState.SelectedMoveNodeWorldPosition = value;
    }

    public List<SKPoint> SelectedPathNodeWorldPositions
    {
        get => InteractionState.SelectedPathNodeWorldPositions;
        set => InteractionState.SelectedPathNodeWorldPositions = value ?? new List<SKPoint>();
    }

    public SKPoint CurMouseDown
    {
        get => InteractionState.CurMouseDown;
        set => InteractionState.CurMouseDown = value;
    }

    /// <summary>当前画布是否已接收过鼠标点击</summary>
    public bool HasMousePosition
    {
        get => InteractionState.HasMousePosition;
        set => InteractionState.HasMousePosition = value;
    }

    /// <summary>
    /// 分离节点时的分离距离（mm）。
    /// 由对话框设置，SeparateNodes 执行时读取。
    /// </summary>
    public float SeparateNodeDistance
    {
        get => InteractionState.SeparateNodeDistance;
        set => InteractionState.SeparateNodeDistance = value;
    }

    private IReadOnlyList<IShape> _old_selectedShapes = [];

    /// <summary>
    /// 分离节点模式下被选中节点的世界坐标（由 ToolSelect 设置，由 SelectionRenderer 读取）。
    /// null 表示未选中任何节点。
    /// </summary>
    public SKPoint? SelectedSeparateNodeWorldPosition
    {
        get => InteractionState.SelectedSeparateNodeWorldPosition;
        set => InteractionState.SelectedSeparateNodeWorldPosition = value;
    }

    public float GridSizeX
    {
        get => ViewState.GridSizeX;
        set => ViewState.GridSizeX = value;
    }

    public float GridSizeY
    {
        get => ViewState.GridSizeY;
        set => ViewState.GridSizeY = value;
    }

    public Rect2D DefaultMachineBounds
    {
        get => DocumentState.DefaultMachineBounds;
        set => DocumentState.DefaultMachineBounds = value;
    }

    public float KeysMoveSharpsStepX
    {
        get => DocumentState.KeysMoveSharpsStepX;
        set => DocumentState.KeysMoveSharpsStepX = value;
    }

    public float KeysMoveSharpsStepY
    {
        get => DocumentState.KeysMoveSharpsStepY;
        set => DocumentState.KeysMoveSharpsStepY = value;
    }
    /// <summary>
    /// 缓存当前选中集的合并边界，供拖拽结束后的事件发布和选择框渲染复用，
    /// 避免在超大选中集下重复扫描全部 SelectedShapes。
    /// </summary>
    public SKRect? CachedSelectionBounds
    {
        get => _selectionService.CachedBounds;
    }

    /// <summary>
    /// 脏区域（世界坐标，Y 向上）。为 null 时全量刷新；有值时 RenderPipeline 只绘制该矩形内的内容。
    /// RenderPipeline 每帧渲染末尾会自动清空。
    /// </summary>
    public SKRect? DirtyRect
    {
        get => ViewState.DirtyRect;
        set => ViewState.DirtyRect = value;
    }

    #endregion

    public void ReportStatus(string status)
    {
        InteractionHost?.UpdateStatus(status);
    }

    public void RequestRedraw()
    {
        InteractionHost?.Redraw();
    }

    public void SetCursor(Cursor cursor)
    {
        InteractionHost?.SetCursor(cursor);
    }

    public MoveNodeDialogResult? RequestMoveNodeDialog(float currentX, float currentY)
    {
        return InteractionHost?.ShowMoveNodeDialog(currentX, currentY);
    }

    public ExtendNodeDialogResult? RequestExtendNodeDialog()
    {
        return InteractionHost?.ShowExtendNodeDialog();
    }

    public bool IsShiftPressed()
    {
        return InteractionHost?.IsShiftPressed() ?? false;
    }

    public SeparateNodeDialogResult? RequestSeparateNodeDialog()
    {
        return InteractionHost?.ShowSeparateNodeDialog(SeparateNodeDistance);
    }

    /// <summary>
    /// 将一个世界坐标矩形合并到 DirtyRect（用于累加多次脏区标记）。
    /// </summary>
    public void MarkDirty(SKRect rect)
    {
        DirtyRect = DirtyRect.HasValue ? SKRect.Union(DirtyRect.Value, rect) : rect;
    }

    public bool CompareSelectedShapes(IReadOnlyList<IShape> selectedShapes)
    {
        bool isDifferent = false;
        if (_old_selectedShapes.Count == selectedShapes.Count)
        {
            var uids = selectedShapes.Select(o => o.UId);
            var oldUids = _old_selectedShapes.Select(o => o.UId);
            foreach (var uid in oldUids)
            {
                if (!uids.Contains(uid))
                {
                    isDifferent = true;
                    break;
                }
            }
        }
        else
        {
            isDifferent = true;
        }

        return isDifferent;
    }

    public void ChangeSelectedState(int state)
    {
        switch (state)
        {
            case 0:
                SelectState = SelectState.None;
                break;
            case 1:
                SelectState = SelectState.FirstSelected;
                break;
            case 2:
                {
                    bool isShiftPressed = System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.LeftShift) || System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.RightShift);
                    if (!isShiftPressed)
                    {
                        SelectState = SelectState.SecondSelected;
                    }
                }
                break;
            case 3:
                {
                    bool isShiftPressed = System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.LeftShift) || System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.RightShift);
                    if (!isShiftPressed)
                    {
                        SelectState = SelectState.ThirdSelected;
                    }
                }
                break;
            default:
                break;
        }
    }


    /// <summary>
    /// 请求下一帧全量重绘。用于图形集合增删、绘图预览等不能只依赖脏区补绘的场景。
    /// 约束：
    /// 1. 这里只处理“渲染范围”语义，不发布选择/变换事件。
    /// 2. 若交互已改变选中图形的几何、缩放、旋转、节点结构，应优先走 PublishTransformChange()，
    ///    由主通路统一完成事件发布、缓存失效、脏区标记和最终刷新。
    /// </summary>
    public void RequestFullRedraw()
    {
        DirtyRect = null;
        IsPartialRender = false;
    }

    public void InvalidateSelectionBoundsCache()
    {
        _selectionService.Invalidate();
    }

    internal void SyncSelectionService(IEnumerable<IShape>? selectedShapes)
    {
        _selectionService.SyncSelection(selectedShapes);
    }

    /// <summary>
    /// 根据当前选中的图形计算合并边界并设为脏区域（带 padding 覆盖选择框与箭头等额外绘制）。
    /// 对 DrawingHatch 类型的图形，额外标记其 FillObjects（原始被填充图形）的 bbox，
    /// 避免拖拽填充容器后原始图形位置不被重绘导致残影。
    /// </summary>
    public void MarkSelectedDirty(SKRect? mergedBoundsOverride = null)
    {
        if (ActiveCanvas == null) return;
        float padding = (DrawObject.rectH / ActiveCanvas.Viewport.Scale) * 2 + 8;

        // 调用方如果已经拿到了本次交互的最终合并边界，优先复用，
        // 避免在鼠标抬起这条链路里再次全量 GetBoundingBox。
        if (mergedBoundsOverride is { } overrideBounds && !overrideBounds.IsEmpty)
        {
            _selectionService.SetOverride(overrideBounds);
            MarkDirty(new SKRect(
                overrideBounds.Left - padding,
                overrideBounds.Top - padding,
                overrideBounds.Right + padding,
                overrideBounds.Bottom + padding));
            return;
        }

        var shapes = ActiveCanvas.Selection.Transformables;
        if (shapes.Count == 0) return;

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        foreach (var s in shapes)
        {
            var b = s.GetAABB();
            if (b.IsEmpty) continue;
            if (b.Left - padding < minX) minX = b.Left - padding;
            if (b.Top - padding < minY) minY = b.Top - padding;
            if (b.Right + padding > maxX) maxX = b.Right + padding;
            if (b.Bottom + padding > maxY) maxY = b.Bottom + padding;

            // 控制点拖动时，使用统一的 GetEffectiveLocalBounds + GetEffectiveTransformMatrix 计算预览 AABB
            //if (s.IsControlPointDragging)
            //{
            //    var localBounds = s.GetEffectiveLocalBounds();
            //    var matrix = s.GetEffectiveTransformMatrix();
            //    var tl = matrix.MapPoint(new SKPoint(localBounds.Left, localBounds.Top));
            //    var tr = matrix.MapPoint(new SKPoint(localBounds.Right, localBounds.Top));
            //    var br = matrix.MapPoint(new SKPoint(localBounds.Right, localBounds.Bottom));
            //    var bl = matrix.MapPoint(new SKPoint(localBounds.Left, localBounds.Bottom));
            //    float pMinX = Math.Min(Math.Min(tl.X, tr.X), Math.Min(br.X, bl.X)) - padding;
            //    float pMaxX = Math.Max(Math.Max(tl.X, tr.X), Math.Max(br.X, bl.X)) + padding;
            //    float pMinY = Math.Min(Math.Min(tl.Y, tr.Y), Math.Min(br.Y, bl.Y)) - padding;
            //    float pMaxY = Math.Max(Math.Max(tl.Y, tr.Y), Math.Max(br.Y, bl.Y)) + padding;
            //    if (pMinX < minX) minX = pMinX;
            //    if (pMinY < minY) minY = pMinY;
            //    if (pMaxX > maxX) maxX = pMaxX;
            //    if (pMaxY > maxY) maxY = pMaxY;
            //}

            // 对 DrawingHatch，额外标记其 FillObjects（原始被填充图形）的 bbox
            if (s is DrSoft.Drawing.Controls.DrawShapes.DrawingHatch hatch)
            {
                foreach (var fo in hatch.Boundaries.OfType<DrawObject>())
                {
                    var fb = fo.GetAABB();
                    if (fb.IsEmpty) continue;
                    if (fb.Left - padding < minX) minX = fb.Left - padding;
                    if (fb.Top - padding < minY) minY = fb.Top - padding;
                    if (fb.Right + padding > maxX) maxX = fb.Right + padding;
                    if (fb.Bottom + padding > maxY) maxY = fb.Bottom + padding;
                }
            }
        }
        if (minX == float.MaxValue) return;
        _selectionService.SetOverride(new SKRect(minX + padding, minY + padding, maxX - padding, maxY - padding));
        MarkDirty(new SKRect(minX, minY, maxX, maxY));
    }

    private readonly SKPaintCache paintCache = new SKPaintCache();
    private readonly GridPaintCache gridPaintCache = new GridPaintCache();
    public const int LargeShapeWorkloadThreshold = 4096;

    /// <summary>
    /// 发布一次“选中图形几何已发生变化”的统一通知。
    /// 该方法负责整理 Bounds 负载、触发必要的跳点重算、更新空间索引脏态，并驱动一次重绘。
    /// 主通路约束：
    /// 1. 交互结束后只要选中图形的几何发生变化（移动、缩放、旋转、节点编辑、路径结构变化），
    ///    都应尽量收敛到这里，不要在调用方再手工串联 PublishSelectSharpsChange()/RequestRedraw()/InvalidateGeometryCaches()。
    /// 2. 调用方只负责补充本次交互特有的前置动作，例如结构变化导致的 InvalidateVisibleCache()、
    ///    或已知 merged bounds override；最终发布和刷新由这里统一收口。
    /// 3. 不要新增直接 Redraw() 的旁路，否则容易再次出现“模型已变但最终帧未刷新”的问题。
    /// </summary>
    public void PublishTransformChange(SKRect? mergedBoundsOverride = null, bool? requiresHatchRegenerationOverride = null)
    {
        if (ActiveCanvas == null) return;

        // TODO: 快照对比，检测是否加入撤销栈，正式发版需要删除此代码
        CommandHistory.HistoryStateSnapshot? historySnapshot = null;
        if (ActiveCanvas is DrawingCanvas drawingCanvasForHistory)
        {
            historySnapshot = drawingCanvasForHistory.CommandHistory.CaptureStateSnapshot();
        }

        var selectedShapes = ActiveCanvas.Selection as IList<IShape>
            ?? ActiveCanvas.Selection.ToList();
        var exceedsWorkload = ExceedsShapeWorkloadThreshold(selectedShapes);
        var requiresHatchRegeneration = requiresHatchRegenerationOverride
            ?? (ActiveCanvas is DrawingCanvas drawingCanvas && drawingCanvas.RequiresHatchRegeneration());
        // 直接根据当前选中集构造 Bounds DTO，避免为了事件负载临时 new DrawingGroup
        // 再触发一次子级边界框扫描。
        //var boundsDto = BuildSelectionBoundsDto(mergedBoundsOverride);
        var boundsDto = BuildSelectionBoundsDto();
        if (boundsDto == null) return;
        PopulateSelectionBoundsMetadata(boundsDto, selectedShapes);

        // 超大选中集下，同步两两求交会在鼠标抬起时直接卡死 UI。
        // 这里保留原有小批量自动重算行为，大批量改为跳过同步重算。
        if (!exceedsWorkload)
        {
            RecalculateJumpPointsAction?.Invoke();
        }

        PublishCanvasChange?.Invoke(
            ActiveCanvas,
            CanvasChangeType.TransformChanged,
            BuildSelectedSharpsDto(selectedShapes, boundsDto, requiresHatchRegeneration));

        if (ActiveCanvas is DrawingCanvas canvas)
        {
            // 图形位置/尺寸发生变化后，空间索引必须按最新几何重建。
            // 大批量拖动期间保留 dirty overlay，避免全量清缓存导致卡顿或旧索引残影。
            canvas.InvalidateGeometryCaches(selectedShapes);
        }

        // 变换完成后标记选中图形脏区，并统一通过宿主抽象请求一次重绘。
        // 不直接走旧 Redraw 委托，避免 Host 主通路与兼容旁路分叉导致刷新遗漏。
        MarkSelectedDirty(mergedBoundsOverride);
        RequestRedraw();

        // TODO: 快照对比，检测是否加入撤销栈，正式发版需要删除此代码
        if (historySnapshot.HasValue && ActiveCanvas is DrawingCanvas drawingCanvasForDiagnostic)
        {
            drawingCanvasForDiagnostic.ScheduleUndoRedoCoverageCheck(
                historySnapshot.Value,
                nameof(PublishTransformChange));
        }
    }

    public void PublishSelectSharpsChange()
    {
        if (ActiveCanvas == null) return;
        // 选择变化事件与 TransformChanged 一样，只需要一个轻量的 Bounds 描述即可，
        // 不需要临时构造 DrawingGroup 再做完整映射。
        // 约束：这里只用于“当前选择集变了，但几何未变”的场景，例如纯选择切换。
        // 若选择中的图形几何已经变化，应走 PublishTransformChange()，避免重复通知与刷新分叉。
        var boundsDto = BuildSelectionBoundsDto();
        if (boundsDto == null) return;
        var selectedShapes = ActiveCanvas.Selection as IList<IShape>
            ?? ActiveCanvas.Selection.ToList();
        PopulateSelectionBoundsMetadata(boundsDto, selectedShapes);

        PublishCanvasChange(
            ActiveCanvas,
            CanvasChangeType.SelectSharps,
            BuildSelectedSharpsDto(selectedShapes, boundsDto));
    }

    /// <summary>
    /// 发布“当前选择集语义发生变化”的统一通知。
    /// 该事件面向外围 UI 和能力开关，不承载完整图元数据，只提供按类型汇总的轻量选择信息。
    /// </summary>
    public void PublishSelectChanged()
    {
        if (ActiveCanvas == null) return;

        var selectedObjects = ActiveCanvas?.Selection?
                .Select(it =>
                new
                {
                    Id = it.UId,
                    Type = ShapeTypeMapper.Map(it.Type),
                    IsPathEditing = it.IsPathEditing
                }).ToList();

        var selectionData = selectedObjects?
            .GroupBy(s => s.Type)
            .ToDictionary(
                g => g.Key,
                g => new SelectChangedInfo
                {
                    Count = g.Count(),
                    AllPathEditing = g.All(it => it.IsPathEditing),
                    IsSelectedMoveNode = SelectedMoveNodeWorldPosition.HasValue,
                    SelectedNodeCount = SelectedPathNodeWorldPositions.Count
                }
            );
        PublishCommandCapabilityChanged(BuildCapabilities());
        PublishCanvasChange(ActiveCanvas, CanvasChangeType.SelectChanged, selectionData);
        _old_selectedShapes = ActiveCanvas.Selection.ToList();
    }

    public void PublishSelectStateChanged()
    {
        if (ActiveCanvas == null) return;
        // 选择变化事件与 TransformChanged 一样，只需要一个轻量的 Bounds 描述即可，
        // 不需要临时构造 DrawingGroup 再做完整映射。
        // 约束：这里只用于“当前选择集变了，但几何未变”的场景，例如纯选择切换。
        // 若选择中的图形几何已经变化，应走 PublishTransformChange()，避免重复通知与刷新分叉。
        var boundsDto = BuildSelectionBoundsDto();
        if (boundsDto == null) return;
        var selectedShapes = ActiveCanvas.Selection as IList<IShape>
            ?? ActiveCanvas.Selection.ToList();
        PopulateSelectionBoundsMetadata(boundsDto, selectedShapes);

        PublishCanvasChange(
            ActiveCanvas,
            CanvasChangeType.SelectStateChanged,
            (_selectState, BuildSelectedSharpsDto(selectedShapes, boundsDto)));
    }

    public SKRect CalculateMergedBounds()
    {
        if (ActiveCanvas == null || ActiveCanvas.SelectedShapeCount == 0)
        {
            return SKRect.Empty;
        }

        return _selectionService.GetMergedBounds(ActiveCanvas!.Selection);
    }

    internal SelectionCapabilities BuildCapabilities()
    {
        IReadOnlyDictionary<ShapeType, int> target = ActiveCanvas?.SelectedCountByType
            .ToDictionary(kvp => ShapeTypeMapper.Map(kvp.Key), kvp => kvp.Value) ?? [];

        IReadOnlyList<IShape> selectedShapes = ActiveCanvas is { } canvas
            ? canvas.Selection
            : Array.Empty<IShape>();
        var selectedShapeData = selectedShapes
            .OfType<IShapeData>()
            .ToArray();
        var capabilities = SelectionCapabilities.From(selectedShapeData);

        capabilities.IsLocked = ActiveCanvas?.Selection.All(s => s.IsLocked) ?? false;
        capabilities.CanUndo = ActiveCanvas?.CommandHistory.CanUndo ?? false;
        capabilities.CanRedo = ActiveCanvas?.CommandHistory.CanRedo ?? false;
        capabilities.CanPaste = DrawingClipboard.Instance.HasContent;
        capabilities.CanEnterNodeEdit = CanEnterNodeEdit(selectedShapes);
        capabilities.CanExtendNode = CanExtendSelection(selectedShapes, capabilities.CanExtendNode);

        return capabilities;
    }

    private static bool CanEnterNodeEdit(IReadOnlyList<IShape> selectedShapes)
    {
        if (selectedShapes.Count != 1)
            return false;

        if (selectedShapes[0] is not DrawCombination combination)
            return false;

        return !combination.IsLocked && combination.Kind == CombinationKind.Extended;
    }

    private static bool CanExtendSelection(IReadOnlyList<IShape> selectedShapes, bool defaultCapability)
    {
        if (!defaultCapability || selectedShapes.Count == 0)
            return false;

        if (selectedShapes.Any(shape => shape.IsLocked))
            return false;

        return !selectedShapes
            .OfType<DrawCombination>()
            .Any(combination => combination.Kind == CombinationKind.Extended);
    }

    private static void PublishCommandCapabilityChanged(SelectionCapabilities capabilities)
    {
        EventBus.Instance.Publish(new CommandCapabilityChangedEvent { Capabilities = capabilities });
    }

    private DrawObjectDto? BuildSelectionBoundsDto(SKRect? mergedBoundsOverride = null)
    {
        if (ActiveCanvas == null || ActiveCanvas.SelectedShapeCount <= 0)
        {
            return null;
        }

        var mergedBounds = mergedBoundsOverride is { } overrideBounds && !overrideBounds.IsEmpty
            ? overrideBounds
            : CalculateMergedBounds();

        // 保持旧 drawGroup DTO 的关键几何语义：
        // 1. X/Y 与 SharpCenter 都取合并包围盒中心
        // 2. RotationCenter 默认先取包围盒中心，调用方再按单选/多选规则覆写
        return new DrawObjectDto
        {
            X = mergedBounds.MidX,
            Y = mergedBounds.MidY,
            SharpCenter = new Point2D(mergedBounds.MidX, mergedBounds.MidY),
            RotationCenter = new Point2D(mergedBounds.MidX, mergedBounds.MidY),
            Width = mergedBounds.Width,
            Height = mergedBounds.Height,
        };
    }

    private SelectedSharpsDto BuildSelectedSharpsDto(
        IList<IShape> selectedShapes,
        DrawObjectDto boundsDto,
        bool requiresHatchRegeneration = false)
    {
        return new SelectedSharpsDto
        {
            Id = ActiveCanvas?.Id ?? 0,
            Name = ActiveCanvas?.Name,
            IsAllLock = selectedShapes.Count > 0 && selectedShapes.All(s => s.IsLocked),
            ResizeConstraint = SelectionResizeConstraintResolver.ResolveForSelection(selectedShapes),
            EditingObject = selectedShapes.Count == 1
                ? DrawObjectMapper.MapWithoutChildren(selectedShapes[0] as DrawObject)
                : null,
            SelectionIds = selectedShapes.Select(s => s.UId).ToList(),
            DrawObjectDtoData = boundsDto,
            RequiresHatchRegeneration = requiresHatchRegeneration
        };
    }

    private static void PopulateSelectionBoundsMetadata(DrawObjectDto boundsDto, IList<IShape> selectedShapes)
    {
        if (selectedShapes.Count == 1)
        {
            var selected = selectedShapes[0];
            boundsDto.ScaleX = selected.ScaleX;
            boundsDto.ScaleY = selected.ScaleY;
            boundsDto.Rotation = selected.Rotation;
            boundsDto.SkewX = selected.SkewX;
            boundsDto.SkewY = selected.SkewY;
            boundsDto.RotationCenter = new Point2D(selected.RotationCenter.X, selected.RotationCenter.Y);
            return;
        }

        if (selectedShapes.Count <= 1)
        {
            return;
        }

        // 多选时旋转中心始终与合并包围盒中心一致
        boundsDto.RotationCenter = new Point2D(boundsDto.X, boundsDto.Y);
    }

    public static bool ExceedsShapeWorkloadThreshold(
        IEnumerable<IShape>? shapes,
        int threshold = LargeShapeWorkloadThreshold)
    {
        if (shapes == null || threshold <= 0)
        {
            return false;
        }

        int remaining = threshold;
        var containers = new Stack<IContainer>();

        foreach (var shape in shapes)
        {
            remaining--;
            if (remaining < 0)
            {
                return true;
            }

            if (shape is DrawingHatch hatch)
            {
                if (hatch.Boundaries.Count > remaining)
                {
                    return true;
                }

                remaining -= hatch.Boundaries.Count;
                if (remaining < 0)
                {
                    return true;
                }
            }

            if (shape is not IContainer container || container.Children.Count == 0)
            {
                continue;
            }

            if (container.Children.Count > remaining)
            {
                return true;
            }

            remaining -= container.Children.Count;
            if (remaining < 0)
            {
                return true;
            }
            containers.Push(container);
        }

        while (containers.Count > 0)
        {
            var container = containers.Pop();
            for (int i = 0; i < container.Children.Count; i++)
            {
                var child = container.Children[i];
                if (child is DrawingHatch hatch)
                {
                    if (hatch.Boundaries.Count > remaining)
                    {
                        return true;
                    }

                    remaining -= hatch.Boundaries.Count;
                    if (remaining < 0)
                    {
                        return true;
                    }
                }

                if (child is not IContainer nestedContainer || nestedContainer.Children.Count == 0)
                {
                    continue;
                }

                if (nestedContainer.Children.Count > remaining)
                {
                    return true;
                }

                remaining -= nestedContainer.Children.Count;
                if (remaining < 0)
                {
                    return true;
                }

                containers.Push(nestedContainer);
            }
        }

        return false;
    }
}

public class BoxSelectionState
{
    public bool IsActive { get; set; }
    public SKPoint Start { get; set; }
    public SKPoint Current { get; set; }
    // 重置
    public void Reset()
    {
        IsActive = false;
        Start = Current = default;
    }
}

[Obsolete("Use BoxSelectionState instead. This alias remains for compatibility during refactoring.")]
public class BoxSelection : BoxSelectionState
{
}

public class NotifyingList<T> : IList<T>
{
    private readonly List<T> _innerList = new();

    // 事件：内容变化时触发
    public event Action? Changed;

    public void Add(T item)
    {
        _innerList.Add(item);
        Changed?.Invoke();  // 触发通知
    }

    public bool Remove(T item)
    {
        var result = _innerList.Remove(item);
        if (result) Changed?.Invoke();
        return result;
    }

    public void Clear()
    {
        if (_innerList.Count > 0)
        {
            _innerList.Clear();
            Changed?.Invoke();
        }
    }

    // 实现其他接口成员（委托给内部列表）
    public T this[int index]
    {
        get => _innerList[index];
        set
        {
            _innerList[index] = value;
            Changed?.Invoke();
        }
    }

    public int Count => _innerList.Count;
    public bool IsReadOnly => false;

    public void Insert(int index, T item)
    {
        _innerList.Insert(index, item);
        Changed?.Invoke();
    }

    public void RemoveAt(int index)
    {
        _innerList.RemoveAt(index);
        Changed?.Invoke();
    }

    public int IndexOf(T item) => _innerList.IndexOf(item);
    public bool Contains(T item) => _innerList.Contains(item);
    public void CopyTo(T[] array, int arrayIndex) => _innerList.CopyTo(array, arrayIndex);

    public IEnumerator<T> GetEnumerator() => _innerList.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
