using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.Algorithm
{
    /// <summary>
    /// 高性能的填充线样式发射器：根据 FillStyleIndex（0=实线 / 1=短虚线 / 2=点虚线）
    /// 把一组扫描线 (Start,End) 直接转换为 DrawObject 列表。
    ///
    /// 相较旧实现的关键优化：
    /// 1. 完全去除 Trace.WriteLine（原实现对每一个“点”都调用 Trace.WriteLine，在
    ///    Parallel.For 中会触发 TraceListener 锁同步，是最主要的耗时点）。
    /// 2. 去除 Parallel.For 与中间 DashSegment / PointF 分配，直接在线上
    ///    走迭代并 new DrawPolyLines / DrawDot，避免 GC 压力。
    /// 3. 点虚线不再计算垂直方向的两端点（消费者只取中点），只做中心点推进。
    /// </summary>
    internal static class FillLineStyleEmitter
    {
        // 短虚线默认参数（与旧实现保持一致：实线 1.0，空白 0.5，两端对齐）
        private const float SegmentSolidLength = 1.0f;
        private const float SegmentBlankLength = 0.5f;
        private const bool SegmentKeepEndsEqual = true;
        private const float SegmentMinLenForDash = 1f;

        // 点虚线默认参数（与旧实现保持一致：实线段 0.2，空白 0.2，渲染半径 0.001）
        private const float DotSolidLength = 0.002f;
        private const float DotBlankLength = 0.4f;
        private const float DotRenderRadius = 1.0f;

        /// <summary>
        /// 把扫描线转换成 DrawObject 列表。
        /// </summary>
        /// <param name="fillLines">扫描线集合（局部坐标）</param>
        /// <param name="fillStyleIndex">0=实线, 1=短虚线, 2=点虚线</param>
        /// <param name="offsetX">输出坐标相对 fillLine 的 X 偏移（通常为 SharpCenter.X）</param>
        /// <param name="offsetY">输出坐标相对 fillLine 的 Y 偏移（通常为 SharpCenter.Y）</param>
        public static List<DrawObject> Convert(
            IReadOnlyList<(SKPoint Start, SKPoint End)> fillLines,
            HatchParamDto hatchParamInfo, SKMatrix matrix, string name)
        {
            var result = new List<DrawObject>(Math.Max(fillLines.Count, 8));
            int fillStyleIndex = hatchParamInfo.FillStyleIndex;

            switch (fillStyleIndex)
            {
                case 1:
                    EmitSegmentDash(result, fillLines, hatchParamInfo, matrix, name);
                    break;
                case 2:
                    EmitDotDashParallel(result, fillLines, hatchParamInfo, matrix, name);
                    //EmitDotDash(result, fillLines, hatchParamInfo, matrix, name);
                    //EmitDotDashParallelIndexed(result, fillLines, hatchParamInfo, matrix, name);
                    //EmitDotDashCSL(result, fillLines, hatchParamInfo, matrix, name);
                    //EmitDotDashUltimate(result, fillLines, hatchParamInfo, matrix, name);
                    break;
                case 0:
                default:
                    EmitSolid(result, fillLines, hatchParamInfo, matrix, name);
                    break;
            }

            return result;
        }

        public static List<DrawObject> Convert3(IReadOnlyList<(SKPoint Start, SKPoint End)> fillLines, HatchParamDto hatchParamInfo, string name)
        {
            var lines = new List<DrawObject>(Math.Max(fillLines.Count, 8));
            lines = EmitSolid3(fillLines, hatchParamInfo, name);
            return lines;
        }




        private static void EmitSolid(
            List<DrawObject> result,
            IReadOnlyList<(SKPoint Start, SKPoint End)> fillLines,
           HatchParamDto hatchParamInfo, SKMatrix matrix, string name)
        {
            int n = fillLines.Count;
            for (int i = 0; i < n; i++)
            {
                var (s, e) = fillLines[i];
                SKPoint s_new = matrix.MapPoint(s);
                SKPoint e_new = matrix.MapPoint(e);
                var points = new List<Point2D>(2)
                {
                    new Point2D(s_new.X , s_new.Y ),
                    new Point2D(e_new.X , e_new.Y ),
                };
                result.Add(new DrawPolyLines(points) { Name = $"{name}-{(i + 1)}", Pen = new SKPaint() { Color = SKColor.Parse(hatchParamInfo.FillColor), Style = SKPaintStyle.Stroke, StrokeWidth = 0.25f } });
            }
        }


        private static List<DrawObject> EmitSolid3(IReadOnlyList<(SKPoint Start, SKPoint End)> fillLines, HatchParamDto hatchParamInfo, string name)
        {
            List<DrawObject> result = new List<DrawObject>();
            int n = fillLines.Count;
            for (int i = 0; i < n; i++)
            {
                var (s_new, e_new) = fillLines[i];
                var points = new List<Point2D>(2)
                {
                    new Point2D(s_new.X , s_new.Y ),
                    new Point2D(e_new.X , e_new.Y ),
                };
                result.Add(new DrawPolyLines(points) { Name = $"{name}-{(i + 1)}", LineStyle = (LineStyle)(hatchParamInfo.FillStyleIndex), Pen = new SKPaint() { Color = SKColor.Parse(hatchParamInfo.FillColor), Style = SKPaintStyle.Stroke, StrokeWidth = 0.25f } });
            }

            return result;
        }

        private static void EmitSegmentDash(
            List<DrawObject> result,
            IReadOnlyList<(SKPoint Start, SKPoint End)> fillLines,
            HatchParamDto hatchParamInfo, SKMatrix matrix, string name)
        {
            float solidLen = SegmentSolidLength;
            float blankLen = SegmentBlankLength;
            float patternLen = solidLen + blankLen;
            if (patternLen <= 0f) return;

            int n = fillLines.Count;
            for (int i = 0; i < n; i++)
            {
                var (s, e) = fillLines[i];
                SKPoint s_new = matrix.MapPoint(s);
                SKPoint e_new = matrix.MapPoint(e);
                float x0 = s_new.X;
                float y0 = s_new.Y;
                float x1 = e_new.X;
                float y1 = e_new.Y;

                float dx = x1 - x0;
                float dy = y1 - y0;
                float totalLen = (float)Math.Sqrt(dx * dx + dy * dy);
                if (totalLen <= 0f) continue;

                // 总长过短直接当实线，避免走迭代
                if (totalLen < SegmentMinLenForDash)
                {
                    result.Add(new DrawPolyLines(new List<Point2D>(2)
                    {
                        new Point2D(x0, y0),
                        new Point2D(x1, y1),
                    })
                    { Name = $"{name}-{(i + 1)}", Pen = new SKPaint() { Color = SKColor.Parse(hatchParamInfo.FillColor), Style = SKPaintStyle.Stroke, StrokeWidth = 0.25f } });
                    continue;
                }

                float ux = dx / totalLen;
                float uy = dy / totalLen;

                // 首尾实线长度对齐（与 LineDashDecomposer 一致）
                float firstSolidLen = solidLen;
                float lastSolidLen = solidLen;
                if (SegmentKeepEndsEqual && totalLen > patternLen)
                {
                    int fullCycles = (int)((totalLen - solidLen) / patternLen);
                    if (fullCycles >= 1)
                    {
                        float remaining = totalLen - fullCycles * patternLen;
                        float half = remaining * 0.5f;
                        if (half <= solidLen * 1.5f && half >= SegmentMinLenForDash)
                        {
                            firstSolidLen = half;
                            lastSolidLen = half;
                        }
                    }
                }

                // 迭代：实线—空白—实线—空白…，只输出实线段。
                float pos = 0f;
                bool isSolid = true;
                int segIndex = 0;
                while (pos < totalLen - 1e-3f)
                {
                    float segLen;
                    if (isSolid)
                    {
                        if (segIndex == 0)
                            segLen = firstSolidLen;
                        else if (IsLastSolid(pos, totalLen, blankLen, solidLen))
                            segLen = lastSolidLen;
                        else
                            segLen = solidLen;
                    }
                    else
                    {
                        segLen = blankLen;
                    }

                    if (pos + segLen > totalLen)
                        segLen = totalLen - pos;

                    if (isSolid && segLen > 1e-3f)
                    {
                        float sx = x0 + ux * pos;
                        float sy = y0 + uy * pos;
                        float ex = x0 + ux * (pos + segLen);
                        float ey = y0 + uy * (pos + segLen);
                        result.Add(new DrawPolyLines(new List<Point2D>(2)
                        {
                            new Point2D(sx, sy),
                            new Point2D(ex, ey),
                        })
                        { Name = $"{name}-{(i + 1)}-{(segIndex + 1)}", Pen = new SKPaint() { Color = SKColor.Parse(hatchParamInfo.FillColor), Style = SKPaintStyle.Stroke, StrokeWidth = 0.25f } });
                    }

                    pos += segLen;
                    isSolid = !isSolid;
                    segIndex++;
                }
            }
        }

        private static bool IsLastSolid(float pos, float total, float blankLen, float solidLen)
        {
            float remaining = total - pos;
            return Math.Abs(remaining - solidLen) < 0.01f
                   || (remaining > solidLen && remaining < solidLen + blankLen + 0.01f);
        }

        private static void EmitDotDash(
            List<DrawObject> result,
            IReadOnlyList<(SKPoint Start, SKPoint End)> fillLines,
            HatchParamDto hatchParamInfo, SKMatrix matrix, string name)
        {
            float solidLen = DotSolidLength;
            float blankLen = DotBlankLength;
            float step = solidLen + blankLen;
            if (step <= 0f) return;

            float halfSolid = solidLen * 0.5f;
            float renderRadius = DotRenderRadius;

            int n = fillLines.Count;
            for (int i = 0; i < n; i++)
            {
                var (s, e) = fillLines[i];
                SKPoint s_new = matrix.MapPoint(s);
                SKPoint e_new = matrix.MapPoint(e);
                float x0 = s_new.X;
                float y0 = s_new.Y;
                float x1 = e_new.X;
                float y1 = e_new.Y;

                float dx = x1 - x0;
                float dy = y1 - y0;
                float totalLen = (float)Math.Sqrt(dx * dx + dy * dy);
                if (totalLen <= 0f) continue;

                float ux = dx / totalLen;
                float uy = dy / totalLen;

                // 直接按中心位置推进，避免 perpendicular / DashSegment / PointF 中间分配
                int index = 0;
                for (float pos = 0f; pos <= totalLen; pos += step)
                {
                    float centerPos = pos + halfSolid;
                    if (centerPos > totalLen) break;

                    float cx = x0 + ux * centerPos;
                    float cy = y0 + uy * centerPos;

                    result.Add(new DrawDot(new Point2D(cx, cy))
                    {
                        Name = $"{name}-{(index + 1)}",
                        Radius = renderRadius,
                        Pen = new SKPaint() { Color = SKColor.Parse(hatchParamInfo.FillColor), Style = SKPaintStyle.Stroke, StrokeWidth = 0.25f },
                    });
                    index++;
                }
            }
        }



        public class DotPointPool
        {
            private readonly ConcurrentBag<(float x, float y)[]> _chunks = new();
            private const int CHUNK_SIZE = 1024;

            public (float x, float y)[] Rent()
            {
                return _chunks.TryTake(out var chunk) ? chunk : new (float, float)[CHUNK_SIZE];
            }

            public void Return((float x, float y)[] chunk, int usedCount)
            {
                if (usedCount > 0)
                    Array.Clear(chunk, 0, usedCount);
                _chunks.Add(chunk);
            }
        }

        private static void EmitDotDashUltimate(
            List<DrawObject> result,
            IReadOnlyList<(SKPoint Start, SKPoint End)> fillLines,
            HatchParamDto hatchParamInfo,
            SKMatrix matrix,
            string name)
        {
            float solidLen = DotSolidLength;
            float blankLen = DotBlankLength;
            float step = solidLen + blankLen;
            if (step <= 0f) return;

            float halfSolid = solidLen * 0.5f;
            float renderRadius = DotRenderRadius;

            var pen = new SKPaint()
            {
                Color = SKColor.Parse(hatchParamInfo.FillColor),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 0.25f
            };

            int n = fillLines.Count;
            var pool = new DotPointPool();

            // 预热缓存
            var transformedLines = new (SKPoint s, SKPoint e)[n];
            Parallel.For(0, n, i =>
            {
                var (s, e) = fillLines[i];
                transformedLines[i] = (matrix.MapPoint(s), matrix.MapPoint(e));
            });

            // 分段处理（每段独立，减少锁竞争）
            int segmentCount = Environment.ProcessorCount * 2;
            int segmentSize = (n + segmentCount - 1) / segmentCount;

            var segmentResults = new List<(float x, float y)>[segmentCount];
            for (int i = 0; i < segmentCount; i++)
                segmentResults[i] = new List<(float x, float y)>(1024);

            Parallel.For(0, segmentCount, new ParallelOptions { MaxDegreeOfParallelism = segmentCount }, segIdx =>
            {
                int startIdx = segIdx * segmentSize;
                int endIdx = Math.Min(startIdx + segmentSize, n);
                var localPoints = segmentResults[segIdx];

                for (int i = startIdx; i < endIdx; i++)
                {
                    var (s_new, e_new) = transformedLines[i];

                    float x0 = s_new.X;
                    float y0 = s_new.Y;
                    float x1 = e_new.X;
                    float y1 = e_new.Y;

                    float dx = x1 - x0;
                    float dy = y1 - y0;

                    // 快速路径：极短线
                    if (Math.Abs(dx) < 0.001f && Math.Abs(dy) < 0.001f) continue;

                    float totalLen = (float)Math.Sqrt(dx * dx + dy * dy);
                    float invLen = 1f / totalLen;
                    float ux = dx * invLen;
                    float uy = dy * invLen;

                    // 使用整数运算避免浮点误差累积
                    int pointCount = (int)((totalLen - halfSolid) / step) + 1;
                    if (pointCount <= 0) continue;

                    // 批量预分配
                    if (localPoints.Capacity < localPoints.Count + pointCount)
                        localPoints.Capacity = localPoints.Count + pointCount;

                    // 使用固定步长累加（最快速的计算方式）
                    float startPos = halfSolid;
                    float startX = x0 + ux * startPos;
                    float startY = y0 + uy * startPos;
                    float deltaX = ux * step;
                    float deltaY = uy * step;

                    float cx = startX;
                    float cy = startY;

                    // 循环展开（每次处理4个点）
                    int pointsRemaining = pointCount;
                    while (pointsRemaining >= 4)
                    {
                        localPoints.Add((cx, cy));
                        cx += deltaX; cy += deltaY;

                        localPoints.Add((cx, cy));
                        cx += deltaX; cy += deltaY;

                        localPoints.Add((cx, cy));
                        cx += deltaX; cy += deltaY;

                        localPoints.Add((cx, cy));
                        cx += deltaX; cy += deltaY;

                        pointsRemaining -= 4;
                    }

                    // 处理剩余点
                    while (pointsRemaining-- > 0)
                    {
                        if (cx > x1 + deltaX && deltaX > 0) break;
                        if (cx < x1 + deltaX && deltaX < 0) break;

                        localPoints.Add((cx, cy));
                        cx += deltaX;
                        cy += deltaY;
                    }
                }
            });

            // 合并结果
            int totalPoints = 0;
            foreach (var seg in segmentResults)
                totalPoints += seg.Count;

            result.Capacity = result.Count + totalPoints;

            // 批量创建对象（减少单个对象创建开销）
            int globalIndex = result.Count;
            foreach (var segment in segmentResults)
            {
                foreach (var point in segment)
                {
                    result.Add(new DrawDot(new Point2D(point.x, point.y))
                    {
                        Name = $"{name}-{globalIndex + 1}",
                        Radius = renderRadius,
                        Pen = pen,
                    });
                    globalIndex++;
                }
            }
        }

        private static void EmitDotDashParallel(
    List<DrawObject> result,
    IReadOnlyList<(SKPoint Start, SKPoint End)> fillLines,
    HatchParamDto hatchParamInfo,
    SKMatrix matrix,
    string name)
        {
            float solidLen = DotSolidLength;
            float blankLen = DotBlankLength;
            float step = solidLen + blankLen;
            if (step <= 0f) return;

            float halfSolid = solidLen * 0.5f;
            float renderRadius = DotRenderRadius;

            int n = fillLines.Count;
            if (n == 0) return;

            // 预创建 Pen
            var pen = new SKPaint()
            {
                Color = SKColor.Parse(hatchParamInfo.FillColor),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 0.25f
            };

            // 第一遍：快速估算总点数（并行）
            var estimatedPointsPerLine = new int[n];
            Parallel.For(0, n, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
            {
                var (s, e) = fillLines[i];
                var s_new = matrix.MapPoint(s);
                var e_new = matrix.MapPoint(e);
                float dx = e_new.X - s_new.X;
                float dy = e_new.Y - s_new.Y;
                float len = (float)Math.Sqrt(dx * dx + dy * dy);
                if (len > 0)
                {
                    int points = (int)((len - halfSolid) / step) + 1;
                    if (points > 0)
                        estimatedPointsPerLine[i] = points;
                }
            });

            // 计算总点数
            int totalEstimatedPoints = 0;
            for (int i = 0; i < n; i++)
                totalEstimatedPoints += estimatedPointsPerLine[i];

            // 预分配容量
            if (totalEstimatedPoints > 0)
                result.Capacity = result.Count + totalEstimatedPoints;

            // 并行处理每条线段
            int processorCount = Environment.ProcessorCount;
            // 使用分块并行处理（减少锁竞争）
            int batchSize = (n + processorCount - 1) / processorCount; // 向上取整
            //int batchSize = Math.Max(1, n / Environment.ProcessorCount);
            var batches = new List<List<(float x, float y)>>();

            var lockObj = new object();
            Parallel.For(0, Environment.ProcessorCount, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, threadId =>
            {
                int startIdx = threadId * batchSize;
                int endIdx = Math.Min(startIdx + batchSize, n);
                if (startIdx >= n) return;

                var localPoints = new List<(float x, float y)>(totalEstimatedPoints / Environment.ProcessorCount + 100);

                for (int i = startIdx; i < endIdx; i++)
                {
                    var (s, e) = fillLines[i];
                    var s_new = matrix.MapPoint(s);
                    var e_new = matrix.MapPoint(e);

                    float x0 = s_new.X;
                    float y0 = s_new.Y;
                    float x1 = e_new.X;
                    float y1 = e_new.Y;

                    float dx = x1 - x0;
                    float dy = y1 - y0;
                    float lenSq = dx * dx + dy * dy;
                    if (lenSq <= 1e-6f) continue;

                    float totalLen = (float)Math.Sqrt(lenSq);
                    float ux = dx / totalLen;
                    float uy = dy / totalLen;

                    // 批量生成点（使用向量化）
                    int pointCount = (int)((totalLen - halfSolid) / step) + 1;
                    if (pointCount <= 0) continue;

                    // 预分配局部容量
                    if (localPoints.Capacity < localPoints.Count + pointCount)
                        localPoints.Capacity = localPoints.Count + pointCount;

                    // 从中间向两边计算（缓存友好）
                    int midPoint = pointCount / 2;
                    float midPos = halfSolid + midPoint * step;
                    float midX = x0 + ux * midPos;
                    float midY = y0 + uy * midPos;

                    // 先添加中点
                    localPoints.Add((midX, midY));

                    // 向两边扩展（减少浮点误差累积）
                    for (int offset = 1; offset <= midPoint; offset++)
                    {
                        float leftPos = halfSolid + (midPoint - offset) * step;
                        float rightPos = halfSolid + (midPoint + offset) * step;

                        if (leftPos >= 0)
                        {
                            float leftX = x0 + ux * leftPos;
                            float leftY = y0 + uy * leftPos;
                            localPoints.Add((leftX, leftY));
                        }

                        if (rightPos <= totalLen)
                        {
                            float rightX = x0 + ux * rightPos;
                            float rightY = y0 + uy * rightPos;
                            localPoints.Add((rightX, rightY));
                        }
                    }

                    // 确保按顺序排列（如果需要）
                    if (localPoints.Count > 1 && localPoints[0].x > localPoints[localPoints.Count - 1].x)
                        localPoints.Reverse();
                }

                // 合并结果
                lock (lockObj)
                {
                    foreach (var point in localPoints)
                    {
                        result.Add(new DrawDot(new Point2D(point.x, point.y))
                        {
                            Name = name,
                            Radius = renderRadius,
                            Pen = pen,
                        });
                    }
                }
            });
        }






        //数据准确性待验证，性能排1，2左右
        private static void EmitDotDashParallelIndexed(
    List<DrawObject> result,
    IReadOnlyList<(SKPoint Start, SKPoint End)> fillLines,
    HatchParamDto hatchParamInfo,
    SKMatrix matrix,
    string name)
        {
            float solidLen = DotSolidLength;
            float blankLen = DotBlankLength;
            float step = solidLen + blankLen;
            if (step <= 0f) return;

            float halfSolid = solidLen * 0.5f;
            float renderRadius = DotRenderRadius;

            int n = fillLines.Count;
            if (n == 0) return;

            var pen = new SKPaint()
            {
                Color = SKColor.Parse(hatchParamInfo.FillColor),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 0.25f
            };

            // 第一遍：计算每条线的点数
            var pointsPerLine = new int[n];
            Parallel.For(0, n, i =>
            {
                var (s, e) = fillLines[i];
                var s_new = matrix.MapPoint(s);
                var e_new = matrix.MapPoint(e);
                float dx = e_new.X - s_new.X;
                float dy = e_new.Y - s_new.Y;
                float len = (float)Math.Sqrt(dx * dx + dy * dy);
                if (len > 0)
                {
                    int points = (int)((len - halfSolid) / step) + 1;
                    if (points > 0)
                        pointsPerLine[i] = points;
                }
            });

            // 计算前缀和（确定每个线段点的起始索引）
            var prefixSum = new int[n + 1];
            for (int i = 0; i < n; i++)
                prefixSum[i + 1] = prefixSum[i] + pointsPerLine[i];

            int totalPoints = prefixSum[n];
            if (totalPoints == 0) return;

            // 预分配数组（无锁竞争）
            var pointsArray = new (float x, float y)[totalPoints];

            // 并行填充数组（每个线程写入不同位置，无需锁）
            Parallel.For(0, n, i =>
            {
                if (pointsPerLine[i] == 0) return;

                var (s, e) = fillLines[i];
                var s_new = matrix.MapPoint(s);
                var e_new = matrix.MapPoint(e);

                float x0 = s_new.X;
                float y0 = s_new.Y;
                float x1 = e_new.X;
                float y1 = e_new.Y;

                float dx = x1 - x0;
                float dy = y1 - y0;
                float lenSq = dx * dx + dy * dy;
                if (lenSq <= 1e-6f) return;

                float totalLen = (float)Math.Sqrt(lenSq);
                float ux = dx / totalLen;
                float uy = dy / totalLen;

                int pointCount = pointsPerLine[i];
                int startIdx = prefixSum[i];

                // 从中间向两边计算
                int midPoint = pointCount / 2;
                float midPos = halfSolid + midPoint * step;
                float midX = x0 + ux * midPos;
                float midY = y0 + uy * midPos;

                pointsArray[startIdx] = (midX, midY);
                int leftIdx = startIdx + 1;
                int rightIdx = startIdx + 1;

                for (int offset = 1; offset <= midPoint; offset++)
                {
                    float leftPos = halfSolid + (midPoint - offset) * step;
                    float rightPos = halfSolid + (midPoint + offset) * step;

                    if (leftPos >= 0)
                    {
                        pointsArray[leftIdx++] = (x0 + ux * leftPos, y0 + uy * leftPos);
                    }

                    if (rightPos <= totalLen)
                    {
                        pointsArray[rightIdx++] = (x0 + ux * rightPos, y0 + uy * rightPos);
                    }
                }

                // 如果需要排序，在这里处理（可选）
                if (pointsArray[startIdx].x > pointsArray[startIdx + pointCount - 1].x)
                {
                    Array.Reverse(pointsArray, startIdx, pointCount);
                }
            });

            // 批量创建 DrawDot 对象
            result.Capacity = result.Count + totalPoints;
            for (int i = 0; i < totalPoints; i++)
            {
                result.Add(new DrawDot(new Point2D(pointsArray[i].x, pointsArray[i].y))
                {
                    Name = name,
                    Radius = renderRadius,
                    Pen = pen,
                });
            }
        }





        //private static void EmitDotDashCSL(
        //                                List<DrawObject> result,
        //                                IReadOnlyList<(SKPoint Start, SKPoint End)> fillLines,
        //                                HatchParamDto hatchParamInfo, SKMatrix matrix, string name)
        //{
        //    float solidLen = DotSolidLength;
        //    float blankLen = DotBlankLength;
        //    float step = solidLen + blankLen;
        //    if (step <= 0f) return;

        //    float halfSolid = solidLen * 0.5f;
        //    float renderRadius = DotRenderRadius;
        //    int n = fillLines.Count;

        //    // ① 复用 SKPaint
        //    var sharedPaint = new SKPaint
        //    {
        //        Color = SKColor.Parse(hatchParamInfo.FillColor),
        //        Style = SKPaintStyle.Stroke,
        //        StrokeWidth = 0.25f,
        //    };

        //    // ② 并行计算每条线段产生的点，结果存入固定槽位的数组，保证顺序
        //    var segments = new List<DrawDot>[n];

        //    Parallel.For(0, n, i =>
        //    {
        //        var (s, e) = fillLines[i];
        //        SKPoint sn = matrix.MapPoint(s);
        //        SKPoint en = matrix.MapPoint(e);

        //        float dx = en.X - sn.X;
        //        float dy = en.Y - sn.Y;
        //        float totalLen = MathF.Sqrt(dx * dx + dy * dy);

        //        if (totalLen <= 0f)
        //        {
        //            segments[i] = null;
        //            return;
        //        }

        //        float ux = dx / totalLen;
        //        float uy = dy / totalLen;

        //        // 预算该线段点数
        //        int dotCount = (int)((totalLen - halfSolid) / step) + 1;
        //        var local = new List<DrawDot>(dotCount);

        //        int index = 0;
        //        for (float pos = 0f; pos <= totalLen; pos += step)
        //        {
        //            float centerPos = pos + halfSolid;
        //            if (centerPos > totalLen) break;

        //            float cx = sn.X + ux * centerPos;
        //            float cy = sn.Y + uy * centerPos;

        //            local.Add(new DrawDot(new Point2D(cx, cy))
        //            {
        //                Name = $"{name}-{index + 1}",
        //                X = cx,
        //                Y = cy,
        //                Radius = renderRadius,
        //                Pen = sharedPaint,
        //            });
        //            index++;
        //        }

        //        segments[i] = local;
        //    });

        //    // ③ 按原顺序顺序合并，保证线段顺序及段内点顺序
        //    int totalDots = 0;
        //    foreach (var seg in segments)
        //        if (seg != null) totalDots += seg.Count;

        //    result.EnsureCapacity(result.Count + totalDots);

        //    foreach (var seg in segments)
        //    {
        //        if (seg is null) continue;
        //        result.AddRange(seg);
        //    }
        //}
    }
}
