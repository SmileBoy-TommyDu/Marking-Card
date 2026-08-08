using System.Diagnostics;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Helpers;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Model;
using DrSoft.Drawing.Rendering;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.Rendering
{
    [RendererFor(typeof(DrawArbitraryCurve))]
    internal class ArbitraryCurveRenderer : IRenderer
    {
        public bool CanRender(IShape obj) => obj is DrawArbitraryCurve;

        public void Render(IShape shape, SKCanvas canvas, IViewport vp, SKPaintCache cache)
        {
            if (shape is not DrawArbitraryCurve curve) return;
            if (curve.Points.Count < 2) return;

            canvas.Save();

            // 任意曲线主描边改为 world-path 渲染，避免 skew 再次作用到描边。
            var adjustedWidth = StrokeWidthHelper.ResolveViewportInvariantStrokeWidth(curve, vp);
            var strokePaint = cache.GetStrokePaint(curve.Pen.Color, adjustedWidth);

            var path = curve.GetPath(cache);
            try
            {
                var matrix = curve.GetTransformMatrix();
                path.Transform(matrix);
                var outlinePaint = OutlineStyleHelper.CreateOutlinePaint(strokePaint, curve);
                canvas.DrawPath(path, outlinePaint ?? strokePaint);
                outlinePaint?.Dispose();
            }
            finally
            {
                cache.ReturnPath(path);
            }

            canvas.Restore();

            Debug.WriteLine($"绘制任意曲线: {curve.Points.Count}个采样点, 闭合={curve.IsClosed}");
        }

        public void PreviewRender(IShape shape, SKCanvas canvas, SKPaint strokePaint, SKPaintCache paintCache)
        {
            if (shape is DrawArbitraryCurve curve && curve.Points.Count >= 2)
            {
                // 预览使用世界坐标直接绘制（与 BezierRenderer 保持一致）
                var path = paintCache.GetPath();
                try
                {
                    CurveInterpolation.FillCatmullRomPath(path, curve.Points);
                    if (curve.IsClosed && curve.Points.Count >= 2)
                    {
                        path.Close();
                    }
                    var outlinePaint = OutlineStyleHelper.CreateOutlinePaint(strokePaint, curve);
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
            // ArbitraryCurve 暂不支持填充渲染
        }
    }
}
