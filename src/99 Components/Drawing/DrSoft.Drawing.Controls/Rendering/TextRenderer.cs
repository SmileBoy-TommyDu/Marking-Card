using System.Windows.Shapes;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Helpers;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Model;
using DrSoft.Drawing.Rendering;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.Rendering
{
    [RendererFor(typeof(DrawText))]
    public class TextRenderer : IRenderer
    {
        public bool CanRender(IShape obj)
        {
            return obj is DrawText;
        }
        public void Render(IShape shape, SKCanvas canvas, IViewport vp, global::DrSoft.Drawing.Rendering.SKPaintCache cache)
        {
            var text = shape as DrawText;
            if (text.TextPath == null) return;

            canvas.Save();

            // 文字主描边改为 world-path 渲染，避免 skew 再次作用到描边。
            var adjustedWidth = StrokeWidthHelper.ResolveViewportInvariantStrokeWidth(text, vp);
            var strokePaint = new SKPaint
            {
                Color = text.Pen.Color,
                StrokeWidth = adjustedWidth,
                Style = SKPaintStyle.Stroke,
                IsAntialias = true,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round
            };

            var path = text.GetPath(cache);
            try
            {
                path.Transform(text.Matrix);

                var outlinePaint = OutlineStyleHelper.CreateOutlinePaint(strokePaint, text);
                canvas.DrawPath(path, outlinePaint ?? strokePaint);
                outlinePaint?.Dispose();
            }
            finally
            {
                cache.ReturnPath(path);
            }

            //测试外框是否与文字路径重叠中心点重叠
            if (false)
            {
                float adjustedLineWidth = DrawObject.lineWidth / vp.Scale;
                using var path1 = text.GetPath();
                float centerX = path1.Bounds.MidX;
                float centerY = path1.Bounds.MidY;
                float width = path1.Bounds.Width;
                float height = path1.Bounds.Height;


                //path1.GetBounds(out SKRect rect);
                var originalSelectionBoxCorners = new[]
                {
                    new SKPoint(centerX-width/2- 0.4f, centerY-height/2 - 0.4f),  // bottom-left
                    new SKPoint(centerX+width/2+0.4f , centerY-height/2 - 0.4f),   // bottom-right
                    new SKPoint(centerX+width/2+0.4f, centerY+height/2 + 0.4f),    // top-right
                    new SKPoint(centerX-width/2- 0.4f, centerY+height/2 + 0.4f)    // top-left
                };

                using var path2 = new SKPath();

                // 构建旋转后选择框的路径
                path2.MoveTo(originalSelectionBoxCorners[0].X, originalSelectionBoxCorners[0].Y);
                for (int i = 1; i < originalSelectionBoxCorners.Length; i++)
                {
                    path2.LineTo(originalSelectionBoxCorners[i].X, originalSelectionBoxCorners[i].Y);
                }
                path2.Close();

                using var strokePaint2 = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = SKColors.Blue,
                    StrokeWidth = adjustedLineWidth, // 使用调整后的线宽
                    IsAntialias = true,
                    PathEffect = SKPathEffect.CreateDash(new float[] { 3.42f / vp.Scale, 2.05f / vp.Scale }, 0),
                };

                // 绘制选择框边框
                canvas.DrawPath(path2, strokePaint2);

            }

            canvas.Restore();
        }

        public void PreviewRender(IShape shape, SKCanvas canvas, SKPaint strokePaint, SKPaintCache paintCache)
        {
            if (shape is not DrawText text) return;
            if (text.Points.Count >= 2)
            {
                if (text.TextPath == null) return;

                using (var paint = new SKPaint())
                {
                    // 设置描边样式
                    paint.Style = SKPaintStyle.Stroke;
                    paint.StrokeWidth = 1;
                    paint.Color = SKColors.Black;
                    canvas.DrawPath(text.TextPath, paint);
                }
            }
        }

        public void RenderHatch(IShape shape, IHatchable hatchable, SKCanvas canvas, IViewport vp, SKPaintCache cache)
        {
            if (shape is not DrawText text || hatchable == null || hatchable.HatchPattern == null
       || hatchable.HatchPattern.HatchLineObjects == null || hatchable.HatchPattern.HatchLineObjects.Count == 0) return;

            canvas.Save();

            // 应用变换矩阵（叠加到当前变换）
            var matrix = text.GetTransformMatrix();
            canvas.Concat(matrix);

            var lines = hatchable.HatchPattern.HatchLineObjects;
            SKMatrix totalMatrix = canvas.TotalMatrix;
            float scaleX = Math.Abs(totalMatrix.ScaleX);

            // 动态LOD阈值 + 填充线降采样
            float lodThreshold = HatchRenderHelper.ComputeLodThreshold(lines.Count);
            var renderLines = HatchRenderHelper.SampleLines(lines, scaleX);
            float adjustedWidth = HatchRenderHelper.ComputeProgressiveStrokeWidth(lines, renderLines, scaleX, text.Pen.StrokeWidth, vp.Scale);

            if (text.HatchParamInfo.FillStyleIndex == 0)
            {
                var fillPaint = cache.GetStrokePaint(SKColor.Parse(text.HatchParamInfo.FillColor), adjustedWidth);
                using (var path = HatchRenderHelper.BuildBatchPath(renderLines))
                {
                    canvas.DrawPath(path, fillPaint);
                }
            }
            else if (text.HatchParamInfo.FillStyleIndex == 1 || text.HatchParamInfo.FillStyleIndex == 2)
            {
                if (scaleX <= lodThreshold)
                {
                    var fillPaint = cache.GetStrokePaint(SKColor.Parse(text.HatchParamInfo.FillColor), adjustedWidth);
                    using (var path = HatchRenderHelper.BuildBatchPath(renderLines))
                    {
                        canvas.DrawPath(path, fillPaint);
                    }
                }
                else
                {
                    HatchRenderHelper.RenderDashLinesIndividually(
                        canvas, renderLines,
                        SKColor.Parse(text.HatchParamInfo.FillColor),
                        scaleX, text.HatchParamInfo.FillStyleIndex);
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
            if (shape is not DrawText text) return;
            if (text is not IHatchable hatchable) return;
            if (!hatchable.IsHatchEnabled) return;
            if (hatchable.HatchInfo == null) return;

            canvas.Save();

            // 应用变换矩阵（叠加到当前变换）
            var matrix = text.GetTransformMatrix();
            canvas.Concat(matrix);

            // 根据视口缩放比例调整线宽，保持视觉上的恒定线宽
            var adjustedWidth = StrokeWidthHelper.ResolveScreenInvariantStrokeWidth(text, vp);
            var fillPaint = cache.GetStrokePaint(text.Pen.Color, adjustedWidth);

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
