using System;
using System.Reflection.Metadata;
using System.Windows.Controls;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Model;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.Tools;

internal readonly record struct SelectionGeometry(
    SKPoint[] Corners,
    SKPoint[] ControlPoints,
    SKPoint Center,
    SKRect Bounds);

internal static class SelectionGeometryBuilder
{
    // 选区几何唯一数据源：
    // 渲染选择框、控制点命中、中心点展示都应复用这里的结果，
    // 不要在调用方重新按 Width/Height 或 AABB 拼装另一套几何，
    // 否则很容易出现“控制点命中位置”和“实际绘制位置”不一致。
    public static SelectionGeometry BuildForSinglePreviewOBBSelection(DrawObject drawObject)
    {
        float adjustedSharpeOffset = DrawObject.sharpeOffset / (float)(DocumentContext.Instance.ActiveCanvas?.Viewport.Scale ?? 1.0);
        float adjustedRectOffset = DrawObject.controlPointOffset / (float)(DocumentContext.Instance.ActiveCanvas?.Viewport.Scale ?? 1.0);

        SKPoint[] corners;
        SKPoint center;
        (corners, center) = drawObject.GetPreviewOBB();

        return new SelectionGeometry(corners.ToOffsetCorners(adjustedSharpeOffset), BuildControlPoints(corners, center, adjustedRectOffset), center, corners.ToRect());
    }

    public static SelectionGeometry BuildForSinglePreviewAABBSelection(DrawObject drawObject)
    {
        float adjustedSharpeOffset = DrawObject.sharpeOffset / (float)(DocumentContext.Instance.ActiveCanvas?.Viewport.Scale ?? 1.0);
        float adjustedRectOffset = DrawObject.controlPointOffset / (float)(DocumentContext.Instance.ActiveCanvas?.Viewport.Scale ?? 1.0);

        SKPoint[] corners;
        SKPoint center;
        (corners, center) = drawObject.GetPreviewAABB();

        return new SelectionGeometry(corners.ToOffsetCorners(adjustedSharpeOffset), BuildControlPoints(corners, center, adjustedRectOffset), center, corners.ToRect());
    }

    public static SelectionGeometry BuildForSingleOBBSelection(DrawObject drawObject)
    {
        float adjustedSharpeOffset = DrawObject.sharpeOffset / (float)(DocumentContext.Instance.ActiveCanvas?.Viewport.Scale ?? 1.0);
        float adjustedRectOffset = DrawObject.controlPointOffset / (float)(DocumentContext.Instance.ActiveCanvas?.Viewport.Scale ?? 1.0);

        SKPoint[] corners;
        SKPoint center;
        (corners, center) = drawObject.GetOBB();

        return new SelectionGeometry(corners.ToOffsetCorners(adjustedSharpeOffset), BuildControlPoints(corners, center, adjustedRectOffset), center, corners.ToRect());
    }

    public static SelectionGeometry BuildForSingleAABBSelection(DrawObject drawObject)
    {
        float adjustedSharpeOffset = DrawObject.sharpeOffset / (float)(DocumentContext.Instance.ActiveCanvas?.Viewport.Scale ?? 1.0);
        float adjustedRectOffset = DrawObject.controlPointOffset / (float)(DocumentContext.Instance.ActiveCanvas?.Viewport.Scale ?? 1.0);

        SKPoint[] corners;
        SKPoint center;
        (corners, center) = drawObject.GetAABB2();

        return new SelectionGeometry(corners.ToOffsetCorners(adjustedSharpeOffset), BuildControlPoints(corners, center, adjustedRectOffset), center, corners.ToRect());
    }

    public static SelectionGeometry BuildForMultiPreviewAABBSelection(SKRect mergedBounds)
    {
        if (mergedBounds.IsEmpty)
        {
            return Empty();
        }

        float adjustedSharpeOffset = DrawObject.sharpeOffset / (float)(DocumentContext.Instance.ActiveCanvas?.Viewport.Scale ?? 1.0);
        float adjustedRectOffset = DrawObject.controlPointOffset / (float)(DocumentContext.Instance.ActiveCanvas?.Viewport.Scale ?? 1.0);
        var center = new SKPoint(mergedBounds.MidX, mergedBounds.MidY);
        float visualTop = Math.Max(mergedBounds.Top, mergedBounds.Bottom);
        float visualBottom = Math.Min(mergedBounds.Top, mergedBounds.Bottom);
        var corners = new[]
        {
            new SKPoint(mergedBounds.Left, visualTop ),
            new SKPoint(mergedBounds.Right, visualTop ),
            new SKPoint(mergedBounds.Right, visualBottom ),
            new SKPoint(mergedBounds.Left, visualBottom )
        };

        corners = corners.ToOffsetCorners(adjustedSharpeOffset);

        return new SelectionGeometry(corners, BuildControlPoints(corners, center, adjustedRectOffset), center, mergedBounds);
    }

    public static SelectionGeometry BuildForMergedBounds(SKRect mergedBounds, float scale)
    {
        if (mergedBounds.IsEmpty)
        {
            return Empty();
        }

        float adjustedSharpeOffset = DrawObject.sharpeOffset / scale;
        float adjustedRectOffset = DrawObject.controlPointOffset / scale;
        var center = new SKPoint(mergedBounds.MidX, mergedBounds.MidY);
        var corners = new[]
        {
            new SKPoint(mergedBounds.Left, mergedBounds.Top ),
            new SKPoint(mergedBounds.Right, mergedBounds.Top ),
            new SKPoint(mergedBounds.Right, mergedBounds.Bottom ),
            new SKPoint(mergedBounds.Left, mergedBounds.Bottom )
        };

        corners = corners.ToOffsetCorners(adjustedSharpeOffset);

        return new SelectionGeometry(corners, BuildControlPoints(corners, center, adjustedRectOffset), center, mergedBounds);
    }




    private static SKPoint[] BuildControlPoints(SKPoint[] corners, SKPoint center, float adjustedRectOffset)
    {
        SKPoint OffsetFromCenter(SKPoint pt)
        {
            float dx = pt.X - center.X;
            float dy = pt.Y - center.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.001f)
            {
                return pt;
            }

            float scale = (len + adjustedRectOffset) / len;
            return new SKPoint(center.X + dx * scale, center.Y + dy * scale);
        }

        var controlPoints = new SKPoint[8];
        controlPoints[0] = OffsetFromCenter(corners[3]);
        controlPoints[1] = OffsetFromCenter(corners[2]);
        controlPoints[2] = OffsetFromCenter(corners[1]);
        controlPoints[3] = OffsetFromCenter(corners[0]);
        controlPoints[4] = MidPoint(controlPoints[0], controlPoints[1]);
        controlPoints[5] = MidPoint(controlPoints[1], controlPoints[2]);
        controlPoints[6] = MidPoint(controlPoints[2], controlPoints[3]);
        controlPoints[7] = MidPoint(controlPoints[3], controlPoints[0]);
        return controlPoints;
    }

    private static SKPoint MidPoint(SKPoint left, SKPoint right)
        => new((left.X + right.X) / 2f, (left.Y + right.Y) / 2f);

    private static SKPoint[] BuildCornersFromLocalBounds(SKRect localBounds, SKMatrix matrix)
    {
        float visualTop = Math.Max(localBounds.Top, localBounds.Bottom);
        float visualBottom = Math.Min(localBounds.Top, localBounds.Bottom);
        return
        [
            matrix.MapPoint(new SKPoint(localBounds.Left, visualTop)),
            matrix.MapPoint(new SKPoint(localBounds.Right, visualTop)),
            matrix.MapPoint(new SKPoint(localBounds.Right, visualBottom)),
            matrix.MapPoint(new SKPoint(localBounds.Left, visualBottom))
        ];
    }

    private static bool TryGetContainerPathBounds(DrawObject drawObject, out SKRect localBounds)
    {
        localBounds = SKRect.Empty;
        if (drawObject is not IContainer container || container.Children == null || container.Children.Count == 0)
            return false;

        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;
        bool hasBounds = false;
        var containerWorldToLocal = drawObject.GetInverseMatrix();

        foreach (var child in container.Children.OfType<DrawObject>())
        {
            using var childPath = child.GetPath();
            if (childPath == null || childPath.IsEmpty)
                continue;

            var childToContainerLocal = SKMatrix.Concat(containerWorldToLocal, child.GetTransformMatrix());
            using var transformed = new SKPath(childPath);
            transformed.Transform(childToContainerLocal);
            var childBounds = transformed.TightBounds;
            if (childBounds.IsEmpty)
                continue;

            if (childBounds.Left < minX) minX = childBounds.Left;
            if (childBounds.Top < minY) minY = childBounds.Top;
            if (childBounds.Right > maxX) maxX = childBounds.Right;
            if (childBounds.Bottom > maxY) maxY = childBounds.Bottom;
            hasBounds = true;
        }

        if (!hasBounds)
            return false;

        localBounds = new SKRect(minX, minY, maxX, maxY);
        return true;
    }

    private static SKRect BoundsFromCorners(SKPoint[] corners)
    {
        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;

        foreach (var point in corners)
        {
            if (point.X < minX) minX = point.X;
            if (point.Y < minY) minY = point.Y;
            if (point.X > maxX) maxX = point.X;
            if (point.Y > maxY) maxY = point.Y;
        }

        return new SKRect(minX, minY, maxX, maxY);
    }

    private static SelectionGeometry Empty()
        => new(Array.Empty<SKPoint>(), Array.Empty<SKPoint>(), SKPoint.Empty, SKRect.Empty);
}
