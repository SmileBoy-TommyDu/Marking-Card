using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Model;
using DrSoft.Drawing.Rendering;
using DrSoft.Drawing.Utility;
using SkiaSharp;
using System.Diagnostics;
using DrSoft.Drawing.Controls.Helpers;

namespace DrSoft.Drawing.Controls.Rendering
{
    [RendererFor(typeof(DrawBezier))]
    public class BezierRenderer : IRenderer
    {
        public bool CanRender(IShape obj)
        {
            return obj is DrawBezier;
        }
        public void Render(IShape shape, SKCanvas canvas, IViewport vp, SKPaintCache cache)
        {
            if (shape is not DrawBezier bezier) return;
            if (bezier.Points.Count < 2) return;

            canvas.Save();

            // 贝塞尔主描边改为 world-path 渲染，避免 skew 再次作用到描边。
            var adjustedWidth = StrokeWidthHelper.ResolveViewportInvariantStrokeWidth(bezier, vp);
            var strokePaint = cache.GetStrokePaint(bezier.Pen.Color, adjustedWidth);

            var path = bezier.GetPath(cache);
            try
            {
                path.Transform(bezier.Matrix);
                var outlinePaint = OutlineStyleHelper.CreateOutlinePaint(strokePaint, bezier);
                canvas.DrawPath(path, outlinePaint ?? strokePaint);
                outlinePaint?.Dispose();
            }
            finally
            {
                cache.ReturnPath(path);
            }

            canvas.Restore();

            //Debug.WriteLine($"绘制贝塞尔曲线: {bezier.Points.Count}个锚点, 闭合={bezier.IsClosed}");
        }

        public void PreviewRender(IShape shape, SKCanvas canvas, SKPaint strokePaint, SKPaintCache paintCache)
        {
            if (shape is DrawBezier bezier && bezier.Points.Count >= 2)
            {
                // 预览使用世界坐标直接绘制（与 ArcRenderer / CircleRenderer / RectangleRenderer 保持一致）
                var path = paintCache.GetPath();
                try
                {
                    CurveInterpolation.FillCatmullRomPath(path, bezier.Points);
                    if (bezier.IsClosed && bezier.Points.Count >= 2)
                    {
                        path.Close();
                    }
                    var outlinePaint = OutlineStyleHelper.CreateOutlinePaint(strokePaint, bezier);
                    canvas.DrawPath(path, outlinePaint ?? strokePaint);
                    outlinePaint?.Dispose();
                }
                finally
                {
                    paintCache.ReturnPath(path);
                }
            }
        }

        /// <summary>
        /// 绘制贝塞尔曲线的控制点和辅助线
        /// </summary>
        public void RenderBezierHandles(DrawBezier bezier, SKCanvas canvas, IViewport vp, SKPaintCache cache)
        {
            var points = bezier.Points;
            if (points.Count == 0) return;

            // 绘制控制点之间的连线（虚线骨架）
            if (points.Count >= 2)
            {
                var skeletonPaint = cache.GetStrokePaint(new SKColor(0xC0, 0x40, 0x20, 80), 1.0f);
                skeletonPaint.PathEffect = SKPathEffect.CreateDash(new float[] { 3, 3 }, 0);

                using (var path = new SKPath())
                {
                    path.MoveTo((float)points[0].X, (float)points[0].Y);
                    for (int i = 1; i < points.Count; i++)
                    {
                        path.LineTo((float)points[i].X, (float)points[i].Y);
                    }
                    canvas.DrawPath(path, skeletonPaint);
                }
            }

            // 绘制所有控制点
            for (int i = 0; i < points.Count; i++)
            {
                var point = points[i];
                bool isEndPoint = (i == 0 || i == points.Count - 1);
                SKColor color = isEndPoint ? SKColors.White : new SKColor(0xFF, 0x80, 0x00);
                RenderControlPoint(canvas, point, color, i, cache, isEndPoint);
            }
        }

        /// <summary>
        /// 绘制控制点
        /// </summary>
        private void RenderControlPoint(SKCanvas canvas, SKPoint point, SKColor fillColor, int index, SKPaintCache cache, bool isEndPoint = false)
        {
            float pointSize = isEndPoint ? 6.0f : 5.0f;

            // 绘制填充
            var fillPaint = cache.GetFillPaint(fillColor);
            canvas.DrawCircle((float)point.X, (float)point.Y, pointSize, fillPaint);

            // 绘制边框（通过CubicTo实现）
            var borderColor = isEndPoint ? new SKColor(0xC0, 0x40, 0x20) : SKColors.Black;
            var borderPaint = cache.GetStrokePaint(borderColor, 1.5f);
            canvas.DrawCircle((float)point.X, (float)point.Y, pointSize, borderPaint);
        }

        public void RenderHatch(IShape shape, IHatchable hatchable, SKCanvas canvas, IViewport vp, SKPaintCache cache)
        {
            if (shape is not DrawBezier bezier || hatchable == null || hatchable.HatchPattern == null
          || hatchable.HatchPattern.HatchLineObjects == null || hatchable.HatchPattern.HatchLineObjects.Count == 0) return;
            //Trace.WriteLine("BesaierS------>" + hatchable.HatchPattern.HatchLineObjects[0].Start.X + "," + hatchable.HatchPattern.HatchLineObjects[0].Start.Y);
            //Trace.WriteLine("BesaierE------>" + hatchable.HatchPattern.HatchLineObjects[0].End.X + "," + hatchable.HatchPattern.HatchLineObjects[0].End.Y);
            canvas.Save();

            // 应用变换矩阵（叠加到当前变换）
            var matrix = bezier.GetTransformMatrix();
            canvas.Concat(ref matrix);

            var lines = hatchable.HatchPattern.HatchLineObjects;
            SKMatrix totalMatrix = canvas.TotalMatrix;
            float scaleX = Math.Abs(totalMatrix.ScaleX);

            // 动态LOD阈值 + 填充线降采样
            float lodThreshold = HatchRenderHelper.ComputeLodThreshold(lines.Count);
            var renderLines = lines;
            //var renderLines = HatchRenderHelper.SampleLines(lines, scaleX);
            //float adjustedWidth = HatchRenderHelper.ComputeProgressiveStrokeWidth(lines, renderLines, scaleX, bezier.Pen.StrokeWidth, vp.Scale);
            var adjustedWidth = StrokeWidthHelper.ResolveScreenInvariantStrokeWidth(bezier, vp);

            if (bezier.HatchParamInfo.FillStyleIndex == 0)
            {
                var fillPaint = cache.GetStrokePaint(SKColor.Parse(bezier.HatchParamInfo.FillColor), adjustedWidth);
                using (var path = HatchRenderHelper.BuildBatchPath(renderLines))
                {
                    canvas.DrawPath(path, fillPaint);
                }
            }
            else if (bezier.HatchParamInfo.FillStyleIndex == 1 || bezier.HatchParamInfo.FillStyleIndex == 2)
            {
                if (scaleX <= lodThreshold)
                {
                    var fillPaint = cache.GetStrokePaint(SKColor.Parse(bezier.HatchParamInfo.FillColor), adjustedWidth);
                    using (var path = HatchRenderHelper.BuildBatchPath(renderLines))
                    {
                        canvas.DrawPath(path, fillPaint);
                    }
                }
                else
                {
                    HatchRenderHelper.RenderDashLinesIndividually(
                        canvas, renderLines,
                        SKColor.Parse(bezier.HatchParamInfo.FillColor),
                        scaleX, bezier.HatchParamInfo.FillStyleIndex);
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
            if (shape is not DrawBezier bezier) return;
            if (bezier is not IHatchable hatchable) return;
            if (!hatchable.IsHatchEnabled) return;
            if (hatchable.HatchInfo == null) return;

            canvas.Save();

            // 应用变换矩阵（叠加到当前变换）
            var matrix = bezier.GetTransformMatrix();
            canvas.Concat(ref matrix);

            // 根据视口缩放比例调整线宽，保持视觉上的恒定线宽
            var adjustedWidth = bezier.Pen.StrokeWidth * 6.83f / vp.Scale;
            var fillPaint = cache.GetStrokePaint(bezier.Pen.Color, adjustedWidth);

            var hatchPattern = hatchable.HatchPattern;
            hatchPattern?.Primitives?.ForEach(line =>
            {
                if (line is DrawPolyLines drawLine && drawLine.Points.Count >= 2)
                {
                    canvas.DrawLine(new SKPoint((float)drawLine.Points[0].X, (float)drawLine.Points[0].Y), new SKPoint((float)drawLine.Points[1].X, (float)drawLine.Points[1].Y), fillPaint);
                }
            });

            canvas.Restore();
        }*/
    }


}

/// <summary>
/// 曲线插值工具类 - 使用Catmull-Rom样条构建通过所有锚点的光滑曲线
/// </summary>
public static class CurveInterpolation
{
    /// <summary>
    /// 使用Catmull-Rom样条插值构建光滑曲线路径
    /// 曲线将通过所有提供的点（除了首尾）
    /// </summary>
    public static SKPath BuildCatmullRomPath(IReadOnlyList<SKPoint> points)
    {
        var path = new SKPath();
        FillCatmullRomPath(path, points);
        return path;
    }

    /// <summary>
    /// 将 Catmull-Rom 样条曲线路径数据写入已有的 SKPath（用于池化复用）
    /// </summary>
    public static void FillCatmullRomPath(SKPath path, IReadOnlyList<SKPoint> points)
    {
        if (points.Count < 2)
            return;

        if (points.Count == 2)
        {
            // 两个点：直线
            path.MoveTo((float)points[0].X, (float)points[0].Y);
            path.LineTo((float)points[1].X, (float)points[1].Y);
            return;
        }

        // 移动到第一个点
        path.MoveTo((float)points[0].X, (float)points[0].Y);

        if (points.Count == 3)
        {
            // 三个点：使用 Catmull-Rom 样条（与 4+ 点保持一致，曲线通过所有锚点）
            // 反射端点生成虚拟 p0/p3，确保曲线通过中间点
            path.MoveTo((float)points[0].X, (float)points[0].Y);
            for (int i = 0; i < points.Count - 1; i++)
            {
                SKPoint p0, p1, p2, p3;
                p1 = points[i];
                p2 = points[i + 1];
                p0 = (i == 0) ? ReflectPoint(p1, p2) : points[i - 1];
                p3 = (i == points.Count - 2) ? ReflectPoint(p2, p1) : points[i + 2];
                AddCatmullRomSegment(path, p0, p1, p2, p3);
            }
            return;
        }

        // 4个或以上点：使用Catmull-Rom样条
        // 对于每一段曲线，需要4个点来定义（P0, P1, P2, P3）
        // 曲线从P1到P2，使用P0和P3作为切线参考
        for (int i = 0; i < points.Count - 1; i++)
        {
            SKPoint p0, p1, p2, p3;

            // 获取Catmull-Rom的四个控制点
            p1 = points[i];
            p2 = points[i + 1];

            // 前向点
            if (i == 0)
            {
                // 第一段：使用反射点或起点本身
                p0 = ReflectPoint(p1, p2);
            }
            else
            {
                p0 = points[i - 1];
            }

            // 后向点
            if (i == points.Count - 2)
            {
                // 最后一段：使用反射点或终点本身
                p3 = ReflectPoint(p2, p1);
            }
            else
            {
                p3 = points[i + 2];
            }

            // 为这一段生成曲线点
            AddCatmullRomSegment(path, p0, p1, p2, p3);
        }
    }

    /// <summary>
    /// 为单个Catmull-Rom曲线段添加立方贝塞尔曲线近似
    /// </summary>
    private static void AddCatmullRomSegment(SKPath path, SKPoint p0, SKPoint p1, SKPoint p2, SKPoint p3)
    {
        // Catmull-Rom转换为立方贝塞尔的公式
        // 控制点1: p1 + (p2 - p0) / 6
        // 控制点2: p2 - (p3 - p1) / 6

        SKPoint cp1 = new SKPoint(
            p1.X + (p2.X - p0.X) / 6.0f,
            p1.Y + (p2.Y - p0.Y) / 6.0f
        );

        SKPoint cp2 = new SKPoint(
            p2.X - (p3.X - p1.X) / 6.0f,
            p2.Y - (p3.Y - p1.Y) / 6.0f
        );

        path.CubicTo(
            cp1.X, cp1.Y,
            cp2.X, cp2.Y,
            p2.X, p2.Y
        );
    }

    /// <summary>
    /// 反射点：用于计算边界处的虚拟控制点
    /// 反射p2关于p1
    /// </summary>
    private static SKPoint ReflectPoint(SKPoint p1, SKPoint p2)
    {
        return new SKPoint(
            2 * p1.X - p2.X,
            2 * p1.Y - p2.Y
        );
    }

    /// <summary>
    /// 使用均匀参数化的二次贝塞尔曲线构建路径
    /// 每个相邻点对的中点作为控制点
    /// </summary>
    public static SKPath BuildQuadraticBezierPath(IReadOnlyList<Point2D> points)
    {
        var path = new SKPath();

        if (points.Count < 2)
            return path;

        path.MoveTo((float)points[0].X, (float)points[0].Y);

        if (points.Count == 2)
        {
            path.LineTo((float)points[1].X, (float)points[1].Y);
            return path;
        }

        // 使用二次贝塞尔曲线，相邻点为控制点
        for (int i = 1; i < points.Count; i++)
        {
            if (i < points.Count - 1)
            {
                // 中间点作为控制点
                path.QuadTo(
                    (float)points[i].X, (float)points[i].Y,
                    (float)points[i + 1].X, (float)points[i + 1].Y
                );
                i++; // 跳过已处理的点
            }
            else
            {
                // 最后一个点（奇数个点的情况）
                path.LineTo((float)points[i].X, (float)points[i].Y);
            }
        }

        return path;
    }

    /// <summary>
    /// 使用平滑的折线段，但每段都是轻微的曲线
    /// 这种方法通过所有点但会更"直"
    /// </summary>

    public static List<SKPoint> FlattenPath(IReadOnlyList<SKPoint> points)
    {
        var result = new List<SKPoint>();
        var path = BuildCatmullRomPath(points);

        // 根据全局分辨率设置步长，单位为毫米，通过配置界面设置
        float stepMm = (float)GlobalVariableManagement.Resolution;

        // path: 原始路径
        // false: 表示不闭合（如果是闭合图形，内部会处理每一个 Contour）
        using (var measure = new SKPathMeasure(path, resScale: 1, forceClosed: false))
        {
            do
            {
                float length = measure.Length;
                // 按照指定的步长（例如 0.01mm）提取点
                for (float distance = 0; distance < length; distance += stepMm)
                {
                    if (measure.GetPosition(distance, out var point))
                    {
                        result.Add(new SKPoint(point.X, point.Y));
                    }
                }

                // 确保终点被加入
                if (measure.GetPosition(length, out var lastPoint))
                {
                    result.Add(new SKPoint(lastPoint.X, lastPoint.Y));
                }

            } while (measure.NextContour()); // 处理路径中的所有子图形（MoveTo 产生的）
        }

        return result;
    }
}
