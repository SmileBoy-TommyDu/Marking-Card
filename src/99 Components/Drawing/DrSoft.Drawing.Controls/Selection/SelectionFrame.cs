using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Tools;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DrSoft.Drawing.Controls.Selection;

internal enum SelectionFrameKind
{
    OrientedBoundingBox,
    AxisAlignedBoundingBox,
}

internal readonly record struct SelectionTypedHandle(SKPoint Point, ControlPointType Type);

internal readonly record struct SelectionGlyphHandle(
    SKPoint Point,
    ControlPointType Type,
    float Degrees,
    bool IsCorner);

internal readonly record struct SelectionFrame(
    SelectionFrameKind Kind,
    SKPoint[] FrameCorners,
    SKRect Bounds,
    SKPoint Center,
    SelectionTypedHandle[] ResizeHandles,
    SelectionGlyphHandle[] GlyphHandles)
{
    public bool IsEmpty =>
        FrameCorners.Length == 0 &&
        ResizeHandles.Length == 0 &&
        GlyphHandles.Length == 0;
}

internal static class SelectionFrameFactory
{
    public static SelectionFrame CreateFromGeometry(
        SelectionGeometry geometry,
        SelectionFrameKind kind,
        bool hideEdgeMidpoints)
    {
        if (geometry.Corners.Length == 0)
        {
            return Empty(kind);
        }

        return new SelectionFrame(
            kind,
            geometry.Corners,
            geometry.Bounds,
            geometry.Center,
            ToTypedHandles(geometry.ControlPoints, hideEdgeMidpoints),
            []);
    }

    public static SelectionFrame CreateSingleAabbResizeFrame(
        DrawObject drawObject,
        float scale,
        bool hideEdgeMidpoints)
    {
        //var aabb = AABBHelper.GetShapesAABB(new List<DrawObject> { drawObject }, scale);

        var aabb = drawObject.GetAABB2().Item1.ToRect();
        float offset = DrawObject.controlPointOffset * 1.5f * 0;
        return CreateAabbResizeFrame(aabb, hideEdgeMidpoints, offset, offset);
    }

    public static SelectionFrame CreateMergedAabbResizeFrame(
        SelectionGeometry geometry,
        bool hideEdgeMidpoints)
    {
        return CreateFromGeometry(
            geometry,
            SelectionFrameKind.AxisAlignedBoundingBox,
            hideEdgeMidpoints);
    }

    public static SelectionFrame CreateSingleAabbRotateSkewFrame(
        DrawObject drawObject,
        SKRect bounds,
        float scale,
        bool hideEdgeMidpoints)
    {
        if (bounds.IsEmpty)
        {
            return Empty(SelectionFrameKind.AxisAlignedBoundingBox);
        }

        float offsetX = bounds.Width / 2f + DrawObject.thirdControlPointOffset / scale;
        float offsetY = bounds.Height / 2f + DrawObject.thirdControlPointOffset / scale;
        return CreateAabbRotateSkewFrame(bounds, hideEdgeMidpoints, offsetX, offsetY);
    }

    public static SelectionFrame CreateMergedAabbRotateSkewFrame(
        SKRect aabb,
        float scale,
        bool hideEdgeMidpoints)
    {
        if (aabb.IsEmpty)
        {
            return Empty(SelectionFrameKind.AxisAlignedBoundingBox);
        }

        float offsetX = aabb.Width / 2f + DrawObject.thirdControlPointOffset * 1.2f / scale;
        float offsetY = aabb.Height / 2f + DrawObject.thirdControlPointOffset * 1.2f / scale;
        return CreateAabbRotateSkewFrame(aabb, hideEdgeMidpoints, offsetX, offsetY);
    }

    public static SelectionFrame CreatePreviewRotateSkewFrame(SKRect aabb, float scale)
    {
        if (aabb.IsEmpty)
        {
            return Empty(SelectionFrameKind.AxisAlignedBoundingBox);
        }

        float offsetX = aabb.Width / 2f + DrawObject.controlPointOffset / scale;
        float offsetY = aabb.Height / 2f + DrawObject.controlPointOffset / scale;
        return CreateAabbRotateSkewFrame(aabb, hideEdgeMidpoints: false, offsetX, offsetY);
    }

    public static SKPoint[] RotateCorners(SKPoint[] corners, SKPoint center, float angleDegrees)
    {
        if (corners == null || corners.Length == 0)
        {
            return corners ?? [];
        }

        float rad = angleDegrees * MathF.PI / 180f;
        float cos = MathF.Cos(rad);
        float sin = MathF.Sin(rad);
        var result = new SKPoint[corners.Length];

        for (int i = 0; i < corners.Length; i++)
        {
            float dx = corners[i].X - center.X;
            float dy = corners[i].Y - center.Y;
            result[i] = new SKPoint(
                center.X + dx * cos - dy * sin,
                center.Y + dx * sin + dy * cos);
        }

        return result;
    }

    public static SKRect ComputeBounds(SKPoint[] corners)
    {
        if (corners == null || corners.Length == 0)
        {
            return SKRect.Empty;
        }

        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;

        foreach (var corner in corners)
        {
            if (corner.X < minX) minX = corner.X;
            if (corner.Y < minY) minY = corner.Y;
            if (corner.X > maxX) maxX = corner.X;
            if (corner.Y > maxY) maxY = corner.Y;
        }

        return new SKRect(minX, minY, maxX, maxY);
    }

    public static bool IsEdgeMidpoint(ControlPointType type)
    {
        return type is ControlPointType.TopCenter
            or ControlPointType.MiddleRight
            or ControlPointType.BottomCenter
            or ControlPointType.MiddleLeft;
    }

    private static SelectionFrame CreateAabbResizeFrame(
        SKRect aabb,
        bool hideEdgeMidpoints,
        float offsetX,
        float offsetY)
    {
        if (aabb.IsEmpty)
        {
            return Empty(SelectionFrameKind.AxisAlignedBoundingBox);
        }

        var typedHandles = new[]
        {
            new SelectionTypedHandle(new SKPoint(aabb.Left - offsetX, aabb.Top - offsetY), ControlPointType.TopLeft),
            new SelectionTypedHandle(new SKPoint(aabb.Right + offsetX, aabb.Top - offsetY), ControlPointType.TopRight),
            new SelectionTypedHandle(new SKPoint(aabb.Right + offsetX, aabb.Bottom + offsetY), ControlPointType.BottomRight),
            new SelectionTypedHandle(new SKPoint(aabb.Left - offsetX, aabb.Bottom + offsetY), ControlPointType.BottomLeft),
            new SelectionTypedHandle(new SKPoint(aabb.MidX, aabb.Top - offsetY), ControlPointType.TopCenter),
            new SelectionTypedHandle(new SKPoint(aabb.Right + offsetX, aabb.MidY), ControlPointType.MiddleRight),
            new SelectionTypedHandle(new SKPoint(aabb.MidX, aabb.Bottom + offsetY), ControlPointType.BottomCenter),
            new SelectionTypedHandle(new SKPoint(aabb.Left - offsetX, aabb.MidY), ControlPointType.MiddleLeft),
        };

        return new SelectionFrame(
            SelectionFrameKind.AxisAlignedBoundingBox,
            BuildAxisAlignedCorners(aabb),
            aabb,
            new SKPoint(aabb.MidX, aabb.MidY),
            FilterTypedHandles(typedHandles, hideEdgeMidpoints),
            []);
    }

    private static SelectionFrame CreateAabbRotateSkewFrame(
        SKRect aabb,
        bool hideEdgeMidpoints,
        float offsetX,
        float offsetY)
    {
        var center = new SKPoint(aabb.MidX, aabb.MidY);
        var glyphHandles = new[]
        {
            new SelectionGlyphHandle(new SKPoint(center.X - offsetX, center.Y + offsetY), ControlPointType.TopLeft, -67.5f, true),
            new SelectionGlyphHandle(new SKPoint(center.X, center.Y + offsetY), ControlPointType.TopCenter, 90f, false),
            new SelectionGlyphHandle(new SKPoint(center.X + offsetX, center.Y + offsetY), ControlPointType.TopRight, -157.5f, true),
            new SelectionGlyphHandle(new SKPoint(center.X - offsetX, center.Y), ControlPointType.MiddleLeft, 0f, false),
            new SelectionGlyphHandle(new SKPoint(center.X + offsetX, center.Y), ControlPointType.MiddleRight, 180f, false),
            new SelectionGlyphHandle(new SKPoint(center.X - offsetX, center.Y - offsetY), ControlPointType.BottomLeft, 22.5f, true),
            new SelectionGlyphHandle(new SKPoint(center.X, center.Y - offsetY), ControlPointType.BottomCenter, -90f, false),
            new SelectionGlyphHandle(new SKPoint(center.X + offsetX, center.Y - offsetY), ControlPointType.BottomRight, 112.5f, true),
        };

        return new SelectionFrame(
            SelectionFrameKind.AxisAlignedBoundingBox,
            BuildAxisAlignedCorners(aabb),
            aabb,
            center,
            [],
            FilterGlyphHandles(glyphHandles, hideEdgeMidpoints));
    }

    private static SKPoint[] BuildAxisAlignedCorners(SKRect bounds)
    {
        return
        [
            new SKPoint(bounds.Left, bounds.Top),
            new SKPoint(bounds.Right, bounds.Top),
            new SKPoint(bounds.Right, bounds.Bottom),
            new SKPoint(bounds.Left, bounds.Bottom),
        ];
    }

    private static SelectionTypedHandle[] ToTypedHandles(SKPoint[] controlPoints, bool hideEdgeMidpoints)
    {
        if (controlPoints.Length < 8)
        {
            return [];
        }

        var typedHandles = new[]
        {
            new SelectionTypedHandle(controlPoints[0], ControlPointType.TopLeft),
            new SelectionTypedHandle(controlPoints[1], ControlPointType.TopRight),
            new SelectionTypedHandle(controlPoints[2], ControlPointType.BottomRight),
            new SelectionTypedHandle(controlPoints[3], ControlPointType.BottomLeft),
            new SelectionTypedHandle(controlPoints[4], ControlPointType.TopCenter),
            new SelectionTypedHandle(controlPoints[5], ControlPointType.MiddleRight),
            new SelectionTypedHandle(controlPoints[6], ControlPointType.BottomCenter),
            new SelectionTypedHandle(controlPoints[7], ControlPointType.MiddleLeft),
        };

        return FilterTypedHandles(typedHandles, hideEdgeMidpoints);
    }

    private static SelectionTypedHandle[] FilterTypedHandles(
        SelectionTypedHandle[] handles,
        bool hideEdgeMidpoints)
    {
        if (!hideEdgeMidpoints)
        {
            return handles;
        }

        return handles.Where(handle => !IsEdgeMidpoint(handle.Type)).ToArray();
    }

    private static SelectionGlyphHandle[] FilterGlyphHandles(
        SelectionGlyphHandle[] handles,
        bool hideEdgeMidpoints)
    {
        if (!hideEdgeMidpoints)
        {
            return handles;
        }

        return handles.Where(handle => !IsEdgeMidpoint(handle.Type)).ToArray();
    }

    private static SelectionFrame Empty(SelectionFrameKind kind)
    {
        return new SelectionFrame(kind, [], SKRect.Empty, SKPoint.Empty, [], []);
    }
}
