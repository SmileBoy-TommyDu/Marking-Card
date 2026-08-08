using DrSoft.Drawing.Controls.DrawShapes;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.Rendering
{
    /// <summary>
    /// 外框样式渲染辅助：根据图形自定义画笔上的 PathEffect，
    /// 创建带虚线/点线效果的临时画笔，避免污染 SKPaintCache 中的缓存画笔。
    /// 返回 null 表示无需虚线效果（使用原始 strokePaint）。
    /// 调用方负责释放返回的画笔。
    /// </summary>
    internal static class OutlineStyleHelper
    {
        internal static SKPaint? CreateOutlinePaint(SKPaint basePaint, DrawObject shape)
        {
            if (shape.CustomPen?.PathEffect == null) return null;

            return new SKPaint
            {
                Color = basePaint.Color,
                StrokeWidth = basePaint.StrokeWidth,
                Style = SKPaintStyle.Stroke,
                IsAntialias = true,
                StrokeCap = shape.CustomPen.StrokeCap,
                PathEffect = shape.CustomPen.PathEffect,
            };
        }
    }
}
