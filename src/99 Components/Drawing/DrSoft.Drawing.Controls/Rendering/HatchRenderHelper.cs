using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Model;

namespace DrSoft.Drawing.Controls.Rendering
{
    /// <summary>
    /// 填充渲染性能优化辅助类。
    /// 提供基于画布缩放的自适应渲染策略：
    /// 1. 动态LOD阈值 - 根据填充线数量和图形尺寸自动切换简化/精细渲染
    /// 2. 填充线采样 - 在缩小视图时跳过视觉上不可区分的填充线
    /// 3. 动态虚线参数 - 根据缩放比例自动调整虚线间隔和实线长度
    /// </summary>
    public static class HatchRenderHelper
    {
        // ── 常量 ──────────────────────────────────────────────────────────
        /// <summary>最小LOD阈值（无论如何不低于此值触发简化渲染）</summary>
        private const float MinLodThreshold = 20f;

        /// <summary>最大LOD阈值（超过此值始终进入精细渲染）</summary>
        private const float MaxLodThreshold = 200f;

        /// <summary>屏幕像素间距阈值，低于此值认为两条线不可区分（需要足够大以保证渐进式可见）</summary>
        private const float PixelMergeThreshold = 3.0f;

        /// <summary>精细模式下相邻填充线在屏幕上的最小像素间距（用于密度阈值计算）</summary>
        private const float MinScreenGapForDetail = 2.5f;

        /// <summary>参考填充线长度（用于线长归一化），单位为模型坐标</summary>
        private const float ReferenceLineLength = 5.0f;

        /// <summary>基础虚线间隔（在缩放=100时的参考值）</summary>
        private const float BaseDotSpacing = 0.1f;

        /// <summary>基础虚线实线长度（在缩放=100时的参考值）</summary>
        private const float BaseDashLength = 0.1f;

        // ── 动态LOD阈值 ──────────────────────────────────────────────────

        /// <summary>
        /// 根据填充线数量和图形屏幕像素面积，动态计算LOD切换阈值。
        /// 填充线越多、图形越小，越早切换到简化渲染模式。
        /// </summary>
        /// <param name="lineCount">填充线总数</param>
        /// <param name="shapeScreenArea">图形在屏幕上的像素面积（宽*高*scaleX*scaleY）</param>
        /// <returns>当 totalMatrix.ScaleX 低于此值时使用简化渲染</returns>
        public static float ComputeLodThreshold(int lineCount, float shapeScreenArea)
        {
            // 基础阈值：线越多越需要简化
            // log2(lineCount) 使阈值随线数对数增长，避免线性增长过快
            float lineComplexity = (float)(Math.Log2(Math.Max(lineCount, 1)) * 8f);

            // 面积修正：屏幕面积越小越需要简化（密度高）
            // 当屏幕面积 < 10000px² 时提高阈值
            float areaPenalty = shapeScreenArea > 0
                ? Math.Max(1f, 10000f / shapeScreenArea)
                : 1f;

            float threshold = lineComplexity * areaPenalty;
            return Math.Clamp(threshold, MinLodThreshold, MaxLodThreshold);
        }

        /// <summary>
        /// 简化版：仅根据线数量计算阈值（不需要面积信息时使用）
        /// </summary>
        public static float ComputeLodThreshold(int lineCount)
        {
            // 100线 → ~53, 500线 → ~72, 2000线 → ~88, 10000线 → ~106
            float threshold = (float)(Math.Log2(Math.Max(lineCount, 1)) * 8f);
            return Math.Clamp(threshold, MinLodThreshold, MaxLodThreshold);
        }

        /// <summary>
        /// 综合数量与线段分布动态计算LOD阈值（推荐使用）。
        /// 综合考虑：
        ///   1. 线段数量（log对数增长）
        ///   2. 平均线间距（模型坐标）—— 越密集越早切换到简化渲染
        ///   3. 平均线长 —— 短线更早简化（视觉影响小）
        ///   4. 包围盒尺寸 —— 用于推算线间距
        /// </summary>
        /// <param name="lines">填充线列表</param>
        /// <returns>LOD切换阈值（scaleX 低于此值进入简化渲染）</returns>
        public static float ComputeLodThreshold(List<(SKPoint Start, SKPoint End)> lines)
        {
            if (lines == null || lines.Count == 0) return MinLodThreshold;

            int count = lines.Count;

            // 基础阈值：随线数对数增长
            float countThreshold = (float)(Math.Log2(Math.Max(count, 1)) * 8f);

            // 一次遍历：计算包围盒与总线长
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            double totalLength = 0;
            for (int i = 0; i < count; i++)
            {
                var line = lines[i];
                if (line.Start.X < minX) minX = line.Start.X;
                if (line.Start.Y < minY) minY = line.Start.Y;
                if (line.End.X < minX) minX = line.End.X;
                if (line.End.Y < minY) minY = line.End.Y;
                if (line.Start.X > maxX) maxX = line.Start.X;
                if (line.Start.Y > maxY) maxY = line.Start.Y;
                if (line.End.X > maxX) maxX = line.End.X;
                if (line.End.Y > maxY) maxY = line.End.Y;

                float dx = line.End.X - line.Start.X;
                float dy = line.End.Y - line.Start.Y;
                totalLength += Math.Sqrt(dx * dx + dy * dy);
            }

            float bboxW = Math.Max(0.0001f, maxX - minX);
            float bboxH = Math.Max(0.0001f, maxY - minY);
            float bboxArea = bboxW * bboxH;

            float avgLineLength = (float)(totalLength / count);
            if (avgLineLength <= 0.0001f) avgLineLength = 0.0001f;

            // 密度阈值：平均线间距 = 包围盒面积 / 总线长（近似平行扫描线模型）
            // 当线间距很小（密集填充），需要更大的 scaleX 才能看清单根线 → 提高阈值
            float avgSpacing = bboxArea / (float)Math.Max(totalLength, 0.0001);
            float densityThreshold = avgSpacing > 0.0001f
                ? MinScreenGapForDetail / avgSpacing
                : MinLodThreshold;

            // 线长因子：线越短，简化模式下的视觉损失越小 → 阈值可放大
            // ReferenceLineLength=5 时为 1.0，线长=1 时为 ~1.5，线长=20 时为 ~0.7
            float lengthFactor = MathF.Sqrt(ReferenceLineLength / avgLineLength);
            lengthFactor = Math.Clamp(lengthFactor, 0.5f, 2.0f);

            // 综合：取数量与密度阈值的较大值，再按线长因子缩放
            float threshold = Math.Max(countThreshold, densityThreshold) * lengthFactor;
            return Math.Clamp(threshold, MinLodThreshold, MaxLodThreshold);
        }
        // ── 填充线采样/降采样 ────────────────────────────────────────────

        /// <summary>
        /// 基于屏幕像素密度对填充线进行降采样。
        /// 当两条相邻线在屏幕上的间距小于阈值时，跳过中间的线。
        /// 视觉效果：缩小时仍显示均匀的填充线，但数量大幅减少。
        /// </summary>
        /// <param name="lines">原始填充线列表</param>
        /// <param name="scaleX">当前变换矩阵的ScaleX</param>
        /// <param name="maxScreenLines">屏幕上最多保留的线条数（防止极端情况）</param>
        /// <returns>采样后的线条列表</returns>
        public static List<(SKPoint Start, SKPoint End)> SampleLines(
            List<(SKPoint Start, SKPoint End)> lines,
            float scaleX,
            int maxScreenLines = 2000)
        {
            if (lines == null || lines.Count == 0) return lines;

            int count = lines.Count;

            // 注意：不能仅凭线数量决定是否跳过采样
            // 即使线很少，如果屏幕间距太小（密集填充），仍需降采样以避免视觉合并

            // 计算采样步长：确保相邻被选中的线在屏幕上间距 >= PixelMergeThreshold 像素
            // 估算相邻线的平均间距（用首尾线的中点距离除以线数近似）
            float avgGap = EstimateAverageGap(lines);
            float screenGap = avgGap * scaleX;

            int step;
            if (screenGap < PixelMergeThreshold && screenGap > 0)
            {
                // 需要跳过的线数：使得保留的线间距 >= PixelMergeThreshold
                step = Math.Max(1, (int)Math.Ceiling(PixelMergeThreshold / screenGap));
            }
            else
            {
                step = 1; // 间距足够大，不需要跳过
            }

            // 额外限制：最多保留 maxScreenLines 条
            int stepByMax = Math.Max(1, count / maxScreenLines);
            step = Math.Max(step, stepByMax);

            if (step <= 1) return lines;

            // 执行采样
            var sampled = new List<(SKPoint Start, SKPoint End)>(count / step + 2);
            for (int i = 0; i < count; i += step)
            {
                sampled.Add(lines[i]);
            }

            // 确保最后一条线被包含（边界完整性）
            if ((count - 1) % step != 0)
            {
                sampled.Add(lines[count - 1]);
            }

            return sampled;
        }

        /// <summary>
        /// 估算相邻线的平均间距（基于起点的距离）
        /// </summary>
        private static float EstimateAverageGap(List<(SKPoint Start, SKPoint End)> lines)
        {
            if (lines.Count < 2) return float.MaxValue;

            // 采样前几条线估算间距，避免遍历全部
            int sampleCount = Math.Min(10, lines.Count - 1);
            float totalDist = 0;
            for (int i = 0; i < sampleCount; i++)
            {
                var p1 = lines[i].Start;
                var p2 = lines[i + 1].Start;
                float dx = p2.X - p1.X;
                float dy = p2.Y - p1.Y;
                totalDist += MathF.Sqrt(dx * dx + dy * dy);
            }

            return totalDist / sampleCount;
        }

        // ── 虚线精细渲染参数 ──────────────────────────────────────────────────

        /// <summary>
        /// 获取虚线/点线渲染参数（保持原始固定值以确保视觉正确性）。
        /// 在精细模式下使用固定参数，避免动态计算导致的视觉突变。
        /// </summary>
        /// <param name="fillStyleIndex">填充样式索引 (1=虚线, 2=点线)</param>
        /// <returns>(dashLength, dotSpacing) 元组</returns>
        public static (float DashLength, float DotSpacing) GetDashParameters(int fillStyleIndex)
        {
            // 使用经过验证的固定值，确保虚线/点线视觉效果正确
            float dotSpacing = BaseDotSpacing; // 0.1f
            float dashLength = fillStyleIndex == 1 ? BaseDashLength : 0f; // 虚线0.1f, 点线0
            return (dashLength, dotSpacing);
        }

        /// <summary>
        /// 计算自适应的点/线宽度
        /// </summary>
        public static float ComputeDotSize(float scaleX)
        {
            return 2.0f / scaleX;
        }

        // ── 精细渲染：逐线绘制（虚线/点线专用） ─────────────────────────────

        /// <summary>
        /// 使用逐线DrawLine方式渲染虚线/点线填充。
        /// 逐线绘制确保每条线段独立应用dash pattern，避免batch path中
        /// 子路径交互导致的视觉异常（虚线变实线问题）。
        /// 性能优于batch path + PathEffect，因为每条线段简单且独立。
        /// </summary>
        public static void RenderDashLinesIndividually(
            SKCanvas canvas,
            List<(SKPoint Start, SKPoint End)> lines,
            SKColor color,
            float scaleX,
            int fillStyleIndex)
        {
            var (dashLength, dotSpacing) = GetDashParameters(fillStyleIndex);
            float dotSize = ComputeDotSize(scaleX);

            using (SKPaint paint = new SKPaint())
            {
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = dotSize;
                paint.StrokeCap = SKStrokeCap.Round;
                paint.Color = color;
                paint.IsAntialias = true;

                float[] intervals = new float[] { dashLength, dotSpacing };
                paint.PathEffect = SKPathEffect.CreateDash(intervals, 0);

                foreach (var line in lines)
                {
                    canvas.DrawLine(line.Start, line.End, paint);
                }
            }
        }

        // ── 虚线几何展开 ──────────────────────────────────────────────────

        /// <summary>
        /// 虚线几何解析类型
        /// </summary>
        public enum DashRenderType
        {
            /// <summary>点虚线（输出点坐标列表）</summary>
            Dot,
            /// <summary>短实线虚线（输出短线段坐标列表）</summary>
            Dash
        }

        /// <summary>
        /// 根据 <see cref="GetDashParameters"/> 的参数，并行将原始填充线展开为 <see cref="DrawObject"/> 列表。
        /// 输出：
        ///   - <see cref="DashRenderType.Dot"/> ：<see cref="DrawDot"/> 列表（点虚线）
        ///   - <see cref="DashRenderType.Dash"/>：<see cref="DrawPolyLines"/> 列表，每个包含 2 个点的短实线段
        /// 性能优化要点（参考 <c>FillLineStyleEmitter.EmitDotDashParallel</c>）：
        ///   1. 两轮 Parallel.For：首轮估算各线输出个数以预分配容量；次轮按处理器分块并行生成。
        ///   2. 从中间向两边计算（缓存友好 + 减少浮点误差累积）。
        ///   3. 按位置写入预分配数组，单线内部以及跨线顺序均保持与输入一致。
        ///   4. 各线程写入独立的本地列表，最后按线程索引顺序合并，避免锁竞争与乱序。
        /// </summary>
        /// <param name="type">解析类型（点虚线 / 短虚线）</param>
        /// <param name="lines">待解析的原始填充线列表</param>
        /// <param name="dashParams">来自 <see cref="GetDashParameters"/> 的 (DashLength, DotSpacing)</param>
        /// <param name="color">填充颜色</param>
        /// <param name="name">输出 <see cref="DrawObject"/> 的名称前缀（可选）</param>
        /// <param name="strokeWidth">短实线描边宽度（仅 Dash 模式使用）</param>
        /// <param name="dotRadius">点半径（仅 Dot 模式使用）</param>
        /// <returns>DrawObject 列表（顺序与输入 lines 一致，单线内点/段从起点到终点顺序排列）</returns>
        public static List<DrawObject> ExpandToDashGeometry(
            DashRenderType type,
            List<(SKPoint Start, SKPoint End)> lines,
            (float DashLength, float DotSpacing) dashParams,
            SKColor color,
            string name = null,
            float strokeWidth = 0.25f,
            float dotRadius = 1.0f)
        {
            if (lines == null || lines.Count == 0)
                return new List<DrawObject>();

            int n = lines.Count;
            bool isDot = type == DashRenderType.Dot;

            float effDashLen = dashParams.DashLength > 0.0001f
                ? dashParams.DashLength
                : BaseDashLength;
            float effGap = dashParams.DotSpacing > 0.0001f
                ? dashParams.DotSpacing
                : BaseDotSpacing;
            float stride = isDot ? effGap : (effDashLen + effGap);
            if (stride <= 0.0001f) return new List<DrawObject>();

            // 共享画笔（与 EmitDotDashParallel 一致：一个 Pen 跨线程共享）
            var pen = new SKPaint
            {
                Color = color,
                Style = isDot ? SKPaintStyle.Fill : SKPaintStyle.Stroke,
                StrokeWidth = strokeWidth
            };
            string nameSafe = name ?? string.Empty;

            // ── 第一轮：并行估算每条线的输出个数 ──
            var perLineCount = new int[n];
            int dop = Math.Max(1, Environment.ProcessorCount);
            var parallelOpts = new ParallelOptions { MaxDegreeOfParallelism = dop };

            Parallel.For(0, n, parallelOpts, i =>
            {
                var (s, e) = lines[i];
                float dx = e.X - s.X;
                float dy = e.Y - s.Y;
                float lenSq = dx * dx + dy * dy;
                if (lenSq <= 1e-8f)
                {
                    perLineCount[i] = isDot ? 1 : 0;
                    return;
                }
                float len = MathF.Sqrt(lenSq);
                if (isDot)
                {
                    // 点：d = 0, stride, 2*stride, ... <= len
                    perLineCount[i] = (int)(len / stride) + 1;
                }
                else
                {
                    // 短实线：d = 0, stride, ... < len，最后一段可能被截断
                    int cnt = (int)Math.Ceiling((double)len / stride);
                    perLineCount[i] = Math.Max(1, cnt);
                }
            });

            // 总量 + 每线的能含起始偏移（以便于预分配一个总数组，保证跨线顺序）
            var lineOffset = new int[n + 1];
            for (int i = 0; i < n; i++)
            {
                lineOffset[i + 1] = lineOffset[i] + perLineCount[i];
            }
            int total = lineOffset[n];
            if (total == 0) return new List<DrawObject>();

            // 预分配本地列表（按线程索引） + 最终输出数组
            var output = new DrawObject[total];

            // ── 第二轮：按处理器分块并行生成 ──
            int batchSize = (n + dop - 1) / dop;

            Parallel.For(0, dop, parallelOpts, threadId =>
            {
                int startIdx = threadId * batchSize;
                int endIdx = Math.Min(startIdx + batchSize, n);
                if (startIdx >= n) return;

                for (int i = startIdx; i < endIdx; i++)
                {
                    int cnt = perLineCount[i];
                    if (cnt <= 0) continue;

                    int baseOffset = lineOffset[i];
                    var (s, e) = lines[i];
                    float x0 = s.X, y0 = s.Y;
                    float dx = e.X - s.X, dy = e.Y - s.Y;
                    float lenSq = dx * dx + dy * dy;

                    if (lenSq <= 1e-8f)
                    {
                        if (isDot && cnt >= 1)
                        {
                            output[baseOffset] = MakeDot(x0, y0, pen, nameSafe, i, 0, dotRadius);
                        }
                        continue;
                    }

                    float totalLen = MathF.Sqrt(lenSq);
                    float ux = dx / totalLen;
                    float uy = dy / totalLen;

                    // 从中间向两边计算，但按“起点起始”的顺序写入预分配位置，保证顺序与原线一致
                    int mid = cnt / 2;

                    if (isDot)
                    {
                        // 中点
                        {
                            float pos = mid * stride;
                            output[baseOffset + mid] = MakeDot(
                                x0 + ux * pos, y0 + uy * pos, pen, nameSafe, i, mid, dotRadius);
                        }
                        for (int off = 1; off <= mid; off++)
                        {
                            int li = mid - off;
                            int ri = mid + off;
                            if (li >= 0)
                            {
                                float pos = li * stride;
                                output[baseOffset + li] = MakeDot(
                                    x0 + ux * pos, y0 + uy * pos, pen, nameSafe, i, li, dotRadius);
                            }
                            if (ri < cnt)
                            {
                                float pos = ri * stride;
                                output[baseOffset + ri] = MakeDot(
                                    x0 + ux * pos, y0 + uy * pos, pen, nameSafe, i, ri, dotRadius);
                            }
                        }
                    }
                    else
                    {
                        // 中点段
                        {
                            float d0 = mid * stride;
                            float d1 = Math.Min(d0 + effDashLen, totalLen);
                            output[baseOffset + mid] = MakeDash(
                                x0 + ux * d0, y0 + uy * d0,
                                x0 + ux * d1, y0 + uy * d1,
                                pen, nameSafe, i, mid);
                        }
                        for (int off = 1; off <= mid; off++)
                        {
                            int li = mid - off;
                            int ri = mid + off;
                            if (li >= 0)
                            {
                                float d0 = li * stride;
                                float d1 = Math.Min(d0 + effDashLen, totalLen);
                                output[baseOffset + li] = MakeDash(
                                    x0 + ux * d0, y0 + uy * d0,
                                    x0 + ux * d1, y0 + uy * d1,
                                    pen, nameSafe, i, li);
                            }
                            if (ri < cnt)
                            {
                                float d0 = ri * stride;
                                float d1 = Math.Min(d0 + effDashLen, totalLen);
                                output[baseOffset + ri] = MakeDash(
                                    x0 + ux * d0, y0 + uy * d0,
                                    x0 + ux * d1, y0 + uy * d1,
                                    pen, nameSafe, i, ri);
                            }
                        }
                    }
                }
            });

            // 从预分配数组转 List（跳过 null 位，但本实现不会产生 null）
            var result = new List<DrawObject>(total);
            for (int i = 0; i < total; i++)
            {
                if (output[i] != null) result.Add(output[i]);
            }
            return result;
        }

        private static DrawDot MakeDot(
            float x, float y, SKPaint pen, string name, int lineIndex, int idx, float radius)
        {
            return new DrawDot(new Point2D(x, y))
            {
                Name = string.IsNullOrEmpty(name) ? string.Empty : $"{name}-{lineIndex + 1}-{idx + 1}",
                Radius = radius,
                Pen = pen,
            };
        }

        private static DrawPolyLines MakeDash(
            float x0, float y0, float x1, float y1,
            SKPaint pen, string name, int lineIndex, int idx)
        {
            var pts = new List<Point2D>(2)
            {
                new Point2D(x0, y0),
                new Point2D(x1, y1),
            };
            return new DrawPolyLines(pts)
            {
                Name = string.IsNullOrEmpty(name) ? string.Empty : $"{name}-{lineIndex + 1}-{idx + 1}",
                Pen = pen,
            };
        }

        // ── 下面是旧版几何展开（返回 SKPoint 列表），保留供其他调用场景 ──

        /// <summary>
        /// 轻量版（返回 SKPoint 几何）：适用于不需要 DrawObject 包装、仅需原始几何坐标的场景。
        /// </summary>
        public static (List<SKPoint> Dots, List<(SKPoint Start, SKPoint End)> Dashes)
            ExpandToDashGeometryRaw(
                DashRenderType type,
                List<(SKPoint Start, SKPoint End)> lines,
                (float DashLength, float DotSpacing) dashParams)
        {
            var dots = new List<SKPoint>();
            var dashes = new List<(SKPoint Start, SKPoint End)>();

            if (lines == null || lines.Count == 0)
                return (dots, dashes);

            float dashLength = dashParams.DashLength;
            float dotSpacing = dashParams.DotSpacing;

            if (type == DashRenderType.Dot)
            {
                // 点虚线：沿线按 dotSpacing 步进取点
                // 若 dashLength > 0（实际不是纯点），仍以 dotSpacing 作为步进，保证可视密度
                float stride = dotSpacing > 0.0001f ? dotSpacing : 0.1f;

                // 预估容量：总线长 / stride
                double totalLen = 0;
                for (int i = 0; i < lines.Count; i++)
                {
                    var s = lines[i].Start; var e = lines[i].End;
                    float dx = e.X - s.X, dy = e.Y - s.Y;
                    totalLen += Math.Sqrt(dx * dx + dy * dy);
                }
                int estCap = Math.Max(16, (int)(totalLen / stride) + lines.Count);
                if (dots.Capacity < estCap) dots.Capacity = estCap;

                foreach (var line in lines)
                {
                    float dx = line.End.X - line.Start.X;
                    float dy = line.End.Y - line.Start.Y;
                    float len = MathF.Sqrt(dx * dx + dy * dy);
                    if (len <= 0.0001f)
                    {
                        dots.Add(line.Start);
                        continue;
                    }

                    float nx = dx / len;
                    float ny = dy / len;

                    for (float d = 0; d <= len; d += stride)
                    {
                        dots.Add(new SKPoint(
                            line.Start.X + nx * d,
                            line.Start.Y + ny * d));
                    }
                }
            }
            else // Dash
            {
                // 短虚线：沿线按 (dashLength + dotSpacing) 步进生成短线段
                float effDashLen = dashLength > 0.0001f ? dashLength : BaseDashLength;
                float effGap = dotSpacing > 0.0001f ? dotSpacing : BaseDotSpacing;
                float stride = effDashLen + effGap;
                if (stride <= 0.0001f) return (dots, dashes);

                // 预估容量
                double totalLen = 0;
                for (int i = 0; i < lines.Count; i++)
                {
                    var s = lines[i].Start; var e = lines[i].End;
                    float dx = e.X - s.X, dy = e.Y - s.Y;
                    totalLen += Math.Sqrt(dx * dx + dy * dy);
                }
                int estCap = Math.Max(16, (int)(totalLen / stride) + lines.Count);
                if (dashes.Capacity < estCap) dashes.Capacity = estCap;

                foreach (var line in lines)
                {
                    float dx = line.End.X - line.Start.X;
                    float dy = line.End.Y - line.Start.Y;
                    float len = MathF.Sqrt(dx * dx + dy * dy);
                    if (len <= 0.0001f) continue;

                    float nx = dx / len;
                    float ny = dy / len;

                    for (float d = 0; d < len; d += stride)
                    {
                        float endOffset = Math.Min(d + effDashLen, len);
                        var p1 = new SKPoint(
                            line.Start.X + nx * d,
                            line.Start.Y + ny * d);
                        var p2 = new SKPoint(
                            line.Start.X + nx * endOffset,
                            line.Start.Y + ny * endOffset);
                        dashes.Add((p1, p2));
                    }
                }
            }

            return (dots, dashes);
        }

        // ── 批量路径渲染辅助 ─────────────────────────────────────────────

        /// <summary>
        /// 将填充线批量添加到单个SKPath中（高效渲染模式）
        /// </summary>
        public static SKPath BuildBatchPath(List<(SKPoint Start, SKPoint End)> lines)
        {
            var path = new SKPath();
            foreach (var line in lines)
            {
                path.MoveTo(line.Start);
                path.LineTo(line.End);
            }
            return path;
        }

        /// <summary>
        /// 判断是否应禁用抗锯齿以提升性能（极端缩小时线宽已足够覆盖像素）
        /// </summary>
        public static bool ShouldDisableAntiAlias(float scaleX)
        {
            return scaleX < 30f;
        }






        /// <summary>
        /// 计算渐进式笔画宽度，实现从"实心填充"到"可见单线"的平滑过渡。
        /// 原理：
        ///   - 当线间距在屏幕上很小时（密集填充），加大笔画使线条合并为实心 → 正确的视觉效果
        ///   - 当线间距在屏幕上足够大时，使用正常笔画 → 单线清晰可见
        ///   - 在两者之间平滑过渡 → 避免突变/跳闪
        /// </summary>
        public static float ComputeProgressiveStrokeWidth(
            List<(SKPoint Start, SKPoint End)> originalLines,
            List<(SKPoint Start, SKPoint End)> renderLines,
            float scaleX,
            float penStrokeWidth,
            float vpScale)
        {
            // 基础宽度（保持视觉上恒定线宽的原始计算方式）
            float baseWidth = penStrokeWidth * 6.83f / vpScale;

            if (originalLines == null || originalLines.Count < 2)
                return baseWidth;
            if (renderLines == null || renderLines.Count < 2)
                return baseWidth;

            // 估算原始（未采样）线列表的模型空间平均间距
            float originalGap = EstimateRenderGap(originalLines);
            if (originalGap <= 0.0001f)
                return baseWidth;

            // 原始线在屏幕上的像素间距（未经采样降频）
            float originalScreenGap = originalGap * scaleX;

            // ── 阈值定义 ──
            // solidThreshold: 低于此值时线条应完全合并为实心（间距太密看不见）
            // visibleThreshold: 高于此值时线条应清晰可见为单独线条
            const float solidThreshold = 2.0f;
            const float visibleThreshold = 5.0f;

            if (originalScreenGap >= visibleThreshold)
            {
                // 线间距在屏幕上足够大 → 使用正常笔画宽度，线条自然清晰可见
                return baseWidth;
            }

            // 估算采样后渲染线列表的模型空间间距
            float renderedGap = EstimateRenderGap(renderLines);
            if (renderedGap <= 0.0001f)
                return baseWidth;

            if (originalScreenGap <= solidThreshold)
            {
                // 原始线间距极密 → 笔画宽度覆盖采样间距，呈现实心效果
                return renderedGap * 1.1f;
            }

            // ── 过渡区：在实心与可见之间平滑插值 ──
            float t = (originalScreenGap - solidThreshold) / (visibleThreshold - solidThreshold);
            float solidFillWidth = renderedGap * 1.1f;  // 覆盖间距的宽度
            return solidFillWidth * (1.0f - t) + baseWidth * t;
        }

        /// <summary>
        /// 估算渲染线列表中相邻线的平均模型空间间距。
        /// 采样前若干对相邻线以快速估算。
        /// </summary>
        private static float EstimateRenderGap(List<(SKPoint Start, SKPoint End)> lines)
        {
            if (lines.Count < 2) return float.MaxValue;

            int sampleCount = Math.Min(10, lines.Count - 1);
            float totalDist = 0;
            for (int i = 0; i < sampleCount; i++)
            {
                var p1 = lines[i].Start;
                var p2 = lines[i + 1].Start;
                float dx = p2.X - p1.X;
                float dy = p2.Y - p1.Y;
                totalDist += MathF.Sqrt(dx * dx + dy * dy);
            }
            return totalDist / sampleCount;
        }

    }
}
