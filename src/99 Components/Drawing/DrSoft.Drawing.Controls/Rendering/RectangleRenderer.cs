using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Helpers;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Model;
using DrSoft.Drawing.Rendering;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Windows.Shapes;

namespace DrSoft.Drawing.Controls.Rendering
{
    [RendererFor(typeof(DrawRectangle))]
    public class RectangleRenderer : IRenderer
    {
        public bool CanRender(IShape obj)
        {
            return obj is DrawRectangle;
        }

        /// <summary>
        /// 绘制线段
        /// </summary>
        /// <param name="shape"></param>
        /// <param name="canvas"></param>
        /// <param name="vp"></param>
        /// <param name="cache"></param>
        public void Render(IShape shape, SKCanvas canvas, IViewport vp, SKPaintCache cache)
        {
            if (shape is not DrawRectangle rectangle) return;

            canvas.Save();

            // 将局部路径先变换到世界坐标，再用世界坐标描边。
            // 这样对象倾斜不会再次作用到描边宽度。
            var adjustedWidth = StrokeWidthHelper.ResolveViewportInvariantStrokeWidth(rectangle, vp);
            var strokePaint = cache.GetStrokePaint(rectangle.Pen.Color, adjustedWidth);

            var path = rectangle.GetPath(cache);
            try
            {
                //var matrix = rectangle.GetTransformMatrix();
                path.Transform(rectangle.Matrix);
                var outlinePaint = OutlineStyleHelper.CreateOutlinePaint(strokePaint, rectangle);
                canvas.DrawPath(path, outlinePaint ?? strokePaint);
                outlinePaint?.Dispose();
            }
            finally
            {
                cache.ReturnPath(path);
            }
            canvas.Restore();
        }

        /// <summary>
        /// 绘制预览线段
        /// </summary>
        /// <param name="shape"></param>
        /// <param name="canvas"></param>
        /// <param name="strokePaint"></param>
        /// <param name="paintCache"></param>
        public void PreviewRender(IShape shape, SKCanvas canvas, SKPaint strokePaint, SKPaintCache paintCache)
        {
            if (shape is not DrawRectangle rectangle) return;

            // 使用路径绘制矩形（支持旋转）
            var path = paintCache.GetPath();
            try
            {
                // 移动到第一个顶点
                var firstPoint = rectangle.Points[0];
                path.MoveTo((float)firstPoint.X, (float)firstPoint.Y);

                // 添加其他顶点
                for (int i = 1; i < rectangle.Points.Count; i++)
                {
                    var point = rectangle.Points[i];
                    path.LineTo((float)point.X, (float)point.Y);
                }

                // 闭合路径
                path.Close();

                // 绘制路径
                var outlinePaint = OutlineStyleHelper.CreateOutlinePaint(strokePaint, rectangle);
                canvas.DrawPath(path, outlinePaint ?? strokePaint);
                outlinePaint?.Dispose();
            }
            finally
            {
                paintCache.ReturnPath(path);
            }
        }

        //private HatchShaderRenderer _shaderRenderer = new HatchShaderRenderer();
        //public void RenderHatch(IShape shape, IHatchable hatchable, SKCanvas canvas, IViewport vp, SKPaintCache cache)
        //{
        //    if (shape is not DrawRectangle rectangle || hatchable?.HatchPattern == null) return;

        //    var lines = hatchable.HatchPattern.HatchLineObjects;
        //    if (lines == null || lines.Count == 0) return;

        //    // 使用 Shader 渲染（性能最优，无采样问题）
        //    _shaderRenderer.RenderHatchWithShader(rectangle, lines, canvas, vp.Scale);
        //}


        public void RenderHatch(IShape shape, IHatchable hatchable, SKCanvas canvas, IViewport vp, SKPaintCache cache)
        {
            if (shape is not DrawRectangle rectangle || hatchable == null || hatchable.HatchPattern == null
                || hatchable.HatchPattern.HatchLineObjects == null || hatchable.HatchPattern.HatchLineObjects.Count == 0) return;

            canvas.Save();

            // 应用变换矩阵（叠加到当前变换）
            var matrix = rectangle.GetTransformMatrix();
            canvas.Concat(ref matrix);

            var lines = hatchable.HatchPattern.HatchLineObjects;
            SKMatrix totalMatrix = canvas.TotalMatrix;
            float scaleX = Math.Abs(totalMatrix.ScaleX);

            // ── 动态LOD阈值：根据线数量自适应决定渲染策略 ──
            float lodThreshold = HatchRenderHelper.ComputeLodThreshold(lines.Count);

            // ── 基于屏幕像素密度对填充线降采样，减少不可区分的线条 ──
            var renderLines = HatchRenderHelper.SampleLines(lines, scaleX);

            // ── 计算渐进式笔画宽度：根据缩放级别平滑过渡 ──
            // 低缩放（密集填充）→ 线条合并为实心效果
            // 高缩放 → 逐步显示单独的线条
            float adjustedWidth = HatchRenderHelper.ComputeProgressiveStrokeWidth(lines, renderLines, scaleX, rectangle.Pen.StrokeWidth, vp.Scale);

            if (rectangle.HatchParamInfo.FillStyleIndex == 0)
            {
                // ─── 实线填充模式 ───
                var fillPaint = cache.GetStrokePaint(SKColor.Parse(rectangle.HatchParamInfo.FillColor), adjustedWidth);
                using (var path = HatchRenderHelper.BuildBatchPath(renderLines))
                {
                    canvas.DrawPath(path, fillPaint);
                }
            }
            else if (rectangle.HatchParamInfo.FillStyleIndex == 1 || rectangle.HatchParamInfo.FillStyleIndex == 2)
            {
                // ─── 虚线/点线填充模式 ───
                if (scaleX <= lodThreshold)
                {
                    // 简化渲染：缩小状态下用实线近似，大幅降低绘制开销
                    var fillPaint = cache.GetStrokePaint(SKColor.Parse(rectangle.HatchParamInfo.FillColor), adjustedWidth);
                    using (var path = HatchRenderHelper.BuildBatchPath(renderLines))
                    {
                        canvas.DrawPath(path, fillPaint);
                    }
                }
                else
                {
                    // 精细渲染：逐线DrawLine + 固定虚线参数，确保视觉正确且高性能
                    HatchRenderHelper.RenderDashLinesIndividually(
                        canvas, renderLines,
                        SKColor.Parse(rectangle.HatchParamInfo.FillColor),
                        scaleX, rectangle.HatchParamInfo.FillStyleIndex);
                }
            }

            canvas.Restore();
        }

        #region
        //public void RenderHatch(IShape shape, SKCanvas canvas, IViewport vp, SKPaintCache cache)
        //{
        //    Trace.WriteLine("RenderHatch>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>");

        //    if (shape is not DrawRectangle rectangle) return;

        //    Trace.WriteLine("Name:" + rectangle.Name);
        //    if (rectangle.HatchLineObjects == null || rectangle.HatchLineObjects.Count == 0)
        //        rectangle.CreateHatchPattern();
        //    //var hatch = rectangle.CreateHatchPattern();
        //    //if (hatch == null || hatch.HatchLineObjects == null || hatch.HatchLineObjects.Count == 0) return;

        //    canvas.Save();

        //    // 应用变换矩阵（叠加到当前变换）
        //    var matrix = rectangle.GetTransformMatrix();
        //    canvas.Concat(ref matrix);

        //    Stopwatch stopwatch = new Stopwatch();
        //    stopwatch.Start();
        //    // 定义10条直线的坐标
        //    var lines = new List<(SKPoint start, SKPoint end)>();
        //    //lines.AddRange(hatch.HatchLineObjects);
        //    lines.AddRange(rectangle.HatchLineObjects);

        //    if (rectangle.HatchParamInfo.FillStyleIndex == 0)
        //    {
        //        var adjustedWidth = rectangle.Pen.StrokeWidth * 6.83f / vp.Scale;
        //        var fillPaint = cache.GetStrokePaint(SKColor.Parse(rectangle.HatchParamInfo.FillColor), adjustedWidth);
        //        using (var path = new SKPath())
        //        {
        //            // 添加其他顶点
        //            for (int i = 1; i < lines.Count; i++)
        //            {
        //                var point = lines[i];
        //                path.MoveTo(point.start.X, point.start.Y);
        //                path.LineTo(point.end.X, point.end.Y);
        //            }

        //            // 绘制路径
        //            canvas.DrawPath(path, fillPaint);
        //        }
        //    }
        //    else if (rectangle.HatchParamInfo.FillStyleIndex == 1 || rectangle.HatchParamInfo.FillStyleIndex == 2)
        //    {
        //        Trace.WriteLine("Render1");

        //        SKMatrix totalMatrix = canvas.TotalMatrix;
        //        Trace.WriteLine("----------------------> " + ";" + totalMatrix.ScaleX + "," + totalMatrix.ScaleY);

        //        //float dotSpacing = 0.2f / totalMatrix.ScaleX;
        //        float dotSpacing = 0.1f;//50
        //                                //float dotSpacing = 0.01f;//200
        //        float dotSize = 2.0f / totalMatrix.ScaleX;
        //        double scaleParam = Math.Pow(10, (1 / dotSpacing));

        //        if (totalMatrix.ScaleX <= 50)
        //        {
        //            // 1. 创建路径并添加所有直线
        //            using (SKPath path = new SKPath())
        //            {
        //                foreach (var line in lines)
        //                {
        //                    path.MoveTo(line.start);
        //                    path.LineTo(line.end);
        //                }

        //                // 2. 配置画笔
        //                var adjustedWidth = rectangle.Pen.StrokeWidth * 6.83f / vp.Scale;
        //                var fillPaint = cache.GetStrokePaint(SKColor.Parse(rectangle.HatchParamInfo.FillColor), adjustedWidth);
        //                canvas.DrawPath(path, fillPaint);
        //            }
        //        }
        //        else
        //        {
        //            float dashLength = 0;
        //            if (rectangle.HatchParamInfo.FillStyleIndex == 1)
        //            {
        //                dashLength = 0.1f;
        //            }

        //            Trace.WriteLine("Render2");
        //            // 2. 配置画笔
        //            using (SKPaint paint = new SKPaint())
        //            {
        //                paint.Style = SKPaintStyle.Stroke;
        //                paint.StrokeWidth = dotSize;           // 点的大小
        //                paint.StrokeCap = SKStrokeCap.Round;   // 关键：圆形端点
        //                paint.Color = SKColor.Parse(rectangle.HatchParamInfo.FillColor);
        //                paint.IsAntialias = true;               // 抗锯齿

        //                // 3. 创建点虚线效果（实线长度为0，间隙为点间距）
        //                float[] intervals = new float[] { dashLength, dotSpacing };
        //                paint.PathEffect = SKPathEffect.CreateDash(intervals, 0);
        //                foreach (var line in lines)
        //                {
        //                    using (SKPath path2 = new SKPath())
        //                    {
        //                        path2.MoveTo(line.start);
        //                        path2.LineTo(line.end);
        //                        canvas.DrawPath(path2, paint);
        //                    }
        //                }
        //            }
        //        }
        //    }

        //    canvas.Restore();
        //    stopwatch.Stop();
        //    Trace.WriteLine("================================Name:" + rectangle.Name + " Render Time(ms)" + stopwatch.Elapsed.TotalMilliseconds);
        //    return;



        //    //if (shape is not DrawRectangle rectangle) return;
        //    //// 根据视口缩放比例调整线宽，保持视觉上的恒定线宽
        //    //var adjustedWidth = rectangle.Pen.StrokeWidth * 6.83f / vp.Scale;
        //    //var fillPaint = cache.GetStrokePaint(rectangle.Pen.Color, adjustedWidth);

        //    //float dotSpacing = 1.0f;
        //    //float dotSize = 0.001f;



        //    //SKMatrix matrix = canvas.TotalMatrix;
        //    ////var matrix = rectangle.GetTransformMatrix();

        //    //float scale = matrix.ScaleX;
        //    //float dotSpacing = 10f / scale;   // 间距也要缩放
        //    //Trace.WriteLine("----------------------> " + dotSpacing + ";" + matrix.ScaleX + "," + matrix.ScaleY);
        //    ////float dotSpacing = 30.1f;
        //    //float dotSize = 5;
        //    //fillPaint.Style = SKPaintStyle.Stroke;
        //    ////fillPaint.StrokeWidth = dotSize;
        //    //fillPaint.StrokeWidth = 0.5f / scale;  // 线宽要相应调整
        //    //fillPaint.StrokeCap = SKStrokeCap.Round;
        //    ////fillPaint.Color = rectangle.Pen.Color;
        //    //fillPaint.IsAntialias = true;
        //    //// 点虚线：实线长度为0，间隙为间距
        //    //// 由于圆头帽会在两端各延长半个线宽，为避免重叠，间隙要 >= 线宽
        //    //float[] intervals = new float[] { 0, dotSpacing };
        //    //fillPaint.PathEffect = SKPathEffect.CreateDash(intervals, 0);

        //    // 使用路径绘制矩形（支持旋转）
        //    //if (rectangle.HatchPath == null) rectangle.CreateHatchPattern();
        //    //if (rectangle.HatchPath == null) return;
        //    //using (var path = rectangle.CreateHatchPattern().SKPath)
        //    //{
        //    //    //    // 移动到第一个顶点
        //    //    //    var firstPoint = rectangle.Points[0];
        //    //    //    path.MoveTo((float)firstPoint.X, (float)firstPoint.Y);

        //    //    //    // 添加其他顶点
        //    //    //    for (int i = 1; i < rectangle.Points.Count; i++)
        //    //    //    {
        //    //    //        var point = rectangle.Points[i];
        //    //    //        path.LineTo((float)point.X, (float)point.Y);
        //    //    //    }

        //    //    //// 闭合路径
        //    //    //path.Close();

        //    //    //// 绘制路径
        //    //    //canvas.DrawPath(path, fillPaint);








        //    //    // 2. 配置画笔
        //    //    using (SKPaint paint = new SKPaint())
        //    //    {
        //    //        paint.Style = SKPaintStyle.Stroke;
        //    //        paint.StrokeWidth = dotSize;           // 点的大小
        //    //        paint.StrokeCap = SKStrokeCap.Round;   // 关键：圆形端点
        //    //        paint.Color = rectangle.Pen.Color;
        //    //        paint.IsAntialias = true;               // 抗锯齿

        //    //        // 3. 创建点虚线效果（实线长度为0，间隙为点间距）
        //    //        float[] intervals = new float[] { 0, dotSpacing };
        //    //        paint.PathEffect = SKPathEffect.CreateDash(intervals, 0);

        //    //        // 4. 渲染
        //    //        canvas.DrawPath(path, paint);
        //    //    }





        //    //}
        //}





        //public void RenderHatch(IShape shape, SKCanvas canvas, IViewport vp, SKPaintCache cache)
        //{
        //    Trace.WriteLine("RenderHatch>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>");


        //    // 定义10条直线的坐标
        //    var lines = new List<(SKPoint start, SKPoint end)>
        //    {
        //        //// 第1条：水平线
        //        //(new SKPoint(50, 50), new SKPoint(300, 50)),

        //        //// 第2条：垂直线
        //        //(new SKPoint(50, 100), new SKPoint(50, 250)),

        //        //// 第3条：斜线
        //        //(new SKPoint(100, 100), new SKPoint(350, 200)),

        //        //// 第4条：水平线
        //        //(new SKPoint(50, 150), new SKPoint(300, 150)),

        //        //// 第5条：垂直线
        //        //(new SKPoint(100, 100), new SKPoint(100, 300)),

        //        //// 第6条：斜线
        //        //(new SKPoint(150, 50), new SKPoint(400, 250)),

        //        //// 第7条：水平线
        //        //(new SKPoint(50, 200), new SKPoint(350, 200)),

        //        //// 第8条：垂直线
        //        //(new SKPoint(200, 50), new SKPoint(200, 300)),

        //        //// 第9条：斜线
        //        //(new SKPoint(50, 250), new SKPoint(300, 100)),

        //        //// 第10条：水平线
        //        //(new SKPoint(50, 300), new SKPoint(400, 300))
        //    };


        //    for (int i = 0; i < 4000; i++)
        //    {
        //        float y = 0.1f * i;
        //        if (i == 0)
        //        {
        //            lines.Add((new SKPoint(-500, 100), new SKPoint(500, 100)));
        //        }
        //        else
        //        {
        //            lines.Add((new SKPoint(-500, 100 + y), new SKPoint(500, 100 + y)));
        //            lines.Add((new SKPoint(-500, 100 - y), new SKPoint(500, 100 - y)));
        //        }
        //    }


        //    for (int i = 0; i < 4000; i++)
        //    {
        //        float y = 0.1f * i;
        //        if (i == 0)
        //        {
        //            lines.Add((new SKPoint(-500, -100), new SKPoint(500, -100)));
        //        }
        //        else
        //        {
        //            lines.Add((new SKPoint(-500, -100 + y), new SKPoint(500, -100 + y)));
        //            lines.Add((new SKPoint(-500, -100 - y), new SKPoint(500, -100 - y)));
        //        }
        //    }

        //    SKColor color = default;
        //    if (color == default) color = SKColors.Red;
        //    SKMatrix matrix = canvas.TotalMatrix;
        //    Trace.WriteLine("----------------------> " + ";" + matrix.ScaleX + "," + matrix.ScaleY);

        //    //float dotSpacing = 0.2f / matrix.ScaleX;
        //    //float dotSpacing = 0.1f;//100
        //    float dotSpacing = 0.01f;//200
        //    float dotSize = 1.0f / matrix.ScaleX;

        //    double scaleParam = Math.Pow(10, (1 / dotSpacing));

        //    if (matrix.ScaleX <= 200)
        //    {
        //        Trace.WriteLine("Render1");
        //        // 1. 创建路径并添加所有直线
        //        using (SKPath path = new SKPath())
        //        {
        //            foreach (var line in lines)
        //            {
        //                path.MoveTo(line.start);
        //                path.LineTo(line.end);
        //            }

        //            // 2. 配置画笔
        //            using (SKPaint paint = new SKPaint())
        //            {
        //                paint.Style = SKPaintStyle.Stroke;
        //                paint.StrokeWidth = dotSize;           // 点的大小
        //                paint.StrokeCap = SKStrokeCap.Round;   // 关键：圆形端点
        //                paint.Color = color;
        //                paint.IsAntialias = true;               // 抗锯齿

        //                // 3. 创建点虚线效果（实线长度为0，间隙为点间距）
        //                float[] intervals = new float[] { 0, dotSpacing };
        //                paint.PathEffect = SKPathEffect.CreateDash(intervals, 0);

        //                // 4. 渲染
        //                canvas.DrawPath(path, paint);

        //            }
        //        }
        //    }
        //    else
        //    {
        //        Trace.WriteLine("Render2");
        //        // 2. 配置画笔
        //        using (SKPaint paint = new SKPaint())
        //        {
        //            paint.Style = SKPaintStyle.Stroke;
        //            paint.StrokeWidth = dotSize;           // 点的大小
        //            paint.StrokeCap = SKStrokeCap.Round;   // 关键：圆形端点
        //            paint.Color = color;
        //            paint.IsAntialias = true;               // 抗锯齿

        //            // 3. 创建点虚线效果（实线长度为0，间隙为点间距）
        //            float[] intervals = new float[] { 0, dotSpacing };
        //            paint.PathEffect = SKPathEffect.CreateDash(intervals, 0);
        //            // 关键改进：每条线段单独绘制
        //            foreach (var line in lines)
        //            {
        //                using (SKPath path2 = new SKPath())
        //                {
        //                    path2.MoveTo(line.start);
        //                    path2.LineTo(line.end);
        //                    canvas.DrawPath(path2, paint);
        //                }
        //            }
        //        }
        //    }


        //    return;



        //    //if (shape is not DrawRectangle rectangle) return;
        //    //// 根据视口缩放比例调整线宽，保持视觉上的恒定线宽
        //    //var adjustedWidth = rectangle.Pen.StrokeWidth * 6.83f / vp.Scale;
        //    //var fillPaint = cache.GetStrokePaint(rectangle.Pen.Color, adjustedWidth);

        //    //float dotSpacing = 1.0f;
        //    //float dotSize = 0.001f;



        //    //SKMatrix matrix = canvas.TotalMatrix;
        //    ////var matrix = rectangle.GetTransformMatrix();

        //    //float scale = matrix.ScaleX;
        //    //float dotSpacing = 10f / scale;   // 间距也要缩放
        //    //Trace.WriteLine("----------------------> " + dotSpacing + ";" + matrix.ScaleX + "," + matrix.ScaleY);
        //    ////float dotSpacing = 30.1f;
        //    //float dotSize = 5;
        //    //fillPaint.Style = SKPaintStyle.Stroke;
        //    ////fillPaint.StrokeWidth = dotSize;
        //    //fillPaint.StrokeWidth = 0.5f / scale;  // 线宽要相应调整
        //    //fillPaint.StrokeCap = SKStrokeCap.Round;
        //    ////fillPaint.Color = rectangle.Pen.Color;
        //    //fillPaint.IsAntialias = true;
        //    //// 点虚线：实线长度为0，间隙为间距
        //    //// 由于圆头帽会在两端各延长半个线宽，为避免重叠，间隙要 >= 线宽
        //    //float[] intervals = new float[] { 0, dotSpacing };
        //    //fillPaint.PathEffect = SKPathEffect.CreateDash(intervals, 0);

        //    // 使用路径绘制矩形（支持旋转）
        //    //if (rectangle.HatchPath == null) rectangle.CreateHatchPattern();
        //    //if (rectangle.HatchPath == null) return;
        //    //using (var path = rectangle.CreateHatchPattern().SKPath)
        //    //{
        //    //    //    // 移动到第一个顶点
        //    //    //    var firstPoint = rectangle.Points[0];
        //    //    //    path.MoveTo((float)firstPoint.X, (float)firstPoint.Y);

        //    //    //    // 添加其他顶点
        //    //    //    for (int i = 1; i < rectangle.Points.Count; i++)
        //    //    //    {
        //    //    //        var point = rectangle.Points[i];
        //    //    //        path.LineTo((float)point.X, (float)point.Y);
        //    //    //    }

        //    //    //// 闭合路径
        //    //    //path.Close();

        //    //    //// 绘制路径
        //    //    //canvas.DrawPath(path, fillPaint);








        //    //    // 2. 配置画笔
        //    //    using (SKPaint paint = new SKPaint())
        //    //    {
        //    //        paint.Style = SKPaintStyle.Stroke;
        //    //        paint.StrokeWidth = dotSize;           // 点的大小
        //    //        paint.StrokeCap = SKStrokeCap.Round;   // 关键：圆形端点
        //    //        paint.Color = rectangle.Pen.Color;
        //    //        paint.IsAntialias = true;               // 抗锯齿

        //    //        // 3. 创建点虚线效果（实线长度为0，间隙为点间距）
        //    //        float[] intervals = new float[] { 0, dotSpacing };
        //    //        paint.PathEffect = SKPathEffect.CreateDash(intervals, 0);

        //    //        // 4. 渲染
        //    //        canvas.DrawPath(path, paint);
        //    //    }





        //    //}
        //}
        #endregion
    }
}
