using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Controls.Tools;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using SkiaSharp;
using System.Collections.Generic;
using System.Linq;

namespace DrSoft.Drawing.Rendering;

public class SelectionRenderer
{
    private readonly SKColor rectColor = new(255, 91, 0);
    private readonly SKColor hatchColor = SKColors.Blue;
    private readonly SKColor referenceColor = new(0, 120, 215);
    private static readonly SKColor forwardSelectionFillColor = new(0, 120, 215, 25);
    private static readonly SKColor forwardSelectionStrokeColor = new(0, 120, 215, 180);
    private static readonly SKColor reverseSelectionFillColor = new(217, 221, 221, 90);
    private static readonly SKColor reverseSelectionStrokeColor = new(176, 184, 184);

    private readonly SelectionModeStateMachine _stateMachine;

    public SelectionRenderer()
    {
        _stateMachine = new SelectionModeStateMachine(rectColor, hatchColor);
    }

    public void RenderHandles(SKCanvas canvas, IEnumerable<DrawObject> selected, SelectState selectState, IViewport vp)
    {
        var renderContext = new SelectionRenderContext(
            DocumentContext.Instance,
            vp,
            SelectionRenderMetrics.FromViewport(vp));
        var modeRenderer = _stateMachine.Resolve(selectState);

        foreach (var drawObject in selected)
        {
            var geometry = SelectionGeometryBuilder.BuildForSinglePreviewOBBSelection(drawObject);
            if (geometry.Corners.Length == 0 && drawObject is not DrawDot)
            {
                continue;
            }

            canvas.Save();

            if (drawObject is DrawDot)
            {
                if (!geometry.Center.IsEmpty)
                {
                    SelectionMarkerRenderer.DrawCenterCross(
                        canvas,
                        geometry.Center,
                        rectColor,
                        vp,
                        renderContext.Metrics.LineWidth);
                }
            }
            else if (drawObject is not DrawingHatch)
            {
                modeRenderer.RenderSingle(canvas, drawObject, renderContext);
            }

            canvas.Restore();
        }
    }

    internal void RenderMergedHandles(
        SKCanvas canvas,
        IReadOnlyList<DrawObject> selectedDrawObjects,
        SKRect mergedBounds,
        SelectState selectState,
        SKPoint center,
        IViewport vp,
        bool hideEdgeMidpoints)
    {
        if (mergedBounds.IsEmpty)
        {
            return;
        }

        var renderContext = new SelectionRenderContext(
            DocumentContext.Instance,
            vp,
            SelectionRenderMetrics.FromViewport(vp));
        var modeRenderer = _stateMachine.Resolve(selectState);
        modeRenderer.RenderMerged(
            canvas,
            selectedDrawObjects,
            mergedBounds,
            center,
            hideEdgeMidpoints,
            renderContext);
    }

    public void RenderRubberBand(SKCanvas canvas, SKPoint a, SKPoint b, IViewport vp, bool isForwardSelection = true)
    {
        var rect = new SKRect(
            System.Math.Min(a.X, b.X),
            System.Math.Min(a.Y, b.Y),
            System.Math.Max(a.X, b.X),
            System.Math.Max(a.Y, b.Y));

        SKColor fillColor;
        SKColor strokeColor;
        if (isForwardSelection)
        {
            fillColor = forwardSelectionFillColor;
            strokeColor = forwardSelectionStrokeColor;
        }
        else
        {
            fillColor = reverseSelectionFillColor;
            strokeColor = reverseSelectionStrokeColor;
        }

        float adjustedStrokeWidth = 0.6f / vp.Scale;
        using var fill = new SKPaint { Style = SKPaintStyle.Fill, Color = fillColor };
        using var stroke = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = strokeColor,
            StrokeWidth = adjustedStrokeWidth,
        };

        canvas.DrawRect(rect, fill);
        canvas.DrawRect(rect, stroke);
    }

    public void RenderReferenceShapeIndicator(SKCanvas canvas, DrawObject referenceShape, IViewport vp)
    {
        float adjustedLineWidth = DrawObject.lineWidth / vp.Scale;
        float adjustedSharpeOffset = DrawObject.sharpeOffset / vp.Scale;
        var bbox = referenceShape.GetAABB();
        if (bbox.IsEmpty)
        {
            return;
        }

        var indicatorRect = new SKRect(
            bbox.Left - adjustedSharpeOffset,
            bbox.Top - adjustedSharpeOffset,
            bbox.Right + adjustedSharpeOffset,
            bbox.Bottom + adjustedSharpeOffset);

        using var strokePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = referenceColor,
            StrokeWidth = adjustedLineWidth,
            IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash(new[] { 3.42f / (float)vp.Scale, 2.05f / (float)vp.Scale }, 0),
        };

        canvas.DrawRect(indicatorRect, strokePaint);
    }

    private static Rect2D CalculateMergedBounds(List<SKRect> boundsList)
    {
        if (boundsList.Count == 0)
        {
            return new Rect2D(0, 0, 0, 0);
        }

        float minX = boundsList.Min(bounds => bounds.Left);
        float maxX = boundsList.Max(bounds => bounds.Right);
        float minY = boundsList.Min(bounds => bounds.Top);
        float maxY = boundsList.Max(bounds => bounds.Bottom);

        return new Rect2D(minX, minY, maxX - minX, maxY - minY);
    }
}
