using System.Diagnostics;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Model;
using SkiaSharp;
using Cursor = System.Windows.Input.Cursor;
using Cursors = System.Windows.Input.Cursors;

namespace DrSoft.Drawing.Controls.Tools;

/// <summary>
/// 旋转中心拖拽会话。
/// 处理鼠标按下、拖拽、释放时对图形旋转中心的移动操作。
/// </summary>
internal sealed class RotationCenterDragSession : IToolSelectSession
{
    private readonly DocumentContext _context;
    private readonly double _hitTestRadius;

    private bool _isDragging;
    private SKPoint _originalRotationCenter;

    public RotationCenterDragSession(DocumentContext context, double hitTestRadius)
    {
        _context = context;
        _hitTestRadius = hitTestRadius;
    }

    public string Name => "RotationCenterDrag";

    public bool IsActive => IsDragging;

    public Cursor? SuggestedCursor => Cursors.Hand;

    public ControlPointType? CompletedControlPoint => null;

    public bool IsDragging => _isDragging;

    public bool TryMouseDown(SKPoint point, out string message)
    {
        bool isThirdSelected = _context.SelectState == SelectState.ThirdSelected;
        if (!isThirdSelected)
        {
            message = "当前不是第三态，不能拖拽旋转中心";
            return false;
        }

        bool isHitRotationCenter = IsHitRotationCenter(point);
        if (!isHitRotationCenter)
        {
            message = "未命中旋转中心";
            return false;
        }

        Start();
        message = "开始拖拽旋转中心";
        return true;
    }

    public bool TryMouseMove(SKPoint point, out string message)
    {
        if (IsDragging)
        {
            Update(point);
            message = "更新旋转中心位置";
            return true;
        }

        bool isThirdSelected = _context.SelectState == SelectState.ThirdSelected;
        if (!isThirdSelected)
        {
            message = "当前不是第三态，忽略旋转中心悬停";
            return false;
        }

        bool isHitRotationCenter = IsHitRotationCenter(point);
        message = isHitRotationCenter
            ? "命中旋转中心"
            : "未命中旋转中心";
        return isHitRotationCenter;
    }

    public bool TryMouseUp(SKPoint point, out string message)
    {
        if (!IsDragging)
        {
            message = "旋转中心未处于拖拽中";
            return false;
        }

        Complete();
        message = "完成旋转中心拖拽";
        return true;
    }

    public bool TryRightMouseDown(SKPoint point, out string message)
    {
        message = "旋转中心会话不处理右键";
        return false;
    }

    /// <summary>
    /// 检测鼠标是否在旋转中心命中范围内
    /// </summary>
    public bool IsHitRotationCenter(SKPoint worldPoint)
    {
        int selCount = _context.ActiveCanvas!.SelectedShapeCount;
        if (selCount <= 0)
            return false;

        var scale = (float)(_context.ActiveCanvas?.Viewport.Scale ?? 1.0);
        var halfSize = DrawObject.rectH / scale;

        if (selCount == 1)
        {
            var shape = _context.ActiveCanvas.Selection.First();
            if (shape is not DrawObject drawObject)
                return false;

            var rc = drawObject.RotationCenter;
            float dx = worldPoint.X - rc.X;
            float dy = worldPoint.Y - rc.Y;
            return (dx * dx + dy * dy) <= (_hitTestRadius / scale) * (_hitTestRadius / scale);
        }
        else
        {
            var mergedBounds = _context.CachedSelectionBounds ?? _context.CalculateMergedBounds();
            if (mergedBounds.IsEmpty) return false;

            var geometry = SelectionGeometryBuilder.BuildForMergedBounds(mergedBounds, (float)(_context.ActiveCanvas?.Viewport.Scale ?? 1.0));
            if (geometry.Corners.Length == 0 || geometry.ControlPoints.Length == 0)
                return false;

            //var rc = geometry.Center;
            var center = _context.MergedRotationCenter;
            var rc = float.IsPositiveInfinity(center.X) || float.IsPositiveInfinity(center.Y) ? geometry.Center : center;

            float dx = worldPoint.X - rc.X;
            float dy = worldPoint.Y - rc.Y;
            return (dx * dx + dy * dy) <= (_hitTestRadius / scale) * (_hitTestRadius / scale);
        }
    }

    /// <summary>
    /// 开始旋转中心拖拽
    /// </summary>
    public void Start()
    {
        int selCount = _context.ActiveCanvas!.SelectedShapeCount;
        if (selCount <= 0)
            return;
        if (selCount == 1)
        {
            var shape = _context.ActiveCanvas?.Selection.First();
            if (shape is not DrawObject drawObject)
                return;

            _isDragging = true;
            _originalRotationCenter = drawObject.RotationCenter;
        }
        else
        {
            var mergedBounds = _context.CachedSelectionBounds ?? _context.CalculateMergedBounds();
            if (mergedBounds.IsEmpty) return;

            var geometry = SelectionGeometryBuilder.BuildForMergedBounds(mergedBounds, (float)(_context.ActiveCanvas?.Viewport.Scale ?? 1.0));
            if (geometry.Corners.Length == 0 || geometry.ControlPoints.Length == 0)
                return;

            _isDragging = true;
            _originalRotationCenter = geometry.Center;
        }
    }

    /// <summary>
    /// 更新旋转中心位置（拖拽中）
    /// </summary>
    public void Update(SKPoint worldPoint)
    {
        if (!_isDragging || _context.ActiveCanvas?.SelectedShapeCount <= 0)
            return;

        var shapes = _context.ActiveCanvas?.Selection.OfType<DrSoft.Drawing.Controls.DrawShapes.DrawObject>().ToList();
        if (shapes == null || shapes.Count == 0) return;
        if (shapes.Count == 1)
        {
            var shape = shapes.FirstOrDefault();
            if (shape is not DrawObject drawObject)
                return;

            // 实时更新旋转中心位置
            drawObject.SetRotationCenter(worldPoint);
            return;
        }

        // 实时更新旋转中心位置
        _context.MergedRotationCenter = worldPoint;
    }

    /// <summary>
    /// 完成拖拽，确认新的旋转中心
    /// </summary>
    public void Complete()
    {
        if (!_isDragging)
            return;

        _isDragging = false;

        // 触发选择状态更新，通知其他组件旋转中心已改变
        _context.MarkSelectedDirty();
    }

    /// <summary>
    /// 取消拖拽，恢复原始旋转中心
    /// </summary>
    public void Cancel()
    {
        if (!_isDragging || _context.ActiveCanvas?.SelectedShapeCount <= 0)
            return;

        if (_context.ActiveCanvas?.SelectedShapeCount == 1)
        {
            var shape = _context.ActiveCanvas.Selection.First();
            if (shape is DrawObject drawObject)
            {
                drawObject.SetRotationCenter(_originalRotationCenter);
            }
        }
        else
        {
            _context.MergedRotationCenter = _originalRotationCenter;
        }

        _isDragging = false;
    }
}
