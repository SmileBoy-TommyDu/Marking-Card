using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Model;
using SkiaSharp;
using System.Linq;

namespace DrSoft.Drawing.Controls.Tools;

internal enum SelectionHoverCursorKind
{
    None,
    ControlPoint,
    Custom
}

internal readonly record struct SelectionHoverResult(
    SelectionHoverCursorKind Kind,
    ControlPointType ControlPointType = ControlPointType.None,
    string? CustomCursorName = null);

/// <summary>
/// 选择工具 MouseMove 阶段的只读判断服务。
/// 负责悬停光标与“准备拖拽 -> 真实拖拽”的提升判定，避免 ToolSelect 内部继续堆叠 move 分支。
/// </summary>
internal sealed class SelectionMouseMoveService
{
    private readonly DocumentContext _context;
    private readonly SelectionControlPointService _selectionControlPointService;
    private readonly PathNodeEditSession _pathNodeEditSession;
    private readonly ShapeDragSession _shapeDragSession;
    private readonly SelectionStateService _selectionStateService;

    public SelectionMouseMoveService(
        DocumentContext context,
        SelectionControlPointService selectionControlPointService,
        PathNodeEditSession pathNodeEditSession,
        ShapeDragSession shapeDragSession,
        SelectionStateService selectionStateService)
    {
        _context = context;
        _selectionControlPointService = selectionControlPointService;
        _pathNodeEditSession = pathNodeEditSession;
        _shapeDragSession = shapeDragSession;
        _selectionStateService = selectionStateService;
    }

    public SelectionHoverResult GetHoverCursor(SKPoint point)
    {
        if (_context.ActiveCanvas == null || _context.IsDrawing)
            return default;

        int selCount = _context.ActiveCanvas.SelectedShapeCount;
        if (selCount <= 0)
            return default;

        ControlPointType controlPointType = ControlPointType.None;
        if (selCount == 1)
        {
            var shape = _context.ActiveCanvas.Selection.First();
            if (shape is DrawObject drawObject && drawObject.Type != ShapeType.Point)
            {
                controlPointType = _selectionControlPointService.GetControlPointAt(drawObject, point);
            }
        }
        else
        {
            controlPointType = _selectionControlPointService.GetControlPointAtForMultipleSelection(
                _context.CalculateMergedBounds(),
                point);
        }

        if (controlPointType != ControlPointType.None)
            return new SelectionHoverResult(SelectionHoverCursorKind.ControlPoint, controlPointType);

        if (selCount != 1)
            return default;

        var selected = _context.ActiveCanvas.Selection.First();
        if (selected is not DrawObject drawObj || !drawObj.IsPathEditing)
            return default;

        bool hasEditableNodes = drawObj is DrawCombination combo
            ? combo.GetPathNodeWorldPositions().Count > 0
            : drawObj.PathNodes?.Count > 0;
        if (!hasEditableNodes)
            return default;

        // 节点模式的 hover 光标与控制点光标分离，由节点编辑会话决定具体语义。
        int nodeIndex = _pathNodeEditSession.GetPathNodeAt(drawObj, point);
        if (nodeIndex >= 0)
        {
            string cursorName = _pathNodeEditSession.IsDeleteNodesMode ? "NodeReduce" : "Node";
            return new SelectionHoverResult(SelectionHoverCursorKind.Custom, CustomCursorName: cursorName);
        }

        if (_pathNodeEditSession.IsAddNodesMode)
            return new SelectionHoverResult(SelectionHoverCursorKind.Custom, CustomCursorName: "NodeAdd");

        if (_pathNodeEditSession.IsDeleteNodesMode)
            return new SelectionHoverResult(SelectionHoverCursorKind.Custom, CustomCursorName: "NodeReduce");

        return default;
    }

    public SelectionHoverResult GetHoverCursorRS(SKPoint point)
    {
        if (_context.ActiveCanvas == null || _context.IsDrawing)
            return default;

        int selCount = _context.ActiveCanvas.SelectedShapeCount;
        if (selCount <= 0)
            return default;

        ControlPointType controlPointType = ControlPointType.None;
        if (selCount == 1)
        {
            var shape = _context.ActiveCanvas.Selection.First();
            if (shape is DrawObject drawObject && drawObject.Type != ShapeType.Point)
            {
                // ThirdSelected 状态下使用 AABB 控制点命中检测
                controlPointType = (_context.SelectState == SelectState.ThirdSelected || _context.SelectState == SelectState.SecondSelected)
                    ? _selectionControlPointService.GetControlPointAtAABB(drawObject, point)
                    : _selectionControlPointService.GetControlPointAt(drawObject, point);
            }
        }
        else
        {
            controlPointType = _selectionControlPointService.GetAllControlPointAtForMultipleSelection(
                _context.CalculateMergedBounds(),
                point);
        }

        if (controlPointType != ControlPointType.None)
            return new SelectionHoverResult(SelectionHoverCursorKind.ControlPoint, controlPointType);

        if (selCount != 1)
            return default;

        var selected = _context.ActiveCanvas.Selection.First();
        if (selected is not DrawObject drawObj || !drawObj.IsPathEditing)
            return default;

        bool hasEditableNodes = drawObj is DrawCombination combo
            ? combo.GetPathNodeWorldPositions().Count > 0
            : drawObj.PathNodes?.Count > 0;
        if (!hasEditableNodes)
            return default;

        // 节点模式的 hover 光标与控制点光标分离，由节点编辑会话决定具体语义。
        int nodeIndex = _pathNodeEditSession.GetPathNodeAt(drawObj, point);
        if (nodeIndex >= 0)
        {
            string cursorName = _pathNodeEditSession.IsDeleteNodesMode ? "NodeReduce" : "Node";
            return new SelectionHoverResult(SelectionHoverCursorKind.Custom, CustomCursorName: cursorName);
        }

        if (_pathNodeEditSession.IsAddNodesMode)
            return new SelectionHoverResult(SelectionHoverCursorKind.Custom, CustomCursorName: "NodeAdd");

        if (_pathNodeEditSession.IsDeleteNodesMode)
            return new SelectionHoverResult(SelectionHoverCursorKind.Custom, CustomCursorName: "NodeReduce");

        return default;
    }

    public bool TryPromotePendingSelectionDrag(SKPoint point, SKPoint dragStartPoint)
    {
        if (_context.ActiveCanvas == null || _context.ActiveCanvas.SelectedShapeCount == 0)
            return false;

        if (_context.ActiveCanvas.Selection.All(it => it.IsLocked))
            return false;

        var dx = point.X - dragStartPoint.X;
        var dy = point.Y - dragStartPoint.Y;
        var distance = System.Math.Sqrt(dx * dx + dy * dy);
        if (distance <= 0.0f)
            return false;

        // 只有真正发生位移后才启动拖拽会话，避免单击选中时错误地产生拖拽预览和命令提交。
        _shapeDragSession.Start(dragStartPoint, _context.CalculateMergedBounds());
        _shapeDragSession.UpdatePreview(point);
        _selectionStateService.SetMoveCursor();
        return true;
    }
}
