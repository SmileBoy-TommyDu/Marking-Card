using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Model;
using DrSoft.Drawing.Rendering;
using SkiaSharp;
using System.Diagnostics;

namespace DrSoft.Drawing.Controls.Rendering
{
    [RendererFor(typeof(DrawDot))]
    public class DotRenderer : IRenderer
    {
        public bool CanRender(IShape obj)
        {
            return obj is DrawDot;
        }
        public void Render(IShape shape, SKCanvas canvas, IViewport vp, SKPaintCache cache)
        {
            if (shape is not DrawDot dot) return;
            if (dot.Points.Count == 0) return;

            canvas.Save();

            // 获取填充画笔
            var fillPaint = cache.GetFillPaint(dot.Pen.Color);

            // 使用GetPath(cache)方法从路径池获取路径并绘制
            var path = dot.GetPath(cache);
            try
            {
                // 点总是填充的
                //path.Transform(dot.Matrix);
                canvas.DrawPath(path, fillPaint);
            }
            finally
            {
                cache.ReturnPath(path);
            }

            canvas.Restore();

            //Debug.WriteLine($"绘制点: 位置=({dot.X:F1}, {dot.Y:F1}), 半径={dot.Radius:F1}, 缩放={dot.ScaleX:F1}");
        }

        public void PreviewRender(IShape shape, SKCanvas canvas, SKPaint strokePaint, SKPaintCache paintCache)
        {
            if (shape.Points.Count == 0) return;

            if (shape is DrawDot dot)
            {
                // 绘制预览点（带变换效果）
                canvas.Save();

                // 使用GetPath(cache)获取预览路径
                var path = dot.GetPath(paintCache);
                try
                {
                    // 预览点也应该是实心的
                    path.Transform(dot.Matrix);
                    var fillPaint = paintCache.GetFillPaint(dot.Pen.Color);
                    canvas.DrawPath(path, fillPaint);
                }
                finally
                {
                    paintCache.ReturnPath(path);
                }

                canvas.Restore();
            }
            else
            {
                // 兼容旧的DrawObject方式
                var center = new SKPoint((float)shape.Points[0].X, (float)shape.Points[0].Y);
                canvas.DrawCircle(center, 4.0f, strokePaint);
            }

            Debug.WriteLine($"预览绘制点: 顶点数={shape.Points.Count}");
        }
    }
}