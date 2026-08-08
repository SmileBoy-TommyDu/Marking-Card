using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Controls;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Controls.Selection;
using DrSoft.Drawing.Controls.Tools;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using SkiaSharp;

namespace DrSoft.Drawing.Rendering;

internal readonly record struct SelectionRenderMetrics(
    float LineWidth,
    float HandleHalfSize,
    float RectOffset)
{
    public static SelectionRenderMetrics FromViewport(IViewport viewport)
    {
        return new SelectionRenderMetrics(
            DrawObject.lineWidth / viewport.Scale,
            DrawObject.rectH / viewport.Scale,
            DrawObject.controlPointOffset / viewport.Scale);
    }
}

internal readonly record struct SelectionRenderContext(
    DocumentContext DocumentContext,
    IViewport Viewport,
    SelectionRenderMetrics Metrics);

internal interface ISelectionModeRenderer
{
    SelectState State { get; }

    void RenderSingle(SKCanvas canvas, DrawObject drawObject, SelectionRenderContext context);

    void RenderMerged(
        SKCanvas canvas,
        IReadOnlyList<DrawObject> selectedDrawObjects,
        SKRect mergedBounds,
        SKPoint center,
        bool hideEdgeMidpoints,
        SelectionRenderContext context);
}

internal sealed class SelectionModeStateMachine
{
    private readonly Dictionary<SelectState, ISelectionModeRenderer> _renderers;
    private readonly ISelectionModeRenderer _fallbackRenderer;

    public SelectionModeStateMachine(SKColor rectColor, SKColor hatchColor)
    {
        _renderers = new Dictionary<SelectState, ISelectionModeRenderer>
        {
            [SelectState.FirstSelected] = new FirstSelectionModeRenderer(rectColor, hatchColor),
            [SelectState.SecondSelected] = new SecondSelectionModeRenderer(),
            [SelectState.ThirdSelected] = new ThirdSelectionModeRenderer(),
        };

        _fallbackRenderer = _renderers[SelectState.FirstSelected];
    }

    public ISelectionModeRenderer Resolve(SelectState state)
    {
        return _renderers.TryGetValue(state, out var renderer)
            ? renderer
            : _fallbackRenderer;
    }
}

internal static class SelectionStrokeRenderer
{
    public static void DrawDashedFrame(
        SKCanvas canvas,
        SKPoint[] corners,
        SKColor color,
        SelectionRenderContext context)
    {
        if (corners.Length == 0)
        {
            return;
        }

        using var strokePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = color,
            StrokeWidth = context.Metrics.LineWidth,
            IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash(
                new[] { 3.42f / (float)context.Viewport.Scale, 2.05f / (float)context.Viewport.Scale },
                0),
        };

        DrawFrame(canvas, corners, strokePaint);
    }

    public static void DrawSolidFrame(
        SKCanvas canvas,
        SKPoint[] corners,
        SKColor color,
        SelectionRenderContext context)
    {
        if (corners.Length == 0)
        {
            return;
        }

        using var strokePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = color,
            StrokeWidth = context.Metrics.LineWidth,
            IsAntialias = true,
        };

        DrawFrame(canvas, corners, strokePaint);
    }

    private static void DrawFrame(SKCanvas canvas, SKPoint[] corners, SKPaint paint)
    {
        using var path = new SKPath();
        path.MoveTo(corners[0].X, corners[0].Y);

        for (int i = 1; i < corners.Length; i++)
        {
            path.LineTo(corners[i].X, corners[i].Y);
        }

        path.Close();
        canvas.DrawPath(path, paint);
    }
}

internal static class SelectionHandleRenderer
{
    public static void DrawSquareHandles(
        SKCanvas canvas,
        IReadOnlyList<SelectionTypedHandle> handles,
        SKColor color,
        float handleHalfSize)
    {
        if (handles.Count == 0)
        {
            return;
        }

        using var fillPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = color,
        };

        foreach (var handle in handles)
        {
            canvas.DrawRect(
                handle.Point.X - handleHalfSize,
                handle.Point.Y - handleHalfSize,
                2 * handleHalfSize,
                2 * handleHalfSize,
                fillPaint);
        }
    }

    public static void DrawGlyphHandles(
        SKCanvas canvas,
        IReadOnlyList<SelectionGlyphHandle> handles,
        SKColor color,
        float handleHalfSize)
    {
        if (handles.Count == 0)
        {
            return;
        }

        float glyphScale = Math.Min(100 * handleHalfSize, 100 * handleHalfSize) / 400f;
        float arrowSize = 7 * glyphScale;
        using var fillPaint = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };
        using var arrowPath = CreateArrowPath(arrowSize);
        using var arcArrowPath = CreateArcArrowPath(arrowSize);

        foreach (var handle in handles)
        {
            DrawRotated(
                canvas,
                handle.IsCorner ? arcArrowPath : arrowPath,
                handle.Point,
                handle.Degrees,
                fillPaint);
        }
    }

    private static void DrawRotated(SKCanvas canvas, SKPath path, SKPoint position, float degrees, SKPaint paint)
    {
        if (position.IsEmpty)
        {
            return;
        }

        canvas.Save();
        canvas.Translate(position);
        canvas.RotateDegrees(degrees);
        canvas.DrawPath(path, paint);
        canvas.Restore();
    }

    private static SKPath CreateArrowPath(float size)
    {
        var path = new SKPath();
        path.MoveTo(0, 1.75f * size);
        path.LineTo(-size * 0.5f, size * 0.55f);
        path.LineTo(size * 0.5f, size * 0.55f);
        path.Close();

        float tailWidth = size * 0.1f;
        path.MoveTo(-tailWidth / 2, size * 0.55f);
        path.LineTo(tailWidth / 2, size * 0.55f);
        path.LineTo(tailWidth / 2, size * -0.55f);
        path.LineTo(-tailWidth / 2, size * -0.55f);
        path.Close();

        path.MoveTo(0, -1.75f * size);
        path.LineTo(-size * 0.5f, size * -0.55f);
        path.LineTo(size * 0.5f, size * -0.55f);
        path.Close();

        return path;
    }

    private static SKPath CreateArcArrowPath(float size)
    {
        var path = new SKPath();
        path.MoveTo(0, 1.75f * size);
        path.LineTo(-size * 0.5f, size * 0.55f);
        path.LineTo(size * 0.5f, size * 0.55f);
        path.Close();

        float tailWidth = size * 0.1f;
        path.MoveTo(-tailWidth / 2, size * 0.55f);
        path.LineTo(tailWidth / 2, size * 0.55f);
        path.LineTo(tailWidth / 2, 0);
        path.LineTo(-tailWidth / 2, 0);
        path.Close();

        float theta = 22.5f;
        (float x1, float y1) = MapToPoint(-tailWidth / 2, 0, theta);
        path.MoveTo(x1, y1);
        (float x2, float y2) = MapToPoint(tailWidth / 2, 0, theta);
        path.LineTo(x2, y2);
        (float x3, float y3) = MapToPoint(tailWidth / 2, size * -0.55f, theta);
        path.LineTo(x3, y3);
        (float x4, float y4) = MapToPoint(-tailWidth / 2, size * -0.55f, theta);
        path.LineTo(x4, y4);
        path.Close();

        (float x5, float y5) = MapToPoint(0, -1.75f * size, theta);
        path.MoveTo(x5, y5);
        (float x6, float y6) = MapToPoint(-size * 0.5f, size * -0.55f, theta);
        path.LineTo(x6, y6);
        (float x7, float y7) = MapToPoint(size * 0.5f, size * -0.55f, theta);
        path.LineTo(x7, y7);
        path.Close();

        return path;
    }

    private static (float x, float y) MapToPoint(float x, float y, float theta)
    {
        float x1 = x * (float)Math.Cos(theta / Math.PI) - y * (float)Math.Sin(theta / Math.PI);
        float y1 = x * (float)Math.Sin(theta / Math.PI) + y * (float)Math.Cos(theta / Math.PI);
        return (x1, y1);
    }
}

internal static class SelectionMarkerRenderer
{
    public static void DrawCenterCross(
        SKCanvas canvas,
        SKPoint center,
        SKColor color,
        IViewport viewport,
        float lineWidth)
    {
        float crossSize = 6.83f / viewport.Scale;
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = color,
            StrokeWidth = lineWidth,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round,
        };

        float halfSize = crossSize / 2f;
        canvas.DrawLine(
            center.X - halfSize,
            center.Y - halfSize,
            center.X + halfSize,
            center.Y + halfSize,
            paint);
        canvas.DrawLine(
            center.X + halfSize,
            center.Y - halfSize,
            center.X - halfSize,
            center.Y + halfSize,
            paint);
    }

    public static void DrawCenterIcon(SKCanvas canvas, SKPoint center, float handleHalfSize)
    {
        float scale = Math.Min(100 * handleHalfSize, 100 * handleHalfSize) / 400f;
        using var stroke = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = scale,
            IsAntialias = true,
        };

        float size = 7 * scale;
        canvas.DrawCircle(center, size * 0.8f, stroke);
        canvas.DrawCircle(center, size * 0.25f, stroke);
    }

    public static void DrawAnchorRealistic(
        SKCanvas canvas,
        SKPoint anchor,
        double viewportScale,
        SKColor color = default)
    {
        if (color == default)
        {
            color = SKColors.Red;
        }

        string svgPath = "M230 30 L230 410 C230 470,110 550,110 710 L850 710 C850 550,730 470,730 410 L730 30 Z M110 630 H850 M370 750 L480 1290 L590 750";
        using var path = SKPath.ParseSvgPathData(svgPath);
        var bounds = path.Bounds;
        float cx = (bounds.Left + bounds.Right) / 2;
        float cy = bounds.Bottom;
        path.Transform(SKMatrix.CreateTranslation(-cx, -cy));
        path.Transform(SKMatrix.CreateRotationDegrees(-45));

        using var paint = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            StrokeWidth = 20,
            StrokeJoin = SKStrokeJoin.Round,
        };

        canvas.Save();
        canvas.Translate(anchor.X, anchor.Y);
        canvas.Scale((float)(0.02f / viewportScale));
        canvas.DrawPath(path, paint);
        canvas.Restore();
    }
}

internal static class SelectionPathNodeRenderer
{
    public static void DrawIfNeeded(
        SKCanvas canvas,
        DrawObject drawObject,
        SelectionRenderContext context)
    {
        if (!drawObject.IsPathEditing || DocumentContext.Instance.IsDragControlPoint)
        {
            return;
        }

        List<SKPoint>? nodeWorldPositions = null;
        if (drawObject is DrawCombination combination)
        {
            nodeWorldPositions = combination.GetPathNodeWorldPositions();
        }
        else if (drawObject.PathNodes?.Count > 0)
        {
            nodeWorldPositions = drawObject.PathNodes
                .Select(local => drawObject.GetTransformMatrix().MapPoint(local))
                .ToList();
        }

        if (nodeWorldPositions == null || nodeWorldPositions.Count == 0)
        {
            return;
        }

        using var nodeStroke = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = SKColors.Black,
            StrokeWidth = context.Metrics.LineWidth,
        };
        using var nodeFillRed = new SKPaint { Style = SKPaintStyle.Fill, Color = SKColors.Red };
        using var nodeStrokeRed = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = SKColors.Red,
            StrokeWidth = context.Metrics.LineWidth,
        };
        using var nodeFillOrange = new SKPaint { Style = SKPaintStyle.Fill, Color = SKColors.Orange };
        using var nodeStrokeOrange = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = SKColors.Orange,
            StrokeWidth = context.Metrics.LineWidth,
        };
        using var nodeFillBlue = new SKPaint { Style = SKPaintStyle.Fill, Color = SKColors.DeepSkyBlue };
        using var nodeStrokeBlue = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = SKColors.DeepSkyBlue,
            StrokeWidth = context.Metrics.LineWidth,
        };

        var documentContext = context.DocumentContext;
        var selectedMoveNodePos = documentContext.SelectedMoveNodeWorldPosition;
        var selectedSeparateNodePos = documentContext.SelectedSeparateNodeWorldPosition;
        var selectedPathNodePositions = documentContext.SelectedPathNodeWorldPositions;
        float nodeTolerance = 2f / (float)context.Viewport.Scale;
        float nodeSize = context.Metrics.HandleHalfSize;

        foreach (var world in nodeWorldPositions)
        {
            bool isSelectedMoveNode = selectedMoveNodePos.HasValue
                && Math.Abs(world.X - selectedMoveNodePos.Value.X) < nodeTolerance
                && Math.Abs(world.Y - selectedMoveNodePos.Value.Y) < nodeTolerance;
            bool isSelectedSeparateNode = selectedSeparateNodePos.HasValue
                && Math.Abs(world.X - selectedSeparateNodePos.Value.X) < nodeTolerance
                && Math.Abs(world.Y - selectedSeparateNodePos.Value.Y) < nodeTolerance;
            bool isSelectedPathNode = selectedPathNodePositions.Any(selectedNode =>
                Math.Abs(world.X - selectedNode.X) < nodeTolerance
                && Math.Abs(world.Y - selectedNode.Y) < nodeTolerance);

            if (isSelectedMoveNode)
            {
                DrawNodeRect(canvas, world, nodeSize, nodeFillRed, nodeStrokeRed);
            }
            else if (isSelectedSeparateNode)
            {
                DrawNodeRect(canvas, world, nodeSize, nodeFillOrange, nodeStrokeOrange);
            }
            else if (isSelectedPathNode)
            {
                DrawNodeRect(canvas, world, nodeSize, nodeFillBlue, nodeStrokeBlue);
            }
            else
            {
                canvas.DrawRect(world.X - nodeSize, world.Y - nodeSize, 2 * nodeSize, 2 * nodeSize, nodeStroke);
            }
        }
    }

    private static void DrawNodeRect(SKCanvas canvas, SKPoint center, float size, SKPaint fill, SKPaint stroke)
    {
        canvas.DrawRect(center.X - size, center.Y - size, 2 * size, 2 * size, fill);
        canvas.DrawRect(center.X - size, center.Y - size, 2 * size, 2 * size, stroke);
    }
}

internal static class SelectionPreviewRenderer
{
    public static void DrawSecondSelectedPreview(
        SKCanvas canvas,
        SKPoint[]? previewCorners,
        SelectionRenderContext context)
    {
        if (previewCorners is not { Length: >= 4 })
        {
            return;
        }

        SelectionStrokeRenderer.DrawSolidFrame(canvas, previewCorners, SKColors.Red, context);
    }
}

internal sealed class FirstSelectionModeRenderer : ISelectionModeRenderer
{
    private readonly SKColor _rectColor;
    private readonly SKColor _hatchColor;

    public FirstSelectionModeRenderer(SKColor rectColor, SKColor hatchColor)
    {
        _rectColor = rectColor;
        _hatchColor = hatchColor;
    }

    public SelectState State => SelectState.FirstSelected;

    public void RenderSingle(SKCanvas canvas, DrawObject drawObject, SelectionRenderContext context)
    {
        var geometry = SelectionGeometryBuilder.BuildForSinglePreviewOBBSelection(drawObject);
        if (geometry.Corners.Length == 0 || geometry.Bounds.IsEmpty)
        {
            return;
        }

        var constraints = SelectionResizeConstraintResolver.ResolveForShape(drawObject);
        bool hideEdgeMidpoints = constraints.HasFlag(SelectionResizeConstraint.HideEdgeMidpointHandles);
        var frame = SelectionFrameFactory.CreateFromGeometry(
            geometry,
            SelectionFrameKind.OrientedBoundingBox,
            hideEdgeMidpoints);
        var color = drawObject.Type == ShapeType.Hatch ? _hatchColor : _rectColor;

        SelectionStrokeRenderer.DrawDashedFrame(canvas, frame.FrameCorners, color, context);
        SelectionHandleRenderer.DrawSquareHandles(
            canvas,
            frame.ResizeHandles,
            color,
            context.Metrics.HandleHalfSize);
        SelectionMarkerRenderer.DrawCenterCross(
            canvas,
            frame.Center,
            color,
            context.Viewport,
            context.Metrics.LineWidth);
        SelectionPathNodeRenderer.DrawIfNeeded(canvas, drawObject, context);
    }

    public void RenderMerged(
        SKCanvas canvas,
        IReadOnlyList<DrawObject> selectedDrawObjects,
        SKRect mergedBounds,
        SKPoint center,
        bool hideEdgeMidpoints,
        SelectionRenderContext context)
    {
        var geometry = SelectionGeometryBuilder.BuildForMultiPreviewAABBSelection(mergedBounds);
        if (geometry.Corners.Length == 0 || geometry.ControlPoints.Length == 0)
        {
            return;
        }

        var frame = SelectionFrameFactory.CreateFromGeometry(
            geometry,
            SelectionFrameKind.OrientedBoundingBox,
            hideEdgeMidpoints);
        SelectionStrokeRenderer.DrawDashedFrame(canvas, frame.FrameCorners, _rectColor, context);
        SelectionHandleRenderer.DrawSquareHandles(
            canvas,
            frame.ResizeHandles,
            _rectColor,
            context.Metrics.HandleHalfSize);
        SelectionMarkerRenderer.DrawCenterCross(
            canvas,
            frame.Center,
            _rectColor,
            context.Viewport,
            context.Metrics.LineWidth);
    }
}

internal sealed class SecondSelectionModeRenderer : ISelectionModeRenderer
{
    public SelectState State => SelectState.SecondSelected;

    public void RenderSingle(SKCanvas canvas, DrawObject drawObject, SelectionRenderContext context)
    {
        if (DocumentContext.Instance.IsDragControlPoint && DocumentContext.Instance.IsScalePreview)
        {
            //SelectionPreviewRenderer.DrawSecondSelectedPreview(canvas, drawObject.GetPreviewOBB().Corners, context);
            SelectionPreviewRenderer.DrawSecondSelectedPreview(canvas, context.DocumentContext.RealScaleOBBCorners, context);
        }

        var geometry = DocumentContext.Instance.IsDragControlPoint ? SelectionGeometryBuilder.BuildForSinglePreviewAABBSelection(drawObject) : SelectionGeometryBuilder.BuildForSingleAABBSelection(drawObject);
        if (geometry.Corners.Length == 0 || geometry.Bounds.IsEmpty) return;

        var constraints = SelectionResizeConstraintResolver.ResolveForShape(drawObject);
        bool hideEdgeMidpoints = constraints.HasFlag(SelectionResizeConstraint.HideEdgeMidpointHandles);
        var frame = SelectionFrameFactory.CreateFromGeometry(geometry, SelectionFrameKind.AxisAlignedBoundingBox, hideEdgeMidpoints);
        if (frame.IsEmpty) return;

        SKPoint[] aabbCorners = new SKPoint[]
{
    new SKPoint(frame.Bounds.Left, frame.Bounds.Top),     // 左上
    new SKPoint(frame.Bounds.Right, frame.Bounds.Top),    // 右上
    new SKPoint(frame.Bounds.Right, frame.Bounds.Bottom), // 右下
    new SKPoint(frame.Bounds.Left, frame.Bounds.Bottom)   // 左下
};

        SelectionStrokeRenderer.DrawDashedFrame(canvas, aabbCorners, SKColors.Black, context);
        SelectionHandleRenderer.DrawSquareHandles(canvas, frame.ResizeHandles, SKColors.Black, context.Metrics.HandleHalfSize);
        SelectionMarkerRenderer.DrawCenterCross(canvas, frame.Center, SKColors.Black, context.Viewport, context.Metrics.LineWidth);
        SelectionPathNodeRenderer.DrawIfNeeded(canvas, drawObject, context);
    }

    public void RenderMerged(
        SKCanvas canvas,
        IReadOnlyList<DrawObject> selectedDrawObjects,
        SKRect mergedBounds,
        SKPoint center,
        bool hideEdgeMidpoints,
        SelectionRenderContext context)
    {
        if (context.DocumentContext.IsDragControlPoint && context.DocumentContext.IsScalePreview)
        {
            //mergedBounds = context.DocumentContext.RealScalePreviewAABB;
            SelectionPreviewRenderer.DrawSecondSelectedPreview(canvas, context.DocumentContext.RealScaleOBBCorners, context);
        }

        var geometry = SelectionGeometryBuilder.BuildForMultiPreviewAABBSelection(mergedBounds);
        if (geometry.Corners.Length == 0 || geometry.ControlPoints.Length == 0) return;

        var frame = SelectionFrameFactory.CreateMergedAabbResizeFrame(geometry, hideEdgeMidpoints);
        SelectionStrokeRenderer.DrawDashedFrame(canvas, frame.FrameCorners, SKColors.Black, context);
        SelectionHandleRenderer.DrawSquareHandles(canvas, frame.ResizeHandles, SKColors.Black, context.Metrics.HandleHalfSize);
        SelectionMarkerRenderer.DrawCenterCross(canvas, frame.Center, SKColors.Black, context.Viewport, context.Metrics.LineWidth);
    }
}

internal sealed class ThirdSelectionModeRenderer : ISelectionModeRenderer
{
    public SelectState State => SelectState.ThirdSelected;

    public void RenderSingle(SKCanvas canvas, DrawObject drawObject, SelectionRenderContext context)
    {
        var documentContext = context.DocumentContext;
        var previewOBBCorners = documentContext.RealRotationCorners;
        var bounds = drawObject.GetAABB2().Corners.ToRect();
        if (context.DocumentContext.IsDragControlPoint && documentContext.IsRotationPreview && previewOBBCorners is { Length: >= 4 })
        {
            SelectionStrokeRenderer.DrawSolidFrame(canvas, previewOBBCorners, SKColors.Red, context);
            bounds = SelectionFrameFactory.ComputeBounds(previewOBBCorners);
        }

        var previewSkewOBBCorners = documentContext.RealSkewOBBCorners;
        if (context.DocumentContext.IsDragControlPoint && documentContext.IsSkewPreview && previewSkewOBBCorners is { Length: >= 4 })
        {
            SelectionStrokeRenderer.DrawSolidFrame(canvas, previewSkewOBBCorners, SKColors.Red, context);
            bounds = documentContext.RealSkewPreviewAABB;
        }

        var constraints = SelectionSkewConstraintResolver.ResolveForShape(drawObject);
        bool hideEdgeMidpoints = constraints.HasFlag(SelectionResizeConstraint.HideEdgeMidpointHandles);

        var frame = SelectionFrameFactory.CreateSingleAabbRotateSkewFrame(drawObject, bounds, (float)context.Viewport.Scale, hideEdgeMidpoints);
        if (frame.IsEmpty) return;

        SelectionHandleRenderer.DrawGlyphHandles(canvas, frame.GlyphHandles, SKColors.Black, context.Metrics.HandleHalfSize);

        var rotationCenter = drawObject.RotationCenter;
        var displayCenter = rotationCenter;
        DrawCenterMarker(canvas, displayCenter, context);
    }

    public void RenderMerged(
        SKCanvas canvas,
        IReadOnlyList<DrawObject> selectedDrawObjects,
        SKRect mergedBounds,
        SKPoint center,
        bool hideEdgeMidpoints,
        SelectionRenderContext context)
    {
        var documentContext = context.DocumentContext;
        var bounds = mergedBounds;
        var previewOBBCorners = documentContext.RealRotationCorners;
        if (context.DocumentContext.IsDragControlPoint && documentContext.IsRotationPreview && previewOBBCorners is { Length: >= 4 })
        {
            SelectionStrokeRenderer.DrawSolidFrame(canvas, previewOBBCorners, SKColors.Red, context);
            bounds = SelectionFrameFactory.ComputeBounds(previewOBBCorners);
        }

        var previewSkewOBBCorners = documentContext.RealSkewOBBCorners;
        if (context.DocumentContext.IsDragControlPoint && documentContext.IsSkewPreview && previewSkewOBBCorners is { Length: >= 4 })
        {
            SelectionStrokeRenderer.DrawSolidFrame(canvas, previewSkewOBBCorners, SKColors.Red, context);
            bounds = documentContext.RealSkewPreviewAABB;
        }

        var frame = SelectionFrameFactory.CreateMergedAabbRotateSkewFrame(bounds, (float)context.Viewport.Scale, hideEdgeMidpoints);
        if (frame.IsEmpty) return;

        SelectionHandleRenderer.DrawGlyphHandles(canvas, frame.GlyphHandles, SKColors.Black, context.Metrics.HandleHalfSize);

        var displayCenter = float.IsPositiveInfinity(center.X) || float.IsPositiveInfinity(center.Y) ? frame.Center : center;
        DrawCenterMarker(canvas, displayCenter, context);
    }

    private static void DrawCenterMarker(SKCanvas canvas, SKPoint center, SelectionRenderContext context)
    {
        if (!context.DocumentContext.IsAnchorPositionShow)
        {
            SelectionMarkerRenderer.DrawCenterIcon(canvas, center, context.Metrics.HandleHalfSize);
            return;
        }

        SelectionMarkerRenderer.DrawAnchorRealistic(
            canvas,
            context.DocumentContext.AnchorPosition,
            context.Viewport.Scale);
    }
}
