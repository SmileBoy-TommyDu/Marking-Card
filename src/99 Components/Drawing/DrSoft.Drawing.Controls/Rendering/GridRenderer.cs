using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Model;
using DrSoft.Drawing.Rendering;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.Rendering
{
    public class GridRenderer
    {
        public bool IsVisible { get; set; } = true;
        // 希望在屏幕上的像素间距（当选择屏幕固定像素网格模式时使用）
        public float PixelSpacing { get; set; } = 100f;
        
        // 网格范围属性，供Viewport使用
        public float GridExtent { get; set; } = 50000f; // 网格从原点到边缘的距离

        public void Render(SKCanvas canvas, IViewport vp, SKImageInfo info, GridPaintCache gridPaintCache)
        {
            if (!IsVisible) return;

            // ✅ 优化 1: 使用缓存的画笔
            var paint = gridPaintCache.GetGridPaint(vp.Scale);
            var axisPaint = gridPaintCache.GetAxisPaint(vp.Scale);

            // 1. 计算屏幕视口在世界坐标系中的“可见边界框”
            var screenRect = SKRect.Create(0, 0, info.Width, info.Height);
            var worldBounds = vp.ScreenToWorldRect(info,screenRect);

            // 2. 增加边距（防止边缘锯齿或刚好在边界上的图形）
            float margin = 50 / vp.Scale; // 动态边距：缩放越大，边距像素保持不变
            var visibleRect = SKRect.Inflate(worldBounds, margin, margin);

            // 3. 动态计算网格步长范围
            float stepx = DocumentContext.Instance.GridSizeX;
            float stepy = DocumentContext.Instance.GridSizeY;
            if (stepx == 0f || stepy == 0f) goto Axis;

            // 对齐到网格步长（向下取整开始，向上取整结束）
            float startX = (float)Math.Floor(visibleRect.Left / stepx) * stepx;
            float endX = (float)Math.Ceiling(visibleRect.Right / stepx) * stepx;
            float startY = (float)Math.Floor(visibleRect.Top / stepy) * stepy;
            float endY = (float)Math.Ceiling(visibleRect.Bottom / stepy) * stepy;

            // ✅ 优化 2: 裁剪画布
            canvas.Save();
            canvas.ClipRect(visibleRect);

            // 4. 绘制网格线（仅绘制可见区域内的线）
            for (float x = startX; x <= endX; x += stepx)
            {
                // 绘制垂直线：从可见区域顶部到底部
                canvas.DrawLine(x, visibleRect.Top, x, visibleRect.Bottom, paint);
            }

            for (float y = startY; y <= endY; y += stepy)
            {
                // 绘制水平线：从可见区域左侧到右侧
                canvas.DrawLine(visibleRect.Left, y, visibleRect.Right, y, paint);
            }

            // 恢复裁剪状态
            canvas.Restore();

            Axis:
            // 5. 绘制坐标轴（仅绘制可见部分，避免画到无限远）
            // X轴 (Y=0)
            if (0 >= visibleRect.Top && 0 <= visibleRect.Bottom)
            {
                canvas.DrawLine(visibleRect.Left, 0, visibleRect.Right, 0, axisPaint);
            }

            // Y轴 (X=0)
            if (0 >= visibleRect.Left && 0 <= visibleRect.Right)
            {
                canvas.DrawLine(0, visibleRect.Top, 0, visibleRect.Bottom, axisPaint);
            }

            DrawOriginMarker(canvas, vp);
        }

        private void DrawOriginMarker(SKCanvas canvas, IViewport vp)
        {
            // 1. 基础尺寸（世界单位）
            // 这里的 1.0f 代表在 Scale=1 时，标记的大小为 1 个世界单位
            float baseLSize = 1.0f * 4;
            float baseLineOffset = 0.6f * 4;
            float currentScale = Math.Max(vp.Scale, 0.001f);

            float LSize = baseLSize / currentScale;
            float lineOffset = baseLineOffset / currentScale;

            // 2. 动态调整线宽
            float strokeWidth = 2.0f / currentScale;

            using var markerPaint = new SKPaint
            {
                Color = SKColors.Red,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = strokeWidth,
                IsAntialias = true,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round
            };

            // 3. 绘制逻辑保持不变，但使用的是动态计算后的 LSize 和 lineOffset
            // 第一象限（右上）
            using (var path1 = new SKPath())
            {
                path1.MoveTo(lineOffset + LSize, lineOffset);
                path1.LineTo(lineOffset, lineOffset);
                path1.LineTo(lineOffset, lineOffset + LSize);
                canvas.DrawPath(path1, markerPaint);
            }

            // 第二象限（左上）
            using (var path2 = new SKPath())
            {
                path2.MoveTo(-lineOffset - LSize, lineOffset);
                path2.LineTo(-lineOffset, lineOffset);
                path2.LineTo(-lineOffset, lineOffset + LSize);
                canvas.DrawPath(path2, markerPaint);
            }

            // 第三象限（左下）
            using (var path3 = new SKPath())
            {
                path3.MoveTo(-lineOffset - LSize, -lineOffset);
                path3.LineTo(-lineOffset, -lineOffset);
                path3.LineTo(-lineOffset, -lineOffset - LSize);
                canvas.DrawPath(path3, markerPaint);
            }

            // 第四象限（右下）
            using (var path4 = new SKPath())
            {
                path4.MoveTo(lineOffset + LSize, -lineOffset);
                path4.LineTo(lineOffset, -lineOffset);
                path4.LineTo(lineOffset, -lineOffset - LSize);
                canvas.DrawPath(path4, markerPaint);
            }
        }
    }
}
