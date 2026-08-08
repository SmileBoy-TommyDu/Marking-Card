using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Model;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DrSoft.Drawing.Controls.Tools;

/// <summary>
/// 选择工具的命中辅助服务。
/// 负责"点击位置命中了谁"和"是否还在当前选择区域上"这两类只读判断。
/// </summary>
internal sealed class SelectionHitService
{
    private readonly DocumentContext _context;

    public SelectionHitService(DocumentContext context)
    {
        _context = context;
    }

    public bool IsPointInSelectedShapes(SKPoint point, float padding)
    {
        foreach (var shape in _context.ActiveCanvas?.Selection ?? Enumerable.Empty<IShape>())
        {
            if (shape is DrawObject drawObject && drawObject.HitTest(point, padding))
                return true;
        }

        return false;
    }

    public IShape? FindClosestHitShape(SKPoint point, float padding)
    {
        if (_context.ActiveCanvas is not DrawingCanvas canvas)
            return null;

        var skPoint = new SKPoint(point.X, point.Y);

        // 预扫描：画布上是否存在 DrawingHatch，决定非 Hatch 图形是否使用不对称容差
        bool anyHatchOnCanvas = canvas.Layers
            .Where(l => l.IsVisible && !l.IsLocked)
            .SelectMany(l => l.Shapes)
            .Any(s => s is DrawingHatch);

        var hitShapes = new List<(IShape Shape, DrawObject DrawObj, float PathDistance, bool IsHatch, bool NearOutline)>();

        foreach (var layer in canvas.Layers.Where(l => l.IsVisible).Reverse())
        {
            if (layer.IsLocked)
                continue;

            foreach (var shape in layer.Shapes)
            {
                if (shape is not DrawObject drawObject)
                    continue;

                var bbox = drawObject.GetOBB().Corners.ToRect();
                if (!bbox.IsEmpty)
                {
                    float dx = MathF.Max(0f, MathF.Max(bbox.Left - skPoint.X, skPoint.X - bbox.Right));
                    float dy = MathF.Max(0f, MathF.Max(bbox.Top - skPoint.Y, skPoint.Y - bbox.Bottom));
                    if (dx * dx + dy * dy > padding * padding)
                        continue;
                }

                // HitTest 容差：有填充时非 Hatch 图形使用不对称容差
                // 向内（点击点在 AABB 内部）扩大到 4x，让深入矩形内部的点击也能命中轮廓
                // 向外保持 1x，Hatch 始终 1x
                float hitTolerance = padding;
                if (anyHatchOnCanvas && drawObject is not DrawingHatch)
                {
                    bool isInsideBounds = !bbox.IsEmpty
                        && skPoint.X >= bbox.Left && skPoint.X <= bbox.Right
                        && skPoint.Y >= bbox.Top && skPoint.Y <= bbox.Bottom;
                    hitTolerance = isInsideBounds ? padding * 4f : padding;
                }

                if (!drawObject.HitTest(point, hitTolerance))
                    continue;

                // 所有图形统一使用路径距离，不再对 DrawCombination 退化为到中心距离。
                // 之前用中心距离会导致大矩形的边框 pathDistance 远超 padding，nearOutline 永远为 false。
                float pathDistance = drawObject.GetDistanceToPath(skPoint);

                bool isHatch = drawObject is DrawingHatch;

                // NearOutline：非 Hatch 图形在轮廓优先范围内标记为 true，排序时绝对优先于填充。
                // 有填充时：向内 2x padding 范围内优先选边框，超过后按距离正常选（可能选到填充）；
                //           向外 1x padding 范围内选边框。
                // 无填充时：1x padding。
                bool nearOutline = false;
                if (!isHatch)
                {
                    float outlinePriorityThreshold = padding;
                    if (anyHatchOnCanvas)
                    {
                        bool isInsideBounds = !bbox.IsEmpty
                            && skPoint.X >= bbox.Left && skPoint.X <= bbox.Right
                            && skPoint.Y >= bbox.Top && skPoint.Y <= bbox.Bottom;
                        outlinePriorityThreshold = isInsideBounds ? padding * 2f : padding;
                    }
                    nearOutline = pathDistance <= outlinePriorityThreshold;
                }

                hitShapes.Add((shape, drawObject, pathDistance, isHatch, nearOutline));
            }
        }

        if (hitShapes.Count == 0)
            return null;

        // 排序规则（优先级从高到低）：
        // 1. NearOutline 降序：轮廓优先范围内的边框图形绝对优先于填充
        // 2. 路径距离升序：距离点击位置越近越优先
        // 3. Hatch 降级：距离相同时非 Hatch 图形优先
        return hitShapes
            .OrderByDescending(h => h.NearOutline)
            .ThenBy(h => h.PathDistance)
            .ThenBy(h => h.IsHatch)
            .First().Shape;
    }

    public bool IsPointOverSelectionBounds(SKPoint point)
    {
        if (_context.ActiveCanvas == null || _context.ActiveCanvas.SelectedShapeCount == 0)
            return false;

        var padding = 8.0f / (_context.ActiveCanvas.Viewport.Scale == 0 ? 1.0f : _context.ActiveCanvas.Viewport.Scale);
        if (_context.ActiveCanvas.SelectedShapeCount == 1)
        {
            var shape = _context.ActiveCanvas.Selection.First();
            if (shape is not DrawObject drawObject)
                return false;

            var geometry = SelectionGeometryBuilder.BuildForSinglePreviewOBBSelection(drawObject);
            return !geometry.Bounds.IsEmpty
                && IsPointInBoundingBox(point, geometry.Bounds, padding);
        }

        var mergedGeometry = SelectionGeometryBuilder.BuildForMergedBounds(GetMergedSelectionBounds(), GetScale());
        return !mergedGeometry.Bounds.IsEmpty
            && IsPointInBoundingBox(point, mergedGeometry.Bounds, padding);
    }

    public bool IsPointOverSelectionBoundsBorder(SKPoint point)
    {
        if (_context.ActiveCanvas == null || _context.ActiveCanvas.SelectedShapeCount == 0)
            return false;

        var scale = _context.ActiveCanvas.Viewport.Scale == 0 ? 1.0f : (float)_context.ActiveCanvas.Viewport.Scale;
        var boundsPadding = 8.0f / scale;
        var borderThickness = 4.0f / scale;

        if (_context.ActiveCanvas.SelectedShapeCount == 1)
        {
            var shape = _context.ActiveCanvas.Selection.FirstOrDefault();
            if (shape is not DrawObject drawObject)
                return false;

            var geometry = SelectionGeometryBuilder.BuildForSinglePreviewOBBSelection(drawObject);
            return IsPointOnSelectionFrameBorder(point, geometry.Corners, boundsPadding, borderThickness);
        }

        var bounds = SelectionGeometryBuilder.BuildForMergedBounds(GetMergedSelectionBounds(), scale).Bounds;
        return IsPointOnBoundingBoxBorder(point, bounds, boundsPadding, borderThickness);
    }

    private float GetScale()
        => (float)(_context.ActiveCanvas?.Viewport.Scale == 0 ? 1.0 : _context.ActiveCanvas?.Viewport.Scale ?? 1.0);

    private SKRect GetMergedSelectionBounds()
        => _context.CachedSelectionBounds
            ?? _context.CalculateMergedBounds();

    private static bool IsPointInBoundingBox(SKPoint point, SKRect bounds, float padding)
    {
        return !bounds.IsEmpty
            && point.X >= bounds.Left - padding
            && point.X <= bounds.Right + padding
            && point.Y >= bounds.Top - padding
            && point.Y <= bounds.Bottom + padding;
    }

    private static bool IsPointOnBoundingBoxBorder(SKPoint point, SKRect bounds, float padding, float borderThickness)
    {
        if (!IsPointInBoundingBox(point, bounds, padding))
            return false;

        float distanceToLeft = MathF.Abs(point.X - bounds.Left);
        float distanceToRight = MathF.Abs(point.X - bounds.Right);
        float distanceToTop = MathF.Abs(point.Y - bounds.Top);
        float distanceToBottom = MathF.Abs(point.Y - bounds.Bottom);

        return distanceToLeft <= borderThickness
            || distanceToRight <= borderThickness
            || distanceToTop <= borderThickness
            || distanceToBottom <= borderThickness;
    }

    private static bool IsPointOnSelectionFrameBorder(
        SKPoint point,
        SKPoint[] corners,
        float padding,
        float borderThickness)
    {
        if (corners.Length < 4)
            return false;

        var bounds = BoundsFromCorners(corners);
        if (!IsPointInBoundingBox(point, bounds, padding))
            return false;

        float hitDistance = MathF.Max(borderThickness, padding);
        float hitDistanceSquared = hitDistance * hitDistance;
        for (int i = 0; i < corners.Length; i++)
        {
            var start = corners[i];
            var end = corners[(i + 1) % corners.Length];
            if (DistanceToSegmentSquared(point, start, end) <= hitDistanceSquared)
                return true;
        }

        return false;
    }

    private static SKRect BoundsFromCorners(SKPoint[] corners)
    {
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

    private static float DistanceToSegmentSquared(SKPoint point, SKPoint start, SKPoint end)
    {
        float dx = end.X - start.X;
        float dy = end.Y - start.Y;
        float lengthSquared = dx * dx + dy * dy;
        if (lengthSquared < 0.0001f)
        {
            float px = point.X - start.X;
            float py = point.Y - start.Y;
            return px * px + py * py;
        }

        float t = ((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared;
        t = Math.Clamp(t, 0f, 1f);
        float closestX = start.X + t * dx;
        float closestY = start.Y + t * dy;
        float deltaX = point.X - closestX;
        float deltaY = point.Y - closestY;
        return deltaX * deltaX + deltaY * deltaY;
    }
}
