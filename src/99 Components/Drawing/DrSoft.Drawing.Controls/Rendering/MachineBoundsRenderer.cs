using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Model;
using SkiaSharp;
using System.Windows;

namespace DrSoft.Drawing.Controls.Rendering
{
    public class MachineBoundsRenderer
    {
        public void Render(
            SKCanvas canvas,
            Viewport viewport,
            SKImageInfo info,
            Rect2D machineBounds)
        {
            if (machineBounds == null)
            {
                return;
            }

            // 首先绘制整个背景为浅灰色
            canvas.Clear(new SKColor(241, 243, 243));

            // 绘制带阴影的边框
            var shadowPaint = new SKPaint
            {
                Color = new SKColor(0, 0, 0, (byte)(0.15f * 255)), // 半透明黑色作为阴影
                Style = SKPaintStyle.Fill,
                StrokeWidth = 3.0f / viewport.Scale, // 稍微宽一些以便产生阴影效果
                IsAntialias = true,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 20 / 2f)
            };
            canvas.DrawRoundRect((float)machineBounds.X,
                (float)(-machineBounds.Y - machineBounds.Height),
                (float)machineBounds.Width,
                (float)machineBounds.Height, 6, 6, shadowPaint); // 应用相同的圆角


            // 绘制机台范围内的白色区域
            var whitePaint = new SKPaint
            {
                Color = SKColors.White,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };

            canvas.DrawRect(
                (float)machineBounds.X,
                (float)(-machineBounds.Y - machineBounds.Height),
                (float)machineBounds.Width,
                (float)machineBounds.Height,
                whitePaint);
        }
    }
}