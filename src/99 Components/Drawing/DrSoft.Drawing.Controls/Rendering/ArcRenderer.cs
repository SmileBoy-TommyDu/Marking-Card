using System.Diagnostics;
using System.Windows.Shapes;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Helpers;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Model;
using DrSoft.Drawing.Rendering;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.Rendering
{
    [RendererFor(typeof(DrawArc))]
    public class ArcRenderer : IRenderer
    {
        public bool CanRender(IShape obj) { return obj is DrawArc; }

        public void Render(IShape shape, SKCanvas canvas, IViewport vp, SKPaintCache cache)
        {
            if (shape is not DrawArc arc) return;
            if (arc.Points.Count < 3 || arc.Radius <= 0) return;

            canvas.Save();

            // 圆弧主描边改为 world-path 渲染，避免 skew 再次作用到描边。
            var adjustedWidth = StrokeWidthHelper.ResolveViewportInvariantStrokeWidth(arc, vp);
            var strokePaint = cache.GetStrokePaint(arc.Pen.Color, adjustedWidth);
            var previewPaint = cache.GetPreviewPaint(adjustedWidth);

            var path = arc.GetPath(cache);
            try
            {
                if (path != null)
                {
                    path.Transform(arc.Matrix);

                    var outlinePaint = OutlineStyleHelper.CreateOutlinePaint(strokePaint, arc);
                    canvas.DrawPath(path, outlinePaint ?? strokePaint);
                    outlinePaint?.Dispose();
                }
            }
            finally
            {
                cache.ReturnPath(path);
            }

            var worldPoints = arc.GetWorldPoints();
            if (worldPoints.Count >= 2)
            {
                var startPoint = worldPoints[0];
                var middlePoint = worldPoints[1];

                if (arc.PreviewLineEndPoint.HasValue)
                {
                    var previewEndPoint = arc.PreviewLineEndPoint.Value;
                    canvas.DrawLine(startPoint, previewEndPoint, previewPaint);
                }

                if (arc.PreviewLineEndPoint2.HasValue)
                {
                    var previewEndPoint = arc.PreviewLineEndPoint2.Value;
                    canvas.DrawLine(middlePoint, previewEndPoint, previewPaint);
                }
            }

            canvas.Restore();
        }

        public void PreviewRender(IShape shape, SKCanvas canvas, SKPaint strokePaint, SKPaintCache paintCache)
        {
            if (shape is not DrawArc arc) return;
            if (arc.Points.Count < 3) return;

            // 预览使用世界坐标直接绘制（与 CircleRenderer / RectangleRenderer 保持一致）
            var p1 = new SKPoint((float)arc.Points[0].X, (float)arc.Points[0].Y);
            var p2 = new SKPoint((float)arc.Points[1].X, (float)arc.Points[1].Y);
            var p3 = new SKPoint((float)arc.Points[2].X, (float)arc.Points[2].Y);

            var path = paintCache.GetPath();
            try
            {
                if (ArcMath.FillArcPath(path, p1, p2, p3))
                {
                    var outlinePaint = OutlineStyleHelper.CreateOutlinePaint(strokePaint, arc);
                    canvas.DrawPath(path, outlinePaint ?? strokePaint);
                    outlinePaint?.Dispose();
                }
            }
            finally
            {
                paintCache.ReturnPath(path);
            }

            var previewPaint = paintCache.GetPreviewPaint(strokePaint.StrokeWidth);

            // 绘制第一条预览虚线（P1 到鼠标位置的辅助线）
            if (arc.PreviewLineEndPoint.HasValue)
            {
                var previewEndPoint = new SKPoint(
                    (float)arc.PreviewLineEndPoint.Value.X,
                    (float)arc.PreviewLineEndPoint.Value.Y);
                canvas.DrawLine(p1, previewEndPoint, previewPaint);
            }

            // 绘制第二条预览虚线（P2 到鼠标位置的辅助线）
            if (arc.PreviewLineEndPoint2.HasValue)
            {
                var previewEndPoint2 = new SKPoint(
                    (float)arc.PreviewLineEndPoint2.Value.X,
                    (float)arc.PreviewLineEndPoint2.Value.Y);
                canvas.DrawLine(p2, previewEndPoint2, previewPaint);
            }
        }

        public void RenderHatch(IShape shape, IHatchable hatchable, SKCanvas canvas, IViewport vp, SKPaintCache cache)
        {
            if (shape is not DrawArc arc || hatchable == null || hatchable.HatchPattern == null
            || hatchable.HatchPattern.HatchLineObjects == null || hatchable.HatchPattern.HatchLineObjects.Count == 0) return;

            canvas.Save();

            // 应用变换矩阵（叠加到当前变换）
            var matrix = arc.GetTransformMatrix();
            canvas.Concat(ref matrix);

            var lines = hatchable.HatchPattern.HatchLineObjects;
            SKMatrix totalMatrix = canvas.TotalMatrix;
            float scaleX = Math.Abs(totalMatrix.ScaleX);

            // 动态LOD阈值 + 填充线降采样
            float lodThreshold = HatchRenderHelper.ComputeLodThreshold(lines.Count);
            var renderLines = HatchRenderHelper.SampleLines(lines, scaleX);
            float adjustedWidth = HatchRenderHelper.ComputeProgressiveStrokeWidth(lines, renderLines, scaleX, arc.Pen.StrokeWidth, vp.Scale);

            if (arc.HatchParamInfo.FillStyleIndex == 0)
            {
                var fillPaint = cache.GetStrokePaint(SKColor.Parse(arc.HatchParamInfo.FillColor), adjustedWidth);
                using (var path = HatchRenderHelper.BuildBatchPath(renderLines))
                {
                    canvas.DrawPath(path, fillPaint);
                }
            }
            else if (arc.HatchParamInfo.FillStyleIndex == 1 || arc.HatchParamInfo.FillStyleIndex == 2)
            {
                if (scaleX <= lodThreshold)
                {
                    var fillPaint = cache.GetStrokePaint(SKColor.Parse(arc.HatchParamInfo.FillColor), adjustedWidth);
                    using (var path = HatchRenderHelper.BuildBatchPath(renderLines))
                    {
                        canvas.DrawPath(path, fillPaint);
                    }
                }
                else
                {
                    HatchRenderHelper.RenderDashLinesIndividually(
                        canvas, renderLines,
                        SKColor.Parse(arc.HatchParamInfo.FillColor),
                        scaleX, arc.HatchParamInfo.FillStyleIndex);
                }
            }

            canvas.Restore();
        }

        ///// <summary>
        ///// 绘制填充线段
        ///// </summary>
        ///// <param name="shape"></param>
        ///// <param name="canvas"></param>
        ///// <param name="cache"></param>
        //public void RenderHatch(IShape shape, SKCanvas canvas, IViewport vp, SKPaintCache cache)
        //{
        //    if (shape is not DrawArc arc) return;
        //    if (arc is not IHatchable hatchable) return;
        //    if (!hatchable.IsHatchEnabled) return;
        //    if (hatchable.HatchInfo == null) return;

        //    canvas.Save();

        //    //// 应用变换矩阵（叠加到当前变换）
        //    //var matrix = arc.GetTransformMatrix();
        //    //canvas.Concat(ref matrix);

        //    // 根据视口缩放比例调整线宽，保持视觉上的恒定线宽
        //    var adjustedWidth = arc.Pen.StrokeWidth * 6.83f / vp.Scale;
        //    var fillPaint = cache.GetStrokePaint(arc.Pen.Color, adjustedWidth);

        //    var hatchPattern = hatchable.HatchPattern;
        //    hatchPattern?.Primitives?.ForEach(line =>
        //    {
        //        if (line is DrawPolyLines drawLine)
        //        {
        //            canvas.DrawLine(new SKPoint((float)drawLine.Points[0].X, (float)drawLine.Points[0].Y), new SKPoint((float)drawLine.Points[1].X, (float)drawLine.Points[1].Y), fillPaint);
        //        }
        //    });

        //    canvas.Restore();
        //}
    }
}

/// <summary>
/// 圆弧数学计算辅助类
/// </summary>
public static class ArcMath
{
    /// <summary>
    /// 计算三点圆弧的圆心
    /// </summary>
    public static SKPoint CalculateCenter(SKPoint p1, SKPoint p2, SKPoint p3)
    {
        float x1 = p1.X, y1 = p1.Y;
        float x2 = p2.X, y2 = p2.Y;
        float x3 = p3.X, y3 = p3.Y;

        // 计算分母
        float D = 2 * (x1 * (y2 - y3) + x2 * (y3 - y1) + x3 * (y1 - y2));

        // 如果分母接近 0，说明三点在一条直线上，无法构成圆
        if (Math.Abs(D) < 0.001f)
        {
            throw new InvalidOperationException("三点共线，无法计算圆弧中心！");
        }

        float centerX = ((x1 * x1 + y1 * y1) * (y2 - y3) + (x2 * x2 + y2 * y2) * (y3 - y1) + (x3 * x3 + y3 * y3) * (y1 - y2)) / D;
        float centerY = ((x1 * x1 + y1 * y1) * (x3 - x2) + (x2 * x2 + y2 * y2) * (x1 - x3) + (x3 * x3 + y3 * y3) * (x2 - x1)) / D;

        return new SKPoint(centerX, centerY);
    }

    /// <summary>
    /// 计算圆弧半径
    /// </summary>
    public static double CalculateRadius(SKPoint center, SKPoint p1)
    {
        return Math.Sqrt(Math.Pow(p1.X - center.X, 2) + Math.Pow(p1.Y - center.Y, 2));
    }

    /// <summary>
    /// 计算开始角度（角度制，范围 (-180, 180]）
    /// </summary>
    public static double CalculateStartAngle(SKPoint center, SKPoint p1)
    {
        double radians = Math.Atan2(p1.Y - center.Y, p1.X - center.X);
        return radians * 180.0 / Math.PI;
    }

    /// <summary>
    /// 计算绝对结束角度（角度制，范围 (-180, 180]）
    /// </summary>
    public static double CalculateEndAngle(SKPoint center, SKPoint p3)
    {
        double radians = Math.Atan2(p3.Y - center.Y, p3.X - center.X);
        return radians * 180.0 / Math.PI;
    }

    /// <summary>
    /// 计算扫描角度（正为逆时针，负为顺时针）
    /// </summary>
    public static double CalculateSweepAngle(SKPoint center, SKPoint p1, SKPoint p2, SKPoint p3)
    {
        // 1. 计算三点绝对角度
        double a1 = Math.Atan2(p1.Y - center.Y, p1.X - center.X) * 180.0 / Math.PI;
        double a2 = Math.Atan2(p2.Y - center.Y, p2.X - center.X) * 180.0 / Math.PI;
        double a3 = Math.Atan2(p3.Y - center.Y, p3.X - center.X) * 180.0 / Math.PI;

        // 2. 计算从 P1 到 P3 的基础差异
        double sweep = a3 - a1;

        // 3. 标准化到 (-180, 180]
        while (sweep <= -180) sweep += 360;
        while (sweep > 180) sweep -= 360;

        // 4. 计算中间点相对于起点的角度差异
        double midDiff = a2 - a1;
        while (midDiff <= -180) midDiff += 360;
        while (midDiff > 180) midDiff -= 360;

        // 5. 核心判定：确保路径经过 P2
        // 如果方向相反或者 sweep 没覆盖到 midDiff，说明真实的弧是长弧（优弧）
        if (Math.Sign(midDiff) != Math.Sign(sweep) || Math.Abs(midDiff) > Math.Abs(sweep))
        {
            sweep = (sweep > 0) ? (sweep - 360) : (sweep + 360);
        }

        return sweep;
    }

    /// <summary>
    /// 根据三点计算外接圆（圆心 + 半径）
    /// 返回 null 表示三点共线，无法构成圆弧
    /// </summary>
    public static (SKPoint center, float radius)? Circumcircle(SKPoint p1, SKPoint p2, SKPoint p3)
    {
        float ax = p1.X, ay = p1.Y;
        float bx = p2.X, by = p2.Y;
        float cx = p3.X, cy = p3.Y;

        float D = 2f * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
        if (MathF.Abs(D) < 1e-6f)
            return null; // 三点共线

        float a2 = ax * ax + ay * ay;
        float b2 = bx * bx + by * by;
        float c2 = cx * cx + cy * cy;

        float ux = (a2 * (by - cy) + b2 * (cy - ay) + c2 * (ay - by)) / D;
        float uy = (a2 * (cx - bx) + b2 * (ax - cx) + c2 * (bx - ax)) / D;

        var center = new SKPoint(ux, uy);
        float radius = SKPoint.Distance(center, p1);

        return (center, radius);
    }

    /// <summary>
    /// 构建经过 P1→P2→P3 的圆弧 SKPath
    /// 通过外接圆计算弧度范围，使圆弧必须经过中间点 P2
    /// </summary>
    public static SKPath? BuildArcPath(SKPoint p1, SKPoint p2, SKPoint p3)
    {
        var path = new SKPath();
        if (FillArcPath(path, p1, p2, p3))
            return path;
        path.Dispose();
        return null;
    }

    /// <summary>
    /// 构建标准椭圆弧 SKPath（通过中心点、长短半径、起止角度）
    /// 支持椭圆弧绘制，不依赖三点计算
    /// </summary>
    /// <param name="center">椭圆中心点</param>
    /// <param name="radiusX">X 方向半径（长半轴）</param>
    /// <param name="radiusY">Y 方向半径（短半轴）</param>
    /// <param name="startAngleDeg">起始角度（度）</param>
    /// <param name="sweepAngleDeg">扫描角度（度，可正可负）</param>
    /// <returns>椭圆弧路径</returns>
    public static SKPath BuildEllipseArcPath(
        SKPoint center,
        float radiusX,
        float radiusY,
        float startAngleDeg,
        float sweepAngleDeg)
    {
        var path = new SKPath();
        FillEllipseArcPath(path, center, radiusX, radiusY, startAngleDeg, sweepAngleDeg);
        return path;
    }

    /// <summary>
    /// 将椭圆弧路径数据写入已有的 SKPath（用于池化复用）
    /// </summary>
    public static void FillEllipseArcPath(
        SKPath path,
        SKPoint center,
        float radiusX,
        float radiusY,
        float startAngleDeg,
        float sweepAngleDeg)
    {
        // 将角度转换为弧度
        float startRad = startAngleDeg * MathF.PI / 180f;
        float sweepRad = sweepAngleDeg * MathF.PI / 180f;
        float absSweep = MathF.Abs(sweepRad);

        // 计算起点和终点
        var startPoint = new SKPoint(
            center.X + radiusX * MathF.Cos(startRad),
            center.Y + radiusY * MathF.Sin(startRad));
        var endPoint = new SKPoint(
            center.X + radiusX * MathF.Cos(startRad + sweepRad),
            center.Y + radiusY * MathF.Sin(startRad + sweepRad));

        // 接近整圆时退回到 AddArc
        if (absSweep >= 6.265f) // 359°
        {
            var rect = new SKRect(
                center.X - radiusX,
                center.Y - radiusY,
                center.X + radiusX,
                center.Y + radiusY);
            path.AddArc(rect, startAngleDeg, sweepAngleDeg);
            return;
        }

        // 使用 ConicTo 精确绘制椭圆弧
        // 当弧接近半圆（> 170°）时拆成两段以保证数值稳定
        const float SplitThreshold = 2.967f; // 170° in radians

        if (absSweep > SplitThreshold)
        {
            float halfSweep = sweepRad * 0.5f;
            float midAngle = startRad + halfSweep;
            var midPoint = new SKPoint(
                center.X + radiusX * MathF.Cos(midAngle),
                center.Y + radiusY * MathF.Sin(midAngle));

            float quarterSweep = sweepRad * 0.25f;
            float w = MathF.Cos(quarterSweep);

            // 第一段：起点 → 中点
            float bisector1 = startRad + quarterSweep;
            var q1 = new SKPoint(
                center.X + (radiusX / w) * MathF.Cos(bisector1),
                center.Y + (radiusY / w) * MathF.Sin(bisector1));
            path.MoveTo(startPoint);
            path.ConicTo(q1, midPoint, w);

            // 第二段：中点 → 终点
            float bisector2 = midAngle + quarterSweep;
            var q2 = new SKPoint(
                center.X + (radiusX / w) * MathF.Cos(bisector2),
                center.Y + (radiusY / w) * MathF.Sin(bisector2));
            path.ConicTo(q2, endPoint, w);
        }
        else
        {
            float halfSweep = sweepRad * 0.5f;
            float w = MathF.Cos(halfSweep);
            float bisector = startRad + halfSweep;
            var q = new SKPoint(
                center.X + (radiusX / w) * MathF.Cos(bisector),
                center.Y + (radiusY / w) * MathF.Sin(bisector));
            path.MoveTo(startPoint);
            path.ConicTo(q, endPoint, w);
        }
    }

    /// <summary>
    /// 将圆弧路径数据写入已有的 SKPath（用于池化复用）
    /// </summary>
    /// <returns>是否成功写入（三点共线时返回 false）</returns>
    public static bool FillArcPath(SKPath path, SKPoint p1, SKPoint p2, SKPoint p3)
    {
        var result = Circumcircle(p1, p2, p3);
        if (result is null) return false;

        var (center, radius) = result.Value;

        // 计算三点对应的角度（弧度）
        float a1 = MathF.Atan2(p1.Y - center.Y, p1.X - center.X);
        float am = MathF.Atan2(p2.Y - center.Y, p2.X - center.X);
        float a3 = MathF.Atan2(p3.Y - center.Y, p3.X - center.X);

        // 将角度归一化到 [0, 2π)
        static float Norm(float a) => ((a % (2 * MathF.PI)) + 2 * MathF.PI) % (2 * MathF.PI);
        a1 = Norm(a1);
        am = Norm(am);
        a3 = Norm(a3);

        // 判断扫描方向：CCW 方向从 a1 出发先到 am 还是先到 a3
        float CcwDist(float from, float to) => Norm(to - from);
        bool sweepCCW = CcwDist(a1, am) < CcwDist(a1, a3);

        // 计算扫描角度（弧度）
        float sweepRad = sweepCCW
            ? CcwDist(a1, a3)
            : -(2 * MathF.PI - CcwDist(a1, a3));

        float absSweep = MathF.Abs(sweepRad);

        // 接近整圆时退回到 AddArc（ConicTo 无法直接表达整圆）
        if (absSweep >= 6.265f) // 359°
        {
            float startDeg = a1 * 180f / MathF.PI;
            float sweepDeg = sweepRad * 180f / MathF.PI;
            var rect = new SKRect(
                center.X - radius,
                center.Y - radius,
                center.X + radius,
                center.Y + radius);
            path.AddArc(rect, startDeg, sweepDeg);
            return true;
        }

        // 使用 ConicTo 精确过起点 p1 和终点 p3，避免 AddArc 的角度往返精度损失。
        // 当弧接近半圆（> 170°）时控制点趋于无穷远，拆成两段以保证数值稳定。
        const float SplitThreshold = 2.967f; // 170° in radians

        if (absSweep > SplitThreshold)
        {
            float halfSweep = sweepRad * 0.5f;
            float midAngle = a1 + halfSweep;
            var pmid = new SKPoint(
                center.X + radius * MathF.Cos(midAngle),
                center.Y + radius * MathF.Sin(midAngle));

            float quarterSweep = sweepRad * 0.25f;
            float w = MathF.Cos(quarterSweep);
            float invW = radius / w;

            // 第一段 p1 → pmid
            float bisector1 = a1 + quarterSweep;
            var q1 = new SKPoint(
                center.X + invW * MathF.Cos(bisector1),
                center.Y + invW * MathF.Sin(bisector1));
            path.MoveTo(p1);
            path.ConicTo(q1, pmid, w);

            // 第二段 pmid → p3
            float bisector2 = midAngle + quarterSweep;
            var q2 = new SKPoint(
                center.X + invW * MathF.Cos(bisector2),
                center.Y + invW * MathF.Sin(bisector2));
            path.ConicTo(q2, p3, w);
        }
        else
        {
            float halfSweep = sweepRad * 0.5f;
            float w = MathF.Cos(halfSweep);
            float bisector = a1 + halfSweep;
            float invW = radius / w;
            var q = new SKPoint(
                center.X + invW * MathF.Cos(bisector),
                center.Y + invW * MathF.Sin(bisector));
            path.MoveTo(p1);
            path.ConicTo(q, p3, w);
        }

        return true;
    }

    /// <summary>
    /// 获取圆弧信息字符串（用于 UI 显示）
    /// </summary>
    public static string GetArcInfo(SKPoint p1, SKPoint p2, SKPoint p3)
    {
        var result = Circumcircle(p1, p2, p3);
        if (result is null) return "三点共线，无法构成圆弧";

        var (center, radius) = result.Value;

        float a1 = MathF.Atan2(p1.Y - center.Y, p1.X - center.X);
        float am = MathF.Atan2(p2.Y - center.Y, p2.X - center.X);
        float a3 = MathF.Atan2(p3.Y - center.Y, p3.X - center.X);

        static float Norm(float a) => ((a % (2 * MathF.PI)) + 2 * MathF.PI) % (2 * MathF.PI);
        a1 = Norm(a1); am = Norm(am); a3 = Norm(a3);

        float CcwDist(float from, float to) => Norm(to - from);
        bool sweepCCW = CcwDist(a1, am) < CcwDist(a1, a3);
        float sweepRad = sweepCCW ? CcwDist(a1, a3) : -(2 * MathF.PI - CcwDist(a1, a3));
        float sweepDeg = sweepRad * 180f / MathF.PI;

        return $"圆心: ({center.X:F1}, {center.Y:F1})   " +
               $"半径: {radius:F1}px   " +
               $"扫描角: {sweepDeg:F1}°   " +
               $"方向: {(sweepCCW ? "逆时针" : "顺时针")}";
    }

    /// <summary>
    /// 获取圆弧的边界矩形
    /// </summary>
    /// <param name="center"></param>
    /// <param name="radius"></param>
    /// <param name="startAngle"></param>
    /// <param name="sweepAngle"></param>
    /// <returns></returns>
    public static SKRect CalculateArcBounds(SKPoint center, float radius, float startAngle, float sweepAngle)
    {
        // 1. 将起点和终点加入候选点集
        var points = new List<SKPoint>();

        // 计算起点坐标
        points.Add(GetPointOnCircle(center, radius, startAngle));
        // 计算终点坐标
        points.Add(GetPointOnCircle(center, radius, startAngle + sweepAngle));

        // 2. 检查 0, 90, 180, 270 度这四个象限点
        float[] quadrants = { 0, 90, 180, 270 };
        foreach (float q in quadrants)
        {
            if (IsAngleOnArc(q, startAngle, sweepAngle))
            {
                points.Add(GetPointOnCircle(center, radius, q));
            }
        }

        // 3. 计算这些点的最小/最大值
        float minX = points.Min(p => p.X);
        float maxX = points.Max(p => p.X);
        float minY = points.Min(p => p.Y);
        float maxY = points.Max(p => p.Y);

        return new SKRect(minX, minY, maxX, maxY);
    }

    // 辅助方法：根据角度获取圆周上的点
    private static SKPoint GetPointOnCircle(SKPoint center, float radius, float angleDegrees)
    {
        double angleRadians = angleDegrees * Math.PI / 180.0;
        return new SKPoint(
            center.X + radius * (float)Math.Cos(angleRadians),
            center.Y + radius * (float)Math.Sin(angleRadians)
        );
    }

    // 辅助方法：判断角度是否在圆弧范围内
    private static bool IsAngleOnArc(float angle, float start, float sweep)
    {
        float end = start + sweep;

        // 标准化角度到 [0, 360)
        float s = (start % 360 + 360) % 360;
        float e = (end % 360 + 360) % 360;
        float a = (angle % 360 + 360) % 360;

        if (sweep >= 360 || sweep <= -360) return true;

        if (sweep > 0) // 顺时针
        {
            return s < e ? (a >= s && a <= e) : (a >= s || a <= e);
        }
        else // 逆时针
        {
            return e < s ? (a >= e && a <= s) : (a >= e || a <= s);
        }
    }
}
