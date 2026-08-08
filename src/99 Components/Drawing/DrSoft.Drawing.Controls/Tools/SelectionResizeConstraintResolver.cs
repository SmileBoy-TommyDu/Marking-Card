using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using System.Collections.Generic;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Utility;

namespace DrSoft.Drawing.Controls.Tools;

/// <summary>
/// 根据当前选区内容解析缩放约束。
/// 这里负责把“哪些图元触发哪些规则”集中收口，避免 DTO 和 UI 暴露 ContainsXxx 之类的类型细节。
/// </summary>
internal static class SelectionResizeConstraintResolver
{
    /// <summary>
    /// 为整个选区解析缩放约束。
    /// 当前第一类触发器是圆弧；后续若有其他图元规则，也应继续扩展这里。
    /// </summary>
    public static SelectionResizeConstraint ResolveForSelection(IEnumerable<IShape>? shapes)
    {
        if (IsHideEdgeMidpoint(shapes))
        {
            return SelectionResizeConstraint.HideEdgeMidpointHandles
                | SelectionResizeConstraint.RequireUniformScale;
        }

        return SelectionResizeConstraint.None;
    }

    /// <summary>
    /// 为单个图形解析缩放约束。
    /// 主要供单选句柄绘制和单选控制点命中路径复用同一套规则。
    /// </summary>
    public static SelectionResizeConstraint ResolveForShape(IShape? shape)
    {
        if (shape == null)
        {
            return SelectionResizeConstraint.None;
        }

        return ResolveForSelection([shape]);
    }

    /// <summary>
    /// 是否隐藏边线中心拖放和手动宽高调整。
    /// </summary>
    private static bool IsHideEdgeMidpoint(IEnumerable<IShape>? shapes)
    {
        if (shapes == null)
        {
            return false;
        }

        foreach (var shape in shapes)
        {
            var shapeContainsArc = IsHideEdgeMidpoint(shape);
            if (shapeContainsArc)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsHideEdgeMidpoint(IShape? shape)
    {
        if (shape == null)
        {
            return false;
        }

        // 圆弧不支持非等比拉伸
        if (shape is DrawArc)
        {
            return true;
        }

        //圆角矩形
        if (shape is DrawRectangle rect && rect.IsCornerRadiusRectangle())
        {
            return true;
        }

        // 容器自身类型并不能表达其子内容的缩放约束，必须下钻到叶子图元判断。
        if (shape is DrawCombination || shape is DrawingGroup) {
            var flattenedShapes = shape.Flatten();
            return IsHideEdgeMidpoint(flattenedShapes);
        }

        return false;
    }
}
