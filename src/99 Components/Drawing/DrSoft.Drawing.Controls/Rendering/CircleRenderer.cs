using System.Diagnostics;
using System.Windows.Shapes;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Helpers;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Model;
using DrSoft.Drawing.Rendering;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.Rendering
{
    [RendererFor(typeof(DrawCircle))]
    public class CircleRenderer : IRenderer
    {
        public bool CanRender(IShape obj)
        {
            return obj is DrawCircle;
        }
        public void Render(IShape shape, SKCanvas canvas, IViewport vp, SKPaintCache cache)
        {
            if (shape is not DrawCircle circle) return;
            canvas.Save();

            // 圆/椭圆主描边改为 world-path 渲染，避免 skew 再次作用到描边。
            var adjustedWidth = StrokeWidthHelper.ResolveViewportInvariantStrokeWidth(circle, vp);
            var strokePaint = cache.GetStrokePaint(circle.Pen.Color, adjustedWidth);

            var path = circle.GetPath(cache);
            try
            {
                var matrix = circle.GetTransformMatrix();
                path.Transform(circle.Matrix);
                var outlinePaint = OutlineStyleHelper.CreateOutlinePaint(strokePaint, circle);
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
            if (shape is DrawCircle circle)
            {
                //float radiusX = circle.Width / 2f;
                //float radiusY = circle.Height / 2f;
                float radiusX = circle.DrawingRadiusX;
                float radiusY = circle.DrawingRadiusY;

                bool widthTooSmall = radiusX <= 0.0001f;
                bool heightTooSmall = radiusY <= 0.0001f;
                if (widthTooSmall || heightTooSmall)
                {
                    return;
                }

                var path = paintCache.GetPath();
                try
                {
                    // 预览阶段 SharpCenter 尚未提交，从 Points[0] 读取实时圆心
                    var center = circle.Points.Count > 0 ? circle.Points[0] : circle.SharpCenter;
                    bool isEllipse = Math.Abs(circle.DrawingRadiusX - circle.DrawingRadiusY) > float.Epsilon;
                    if (isEllipse)
                    {
                        float left = center.X - radiusX;
                        float right = center.X + radiusX;
                        float bottom = center.Y - radiusY;
                        float top = center.Y + radiusY;

                        path.AddOval(new SKRect(left, bottom, right, top));
                    }
                    else
                    {
                        path.AddCircle(center.X, center.Y, radiusX);
                    }

                    var outlinePaint = OutlineStyleHelper.CreateOutlinePaint(strokePaint, circle);
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
            if (shape is not DrawCircle circle || hatchable == null || hatchable.HatchPattern == null
              || hatchable.HatchPattern.HatchLineObjects == null || hatchable.HatchPattern.HatchLineObjects.Count == 0) return;

            canvas.Save();

            // 应用变换矩阵（叠加到当前变换）
            var matrix = circle.GetTransformMatrix();
            canvas.Concat(matrix);

            var lines = hatchable.HatchPattern.HatchLineObjects;
            SKMatrix totalMatrix = canvas.TotalMatrix;
            float scaleX = Math.Abs(totalMatrix.ScaleX);

            // 动态LOD阈值 + 填充线降采样
            float lodThreshold = HatchRenderHelper.ComputeLodThreshold(lines.Count);
            var renderLines = HatchRenderHelper.SampleLines(lines, scaleX);
            float adjustedWidth = HatchRenderHelper.ComputeProgressiveStrokeWidth(lines, renderLines, scaleX, circle.Pen.StrokeWidth, vp.Scale);

            if (circle.HatchParamInfo.FillStyleIndex == 0)
            {
                var fillPaint = cache.GetStrokePaint(SKColor.Parse(circle.HatchParamInfo.FillColor), adjustedWidth);
                using (var path = HatchRenderHelper.BuildBatchPath(renderLines))
                {
                    canvas.DrawPath(path, fillPaint);
                }
            }
            else if (circle.HatchParamInfo.FillStyleIndex == 1 || circle.HatchParamInfo.FillStyleIndex == 2)
            {
                if (scaleX <= lodThreshold)
                {
                    var fillPaint = cache.GetStrokePaint(SKColor.Parse(circle.HatchParamInfo.FillColor), adjustedWidth);
                    using (var path = HatchRenderHelper.BuildBatchPath(renderLines))
                    {
                        canvas.DrawPath(path, fillPaint);
                    }
                }
                else
                {
                    HatchRenderHelper.RenderDashLinesIndividually(
                        canvas, renderLines,
                        SKColor.Parse(circle.HatchParamInfo.FillColor),
                        scaleX, circle.HatchParamInfo.FillStyleIndex);
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
            if (shape is not DrawCircle circle) return;
            if (circle is not IHatchable hatchable) return;
            if (!hatchable.IsHatchEnabled) return;
            if (hatchable.HatchInfo == null) return;

            canvas.Save();

            // 应用变换矩阵（叠加到当前变换）
            var matrix = circle.GetTransformMatrix();
            canvas.Concat(matrix);

            // 根据视口缩放比例调整线宽，保持视觉上的恒定线宽
            var adjustedWidth = circle.Pen.StrokeWidth * 6.83f / vp.Scale;
            var fillPaint = cache.GetStrokePaint(circle.Pen.Color, adjustedWidth);

            var hatchPattern = hatchable.HatchPattern;
            hatchPattern?.Primitives?.ForEach(line =>
            {
                if (line is DrawPolyLines drawLine)
                {
                    canvas.DrawLine(new SKPoint((float)drawLine.Points[0].X, (float)drawLine.Points[0].Y), new SKPoint((float)drawLine.Points[1].X, (float)drawLine.Points[1].Y), fillPaint);
                }
            });

            canvas.Restore();
        }*/
    }
}
