using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Models;
using SkiaSharp;
using System;
using System.Windows.Input;
using Cursor = System.Windows.Input.Cursor;

namespace DrSoft.Drawing.Controls.Tools;

internal sealed class StickyControlPointSession : IToolSelectSession
{
    private readonly DocumentContext _context;
    private readonly Action<Type, ControlPointType, SKPoint> _startControlPointDrag;

    private ControlPointType _stickyControlPointType = ControlPointType.None;
    private SKPoint _stickyControlPointMousePoint = SKPoint.Empty;
    private Type? _stickySessionType;

    public StickyControlPointSession(
        DocumentContext context,
        Action<Type, ControlPointType, SKPoint> startControlPointDrag)
    {
        _context = context;
        _startControlPointDrag = startControlPointDrag;
    }

    public string Name => "StickyControlPoint";

    public bool IsActive => false;

    public Cursor? SuggestedCursor => null;

    public ControlPointType? CompletedControlPoint => null;

    public void Arm(ControlPointType controlPointType, SKPoint point, Type sessionType)
    {
        if (controlPointType == ControlPointType.None)
        {
            Clear();
            return;
        }

        _stickyControlPointType = controlPointType;
        _stickyControlPointMousePoint = point;
        _stickySessionType = sessionType;
    }

    public void Clear()
    {
        _stickyControlPointType = ControlPointType.None;
        _stickyControlPointMousePoint = SKPoint.Empty;
        _stickySessionType = null;
    }

    public bool TryMouseDown(SKPoint point, out string message)
    {
        bool hasStickyControlPoint = _stickyControlPointType != ControlPointType.None;
        if (!hasStickyControlPoint)
        {
            message = "没有可复用的粘滞控制点";
            return false;
        }

        bool hasSelection = _context.ActiveCanvas?.SelectedShapeCount > 0;
        if (!hasSelection)
        {
            Clear();
            message = "选区已丢失，清除粘滞控制点";
            return false;
        }

        bool isWithinStickyControlPoint = IsWithinStickyControlPoint(point);
        if (!isWithinStickyControlPoint)
        {
            Clear();
            message = "鼠标离开粘滞控制点范围，清除粘滞状态";
            return false;
        }

        Type? stickySessionType = _stickySessionType;
        if (stickySessionType == null)
        {
            Clear();
            message = "缺少粘滞会话类型，清除粘滞状态";
            return false;
        }

        _startControlPointDrag(stickySessionType, _stickyControlPointType, point);
        message = "命中粘滞控制点，重启上一次控制点会话";
        return true;
    }

    public bool TryMouseMove(SKPoint point, out string message)
    {
        bool hasStickyControlPoint = _stickyControlPointType != ControlPointType.None;
        if (!hasStickyControlPoint)
        {
            message = "没有可复用的粘滞控制点";
            return false;
        }

        bool isWithinStickyControlPoint = IsWithinStickyControlPoint(point);
        if (isWithinStickyControlPoint)
        {
            message = "鼠标仍在粘滞控制点范围内";
            return false;
        }

        Clear();
        message = "鼠标移出粘滞控制点范围，清除粘滞状态";
        return false;
    }

    public bool TryMouseUp(SKPoint point, out string message)
    {
        message = "粘滞控制点不处理抬起";
        return false;
    }

    public bool TryRightMouseDown(SKPoint point, out string message)
    {
        message = "粘滞控制点不处理右键";
        return false;
    }

    public void Cancel()
    {
        Clear();
    }

    private bool IsWithinStickyControlPoint(SKPoint point)
    {
        float scale = (float)(_context.ActiveCanvas?.Viewport.Scale ?? 1.0);
        if (scale <= 0f)
        {
            scale = 1f;
        }

        float tolerance = DrawObject.rectH / scale;
        bool isWithinX = Math.Abs(point.X - _stickyControlPointMousePoint.X) <= tolerance;
        bool isWithinY = Math.Abs(point.Y - _stickyControlPointMousePoint.Y) <= tolerance;
        bool isWithinStickyControlPoint = isWithinX && isWithinY;
        return isWithinStickyControlPoint;
    }
}
