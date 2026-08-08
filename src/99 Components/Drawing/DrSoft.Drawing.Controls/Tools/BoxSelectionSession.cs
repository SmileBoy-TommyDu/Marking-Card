using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Model;
using DrSoft.Drawing.Utility;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Cursor = System.Windows.Input.Cursor;

namespace DrSoft.Drawing.Controls.Tools;

internal readonly record struct BoxSelectionResult(bool Handled, bool NeedRedraw, int SelectedCount);

/// <summary>
/// 管理一次框选手势的短生命周期会话。
/// 负责框选预览脏区、选择矩形语义，以及提交后的选中集更新。
/// </summary>
internal sealed class BoxSelectionSession : IToolSelectSession
{
    private readonly DocumentContext _context;
    private SKRect? _lastPreviewDirty;
    private bool _lastMouseUpNeedRedraw;

    public BoxSelectionSession(DocumentContext context)
    {
        _context = context;
    }

    public string Name => "BoxSelection";

    public bool IsActive => _context.BoxSelect.IsActive;

    public Cursor? SuggestedCursor => null;

    public ControlPointType? CompletedControlPoint => null;

    internal bool LastMouseUpNeedRedraw => _lastMouseUpNeedRedraw;

    public bool TryMouseDown(SKPoint point, out string message)
    {
        message = "框选会话不处理按下";
        return false;
    }

    public bool TryMouseMove(SKPoint point, out string message)
    {
        if (!IsActive)
        {
            message = "框选未激活";
            return false;
        }

        Update(point);
        message = "更新框选预览";
        return true;
    }

    public bool TryMouseUp(SKPoint point, out string message)
    {
        _lastMouseUpNeedRedraw = false;

        if (!IsActive)
        {
            message = "框选未激活";
            return false;
        }

        BoxSelectionResult result = Complete(point);
        _lastMouseUpNeedRedraw = result.NeedRedraw;
        message = result.NeedRedraw
            ? "完成框选并更新选中集"
            : "框选位移过小，按点击处理";
        return result.Handled;
    }

    public bool TryRightMouseDown(SKPoint point, out string message)
    {
        message = "框选会话不处理右键";
        return false;
    }

    /// <summary>
    /// 进入框选会话，并初始化起点/当前点为同一点。
    /// </summary>
    public void Start(SKPoint point)
    {
        _context.BoxSelect.IsActive = true;
        _context.BoxSelect.Start = point;
        _context.BoxSelect.Current = point;
        _lastPreviewDirty = null;
    }

    public void Update(SKPoint point)
    {
        _context.BoxSelect.Current = point;
        MarkPreviewDirty(BuildPreviewDirtyRect());

        var modeText = IsForwardSelection() ? "完全包含(严格)" : "相交包含(宽松)";
        _context.ReportStatus($"框选 ({modeText}): ({point.X:F1}, {point.Y:F1})");
    }

    public void UpdateFromSelectionStart(SKPoint selectionStart, SKPoint point)
    {
        _context.BoxSelect.IsActive = true;
        _context.BoxSelect.Start = selectionStart;
        _context.BoxSelect.Current = point;
        MarkPreviewDirty(BuildPreviewDirtyRect());
    }

    public BoxSelectionResult Complete(SKPoint point)
    {
        if (!_context.BoxSelect.IsActive || _context.ActiveCanvas == null)
            return new BoxSelectionResult(false, false, 0);

        var screenPoint = _context.ActiveCanvas.Viewport.WorldToScreen(point);
        var boxStartPoint = _context.ActiveCanvas.Viewport.WorldToScreen(_context.BoxSelect.Start);


        float dx = screenPoint.X - boxStartPoint.X;
        float dy = screenPoint.Y - boxStartPoint.Y;
        float distance = dx * dx + dy * dy;

        // 零位移或极小位移视为点击，不进入真正的框选提交。
        float worldThreshold = 1.0f;
        if (distance.Lte(worldThreshold))
        {
            _context.BoxSelect.IsActive = false;
            _lastPreviewDirty = null;
            return new BoxSelectionResult(true, false, 0);
        }


        //float dx = point.X - _context.BoxSelect.Start.X;
        //float dy = point.Y - _context.BoxSelect.Start.Y;
        //float distance = dx * dx + dy * dy;

        //// 零位移或极小位移视为点击，不进入真正的框选提交。
        //float worldThreshold = 3.0f / (float)_context.ActiveCanvas.Viewport.Scale;
        //if (distance.Lte(worldThreshold * worldThreshold))
        //{
        //    _context.BoxSelect.IsActive = false;
        //    _lastPreviewDirty = null;
        //    return new BoxSelectionResult(true, false, 0);
        //}

        var selectionRect = GetSelectionRect();
        int selectedCount = SelectShapesInRect(selectionRect, point);
        _context.BoxSelect.IsActive = false;
        _lastPreviewDirty = null;
        _context.ReportStatus($"框选完成: 选中 {selectedCount} 个图形");
        return new BoxSelectionResult(true, true, selectedCount);
    }

    public void Cancel()
    {
        _context.BoxSelect.Reset();
        _lastPreviewDirty = null;
        _lastMouseUpNeedRedraw = false;
    }

    private void MarkPreviewDirty(SKRect current)
    {
        if (_lastPreviewDirty.HasValue)
            _context.MarkDirty(_lastPreviewDirty.Value);

        _context.MarkDirty(current);
        _lastPreviewDirty = current;
    }

    private SKRect BuildPreviewDirtyRect()
    {
        const float pad = 4f;
        float x1 = Math.Min(_context.BoxSelect.Start.X, _context.BoxSelect.Current.X) - pad;
        float y1 = Math.Min(_context.BoxSelect.Start.Y, _context.BoxSelect.Current.Y) - pad;
        float x2 = Math.Max(_context.BoxSelect.Start.X, _context.BoxSelect.Current.X) + pad;
        float y2 = Math.Max(_context.BoxSelect.Start.Y, _context.BoxSelect.Current.Y) + pad;
        return new SKRect(x1, y1, x2, y2);
    }

    private int SelectShapesInRect(SKRect rect, SKPoint mouseEndPoint)
    {
        if (_context.ActiveCanvas == null)
            return 0;

        bool isForwardSelection = IsForwardSelection();
        var skSelRect = rect;
        var newlySelectedShapes = new List<IShape>();

        foreach (var layer in ((DrawingCanvas)_context.ActiveCanvas).Layers.Where(l => l.IsVisible && !l.IsLocked))
        {
            foreach (var shape in layer.Shapes)
            {
                if (shape is not DrawObject drawObject)
                    continue;

                var shapeBounds = drawObject.GetAABB();
                if (shapeBounds.IsEmpty)
                    continue;

                // 先做基于包围圆的廉价预过滤，避免对明显不相交图形继续走命中/相交计算。
                float cx = (shapeBounds.Left + shapeBounds.Right) / 2f;
                float cy = (shapeBounds.Top + shapeBounds.Bottom) / 2f;
                float halfDiag = MathF.Sqrt(shapeBounds.Width * shapeBounds.Width + shapeBounds.Height * shapeBounds.Height) / 2f;
                float nearestX = MathF.Max(skSelRect.Left, MathF.Min(cx, skSelRect.Right));
                float nearestY = MathF.Max(skSelRect.Top, MathF.Min(cy, skSelRect.Bottom));
                float distX = cx - nearestX;
                float distY = cy - nearestY;
                if (distX * distX + distY * distY > halfDiag * halfDiag)
                    continue;

                // 正向框选要求完全包含，反向框选允许相交即选中。
                bool shouldSelect = isForwardSelection
                    ? IsShapeFullyInsideRect(drawObject, rect)
                    : drawObject.IntersectsWith(rect);

                if (shouldSelect && !_context.ActiveCanvas.Selection.Contains(shape))
                {
                    shape.IsSelected = true;
                    newlySelectedShapes.Add(shape);
                }
            }
        }

        //if (newlySelectedShapes.Count > 0)
        //{
        //    // 记录“最接近鼠标抬起位置”的图形作为 LastSelectedShape，
        //    // 以保留后续对齐/布尔流程对“最后所选对象”的现有语义。
        //    IShape? lastSelected = null;
        //    float minDist = float.MaxValue;

        //    foreach (var shape in newlySelectedShapes)
        //    {
        //        if (shape is not DrawObject drawObj)
        //            continue;

        //        var bbox = drawObj.GetBoundingBox();
        //        float closestX = MathF.Max(bbox.Left, MathF.Min(mouseEndPoint.X, bbox.Right));
        //        float closestY = MathF.Max(bbox.Top, MathF.Min(mouseEndPoint.Y, bbox.Bottom));
        //        float dx = mouseEndPoint.X - closestX;
        //        float dy = mouseEndPoint.Y - closestY;
        //        float dist = dx * dx + dy * dy;
        //        if (dist < minDist)
        //        {
        //            minDist = dist;
        //            lastSelected = shape;
        //        }
        //    }

        //    if (lastSelected != null)
        //        ((DrawingCanvas)_context.ActiveCanvas).LastSelectedShape = lastSelected;
        //}

        _context.ActiveCanvas.SetSelectedShapes();
        return _context.ActiveCanvas.SelectedShapeCount;
    }

    private bool IsForwardSelection()
    {
        return _context.BoxSelect.Current.X > _context.BoxSelect.Start.X;
    }

    private static bool IsShapeFullyInsideRect(DrawObject shape, SKRect rect)
    {
        try
        {
            var bounds = shape.GetAABB();
            double rectLeft = rect.Left;
            double rectRight = rect.Left + rect.Width;
            double rectTop = rect.Top;
            double rectBottom = rect.Top + rect.Height;

            double shapeLeft = bounds.Left;
            double shapeRight = bounds.Right;
            double shapeTop = bounds.Top;
            double shapeBottom = bounds.Bottom;

            const double tolerance = 0.5;
            bool leftInside = shapeLeft >= rectLeft - tolerance;
            bool rightInside = shapeRight <= rectRight + tolerance;
            bool topInside = shapeTop >= rectTop - tolerance;
            bool bottomInside = shapeBottom <= rectBottom + tolerance;

            return leftInside && rightInside && topInside && bottomInside;
        }
        catch
        {
            return false;
        }
    }

    private SKRect GetSelectionRect()
    {
        var start = _context.BoxSelect.Start;
        var current = _context.BoxSelect.Current;
        var x1 = Math.Min(start.X, current.X);
        var y1 = Math.Min(start.Y, current.Y);
        var x2 = Math.Max(start.X, current.X);
        var y2 = Math.Max(start.Y, current.Y);
        return SKRect.Create(x1, y1, x2 - x1, y2 - y1);
    }
}
