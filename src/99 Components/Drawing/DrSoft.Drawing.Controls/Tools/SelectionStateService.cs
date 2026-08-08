using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Model;
using SkiaSharp;
using System;
using Cursors = System.Windows.Input.Cursors;

namespace DrSoft.Drawing.Controls.Tools;

/// <summary>
/// 选择态的轻量协调服务。
/// 负责选中/清空/悬停光标这类状态切换，不承担命中或拖拽几何计算。
/// </summary>
internal sealed class SelectionStateService
{
    private readonly DocumentContext _context;

    public SelectionStateService(DocumentContext context)
    {
        _context = context;
    }

    public bool IsOverSelectedShape { get; private set; }

    public void SelectShape(IShape shape)
    {
        if (_context.ActiveCanvas == null || _context.ActiveCanvas.Selection.Contains(shape))
            return;

        shape.IsSelected = true;
        _context.ReportStatus($"选中: {shape.Name}");
        SetMoveCursor();

        if (_context.ActiveCanvas is DrawingCanvas drawingCanvas)
            drawingCanvas.LastSelectedShape = shape;

        _context.ActiveCanvas.SetSelectedShapes();
    }

    public void ClearSelection(Action? clearMoveNodeSelection = null)
    {
        if (_context.ActiveCanvas == null)
            return;

        _context.ActiveCanvas.ClearSelectedShapes();
        _context.ReportStatus("清除选择");
        clearMoveNodeSelection?.Invoke();
        ResetHoverCursor();
    }

    public bool UpdateAlignmentReference(IShape shape)
    {
        if (_context.ActiveCanvas is not DrawingCanvas drawingCanvas)
            return false;

        // 多选场景下，LastSelectedShape 既影响视觉参考指示，也影响后续对齐/布尔等语义。
        if (drawingCanvas.LastSelectedShape == shape)
            return false;

        drawingCanvas.LastSelectedShape = shape;
        return _context.ActiveCanvas.SelectedShapeCount > 1;
    }

    public void UpdateHoverState(SKPoint point, SelectionHitService hitService)
    {
        var selectedShapes = _context.ActiveCanvas?.Selection;
        if (selectedShapes == null || selectedShapes.Count == 0)
        {
            ResetHoverCursor();
            return;
        }

        bool isOverShape = hitService.IsPointOverSelectionBounds(point);
        bool isOverSelectionBoundsBorder = hitService.IsPointOverSelectionBoundsBorder(point);
        bool shouldUseMoveCursor = _context.SelectState == SelectState.FirstSelected
            && isOverSelectionBoundsBorder;
        if (isOverShape != IsOverSelectedShape)
        {
            IsOverSelectedShape = isOverShape;
            if (isOverShape)
            {
                // 多选时区分“边线热区”和“框内主体”：
                // 边线保持移动语义，框内主体保持选择光标，避免空白区域看起来像视口抓手。
                _context.SetCursor(shouldUseMoveCursor
                    ? Cursors.SizeAll
                    : CanvasCursorFactory.GetCursor("pointer", Cursors.Arrow));
                _context.ReportStatus($"鼠标在选中图形上 - 可拖拽移动 ({selectedShapes.Count}个)");
            }
            else
            {
                _context.SetCursor(CanvasCursorFactory.GetCursor("pointer", Cursors.Arrow));
                _context.ReportStatus("就绪");
            }

            return;
        }

        if (isOverShape && IsOverSelectedShape)
            _context.SetCursor(shouldUseMoveCursor
                ? Cursors.SizeAll
                : CanvasCursorFactory.GetCursor("pointer", Cursors.Arrow));
    }

    public void SetMoveCursor()
    {
        _context.SetCursor(Cursors.SizeAll);
        IsOverSelectedShape = true;
    }

    private void ResetHoverCursor()
    {
        if (!IsOverSelectedShape)
            return;

        _context.SetCursor(CanvasCursorFactory.GetCursor("pointer", Cursors.Arrow));
        IsOverSelectedShape = false;
    }
}
