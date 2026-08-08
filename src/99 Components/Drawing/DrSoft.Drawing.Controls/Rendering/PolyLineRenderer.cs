using System.Windows.Media;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Helpers;
using DrSoft.Drawing.Model;
using DrSoft.Drawing.Rendering;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.Rendering
{
    [RendererFor(typeof(DrawPolyLines))]
    public class PolyLineRenderer : IRenderer
    {
        public bool CanRender(IShape obj)
        {
            return obj is DrawPolyLines;
        }
        public void Render(IShape shape, SKCanvas canvas, IViewport vp, SKPaintCache cache)
        {
            if (shape is not DrawPolyLines polyline) { return; }
            if (polyline.Points.Count < 2) return;

            canvas.Save();

            // 将局部路径先变换到世界坐标，再用世界坐标描边。
            // 这样对象倾斜不会再次作用到描边宽度。
            var adjustedWidth = StrokeWidthHelper.ResolveViewportInvariantStrokeWidth(polyline, vp);
            var strokePaint = cache.GetStrokePaint(polyline.Pen.Color, adjustedWidth);

            var path = polyline.GetPath(cache);
            try
            {
                var matrix = polyline.GetTransformMatrix();
                path.Transform(matrix);

                switch (polyline.LineStyle)
                {
                    case LineStyle.Solid:
                        var outlinePaint = OutlineStyleHelper.CreateOutlinePaint(strokePaint, polyline);
                        canvas.DrawPath(path, outlinePaint ?? strokePaint);
                        outlinePaint?.Dispose();
                        break;
                    case LineStyle.Dashed:
                        using (SKPaint paint = new SKPaint())
                        {
                            paint.Style = SKPaintStyle.Stroke;
                            paint.StrokeWidth = adjustedWidth;
                            paint.StrokeCap = SKStrokeCap.Round;
                            paint.Color = polyline.Pen.Color;
                            paint.IsAntialias = true;
                            paint.PathEffect = SKPathEffect.CreateDash(new float[] { 0.1f, 0.1f }, 0);
                            canvas.DrawPath(path, paint);
                        }
                        break;
                    case LineStyle.Dotted:
                        using (SKPaint paint = new SKPaint())
                        {
                            paint.Style = SKPaintStyle.Stroke;
                            paint.StrokeWidth = adjustedWidth;
                            paint.StrokeCap = SKStrokeCap.Round;
                            paint.Color = polyline.Pen.Color;
                            paint.IsAntialias = true;
                            strokePaint.PathEffect = SKPathEffect.CreateDash(new float[] { 0f, 0.1f }, 0);
                            canvas.DrawPath(path, paint);
                        }
                        break;
                }

                //canvas.DrawPath(path, strokePaint);
            }
            finally
            {
                cache.ReturnPath(path);
            }

            canvas.Restore();
        }

        public void PreviewRender(IShape shape, SKCanvas canvas, SKPaint strokePaint, SKPaintCache paintCache)
        {
            if (shape is DrawPolyLines polyline && polyline.Points.Count >= 2)
            {
                // 使用路径绘制多线段（支持旋转）
                var path = paintCache.GetPath();
                try
                {
                    // 移动到第一个顶点
                    var firstPoint = shape.Points[0];
                    path.MoveTo((float)firstPoint.X, (float)firstPoint.Y);

                    // 添加其他顶点
                    for (int i = 1; i < shape.Points.Count; i++)
                    {
                        var point = shape.Points[i];
                        path.LineTo((float)point.X, (float)point.Y);
                    }

                    // 绘制路径
                    var outlinePaint = OutlineStyleHelper.CreateOutlinePaint(strokePaint, polyline);
                    canvas.DrawPath(path, outlinePaint ?? strokePaint);
                    outlinePaint?.Dispose();
                }
                finally
                {
                    paintCache.ReturnPath(path);
                }

            }
        }
    }
}
