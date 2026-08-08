using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Shapes;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using DrSoft.Drawing.Rendering;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.Rendering
{
    [RendererFor(typeof(DrawingHatch))]
    public class DrawingHatchRender : IRenderer
    {
        /// <summary>
        /// DrawingHatch 被选中时的高亮颜色（绿色）。
        /// 替代普通选择框，使同一图形的多个填充物可通过颜色区分当前选中的是哪一个。
        /// </summary>
        private static readonly SKColor SelectedHighlightColor = SKColor.Parse("#FF5B00");

        public bool CanRender(IShape obj) => obj is DrawingHatch;

        public void PreviewRender(IShape shape, SKCanvas canvas, SKPaint strokePaint, SKPaintCache cache) { }

        public void Render(IShape shape, SKCanvas canvas, IViewport vp, SKPaintCache cache)
        {
            if (shape is not DrawingHatch drawingHatch || drawingHatch == null) return;

            if (drawingHatch.Boundaries.Count == 0)
            {
                // 无 TargetShapes 的直接渲染模式（DXF 导入场景）
                RenderChildrenDirectly(drawingHatch, canvas, vp, cache);
                return;
            }

            // 使用 DrawingHatch 自身的 HatchParamInfo 渲染，而非目标图形的 HatchParamInfo。
            // 同一目标图形可能被多次填充（不同颜色），每次填充对应独立的 DrawingHatch，
            // 若从目标图形读取则会被后一次填充覆盖导致颜色错乱。
            var hatchParam = drawingHatch.HatchParamInfo;
            if (hatchParam == null) return;

            // 选中时用高亮颜色渲染填充线，以替代普通选择框的视觉反馈。
            // 扩展点：如需支持更多高亮状态（悬停、焦点等），在此方法中统一处理。
            SKColor renderColor = ResolveRenderColor(drawingHatch, hatchParam);

            var lines = CollectCurrentLines(drawingHatch.Children);

            if (lines.Count == 0) return;

            foreach (var targetShape in drawingHatch.Boundaries)
            {
                if (targetShape is not DrawObject drawObject) continue;
                RenderHatchLines(drawingHatch, targetShape as DrawObject, lines, hatchParam, renderColor, canvas, vp, cache);
            }
        }

        // ── 颜色解析 ──────────────────────────────────────────────────────────

        /// <summary>
        /// 解析本次渲染应使用的颜色。
        /// 选中时返回高亮色，未选中时返回填充参数中定义的原始颜色。
        /// </summary>
        protected virtual SKColor ResolveRenderColor(DrawingHatch hatch, HatchParamDto hatchParam)
        {
            if (hatch.IsSelected)
                return SelectedHighlightColor;

            return SKColor.Parse(hatchParam.FillColor);
        }

        private static List<(SKPoint Start, SKPoint End)> CollectCurrentLines(IEnumerable<IShape> children)
        {
            var lines = new List<(SKPoint Start, SKPoint End)>();
            foreach (var child in children)
            {
                if (TryGetCurrentLine(child, out var line))
                {
                    lines.Add(line);
                }
            }

            return lines;
        }

        private static bool TryGetCurrentLine(IShape shape, out (SKPoint Start, SKPoint End) line)
        {
            line = default;

            if (shape is not DrawObject drawObject || drawObject.Points == null || drawObject.Points.Count < 2)
                return false;

            var start = MapCommittedPointToCurrent(drawObject, drawObject.Points[0]);
            var end = MapCommittedPointToCurrent(drawObject, drawObject.Points[1]);
            line = (start, end);
            return true;
        }

        private static SKPoint MapCommittedPointToCurrent(DrawObject drawObject, SKPoint committedWorldPoint)
        {
            var committedTransform = drawObject.GetTransformMatrix();
            var inverseTransform = committedTransform.Invert();
            var localPoint = inverseTransform.MapPoint(committedWorldPoint);
            var currentTransform = committedTransform.PostConcat(drawObject.DeltaMatrix);
            return currentTransform.MapPoint(localPoint);
        }

        // ── 线段渲染核心 ──────────────────────────────────────────────────────

        /// <summary>
        /// 对给定填充线列表，根据目标图形类型选择采样/宽度策略，然后统一渲染。
        /// </summary>
        private void RenderHatchLines(
            DrawingHatch drawingHatch,
            DrawObject? targetShape,
            List<(SKPoint, SKPoint)> lines,
            HatchParamDto hatchParam,
            SKColor renderColor,
            SKCanvas canvas,
            IViewport vp,
            SKPaintCache cache)
        {
            canvas.Save();

            SKMatrix totalMatrix = canvas.TotalMatrix;
            float scaleX = Math.Abs(totalMatrix.ScaleX);
            float lodThreshold = HatchRenderHelper.ComputeLodThreshold(lines.Count);

            List<(SKPoint, SKPoint)> renderLines = lines;
            float adjustedWidth = targetShape!.Pen.StrokeWidth * 6.83f / vp.Scale;

           // // 各图形类型在采样策略和笔画宽度上存在细微差异，集中在此处理
           // if (targetShape is DrawRectangle rectangle)
           // {
           //     renderLines = (hatchParam.FillTypeIndex == 0 || hatchParam.FillTypeIndex == 1 || hatchParam.FillTypeIndex == 2 || hatchParam.FillTypeIndex == 3)
           //         ? HatchRenderHelper.SampleLines(lines, scaleX)
           //         : lines;
           //     //      adjustedWidth = (hatchParam.FillTypeIndex == 0 || hatchParam.FillTypeIndex == 1) ? HatchRenderHelper.ComputeProgressiveStrokeWidth(
           //     //lines, renderLines, scaleX, rectangle.Pen.StrokeWidth, vp.Scale) : targetShape!.Pen.StrokeWidth * 6.83f / vp.Scale;

           //     //renderLines = HatchRenderHelper.SampleLines(lines, scaleX);
           //     //adjustedWidth = HatchRenderHelper.ComputeProgressiveStrokeWidth(
           //     //    lines, renderLines, scaleX, rectangle.Pen.StrokeWidth, vp.Scale);
           // }
           // else if (targetShape is DrawPolygon polygon)
           // {
           //     renderLines = (hatchParam.FillTypeIndex == 0 || hatchParam.FillTypeIndex == 1)
           //? HatchRenderHelper.SampleLines(lines, scaleX)
           //: lines;
           // }
           // else if (targetShape is DrawCircle circle)
           // {
           //     renderLines = (hatchParam.FillTypeIndex == 0 || hatchParam.FillTypeIndex == 1)
           //         ? HatchRenderHelper.SampleLines(lines, scaleX)
           //         : lines;
           //     //adjustedWidth = HatchRenderHelper.ComputeProgressiveStrokeWidth(
           //     //    lines, renderLines, scaleX, circle.Pen.StrokeWidth, vp.Scale);
           // }
           // else if (targetShape is DrawArc arc)
           // {
           //     renderLines = HatchRenderHelper.SampleLines(lines, scaleX);
           //     //adjustedWidth = HatchRenderHelper.ComputeProgressiveStrokeWidth(
           //     //    lines, renderLines, scaleX, arc.Pen.StrokeWidth, vp.Scale);
           // }
           // else if (targetShape is DrawText text)
           // {
           //     renderLines = HatchRenderHelper.SampleLines(lines, scaleX);
           //     //adjustedWidth = HatchRenderHelper.ComputeProgressiveStrokeWidth(
           //     //    lines, renderLines, scaleX, text.Pen.StrokeWidth, vp.Scale);
           // }
           // else
           // {
           //     // DrawPolyLines / DrawPolygon / DrawBezier / DrawArbitraryCurve/DrawCombination 等
           //     // 使用模型坐标固定宽度，由 canvas transform 自然缩放
           //     renderLines = lines;
           //     //adjustedWidth = targetShape?.Pen?.StrokeWidth ?? 0.25f;
           //     //adjustedWidth = targetShape!.Pen.StrokeWidth * 6.83f / vp.Scale;
           // }

            DrawLinesWithStyle(canvas, renderLines, hatchParam, renderColor, adjustedWidth, scaleX, lodThreshold, vp);

            canvas.Restore();
        }

        /// <summary>
        /// 按填充样式（实线/虚线/点线）绘制填充线，LOD 降级策略统一在此处理。
        /// </summary>
        private static void DrawLinesWithStyle(
            SKCanvas canvas,
            List<(SKPoint Start, SKPoint End)> renderLines,
            HatchParamDto hatchParam,
            SKColor renderColor,
            float adjustedWidth,
            float scaleX,
            float lodThreshold,
            IViewport vp)
        {
            if (hatchParam.FillStyleIndex == 0)
            {
                // 实线填充
                var fillPaint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = renderColor,
                    StrokeWidth = adjustedWidth,
                    IsAntialias = true,
                    StrokeCap = SKStrokeCap.Round,
                    StrokeJoin = SKStrokeJoin.Round,
                };
                using (fillPaint)
                using (var path = HatchRenderHelper.BuildBatchPath(renderLines))
                {
                    canvas.DrawPath(path, fillPaint);
                }
            }
            else if (hatchParam.FillStyleIndex == 1 || hatchParam.FillStyleIndex == 2)
            {
                if (scaleX <= lodThreshold)
                {
                    // LOD 降级：缩小状态下用实线近似，减少绘制开销
                    var fillPaint = new SKPaint
                    {
                        Style = SKPaintStyle.Stroke,
                        Color = renderColor,
                        StrokeWidth = adjustedWidth,
                        IsAntialias = true,
                        StrokeCap = SKStrokeCap.Round,
                        StrokeJoin = SKStrokeJoin.Round,
                    };
                    using (fillPaint)
                    using (var path = HatchRenderHelper.BuildBatchPath(renderLines))
                    {
                        canvas.DrawPath(path, fillPaint);
                    }
                }
                else
                {
                    HatchRenderHelper.RenderDashLinesIndividually(
                        canvas, renderLines, renderColor, scaleX, hatchParam.FillStyleIndex);
                }
            }
        }

        // ── DXF 直接渲染（无 TargetShapes） ───────────────────────────────────

        /// <summary>
        /// 无 TargetShapes 时直接渲染 Children（DXF 导入场景）。
        /// DrawPolyLines → 批量路径绘制；DrawDot → 圆形绘制。
        /// </summary>
        private void RenderChildrenDirectly(DrawingHatch drawingHatch, SKCanvas canvas, IViewport vp, SKPaintCache cache)
        {
            if (drawingHatch.Children == null || drawingHatch.Children.Count == 0) return;

            canvas.Save();

            SKMatrix totalMatrix = canvas.TotalMatrix;
            float scaleX = Math.Abs(totalMatrix.ScaleX);

            var lines = CollectCurrentLines(drawingHatch.Children);

            if (lines.Count > 0)
            {
                float lodThreshold = HatchRenderHelper.ComputeLodThreshold(lines.Count);
                List<(SKPoint, SKPoint)> renderLines = lines;
                float adjustedWidth = drawingHatch.Children.FirstOrDefault()!.Pen.StrokeWidth * 6.83f / vp.Scale;
                //var renderLines = HatchRenderHelper.SampleLines(lines, scaleX);
                //float adjustedWidth = HatchRenderHelper.ComputeProgressiveStrokeWidth(
                //    lines, renderLines, scaleX, 0.3f, vp.Scale);

                SKColor lineColor = drawingHatch.IsSelected ? SelectedHighlightColor : SKColors.Black;
                var fillPaint = cache.GetStrokePaint(lineColor, adjustedWidth);
                using (var path = HatchRenderHelper.BuildBatchPath(renderLines))
                {
                    canvas.DrawPath(path, fillPaint);
                }
            }

            // 绘制点
            foreach (var child in drawingHatch.Children)
            {
                if (child is DrawDot dot && dot.Points?.Count >= 1)
                {
                    SKColor dotColor = drawingHatch.IsSelected ? SelectedHighlightColor : SKColors.Black;
                    var dotPaint = cache.GetFillPaint(dotColor);
                    canvas.DrawCircle(dot.Points[0].X, dot.Points[0].Y, dot.Radius, dotPaint);
                }
            }

            canvas.Restore();
        }
    }
}
