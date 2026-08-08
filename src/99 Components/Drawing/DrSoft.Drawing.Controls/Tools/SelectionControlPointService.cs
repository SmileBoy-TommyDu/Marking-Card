using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Controls.Selection;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using SkiaSharp;
using Cursor = System.Windows.Input.Cursor;
using Cursors = System.Windows.Input.Cursors;

namespace DrSoft.Drawing.Controls.Tools;

/// <summary>
/// 选择框控制点的几何服务。
/// 负责单选/多选控制点的位置推导与命中判断，让 ToolSelect 不再直接维护控制点几何细节。
/// </summary>
internal sealed class SelectionControlPointService
{
    private readonly DocumentContext _context;

    /// <summary>
    /// 创建一个选择控制点服务，复用当前文档上下文中的视口缩放和选区几何规则。
    /// </summary>
    public SelectionControlPointService(DocumentContext context)
    {
        _context = context;
    }

    /// <summary>
    /// 命中检测单个图形的控制点。
    /// </summary>
    /// <param name="drawObject">当前单选图形。</param>
    /// <param name="point">鼠标所在的世界坐标。</param>
    /// <returns>命中的控制点类型；未命中时返回 <see cref="ControlPointType.None"/>。</returns>
    public ControlPointType GetControlPointAt(DrawObject drawObject, SKPoint point)
    {
        if (drawObject.Type == ShapeType.Hatch)
            return ControlPointType.None;

        var scale = _context.ActiveCanvas?.Viewport.Scale ?? 1.0;
        var halfSize = DrawObject.rectH / (float)scale;
        var controlPoints = GetSelectionControlPoints(drawObject);

        foreach (var cp in controlPoints)
        {
            if (Math.Abs(point.X - cp.Point.X) <= halfSize && Math.Abs(point.Y - cp.Point.Y) <= halfSize)
                return cp.Type;
        }

        return ControlPointType.None;
    }

    /// <summary>
    /// SecondSelected / ThirdSelected 模式下基于轴对齐外接矩形（AABB）的控制点命中检测。
    /// AABB 使用与 SelectionRenderer.GetSelectedShapesAABB 完全一致的计算方式（GetBoundingBox + controlPointOffset），
    /// 确保命中检测位置与渲染位置保持一致。
    /// </summary>
    public ControlPointType GetControlPointAtAABB(DrawObject drawObject, SKPoint point)
    {
        if (drawObject.Type == ShapeType.Hatch)
            return ControlPointType.None;

        var scale = (float)(_context.ActiveCanvas?.Viewport.Scale ?? 1.0);
        var halfSize = DrawObject.rectH / scale;

        // ── 与 SelectionRenderer 完全一致的 AABB 计算 ──
        var geometry = SelectionGeometryBuilder.BuildForSingleAABBSelection(drawObject);
        if (geometry.Bounds.IsEmpty)
            return ControlPointType.None;

        var aabbPoints = new (SKPoint Point, ControlPointType Type)[]
{
            (geometry.ControlPoints[0], ControlPointType.TopLeft),
            (geometry.ControlPoints[1], ControlPointType.TopRight),
            (geometry.ControlPoints[2], ControlPointType.BottomRight),
            (geometry.ControlPoints[3], ControlPointType.BottomLeft),
            (geometry.ControlPoints[4], ControlPointType.TopCenter),
            (geometry.ControlPoints[5], ControlPointType.MiddleRight),
            (geometry.ControlPoints[6], ControlPointType.BottomCenter),
            (geometry.ControlPoints[7], ControlPointType.MiddleLeft)
};

        var controlPoints = new List<(SKPoint Point, ControlPointType Type)>();
        if (_context.SelectState == SelectState.ThirdSelected)
        {
            var constraints = SelectionSkewConstraintResolver.ResolveForShape(drawObject);
            // 选择框展示只消费约束，不再关心触发器是圆弧还是其他未来规则。
            var hideEdgeMidpoints = constraints.HasFlag(SelectionResizeConstraint.HideEdgeMidpointHandles);
            controlPoints = GetVisibleSkewControlPoints(aabbPoints, hideEdgeMidpoints).ToList();
        }
        else
        {
            var constraints = SelectionResizeConstraintResolver.ResolveForShape(drawObject);
            // 选择框展示只消费约束，不再关心触发器是圆弧还是其他未来规则。
            var hideEdgeMidpoints = constraints.HasFlag(SelectionResizeConstraint.HideEdgeMidpointHandles);
            controlPoints = GetVisibleControlPoints(aabbPoints, hideEdgeMidpoints).ToList();
        }

        foreach (var cp in controlPoints)
        {
            if (Math.Abs(point.X - cp.Point.X) <= halfSize * 1.2f && Math.Abs(point.Y - cp.Point.Y) <= halfSize * 1.2f)
                return cp.Type;
        }

        return ControlPointType.None;
    }

    private static (SKPoint Point, ControlPointType Type)[] GetVisibleControlPoints((SKPoint Point, ControlPointType Type)[] controlPoints, bool hideEdgeMidpoints)
    {
        if (!hideEdgeMidpoints || controlPoints.Length < 8)
        {
            return controlPoints;
        }

        return controlPoints.Where(o => o.Type == ControlPointType.TopLeft || o.Type == ControlPointType.TopRight || o.Type == ControlPointType.BottomLeft || o.Type == ControlPointType.BottomRight).ToArray();
    }
    private static (SKPoint Point, ControlPointType Type)[] GetVisibleSkewControlPoints((SKPoint Point, ControlPointType Type)[] controlPoints, bool hideEdgeMidpoints)
    {
        if (!hideEdgeMidpoints || controlPoints.Length < 8)
        {
            return controlPoints;
        }

        return controlPoints.Where(o => o.Type == ControlPointType.TopLeft || o.Type == ControlPointType.TopRight || o.Type == ControlPointType.BottomLeft || o.Type == ControlPointType.BottomRight).ToArray();
    }

    /// <summary>
    /// 命中检测多选合并边界框的控制点。
    /// </summary>
    /// <param name="mergedBounds">当前多选对象的合并世界包围盒。</param>
    /// <param name="point">鼠标所在的世界坐标。</param>
    /// <returns>命中的控制点类型；未命中时返回 <see cref="ControlPointType.None"/>。</returns>
    public ControlPointType GetControlPointAtForMultipleSelection(SKRect mergedBounds, SKPoint point)
    {
        var geometry = SelectionGeometryBuilder.BuildForMergedBounds(mergedBounds, GetScale());
        if (geometry.ControlPoints.Length == 0)
            return ControlPointType.None;

        var constraints = ResolveSelectionConstraints();
        // 多选命中和多选绘制必须共享同一条“隐藏边中点”规则，
        // 否则会出现句柄没画出来却还能点到的交互分叉。
        var allowEdgeMidpoints = !constraints.HasFlag(SelectionResizeConstraint.HideEdgeMidpointHandles);
        var frame = SelectionFrameFactory.CreateFromGeometry(
            geometry,
            SelectionFrameKind.AxisAlignedBoundingBox,
            hideEdgeMidpoints: !allowEdgeMidpoints);
        return HitTestControlPoint(frame.ResizeHandles, point);
    }

    public ControlPointType GetAllControlPointAtForMultipleSelection(SKRect mergedBounds, SKPoint point)
    {
        var geometry = SelectionGeometryBuilder.BuildForMergedBounds(mergedBounds, GetScale());
        if (geometry.ControlPoints.Length == 0)
            return ControlPointType.None;

        var constraints = ResolveSelectionConstraints();
        // 多选命中和多选绘制必须共享同一条“隐藏边中点”规则，
        // 否则会出现句柄没画出来却还能点到的交互分叉。
        var allowEdgeMidpoints = !constraints.HasFlag(SelectionResizeConstraint.HideEdgeMidpointHandles);
        var frame = SelectionFrameFactory.CreateFromGeometry(
            geometry,
            SelectionFrameKind.AxisAlignedBoundingBox,
            hideEdgeMidpoints: !allowEdgeMidpoints);
        return HitTestControlPoint(frame.ResizeHandles, point);
    }

    /// <summary>
    /// 获取单个图形指定控制点的世界坐标。
    /// </summary>
    /// <param name="drawObject">当前单选图形。</param>
    /// <param name="controlPointType">目标控制点类型。</param>
    /// <returns>控制点的世界坐标；不存在时返回 <see cref="SKPoint.Empty"/>。</returns>
    public SKPoint GetControlPointWorldPosition(DrawObject drawObject, ControlPointType controlPointType)
    {
        var controlPoints = GetSelectionControlPoints(drawObject);
        foreach (var cp in controlPoints)
        {
            if (cp.Type == controlPointType)
                return cp.Point;
        }

        return SKPoint.Empty;
    }

    /// <summary>
    /// 生成单个图形的所有控制点，并附带对应的控制点语义。
    /// </summary>
    /// <param name="drawObject">当前单选图形。</param>
    /// <returns>控制点数组，顺序与 <see cref="ControlPointType"/> 语义一致。</returns>
    public (SKPoint Point, ControlPointType Type)[] GetSelectionControlPoints(DrawObject drawObject)
    {
        var geometry = SelectionGeometryBuilder.BuildForSinglePreviewOBBSelection(drawObject);
        var constraints = SelectionResizeConstraintResolver.ResolveForShape(drawObject);
        // 单选句柄暴露直接受选区约束裁剪，避免 UI 再各自做一遍类型判断。
        var allowEdgeMidpoints = !constraints.HasFlag(SelectionResizeConstraint.HideEdgeMidpointHandles);
        var frame = SelectionFrameFactory.CreateFromGeometry(
            geometry,
            SelectionFrameKind.OrientedBoundingBox,
            hideEdgeMidpoints: !allowEdgeMidpoints);
        return frame.ResizeHandles.Select(handle => (handle.Point, handle.Type)).ToArray();
    }

    /// <summary>
    /// 为单个图形的指定控制点选择鼠标光标。
    /// </summary>
    /// <param name="drawObject">当前单选图形。</param>
    /// <param name="controlPointType">命中的控制点类型。</param>
    /// <returns>与当前图形姿态匹配的缩放光标。</returns>
    public Cursor GetCursorForControlPoint(DrawObject drawObject, ControlPointType controlPointType)
    {

        if (UsesAxisAlignedCursorMapping(drawObject))
        {
            if (drawObject is DrawText)
            {
                Cursor textCursor = controlPointType switch
                {
                    ControlPointType.TopLeft or ControlPointType.BottomRight => Cursors.SizeNESW,
                    ControlPointType.TopRight or ControlPointType.BottomLeft => Cursors.SizeNWSE,
                    ControlPointType.TopCenter or ControlPointType.BottomCenter => Cursors.SizeNS,
                    ControlPointType.MiddleLeft or ControlPointType.MiddleRight => Cursors.SizeWE,
                    _ => Cursors.Arrow
                };
                return textCursor;
            }

            return GetAxisAlignedCursor(controlPointType);
        }

        var typed = GetSelectionControlPoints(drawObject);
        return GetCursorForTypedControlPoints(typed, controlPointType);
    }

    /// <summary>
    /// 为多选合并边界框的指定控制点选择鼠标光标。
    /// </summary>
    /// <param name="mergedBounds">当前多选对象的合并世界包围盒。</param>
    /// <param name="controlPointType">命中的控制点类型。</param>
    /// <returns>与控制点语义对应的标准缩放光标。</returns>
    public Cursor GetCursorForMergedBounds(SKRect mergedBounds, ControlPointType controlPointType)
    {
        return GetAxisAlignedCursor(controlPointType);
    }

    /// <summary>
    /// 读取当前视口缩放，保证控制点命中范围随缩放一致换算到世界坐标。
    /// </summary>
    private float GetScale() => (float)(_context.ActiveCanvas?.Viewport.Scale ?? 1.0);

    /// <summary>
    /// 在一组世界坐标控制点中执行统一的命中检测。
    /// </summary>
    private ControlPointType HitTestControlPoint(IReadOnlyList<SelectionTypedHandle> controlPoints, SKPoint point)
    {
        // 先过滤掉当前规则不允许暴露的句柄，再做统一命中计算。
        var halfSize = DrawObject.rectH / GetScale();
        foreach (var cp in controlPoints)
        {
            if (Math.Abs(point.X - cp.Point.X) <= halfSize && Math.Abs(point.Y - cp.Point.Y) <= halfSize)
                return cp.Type;
        }

        return ControlPointType.None;
    }

    /// <summary>
    /// 读取当前活动选区的缩放约束。
    /// 控制点命中与控制点暴露必须共享同一份约束，避免“看得见但点不到”或反之。
    /// </summary>
    private SelectionResizeConstraint ResolveSelectionConstraints()
    {
        var selectedShapes = _context.ActiveCanvas?.Selection;

        if (_context.SelectState == SelectState.ThirdSelected)
        {
            return SelectionSkewConstraintResolver.ResolveForSelection(selectedShapes);
        }

        var constraints = SelectionResizeConstraintResolver.ResolveForSelection(selectedShapes);
        return constraints;
    }

    /// <summary>
    /// 按控制点连线方向为旋转/镜像后的图形选择更贴近视觉方向的光标。
    /// </summary>
    private static Cursor GetCursorForTypedControlPoints(
        (SKPoint Point, ControlPointType Type)[] controlPoints,
        ControlPointType controlPointType)
    {
        var current = FindPoint(controlPoints, controlPointType);
        if (current == null)
            return Cursors.Arrow;

        var opposite = controlPointType switch
        {
            ControlPointType.TopLeft => FindPoint(controlPoints, ControlPointType.BottomRight),
            ControlPointType.TopRight => FindPoint(controlPoints, ControlPointType.BottomLeft),
            ControlPointType.BottomLeft => FindPoint(controlPoints, ControlPointType.TopRight),
            ControlPointType.BottomRight => FindPoint(controlPoints, ControlPointType.TopLeft),
            ControlPointType.TopCenter => FindPoint(controlPoints, ControlPointType.BottomCenter),
            ControlPointType.BottomCenter => FindPoint(controlPoints, ControlPointType.TopCenter),
            ControlPointType.MiddleLeft => FindPoint(controlPoints, ControlPointType.MiddleRight),
            ControlPointType.MiddleRight => FindPoint(controlPoints, ControlPointType.MiddleLeft),
            _ => null
        };
        if (opposite == null)
            return Cursors.Arrow;

        // 对旋转/镜像后的对象，光标方向以当前控制点和其对角/对边控制点的连线为准，
        // 这样鼠标提示会跟随图形真实视觉方向变化。
        var dx = current.Value.X - opposite.Value.X;
        var dy = current.Value.Y - opposite.Value.Y;
        if (Math.Abs(dx) < 0.001f && Math.Abs(dy) < 0.001f)
            return Cursors.Arrow;

        double angle = Math.Atan2(dy, dx) * 180d / Math.PI;
        angle = (angle + 360d) % 180d;

        if (angle < 22.5d || angle >= 157.5d)
            return Cursors.SizeWE;
        if (angle < 67.5d)
            return Cursors.SizeNESW;
        if (angle < 112.5d)
            return Cursors.SizeNS;
        return Cursors.SizeNWSE;
    }

    /// <summary>
    /// 按控制点语义返回标准轴对齐缩放光标。
    /// </summary>
    private static Cursor GetAxisAlignedCursor(ControlPointType controlPointType)
    {
        // 这里返回的是“控制点语义”对应的标准缩放光标，
        // 不依赖对象当前长宽比，适用于轴对齐的单选和多选框。
        return controlPointType switch
        {
            ControlPointType.TopLeft or ControlPointType.BottomRight => Cursors.SizeNWSE,
            ControlPointType.TopRight or ControlPointType.BottomLeft => Cursors.SizeNESW,
            ControlPointType.TopCenter or ControlPointType.BottomCenter => Cursors.SizeNS,
            ControlPointType.MiddleLeft or ControlPointType.MiddleRight => Cursors.SizeWE,
            _ => Cursors.Arrow
        };
    }

    /// <summary>
    /// 判断当前图形是否可以安全使用固定语义光标，而不需要跟随几何方向旋转。
    /// </summary>
    private static bool UsesAxisAlignedCursorMapping(DrawObject drawObject)
    {
        const float epsilon = 0.001f;

        // 只要对象发生旋转、倾斜或单轴镜像，固定语义光标就可能和视觉方向不一致，
        // 这时回退到几何角度判定。
        return Math.Abs(drawObject.Rotation) < epsilon
            && Math.Abs(drawObject.SkewX) < epsilon
            && Math.Abs(drawObject.SkewY) < epsilon
            && drawObject.ScaleX >= 0f
            && drawObject.ScaleY >= 0f;
    }

    /// <summary>
    /// 从带语义的控制点集合中查找指定控制点的位置。
    /// </summary>
    private static SKPoint? FindPoint((SKPoint Point, ControlPointType Type)[] controlPoints, ControlPointType type)
    {
        foreach (var item in controlPoints)
        {
            if (item.Type == type)
                return item.Point;
        }

        return null;
    }
}
