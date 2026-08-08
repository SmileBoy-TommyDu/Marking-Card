using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Model;
using DrSoft.Drawing.Rendering;
using SkiaSharp;
using System.Diagnostics;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TaskbarClock;

namespace DrSoft.Drawing.Controls.Rendering
{
    public class RulerRenderer
    {
        public bool IsVisible { get; set; } = true;
        
        /// <summary>
        /// 标尺宽度（像素）
        /// </summary>
        public float RulerWidth { get; set; } = 24f;
        
        /// <summary>
        /// 标尺背景颜色
        /// </summary>
        public SKColor RulerBackgroundColor { get; set; } = new SKColor(241, 243, 243); 
        
        /// <summary>
        /// 刻度线颜色
        /// </summary>
        public SKColor TickColor { get; set; } = SKColors.Black;
        
        /// <summary>
        /// 文字颜色
        /// </summary>
        public SKColor TextColor { get; set; } = SKColors.Black;
        
        /// <summary>
        /// 边框颜色
        /// </summary>
        public SKColor BorderColor { get; set; } = SKColors.DarkGray;

        /// <summary>
        /// 实时刻度颜色
        /// </summary>
        public SKColor RealMarkerColor { get; set; } = SKColors.Red;

        /// <summary>
        /// 大刻度在屏幕上的目标像素间距
        /// </summary>
        public float TargetMajorTickPixelSpacing { get; set; } = 80f;

        /// <summary>
        /// 小刻度数量（每两个大刻度之间）
        /// </summary>
        public int MinorTickCount { get; set; } = 10;


        public void DrawRealMarker(SKPoint worldPoint, SKCanvas canvas, IViewport vp)
        {
            if (!IsVisible) return;
            // 绘制实时刻度线
            var screenPoint = vp.WorldToScreen(worldPoint);
            using var paint = new SKPaint
            {
                Color = RealMarkerColor,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2,
                IsAntialias = true
            };
            // 绘制水平实时刻度线
            canvas.DrawLine(0, screenPoint.Y, 25, screenPoint.Y, paint);
            // 绘制垂直实时刻度线
            canvas.DrawLine(screenPoint.X, 0, screenPoint.X, 25, paint);
        }

        public void Render(SKCanvas canvas, IViewport vp, SKImageInfo info)
        {
            if (!IsVisible) return;
            
            // 保存当前画布状态
            canvas.Save();
            
            try
            {            
                // 绘制X轴标尺（画布上方）
                DrawHorizontalRuler(canvas, vp, info);
                
                // 绘制Y轴标尺（画布左边）
                DrawVerticalRuler(canvas, vp, info);
            }
            finally
            {
                // 恢复画布状态
                canvas.Restore();
            }
        }

        private void DrawHorizontalRuler(SKCanvas canvas, IViewport vp, SKImageInfo info)
        {
            // 计算标尺区域: 从(0,0)到(info.Width, RulerWidth)
            var rulerRect = new SKRect(0, 0, info.Width, RulerWidth);

            // 绘制标尺背景
            using var backgroundPaint = new SKPaint
            {
                Color = RulerBackgroundColor,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawRect(rulerRect, backgroundPaint);

            // 绘制标尺边框
            using var borderPaint = new SKPaint
            {
                Color = BorderColor,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1,
                IsAntialias = true
            };
            canvas.DrawRect(rulerRect, borderPaint);

            // 获取可见范围的世界坐标
            var visibleWorldBounds = GetVisibleWorldBounds(vp, info);

            // 计算刻度间隔（世界单位，mm）
            float majorInterval = GetMajorTickInterval(vp.Scale);
            float minorInterval = majorInterval / MinorTickCount;

            // 使用整数索引避免浮点数精度问题
            int startMinorIndex = (int)Math.Floor(visibleWorldBounds.Left / minorInterval);
            int endMinorIndex = (int)Math.Ceiling(visibleWorldBounds.Right / minorInterval);

            // 创建画笔
            using var tickPaint = new SKPaint
            {
                Color = TickColor,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1,
                IsAntialias = true
            };

            using var textPaint = new SKPaint
            {
                Color = TextColor,
                Style = SKPaintStyle.Fill,
                TextSize = 10,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
            };

            // 绘制刻度线和文字
            for (int i = startMinorIndex; i <= endMinorIndex; i++)
            {
                float x = i * minorInterval;

                // 将世界坐标转换为屏幕坐标
                var screenPoint = vp.WorldToScreen(new SKPoint(x, 0));

                // 确保在标尺区域内（留一点边距）
                if (screenPoint.X < -1 || screenPoint.X > info.Width + 1)
                    continue;

                bool isMajor = i % MinorTickCount == 0;

                if (isMajor)
                {
                    // 大刻度线
                    float tickHeight = RulerWidth * 0.5f;
                    canvas.DrawLine(screenPoint.X, RulerWidth, screenPoint.X, RulerWidth - tickHeight, tickPaint);

                    // 绘制刻度值
                    string label = FormatTickLabel(x, majorInterval);
                    float textWidth = textPaint.MeasureText(label);

                    // 文本居中于刻度线，避免超出边界
                    float textX = screenPoint.X - textWidth / 2;
                    textX = Math.Max(2, Math.Min(textX, info.Width - textWidth - 2));

                    canvas.DrawText(label, textX, RulerWidth - tickHeight - 2, textPaint);
                }
                else
                {
                    // 小刻度线
                    float tickHeight = RulerWidth * 0.3f;
                    canvas.DrawLine(screenPoint.X, RulerWidth, screenPoint.X, RulerWidth - tickHeight, tickPaint);
                }
            }
        }

        private void DrawVerticalRuler(SKCanvas canvas, IViewport vp, SKImageInfo info)
        {
            // 计算标尺区域: 从(0, RulerWidth)到(RulerWidth, info.Height)
            var rulerRect = new SKRect(0, RulerWidth, RulerWidth, info.Height);

            // 绘制标尺背景
            using var backgroundPaint = new SKPaint
            {
                Color = RulerBackgroundColor,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawRect(rulerRect, backgroundPaint);

            // 绘制标尺边框
            using var borderPaint = new SKPaint
            {
                Color = BorderColor,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1,
                IsAntialias = true
            };
            canvas.DrawRect(rulerRect, borderPaint);

            // 获取可见范围的世界坐标
            var visibleWorldBounds = GetVisibleWorldBounds(vp, info);

            // 计算刻度间隔（世界单位，mm）
            float majorInterval = GetMajorTickInterval(vp.Scale);
            float minorInterval = majorInterval / MinorTickCount;

            // 使用整数索引避免浮点数精度问题
            // 注意：世界坐标Y轴向上为正，SKRect.Top=minY(屏幕底部)，SKRect.Bottom=maxY(屏幕顶部)
            int startMinorIndex = (int)Math.Floor(visibleWorldBounds.Top / minorInterval);
            int endMinorIndex = (int)Math.Ceiling(visibleWorldBounds.Bottom / minorInterval);

            // 创建画笔
            using var tickPaint = new SKPaint
            {
                Color = TickColor,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1,
                IsAntialias = true
            };

            using var textPaint = new SKPaint
            {
                Color = TextColor,
                Style = SKPaintStyle.Fill,
                TextSize = 10,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
            };

            // 绘制刻度线和文字
            for (int i = startMinorIndex; i <= endMinorIndex; i++)
            {
                float y = i * minorInterval;

                // 将世界坐标转换为屏幕坐标
                var screenPoint = vp.WorldToScreen(new SKPoint(0, y));

                // 确保在标尺区域内（留一点边距）
                if (screenPoint.Y < RulerWidth - 1 || screenPoint.Y > info.Height + 1)
                    continue;

                bool isMajor = i % MinorTickCount == 0;

                if (isMajor)
                {
                    // 大刻度线
                    float tickWidth = RulerWidth * 0.6f;
                    canvas.DrawLine(RulerWidth, screenPoint.Y, RulerWidth - tickWidth, screenPoint.Y, tickPaint);

                    // 绘制刻度值（垂直显示）
                    string label = FormatTickLabel(y, majorInterval);
                    float textWidth = textPaint.MeasureText(label);

                    // 旋转270度（90+180），文本靠近左边缘，避免与刻度线粘连
                    canvas.Save();
                    canvas.Translate(10, screenPoint.Y);
                    canvas.RotateDegrees(270);
                    canvas.DrawText(label, -textWidth / 2, 0, textPaint);
                    canvas.Restore();
                }
                else
                {
                    // 小刻度线
                    float tickWidth = RulerWidth * 0.3f;
                    canvas.DrawLine(RulerWidth, screenPoint.Y, RulerWidth - tickWidth, screenPoint.Y, tickPaint);
                }
            }
        }

        /// <summary>
        /// 根据缩放级别计算大刻度间隔（世界单位，mm）
        /// 大刻度在屏幕上的像素间距保持在一个舒适范围内
        /// </summary>
        private float GetMajorTickInterval(float scale)
        {
            // 目标：大刻度在屏幕上的间距约为 TargetMajorTickPixelSpacing 像素
            float rawInterval = TargetMajorTickPixelSpacing / scale;

            // 规整化到标准序列 [0.1, 0.2, 0.5, 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000...]
            float magnitude = (float)Math.Pow(10, Math.Floor(Math.Log10(rawInterval)));
            float normalized = rawInterval / magnitude;

            if (normalized <= 0.2f)
                return 0.1f * magnitude;
            else if (normalized <= 0.5f)
                return 0.2f * magnitude;
            else if (normalized <= 1.0f)
                return 0.5f * magnitude;
            else if (normalized <= 2.0f)
                return 1.0f * magnitude;
            else if (normalized <= 5.0f)
                return 2.0f * magnitude;
            else
                return 5.0f * magnitude;
        }

        /// <summary>
        /// 格式化刻度标签，根据大刻度间隔决定小数位数
        /// </summary>
        private string FormatTickLabel(float value, float majorInterval)
        {
            if (majorInterval >= 1)
                return ((int)Math.Round(value)).ToString();
            else if (majorInterval >= 0.1f)
                return value.ToString("F1");
            else
                return value.ToString("F2");
        }

        private SKRect GetVisibleWorldBounds(IViewport vp, SKImageInfo info)
        {
            // 计算屏幕四个角对应的世界坐标
            var screenCorners = new SKPoint[]
            {
                new SKPoint(0, 0),
                new SKPoint(info.Width, 0),
                new SKPoint(0, info.Height),
                new SKPoint(info.Width, info.Height)
            };
            
            // 转换屏幕坐标到世界坐标
            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minY = float.MaxValue;
            float maxY = float.MinValue;
            
            foreach (var screenPoint in screenCorners)
            {
                var worldPoint = vp.ScreenToWorld(screenPoint);
                minX = Math.Min(minX, worldPoint.X);
                maxX = Math.Max(maxX, worldPoint.X);
                minY = Math.Min(minY, worldPoint.Y);
                maxY = Math.Max(maxY, worldPoint.Y);
            }
            
            return new SKRect(minX, minY, maxX, maxY);
        }
    }
}