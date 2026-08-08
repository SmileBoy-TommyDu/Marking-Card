using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Model;
using SkiaSharp;
using System;
using System.Diagnostics;
using DrSoft.Drawing.Controls.DrawShapes;

namespace DrSoft.Drawing.Controls.Tools;

internal enum SelectionMouseDownAction
{
    None = 0,
    PrepareDragSelection,
    StartDraggingSelection,
    StartBoxSelection,
}

internal readonly record struct SelectionMouseDownResult(
    bool Handled,
    bool NeedRedraw,
    SelectionMouseDownAction Action);

/// <summary>
/// 选择工具 MouseDown 阶段的分发服务。
/// 只负责判定“命中当前选择 / 命中新对象 / 进入框选”的动作，不负责后续拖拽或框选会话本身。
/// </summary>
internal sealed class SelectionMouseDownService
{
    private readonly DocumentContext _context;
    private readonly SelectionHitService _selectionHitService;
    private readonly SelectionStateService _selectionStateService;
    private readonly float clickPadding = 8.0f;

    public SelectionMouseDownService(
        DocumentContext context,
        SelectionHitService selectionHitService,
        SelectionStateService selectionStateService)
    {
        _context = context;
        _selectionHitService = selectionHitService;
        _selectionStateService = selectionStateService;
    }

    public SelectionMouseDownResult Handle(
        SKPoint point,
        bool isShiftPressed,
        Action clearMoveNodeSelection,
        Action notifyMenuEvent)
    {
        if (_context.ActiveCanvas == null)
            return default;

        // 先判断是否命中当前已选对象；这一分支优先级最高，避免点击已选对象时误进入框选。
        var selectedShapeResult = TryHandleSelectedShapeClick(point, clearMoveNodeSelection, notifyMenuEvent);
        if (selectedShapeResult.Handled)
            return selectedShapeResult;

        return HandleUnselectedShapeOrBoxSelect(point, isShiftPressed, clearMoveNodeSelection, notifyMenuEvent);
    }

    private SelectionMouseDownResult TryHandleSelectedShapeClick(
        SKPoint point,
        Action clearMoveNodeSelection,
        Action notifyMenuEvent)
    {
        if (_context.ActiveCanvas?.SelectedShapeCount == 0)
            return default;

        var padding = clickPadding / (_context.ActiveCanvas.Viewport.Scale == 0 ? 1.0f : (float)_context.ActiveCanvas.Viewport.Scale);
        bool isOverSelectedGeometry = _selectionHitService.IsPointInSelectedShapes(point, padding);
        bool isOverSelectionBoundsOnly = _selectionHitService.IsPointOverSelectionBoundsBorder(point);

        // 旋转/倾斜的非容器图形（矩形/圆/多边形等），AABB 比实际图形大，
        // 点击在 AABB 空白区域时不算命中已选图形，让流程继续到 HandleUnselectedShapeOrBoxSelect。
        // 但容器图形（DrawingHatch/DrawingGroup）的 HitTest 只检测子元素（填充线/子图形），
        // 点击在容器内部空白（如填充线之间）时 isOverSelectedGeometry 为 false，
        // 需要用 AABB 兜底，否则无法选中容器图形。
        bool isContainerShape = _context.ActiveCanvas.Selection
            .Any(s => s is DrawingHatch or DrawingGroup);
        bool isOverSelectionBounds = isContainerShape
            ? _selectionHitService.IsPointOverSelectionBounds(point)
            : false;

        bool canDrag = isOverSelectedGeometry || isOverSelectionBoundsOnly || isOverSelectionBounds;
        if (!canDrag)
            return default;

        // 当已经有选中图形且点击在选中区域上时，优先从已选图形中查找命中
        // 避免点击群组内的子图形时误选中整个群组
        IShape? closestShape = null;

        // Hatch 已选时，先检查其 Boundaries（外框图形）是否被点击，实现 Hatch→外框 的切换
        foreach (var selShape in _context.ActiveCanvas.Selection)
        {
            if (selShape is DrawingHatch hatch)
            {
                foreach (var boundary in hatch.Boundaries)
                {
                    if (boundary is DrawObject boundaryObj && boundaryObj.HitTest(point, padding))
                    {
                        closestShape = boundary;
                        break;
                    }
                }
                if (closestShape != null) break;
            }
        }

        // 从已选图形中查找命中（非 Hatch 图形如矩形边框的 HitTest 通过则保持选中）
        if (closestShape == null)
        {
            foreach (var selectedShape in _context.ActiveCanvas.Selection)
            {
                if (selectedShape is DrawObject drawObj && drawObj.HitTest(point, padding))
                {
                    closestShape = selectedShape;
                    break;
                }
            }
        }
        
        // 已选图形均未命中，全局搜索（可能找到 Hatch 填充或其他未选图形）
        if (closestShape == null)
        {
            closestShape = _selectionHitService.FindClosestHitShape(point, padding);
        }
        
        //Debug.WriteLine($"选中图形：{closestShape?.Type}");
        if (closestShape != null && !_context.ActiveCanvas.Selection.Contains(closestShape))
        {
            _context.MarkSelectedDirty();
            _selectionStateService.ClearSelection(clearMoveNodeSelection);
            _selectionStateService.SelectShape(closestShape);
            _context.MarkSelectedDirty();
            notifyMenuEvent();

            return new SelectionMouseDownResult(
                Handled: true,
                NeedRedraw: true,
                Action: SelectionMouseDownAction.StartDraggingSelection);
        }

        // 点击当前已选对象但未切换选中集时，只在“最后选择对象”发生变化时请求重绘。
        bool needRedraw = closestShape != null && _selectionStateService.UpdateAlignmentReference(closestShape);
        if (needRedraw)
            _context.MarkSelectedDirty();

        return new SelectionMouseDownResult(
            Handled: true,
            NeedRedraw: needRedraw,
            Action: SelectionMouseDownAction.PrepareDragSelection);
    }

    private SelectionMouseDownResult HandleUnselectedShapeOrBoxSelect(
        SKPoint point,
        bool isShiftPressed,
        Action clearMoveNodeSelection,
        Action notifyMenuEvent)
    {
        bool needRedraw = false;

        if (!isShiftPressed && _context.ActiveCanvas != null)
        {
            if (_context.ActiveCanvas.SelectedShapeCount > 0)
            {
                _context.MarkSelectedDirty();
                _selectionStateService.ClearSelection(clearMoveNodeSelection);
                needRedraw = true;
            }

            _context.CurMouseDown = point;
            _context.HasMousePosition = true;
        }

        var padding = clickPadding / (_context.ActiveCanvas?.Viewport.Scale ?? 1.0f);
        var closestShape = _selectionHitService.FindClosestHitShape(point, padding);
        if (closestShape != null)
        {
            _selectionStateService.SelectShape(closestShape);
            _context.MarkSelectedDirty();
            notifyMenuEvent();

            return new SelectionMouseDownResult(
                Handled: true,
                NeedRedraw: true,
                Action: SelectionMouseDownAction.PrepareDragSelection);
        }

        return new SelectionMouseDownResult(
            Handled: true,
            NeedRedraw: needRedraw,
            Action: SelectionMouseDownAction.StartBoxSelection);
    }
}
