using System.Diagnostics;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Helpers;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Model;
using DrSoft.Drawing.Rendering;
using SkiaSharp;
using static System.Net.Mime.MediaTypeNames;

namespace DrSoft.Drawing.Controls.Rendering
{
    [RendererFor(typeof(DrawPolygon))]
    public class PolygonRenderer : IRenderer
    {
        public bool CanRender(IShape obj)
        {
            return obj is DrawPolygon;
        }
        //public void Render(IShape shape, SKCanvas canvas, IViewport vp, SKPaintCache cache)
        //{
        //    if (shape is not DrawPolygon polygon) return;
        //    if (polygon.Points.Count < 3) return;

        //    canvas.Save();

        //    // 多边形主描边改为 world-path 渲染，避免 skew 再次作用到描边。
        //    var adjustedWidth = StrokeWidthHelper.ResolveViewportInvariantStrokeWidth(polygon, vp);
        //    var strokePaint = cache.GetStrokePaint(polygon.Pen.Color, adjustedWidth);

        //    var path = polygon.GetPath(cache);
        //    try
        //    {
        //        var matrix = polygon.GetTransformMatrix();
        //        path.Transform(matrix);
        //        var outlinePaint = OutlineStyleHelper.CreateOutlinePaint(strokePaint, polygon);
        //        canvas.DrawPath(path, outlinePaint ?? strokePaint);
        //        outlinePaint?.Dispose();
        //    }
        //    finally
        //    {
        //        cache.ReturnPath(path);
        //    }

        //    canvas.Restore();
        //}


        public void Render(IShape shape, SKCanvas canvas, IViewport vp, SKPaintCache cache)
        {
            if (shape is not DrawPolygon polygon) return;
            if (polygon.Points.Count < 3) return;

            canvas.Save();

            // 多边形主描边改为 world-path 渲染，避免 skew 再次作用到描边。
            var adjustedWidth = StrokeWidthHelper.ResolveViewportInvariantStrokeWidth(polygon, vp);
            var strokePaint = cache.GetStrokePaint(polygon.Pen.Color, adjustedWidth);

            var path = polygon.GetPath(cache);
            try
            {
                //var matrix = polygon.GetTransformMatrix();
                path.Transform(polygon.Matrix);
                var outlinePaint = OutlineStyleHelper.CreateOutlinePaint(strokePaint, polygon);
                canvas.DrawPath(path, outlinePaint ?? strokePaint);
                outlinePaint?.Dispose();
            }
            finally
            {
                cache.ReturnPath(path);
            }

            canvas.Restore();
        }

        public void PreviewRender(IShape shape, SKCanvas canvas, SKPaint strokePaint, SKPaintCache paintCache)
        {
            if (shape is DrawPolygon polygon && polygon.Points.Count >= 3)
            {
                // 预览渲染与 Render 保持一致：使用 GetPath+Matrix
                var path = polygon.GetPath(paintCache);
                try
                {
                    path.Transform(polygon.Matrix);
                    var outlinePaint = OutlineStyleHelper.CreateOutlinePaint(strokePaint, polygon);
                    canvas.DrawPath(path, outlinePaint ?? strokePaint);
                    outlinePaint?.Dispose();
                }
                finally
                {
                    paintCache.ReturnPath(path);
                }
            }
        }

        public void RenderHatch(IShape shape, IHatchable hatchable, SKCanvas canvas, IViewport vp, SKPaintCache cache)
        {
            if (shape is not DrawPolygon polygon || hatchable == null || hatchable.HatchPattern == null
                || hatchable.HatchPattern.HatchLineObjects == null || hatchable.HatchPattern.HatchLineObjects.Count == 0) return;

            canvas.Save();

            // 应用变换矩阵（叠加到当前变换）
            var matrix = polygon.GetTransformMatrix();
            canvas.Concat(ref matrix);

            var lines = hatchable.HatchPattern.HatchLineObjects;
            SKMatrix totalMatrix = canvas.TotalMatrix;
            float scaleX = Math.Abs(totalMatrix.ScaleX);

            // 动态LOD阈值 + 填充线降采样
            float lodThreshold = HatchRenderHelper.ComputeLodThreshold(lines.Count);
            var renderLines = lines;
            //var renderLines = HatchRenderHelper.SampleLines(lines, scaleX);
            //float adjustedWidth = HatchRenderHelper.ComputeProgressiveStrokeWidth(lines, renderLines, scaleX, polygon.Pen.StrokeWidth, vp.Scale);
            var adjustedWidth = StrokeWidthHelper.ResolveScreenInvariantStrokeWidth(polygon, vp);

            if (polygon.HatchParamInfo.FillStyleIndex == 0)
            {
                var fillPaint = cache.GetStrokePaint(SKColor.Parse(polygon.HatchParamInfo.FillColor), adjustedWidth);
                using (var path = HatchRenderHelper.BuildBatchPath(renderLines))
                {
                    canvas.DrawPath(path, fillPaint);
                }
            }
            else if (polygon.HatchParamInfo.FillStyleIndex == 1 || polygon.HatchParamInfo.FillStyleIndex == 2)
            {
                if (scaleX <= lodThreshold)
                {
                    var fillPaint = cache.GetStrokePaint(SKColor.Parse(polygon.HatchParamInfo.FillColor), adjustedWidth);
                    using (var path = HatchRenderHelper.BuildBatchPath(renderLines))
                    {
                        canvas.DrawPath(path, fillPaint);
                    }
                }
                else
                {
                    HatchRenderHelper.RenderDashLinesIndividually(
                        canvas, renderLines,
                        SKColor.Parse(polygon.HatchParamInfo.FillColor),
                        scaleX, polygon.HatchParamInfo.FillStyleIndex);
                }
            }

            canvas.Restore();
        }

        /*/// <summary>
        /// 绘制填充线段
        /// </summary>
        /// <param name="shape"></param>
        /// <param name="canvas"></param>
        /// <param name="cache"></param>
        public void RenderHatch(IShape shape, SKCanvas canvas, IViewport vp, SKPaintCache cache)
        {
            if (shape is not DrawPolygon polygon) return;
            if (polygon is not IHatchable hatchable) return;
            if (!hatchable.IsHatchEnabled) return;
            if (hatchable.HatchInfo == null) return;

            canvas.Save();

            // 应用变换矩阵（叠加到当前变换）
            var matrix = polygon.GetTransformMatrix();
            canvas.Concat(ref matrix);

            // 根据视口缩放比例调整线宽，保持视觉上的恒定线宽
            var adjustedWidth = polygon.Pen.StrokeWidth * 6.83f / vp.Scale;
            var fillPaint = cache.GetStrokePaint(polygon.Pen.Color, adjustedWidth);

            var hatchPattern = hatchable.HatchPattern;
            if (hatchPattern?.Primitives == null)
            {
                canvas.Restore();
                return;
            }

            foreach (var line in hatchPattern.Primitives)
            {
                if (line is DrawPolyLines drawLine && drawLine.Points.Count >= 2)
                {
                    canvas.DrawLine(new SKPoint((float)drawLine.Points[0].X, (float)drawLine.Points[0].Y), new SKPoint((float)drawLine.Points[1].X, (float)drawLine.Points[1].Y), fillPaint);
                }
            }

            canvas.Restore();
        }*/
    }
}
