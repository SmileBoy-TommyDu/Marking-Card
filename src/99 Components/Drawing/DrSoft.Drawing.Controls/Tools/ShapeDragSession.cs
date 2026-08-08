using DrSoft.Drawing.Controls.Commands;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Model;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Cursor = System.Windows.Input.Cursor;
using Cursors = System.Windows.Input.Cursors;

namespace DrSoft.Drawing.Controls.Tools;

/// <summary>
/// 管理选中图形拖拽移动的会话对象。
/// 负责拖拽预览、同步/异步提交、脏区维护，以及最终命令入栈。
/// </summary>
internal sealed class ShapeDragSession : IToolSelectSession
{
    private const int DeferredDragCommitBatchSize = 256;

    private readonly DocumentContext _context;
    private readonly SelectionMouseDownService? _selectionMouseDownService;
    private readonly SelectionStateService? _selectionStateService;
    private readonly PathNodeEditSession? _pathNodeEditSession;
    private readonly BoxSelectionSession? _boxSelectionSession;
    private readonly Action? _notifyMenuEvent;

    private bool _isDragging;
    private bool _isPreparingDrag;
    private SKPoint _prepareDragPoint = SKPoint.Empty;
    private SKRect _originalMergedBounds = SKRect.Empty;
    private SKRect? _lastPreviewDirty;
    private IDeferredCommand? _pendingTransformCommand;
    private bool _lastMouseDownNeedRedraw;

    public ShapeDragSession(DocumentContext context)
    {
        _context = context;
    }

    public ShapeDragSession(
        DocumentContext context,
        SelectionMouseDownService selectionMouseDownService,
        SelectionStateService selectionStateService,
        PathNodeEditSession pathNodeEditSession,
        BoxSelectionSession boxSelectionSession,
        Action notifyMenuEvent)
    {
        _context = context;
        _selectionMouseDownService = selectionMouseDownService;
        _selectionStateService = selectionStateService;
        _pathNodeEditSession = pathNodeEditSession;
        _boxSelectionSession = boxSelectionSession;
        _notifyMenuEvent = notifyMenuEvent;
    }

    public string Name => "ShapeDrag";

    public bool IsActive => _isDragging || _isPreparingDrag;

    public Cursor? SuggestedCursor => _isDragging ? Cursors.SizeAll : null;

    public ControlPointType? CompletedControlPoint => null;

    internal bool LastMouseDownNeedRedraw => _lastMouseDownNeedRedraw;

    public bool IsDragging => _isDragging;

    public bool TryMouseDown(SKPoint point, out string message)
    {
        _lastMouseDownNeedRedraw = false;

        if (_selectionMouseDownService == null || _pathNodeEditSession == null)
        {
            message = "拖拽图形依赖服务未初始化";
            return false;
        }

        bool isInControlPointState = _context.SelectState == SelectState.SecondSelected
            || _context.SelectState == SelectState.ThirdSelected;

        SelectionMouseDownResult selectionResult = _selectionMouseDownService.Handle(
            point,
            _context.IsShiftPressed(),
            _pathNodeEditSession.ClearSelectedMoveNode,
            _notifyMenuEvent ?? (() => { }));
        if (!selectionResult.Handled)
        {
            message = "图形拖拽未命中任何选择动作";
            return false;
        }

        _lastMouseDownNeedRedraw = selectionResult.NeedRedraw;

        switch (selectionResult.Action)
        {
            case SelectionMouseDownAction.StartDraggingSelection:
                {
                    SKRect originalMergedBounds = _context.CalculateMergedBounds();
                    Start(point, originalMergedBounds);
                    message = "开始直接拖拽已选图形";
                    return true;
                }
            case SelectionMouseDownAction.PrepareDragSelection:
                {
                    if (isInControlPointState)
                    {
                        _isPreparingDrag = false;
                        _prepareDragPoint = SKPoint.Empty;
                        message = "当前是控制点框态，禁止准备拖拽图形";
                        return true;
                    }

                    _isPreparingDrag = true;
                    _prepareDragPoint = point;
                    message = "记录按下点，等待拖拽升级";
                    return true;
                }
            case SelectionMouseDownAction.StartBoxSelection:
                {
                    _boxSelectionSession?.Start(point);
                    _isPreparingDrag = false;
                    _prepareDragPoint = SKPoint.Empty;
                    message = "切换到框选会话";
                    return true;
                }
            default:
                message = "选择动作已处理，但无需进入图形拖拽";
                return true;
        }
    }

    public bool TryMouseMove(SKPoint point, out string message)
    {
        if (_isDragging)
        {
            UpdatePreview(point);
            _selectionStateService?.SetMoveCursor();
            message = "更新图形拖拽预览";
            return true;
        }

        if (_isPreparingDrag)
        {
            if (_context.ActiveCanvas == null || _context.ActiveCanvas.SelectedShapeCount == 0)
            {
                message = "准备拖拽时选区已丢失";
                return true;
            }

            bool allLocked = _context.ActiveCanvas.Selection.All(item => item.IsLocked);
            if (allLocked)
            {
                message = "准备拖拽的图形全部已锁定";
                return true;
            }

            float dx = point.X - _prepareDragPoint.X;
            float dy = point.Y - _prepareDragPoint.Y;
            double distance = System.Math.Sqrt(dx * dx + dy * dy);
            bool promoted = distance > 0.0f;
            if (promoted)
            {
                SKRect originalMergedBounds = _context.CalculateMergedBounds();
                Start(_prepareDragPoint, originalMergedBounds);
                UpdatePreview(point);
                _selectionStateService?.SetMoveCursor();
                _isPreparingDrag = false;
                message = "位移超过阈值，升级为真实拖拽";
                return true;
            }

            message = "仍处于准备拖拽阶段";
            return true;
        }

        message = "图形拖拽会话未激活";
        return false;
    }

    public bool TryMouseUp(SKPoint point, out string message)
    {
        if (_isDragging)
        {
            Complete(point);
            _isPreparingDrag = false;
            _prepareDragPoint = SKPoint.Empty;
            message = "完成图形拖拽";
            return true;
        }

        if (_isPreparingDrag)
        {
            _isPreparingDrag = false;
            _prepareDragPoint = SKPoint.Empty;
            message = "结束准备拖拽，按点击处理";
            return true;
        }

        message = "图形拖拽会话未激活";
        return false;
    }

    public bool TryRightMouseDown(SKPoint point, out string message)
    {
        message = "图形拖拽会话不处理右键";
        return false;
    }

    /// <summary>
    /// 初始化拖拽会话，并为最终命令提交捕获 before-state。
    /// </summary>
    public void Start(SKPoint point, SKRect originalMergedBounds)
    {
        if (_context.ActiveCanvas == null)
            return;

        _context.BoxSelect.Start = point;
        _context.BoxSelect.Current = point;
        _context.IsDrawing = true;
        _isDragging = true;
        _lastPreviewDirty = null;
        _originalMergedBounds = originalMergedBounds;

        var selectedForCommand = _context.ActiveCanvas.Selection
                        .OfType<DrawObject>()
                        .Where(shape => shape.CanTransform);
        _pendingTransformCommand = CreateCommand(selectedForCommand);
        var previewCorners = TryBuildSingleSelectionPreviewCorners();

        // 移动预览框必须从正式选择框同一份 merged bounds 起步。
        // CalculateSharpsBounds 会回到旧的 Width/Height 近似，旋转/倾斜多选时会让预览框相对选框横向偏移。
        _context.CachedDragPreviewBounds = !originalMergedBounds.IsEmpty
            ? originalMergedBounds
            : _context.CalculateMergedBounds();
        _context.CachedDragPreviewCorners = previewCorners;
        _context.MarkSelectedDirty();
        _context.SetCursor(Cursors.SizeAll);
        _context.ReportStatus($"开始拖拽 {_context.ActiveCanvas.SelectedShapeCount} 个图形");
    }

    public void UpdatePreview(SKPoint point)
    {
        if (!_isDragging || _context.ActiveCanvas == null || _context.ActiveCanvas.SelectedShapeCount == 0)
            return;

        _context.BoxSelect.Current = point;

        // 预览阶段不真实改写图形，只移动一个缓存的合并包围盒并标脏旧/新区域。
        var merged = _context.CachedDragPreviewBounds;
        if (merged == null || merged.Value.IsEmpty)
        {
            merged = _context.CalculateMergedBounds();
            if (merged == null || merged.Value.IsEmpty)
                return;

            _context.CachedDragPreviewBounds = merged;
        }

        float dx = _context.BoxSelect.Current.X - _context.BoxSelect.Start.X;
        float dy = _context.BoxSelect.Current.Y - _context.BoxSelect.Start.Y;
        const float pad = 12f;
        var currentDirty = _context.CachedDragPreviewCorners is { Length: > 0 } corners
            ? BoundsFromTranslatedCorners(corners, dx, dy, pad)
            : new SKRect(
                merged.Value.Left + dx - pad,
                merged.Value.Top + dy - pad,
                merged.Value.Right + dx + pad,
                merged.Value.Bottom + dy + pad);

        MarkPreviewDirty(currentDirty);
        _context.ReportStatus($"拖动预览 {_context.ActiveCanvas.SelectedShapeCount} 个图形: ΔX={dx:F1}, ΔY={dy:F1}");
    }

    public void Complete(SKPoint point)
    {
        if (!_isDragging || _context.ActiveCanvas == null)
        {
            ResetImmediateState();
            return;
        }

        var selectedShapes = _context.ActiveCanvas.Selection as IList<IShape>
            ?? _context.ActiveCanvas.Selection.ToList();
        var unlockedShapes = selectedShapes.Where(it => !it.IsLocked).ToList();
        if (unlockedShapes.Count == 0)
        {
            ResetImmediateState();
            return;
        }

        var dx = point.X - _context.BoxSelect.Start.X;
        var dy = point.Y - _context.BoxSelect.Start.Y;

        if (!_originalMergedBounds.IsEmpty)
            _context.MarkSelectedDirty(_originalMergedBounds);
        else
            _context.MarkSelectedDirty();

        var movedBoundsOverride = BuildMovedBoundsOverride(_originalMergedBounds, unlockedShapes, dx, dy);
        var requiresHatchRegenerationOverride = BuildRequiresHatchRegenerationOverride(unlockedShapes);

        // 大工作量场景改为异步分批提交，避免鼠标抬起瞬间长时间阻塞 UI 线程。
        if (DocumentContext.ExceedsShapeWorkloadThreshold(unlockedShapes))
        {
            if (_lastPreviewDirty.HasValue)
                _context.MarkDirty(_lastPreviewDirty.Value);

            _context.IsApplyingDeferredDragCommit = true;
            _context.IsDrawing = false;
            _isDragging = false;
            _context.ReportStatus($"正在提交 {unlockedShapes.Count} 个图形移动...");
            ApplyDeferredDragCommitAsync(unlockedShapes, dx, dy, movedBoundsOverride, requiresHatchRegenerationOverride);
            return;
        }

        var deltaMatrix = SKMatrix.CreateTranslation(dx, dy);
        if (unlockedShapes.Count > 1)
        {
            _context.MergedRotationCenter = deltaMatrix.MapPoint(_context.MergedRotationCenter);
        }

        ApplyDragTranslation(unlockedShapes, dx, dy);
        _context.ReportStatus($"移动 {unlockedShapes.Count} 个图形完成: ΔX={dx:F1}, ΔY={dy:F1}");

        CommitPendingTransformCommand();

        if (_context.ShowJumpLine)
        {
            _context.IsPartialRender = false;
            _context.DirtyRect = null;
        }

        _context.PublishTransformChange(movedBoundsOverride, requiresHatchRegenerationOverride);
        _context.SetCursor(Cursors.SizeAll);
        ResetImmediateState();
    }

    public void Cancel()
    {
        _pendingTransformCommand = null;
        _isPreparingDrag = false;
        _prepareDragPoint = SKPoint.Empty;
        _lastMouseDownNeedRedraw = false;
        ResetImmediateState();
    }

    private void MarkPreviewDirty(SKRect current)
    {
        if (_lastPreviewDirty.HasValue)
            _context.MarkDirty(_lastPreviewDirty.Value);

        _context.MarkDirty(current);
        _lastPreviewDirty = current;
    }

    private void ResetImmediateState()
    {
        _context.IsDrawing = false;
        _isDragging = false;
        _originalMergedBounds = SKRect.Empty;
        _lastPreviewDirty = null;
        _context.CachedDragPreviewBounds = null;
        _context.CachedDragPreviewCorners = null;
        _context.BoxSelect.Reset();
    }

    private SKPoint[]? TryBuildSingleSelectionPreviewCorners()
    {
        if (_context.ActiveCanvas == null)
            return null;

        var selected = TryResolveSinglePreviewTarget(_context.ActiveCanvas.Selection);
        if (selected is not DrawObject drawObject)
            return null;

        var geometry = SelectionGeometryBuilder.BuildForSinglePreviewOBBSelection(
            drawObject);
        return geometry.Corners.Length == 0 ? null : geometry.Corners;
    }

    private static IShape? TryResolveSinglePreviewTarget(IReadOnlyList<IShape> selectedShapes)
    {
        if (selectedShapes.Count == 1)
            return selectedShapes[0];

        // Defensive normalization: UI/container selection should normally contain only the
        // selected container, but stale child selection flags can temporarily leave children
        // in SelectedShapes. Preview geometry still has to follow the visible container frame.
        var containers = selectedShapes
            .OfType<DrawObject>()
            .Where(shape => shape is DrawCombination or DrawingGroup)
            .ToList();
        if (containers.Count != 1)
            return null;

        var container = containers[0];
        return selectedShapes
            .OfType<DrawObject>()
            .All(shape => ReferenceEquals(shape, container) || ContainsDescendant(container, shape))
            ? container
            : null;
    }

    private static bool ContainsDescendant(DrawObject container, DrawObject candidate)
    {
        return container switch
        {
            DrawCombination combination => ContainsDescendant(combination.Children, candidate),
            DrawingGroup group => ContainsDescendant(group.Children, candidate),
            _ => false
        };
    }

    private static bool ContainsDescendant(IEnumerable<IShape> children, DrawObject candidate)
    {
        foreach (var child in children)
        {
            if (ReferenceEquals(child, candidate))
                return true;

            if (child is DrawObject childObject && ContainsDescendant(childObject, candidate))
                return true;
        }

        return false;
    }

    private static IDeferredCommand CreateCommand(IEnumerable<DrawObject> selectedShapes)
    {
        var list = selectedShapes as IList<DrawObject> ?? selectedShapes.ToList();
        return new CommandTransform(CommandTransform.CollectWithChildren(list), "移动图形");
    }

    private static SKRect BoundsFromTranslatedCorners(SKPoint[] corners, float dx, float dy, float padding)
    {
        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;

        foreach (var corner in corners)
        {
            var x = corner.X + dx;
            var y = corner.Y + dy;
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }

        return new SKRect(minX - padding, minY - padding, maxX + padding, maxY + padding);
    }

    private void CommitPendingTransformCommand()
    {
        if (_pendingTransformCommand == null)
            return;

        _pendingTransformCommand.CaptureAfterState();
        _context.ActiveCanvas?.CommandManager.PushExecutedCommand(_pendingTransformCommand);
        _pendingTransformCommand = null;
    }

    private static void ApplyDragTranslationRange(
        IList<IShape> selectedShapes,
        int startIndex,
        int endIndex,
        float dx,
        float dy)
    {
        for (int i = startIndex; i < endIndex; i++)
        {
            if (selectedShapes[i] is DrawObject drawObject
                && drawObject.CanTransform)
            {
                drawObject.Translate(dx, dy);
            }
        }
    }

    private static void ApplyDragTranslation(IList<IShape> selectedShapes, float dx, float dy)
    {
        // 超大选中集改走分片并行，减少单线程批量平移的尾延迟。
        if (selectedShapes.Count >= DocumentContext.LargeShapeWorkloadThreshold)
        {
            Parallel.ForEach(
                Partitioner.Create(0, selectedShapes.Count, 8192),
                range => ApplyDragTranslationRange(selectedShapes, range.Item1, range.Item2, dx, dy));
            return;
        }

        ApplyDragTranslationRange(selectedShapes, 0, selectedShapes.Count, dx, dy);
    }

    private static bool? BuildRequiresHatchRegenerationOverride(IList<IShape> selectedShapes)
    {
        bool? requiresHatchRegenerationOverride = false;
        for (int i = 0; i < selectedShapes.Count; i++)
        {
            var shape = selectedShapes[i];
            if (shape is IContainer)
                return null;

            if (shape is DrawObject drawObject && drawObject.CanTransform
                || shape is IHatchable hatchable && hatchable.HatchParamInfo != null)
            {
                requiresHatchRegenerationOverride = true;
            }
        }

        return requiresHatchRegenerationOverride;
    }

    private static SKRect? BuildMovedBoundsOverride(
        SKRect originalMergedBounds,
        IList<IShape> selectedShapes,
        float dx,
        float dy)
    {
        if (originalMergedBounds.IsEmpty)
            return null;

        for (int i = 0; i < selectedShapes.Count; i++)
        {
            if (selectedShapes[i] is not DrawObject drawObject
                || drawObject.CanTransform)
            {
                return null;
            }
        }

        return new SKRect(
            originalMergedBounds.Left + dx,
            originalMergedBounds.Top + dy,
            originalMergedBounds.Right + dx,
            originalMergedBounds.Bottom + dy);
    }

    private async void ApplyDeferredDragCommitAsync(
        IList<IShape> selectedShapes,
        float dx,
        float dy,
        SKRect? movedBoundsOverride,
        bool? requiresHatchRegenerationOverride)
    {
        bool? computedRequiresHatchRegeneration = requiresHatchRegenerationOverride;
        Exception? failure = null;

        async Task YieldToUiAsync()
        {
            if (System.Windows.Application.Current != null)
            {
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                return;
            }

            await Task.Yield();
        }

        try
        {
            await YieldToUiAsync();

            if (computedRequiresHatchRegeneration == null
                && _context.ActiveCanvas is DrawingCanvas drawingCanvas)
            {
                computedRequiresHatchRegeneration = await Task.Run(
                    () => (bool?)drawingCanvas.RequiresHatchRegeneration(selectedShapes));
            }

            // 每批只改写一部分图形，并在批次间让出 UI，降低长帧风险。
            for (int startIndex = 0; startIndex < selectedShapes.Count; startIndex += DeferredDragCommitBatchSize)
            {
                int batchStart = startIndex;
                int batchEnd = System.Math.Min(batchStart + DeferredDragCommitBatchSize, selectedShapes.Count);

                await Task.Run(() =>
                {
                    lock (_context.DeferredDragCommitSyncRoot)
                    {
                        ApplyDragTranslationRange(selectedShapes, batchStart, batchEnd, dx, dy);
                    }
                });

                if (batchEnd < selectedShapes.Count)
                    await YieldToUiAsync();
            }
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        _context.IsApplyingDeferredDragCommit = false;
        _pendingTransformCommand ??= null;
        ResetImmediateState();

        if (failure != null)
        {
            _context.ReportStatus($"移动图形失败: {failure.Message}");
            _context.RequestRedraw();
            return;
        }

        _context.ReportStatus($"移动 {selectedShapes.Count} 个图形完成: ΔX={dx:F1}, ΔY={dy:F1}");
        CommitPendingTransformCommand();

        if (_context.ShowJumpLine)
        {
            _context.IsPartialRender = false;
            _context.DirtyRect = null;
        }

        _context.PublishTransformChange(movedBoundsOverride, computedRequiresHatchRegeneration);
        _context.SetCursor(Cursors.SizeAll);
    }
}
