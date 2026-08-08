using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Helpers;
using DrSoft.Drawing.Model;
using DrSoft.Drawing.Rendering;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.Rendering
{
    [RendererFor(typeof(DrawCubicPath))]
    public class CubicPathRenderer : IRenderer
    {
        public bool CanRender(IShape obj) => obj is DrawCubicPath;

        public void Render(IShape shape, SKCanvas canvas, IViewport vp, SKPaintCache cache)
        {
            if (shape is not DrawCubicPath cubic) return;
            if (cubic.Points.Count < 2) return;

            canvas.Save();

            // CubicPath 主描边改为 world-path 渲染，避免 skew 再次作用到描边。
            var adjustedWidth = StrokeWidthHelper.ResolveViewportInvariantStrokeWidth(cubic, vp);
            var strokePaint = cache.GetStrokePaint(cubic.Pen.Color, adjustedWidth);

            var path = cubic.GetPath(cache);
            try
            {
                var matrix = cubic.GetTransformMatrix();
                path.Transform(matrix);
                var outlinePaint = OutlineStyleHelper.CreateOutlinePaint(strokePaint, cubic);
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
            if (shape is not DrawCubicPath cubic) return;
            if (cubic.Points.Count < 2 || cubic.ControlHandles.Count != cubic.Points.Count * 2) return;

            var path = paintCache.GetPath();
            try
            {
                DrawCubicPath.BuildWorldPath(cubic.Points, cubic.ControlHandles, cubic.IsClosed);
                // 直接用世界坐标路径绘制（预览阶段图形尚未加入画布）
                int n = cubic.Points.Count;
                path.MoveTo(cubic.Points[0]);
                for (int i = 0; i < n - 1; i++)
                {
                    var cp1 = cubic.ControlHandles[i * 2];
                    var cp2 = cubic.ControlHandles[(i + 1) * 2 + 1];
                    path.CubicTo(cp1, cp2, cubic.Points[i + 1]);
                }
                if (cubic.IsClosed && n >= 2)
                {
                    var cp1 = cubic.ControlHandles[(n - 1) * 2];
                    var cp2 = cubic.ControlHandles[1];
                    path.CubicTo(cp1, cp2, cubic.Points[0]);
                    path.Close();
                }
                var outlinePaint = OutlineStyleHelper.CreateOutlinePaint(strokePaint, cubic);
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
