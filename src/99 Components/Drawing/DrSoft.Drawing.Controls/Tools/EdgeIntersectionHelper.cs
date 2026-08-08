using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Models;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.Tools
{
    /// <summary>
    /// AABB（轴平行包围盒）辅助工具。
    /// 计算图形轮廓与其外接 AABB 四条边的交点。
    /// 规则：
    ///   - 单边只有一个交点时 → 取该交点
    ///   - 边与图形边重合（无数交点）时 → 取重合段的两端点与中点
    /// </summary>
    internal static class EdgeIntersectionHelper
    {
        /// <summary>
        /// 计算图形轮廓与 AABB 四条边的交点结果。
        /// 每条边对应一个边结果，包含该边上的所有关键交点。
        /// </summary>
        internal readonly record struct AABBEdgeIntersectionResult(
            SKPoint? SinglePoint,        // 单个交点（仅一个交点时有值）
            List<SKPoint>? OverlapPoints // 重合段的端点与中点（重合时有值）
        )
        {
            /// <summary>是否有有效交点（单点或重合段）。</summary>
            public bool HasIntersection => SinglePoint.HasValue || (OverlapPoints?.Count > 0);
        }

        /// <summary>
        /// AABB 四条边的交点结果，顺序为：Top、Right、Bottom、Left。
        /// </summary>
        internal readonly record struct AABBIntersectionResult(
            AABBEdgeIntersectionResult Top,
            AABBEdgeIntersectionResult Right,
            AABBEdgeIntersectionResult Bottom,
            AABBEdgeIntersectionResult Left);

        /// <summary>
        /// 计算多个图形的世界坐标路径在 X/Y 轴方向上的四个极值顶点。
        /// 先用粗采样定位大致区域，再用三分搜索法精确到浮点精度（远优于 0.001）。
        /// 适用于贝塞尔曲线、圆弧等非线性图形。
        /// </summary>
        public static AABBIntersectionResult ComputeEdgeVertexs(List<DrawObject> drawObjects)
        {
            if (drawObjects == null || drawObjects.Count == 0)
                return default;

            // ── 1. 沿所有图形的曲线路径粗采样，建立 SKPathMeasure 列表 ──
            var measures = new List<(SKPathMeasure Measure, float Length)>();

            foreach (var drawObject in drawObjects)
            {
                if (drawObject == null) continue;
                using var localPath = drawObject.GetPath();
                if (localPath == null || localPath.IsEmpty) continue;

                using var worldPath = new SKPath();
                localPath.Transform(drawObject.GetTransformMatrix(), worldPath);
                if (worldPath.IsEmpty) continue;

                var measure = new SKPathMeasure(worldPath, false, 1f);
                float length = measure.Length;
                if (length < 0.0001f) { measure.Dispose(); continue; }

                measures.Add((measure, length));
            }

            if (measures.Count == 0)
                return default;

            // ── 2. 三分搜索法求四个方向的极值点 ──
            var topPoint = FindGlobalExtreme(measures, getCoord: p => p.Y, findMin: true);
            var bottomPoint = FindGlobalExtreme(measures, getCoord: p => p.Y, findMin: false);
            var leftPoint = FindGlobalExtreme(measures, getCoord: p => p.X, findMin: true);
            var rightPoint = FindGlobalExtreme(measures, getCoord: p => p.X, findMin: false);

            // ── 3. 构建结果 ──
            var topResult = new AABBEdgeIntersectionResult(
                SinglePoint: topPoint, OverlapPoints: null);
            var bottomResult = new AABBEdgeIntersectionResult(
                SinglePoint: bottomPoint, OverlapPoints: null);
            var leftResult = new AABBEdgeIntersectionResult(
                SinglePoint: leftPoint, OverlapPoints: null);
            var rightResult = new AABBEdgeIntersectionResult(
                SinglePoint: rightPoint, OverlapPoints: null);

            // 清理 SKPathMeasure 资源
            foreach (var (m, _) in measures) m.Dispose();

            return new AABBIntersectionResult(topResult, rightResult, bottomResult, leftResult);
        }

        /// <summary>
        /// 在所有路径中找到指定坐标方向的全局极值点。
        /// 先用粗采样（500 点/路径）定位，再用三分搜索精确到浮点精度。
        /// </summary>
        private static SKPoint FindGlobalExtreme(
            List<(SKPathMeasure Measure, float Length)> measures,
            Func<SKPoint, float> getCoord,
            bool findMin)
        {
            const int coarseSamples = 500;
            float bestValue = findMin ? float.MaxValue : float.MinValue;
            SKPoint bestPoint = SKPoint.Empty;
            int bestMeasureIdx = -1;
            float bestD = 0;
            float bestStep = 0;

            // ── 粗采样阶段：找到全局最优的采样点和所在区间 ──
            for (int mi = 0; mi < measures.Count; mi++)
            {
                var (measure, length) = measures[mi];
                float step = length / coarseSamples;

                for (int i = 0; i <= coarseSamples; i++)
                {
                    float d = MathF.Min(i * step, length);
                    if (measure.GetPosition(d, out var pt))
                    {
                        float val = getCoord(pt);
                        if (findMin ? val < bestValue : val > bestValue)
                        {
                            bestValue = val;
                            bestPoint = pt;
                            bestMeasureIdx = mi;
                            bestD = d;
                            bestStep = step;
                        }
                    }
                }
            }

            if (bestMeasureIdx < 0)
                return bestPoint;

            // ── 三分搜索精确化：在最佳采样点附近 [d-step, d+step] 窗口内搜索 ──
            var (bestMeasure, bestLength) = measures[bestMeasureIdx];
            float lo = MathF.Max(0, bestD - bestStep);
            float hi = MathF.Min(bestLength, bestD + bestStep);

            // 50 次三分搜索迭代，精度达 (2*step) * (2/3)^50 ≈ 10^-9 量级
            for (int iter = 0; iter < 50; iter++)
            {
                if (hi - lo < 1e-9f) break;

                float m1 = lo + (hi - lo) / 3f;
                float m2 = hi - (hi - lo) / 3f;

                bestMeasure.GetPosition(m1, out var pt1);
                bestMeasure.GetPosition(m2, out var pt2);

                float v1 = getCoord(pt1);
                float v2 = getCoord(pt2);

                bool m1Better = findMin ? v1 < v2 : v1 > v2;
                if (m1Better)
                    hi = m2;
                else
                    lo = m1;
            }

            float bestDRefined = (lo + hi) / 2f;
            if (bestMeasure.GetPosition(bestDRefined, out var refinedPt))
            {
                float refinedVal = getCoord(refinedPt);
                if (findMin ? refinedVal <= bestValue : refinedVal >= bestValue)
                    bestPoint = refinedPt;
            }

            return bestPoint;
        }

        /// <summary>
        /// 从同侧边缘点列表中构建结果：单点 → SinglePoint，多点 → OverlapPoints（含两端点和中点）。
        /// </summary>
        private static AABBEdgeIntersectionResult BuildEdgeResult(
            List<SKPoint> points, bool isHorizontal, float edgePos)
        {
            if (points.Count == 0) return default;

            if (points.Count == 1)
            {
                return new AABBEdgeIntersectionResult(SinglePoint: points[0], OverlapPoints: null);
            }

            // 多个同侧点：按沿边方向排序，取两端点和中点
            var sorted = isHorizontal
                ? points.OrderBy(p => p.X).ToList()
                : points.OrderBy(p => p.Y).ToList();

            var first = sorted[0];
            var last = sorted[sorted.Count - 1];
            var mid = new SKPoint(
                (first.X + last.X) / 2f,
                (first.Y + last.Y) / 2f);

            return new AABBEdgeIntersectionResult(
                SinglePoint: mid,
                OverlapPoints: new List<SKPoint> { first, mid, last });
        }



        /// <summary>
        /// 计算单个图形轮廓与其外接 AABB 四条边的交点。
        /// </summary>
        /// <param name="drawObject">图形对象（将自动获取其路径和 AABB）。</param>
        /// <param name="tolerance">重合判断容差（默认 0.01）。</param>
        /// <returns>四条边的交点结果（Top / Right / Bottom / Left）。</returns>
        public static AABBIntersectionResult ComputeEdgeIntersections(
            DrawObject drawObject,
            float tolerance = 0.001f)
        {
            if (drawObject == null)
            {
                return new AABBIntersectionResult(
                    default, default, default, default);
            }

            using var localPath = drawObject.GetPath();
            if (localPath == null || localPath.IsEmpty)
            {
                return new AABBIntersectionResult(
                    default, default, default, default);
            }

            var aabb = drawObject.GetAABB();
            if (aabb.IsEmpty)
            {
                return new AABBIntersectionResult(
                    default, default, default, default);
            }

            // 将局部路径变换到世界坐标（包含旋转/倾斜/缩放），与 AABB 保持同一坐标系
            using var worldPath = new SKPath();
            localPath.Transform(drawObject.GetTransformMatrix(), worldPath);

            return new AABBIntersectionResult(
                Top: ComputeIntersectionsForEdge(worldPath, aabb.Top, isHorizontal: true, tolerance),
                Right: ComputeIntersectionsForEdge(worldPath, aabb.Right, isHorizontal: false, tolerance),
                Bottom: ComputeIntersectionsForEdge(worldPath, aabb.Bottom, isHorizontal: true, tolerance),
                Left: ComputeIntersectionsForEdge(worldPath, aabb.Left, isHorizontal: false, tolerance));
        }



        /// <summary>
        /// 计算多个图形在世界坐标系中的四个方向极值点。
        /// 返回 AABBIntersectionResult，包含 Top、Right、Bottom、Left 四个方向的极值点。
        /// </summary>
        public static AABBIntersectionResult GetExtremePoints(
            List<DrawObject> drawObjects)
        {
            if (drawObjects == null || drawObjects.Count == 0)
                return default;

            // ── 1. 收集所有图形的世界路径 ──
            var measures = new List<(SKPathMeasure Measure, float Length)>();

            foreach (var drawObject in drawObjects)
            {
                if (drawObject == null) continue;
                using var localPath = drawObject.GetPath();
                if (localPath == null || localPath.IsEmpty) continue;

                using var worldPath = new SKPath();
                localPath.Transform(drawObject.GetTransformMatrix(), worldPath);
                if (worldPath.IsEmpty) continue;

                var measure = new SKPathMeasure(worldPath, false, 1f);
                float length = measure.Length;
                if (length < 0.0001f) { measure.Dispose(); continue; }

                measures.Add((measure, length));
            }

            if (measures.Count == 0)
                return default;

            // ── 2. 使用三分搜索找四个方向的极值点 ──
            var topPoint = FindGlobalExtreme(measures, p => p.Y, findMin: true);
            var bottomPoint = FindGlobalExtreme(measures, p => p.Y, findMin: false);
            var leftPoint = FindGlobalExtreme(measures, p => p.X, findMin: true);
            var rightPoint = FindGlobalExtreme(measures, p => p.X, findMin: false);

            // ── 3. 清理资源 ──
            foreach (var (m, _) in measures) m.Dispose();

            // ── 4. 构造 AABBIntersectionResult ──
            var topResult = new AABBEdgeIntersectionResult(
                SinglePoint: topPoint,
                OverlapPoints: null);

            var bottomResult = new AABBEdgeIntersectionResult(
                SinglePoint: bottomPoint,
                OverlapPoints: null);

            var leftResult = new AABBEdgeIntersectionResult(
                SinglePoint: leftPoint,
                OverlapPoints: null);

            var rightResult = new AABBEdgeIntersectionResult(
                SinglePoint: rightPoint,
                OverlapPoints: null);

            return new AABBIntersectionResult(
                Top: topResult,
                Right: rightResult,
                Bottom: bottomResult,
                Left: leftResult);
        }


        /// <summary>
        /// 计算多个图形轮廓与共享 AABB 四条边的交点（合并所有图形的交点）。
        /// 每个图形的局部路径会通过 GetTransformMatrix 变换到世界坐标后再与 AABB 求交。
        /// </summary>
        /// <param name="drawObjects">图形对象列表。</param>
        /// <param name="aabb">共享的世界坐标 AABB。</param>
        /// <param name="tolerance">重合判断容差（默认 0.001）。</param>
        /// <returns>四条边的合并交点结果（Top / Right / Bottom / Left）。</returns>
        public static AABBIntersectionResult ComputeEdgeIntersections(
            List<DrawObject> drawObjects,
            SKRect aabb,
            float tolerance = 0.001f)
        {
            if (drawObjects == null || drawObjects.Count == 0 || aabb.IsEmpty)
            {
                return new AABBIntersectionResult(
                    default, default, default, default);
            }

            // 收集每条边上所有图形的交点坐标（水平边收集 X，垂直边收集 Y）
            var topXs = new List<float>();
            var rightYs = new List<float>();
            var bottomXs = new List<float>();
            var leftYs = new List<float>();

            foreach (var drawObject in drawObjects)
            {
                if (drawObject == null) continue;

                using var localPath = drawObject.GetPath();
                if (localPath == null || localPath.IsEmpty) continue;

                // 局部路径 → 世界路径
                using var worldPath = new SKPath();
                localPath.Transform(drawObject.Matrix, worldPath);
                if (worldPath.IsEmpty) continue;

                // 对每条边采样收集原始交点坐标
                CollectEdgeCoordinates(worldPath, aabb.Top, isHorizontal: true, tolerance, topXs);
                CollectEdgeCoordinates(worldPath, aabb.Right, isHorizontal: false, tolerance, rightYs);
                CollectEdgeCoordinates(worldPath, aabb.Bottom, isHorizontal: true, tolerance, bottomXs);
                CollectEdgeCoordinates(worldPath, aabb.Left, isHorizontal: false, tolerance, leftYs);
            }

            return new AABBIntersectionResult(
                Top: AnalyzeMultiShapeEdgeX(topXs, aabb.Top),
                Right: AnalyzeMultiShapeEdgeY(rightYs, aabb.Right),
                Bottom: AnalyzeMultiShapeEdgeX(bottomXs, aabb.Bottom),
                Left: AnalyzeMultiShapeEdgeY(leftYs, aabb.Left));
        }

        /// <summary>
        /// 多图形边交点分析（水平边）：取首个交点为 SinglePoint，最后交点为 OverlapPoints。
        /// </summary>
        private static AABBEdgeIntersectionResult AnalyzeMultiShapeEdgeX(
            List<float> intersectionXs, float edgeY)
        {
            if (intersectionXs.Count == 0) return default;

            // 去重并排序
            var unique = intersectionXs
                .GroupBy(x => MathF.Round(x / 0.001f))
                .Select(g => g.First())
                .OrderBy(x => x)
                .ToList();

            if (unique.Count == 0) return default;

            var first = new SKPoint(unique[0], edgeY);
            var last = new SKPoint(unique[unique.Count - 1], edgeY);

            return new AABBEdgeIntersectionResult(
                SinglePoint: first,
                OverlapPoints: new List<SKPoint> { first, last });
        }

        /// <summary>
        /// 多图形边交点分析（垂直边）：取首个交点为 SinglePoint，最后交点为 OverlapPoints。
        /// </summary>
        private static AABBEdgeIntersectionResult AnalyzeMultiShapeEdgeY(
            List<float> intersectionYs, float edgeX)
        {
            if (intersectionYs.Count == 0) return default;

            // 去重并排序
            var unique = intersectionYs
                .GroupBy(y => MathF.Round(y / 0.001f))
                .Select(g => g.First())
                .OrderBy(y => y)
                .ToList();

            if (unique.Count == 0) return default;

            var first = new SKPoint(edgeX, unique[0]);
            var last = new SKPoint(edgeX, unique[unique.Count - 1]);

            return new AABBEdgeIntersectionResult(
                SinglePoint: first,
                OverlapPoints: new List<SKPoint> { first, last });
        }

        /// <summary>
        /// 沿世界路径采样，收集与单条 AABB 边的原始交点坐标（不构造结果对象）。
        /// 水平边收集 X 坐标，垂直边收集 Y 坐标。
        /// </summary>
        private static void CollectEdgeCoordinates(
            SKPath worldPath,
            float edgeValue,
            bool isHorizontal,
            float tolerance,
            List<float> coordinates)
        {
            using var measure = new SKPathMeasure(worldPath, false, 1f);
            float pathLength = measure.Length;
            if (pathLength < 0.001f) return;

            float step = pathLength / 200f;
            var samplePoints = new List<SKPoint>();

            for (float d = 0; d <= pathLength + 0.001f; d += step)
            {
                float sampleD = MathF.Min(d, pathLength);
                if (measure.GetPosition(sampleD, out var pt))
                    samplePoints.Add(pt);
            }

            // 处理闭合路径
            if (samplePoints.Count >= 2)
            {
                var first = samplePoints[0];
                var last = samplePoints[samplePoints.Count - 1];
                float dSq = (first.X - last.X) * (first.X - last.X) +
                            (first.Y - last.Y) * (first.Y - last.Y);
                if (dSq > 0.01f)
                    samplePoints.Add(first);
            }

            if (samplePoints.Count < 2) return;

            if (isHorizontal)
            {
                CollectHorizontalCoordinates(samplePoints, edgeValue, tolerance, coordinates);
            }
            else
            {
                CollectVerticalCoordinates(samplePoints, edgeValue, tolerance, coordinates);
            }
        }

        /// <summary>
        /// 检测路径与水平边（Y = edgeY）的交点，将 X 坐标追加到列表中。
        /// </summary>
        private static void CollectHorizontalCoordinates(
            List<SKPoint> points, float edgeY, float tolerance, List<float> coordinates)
        {
            for (int i = 0; i < points.Count - 1; i++)
            {
                var p1 = points[i];
                var p2 = points[i + 1];

                bool p1OnEdge = MathF.Abs(p1.Y - edgeY) < tolerance;
                bool p2OnEdge = MathF.Abs(p2.Y - edgeY) < tolerance;

                if (p1OnEdge && p2OnEdge)
                {
                    float x1 = MathF.Min(p1.X, p2.X);
                    float x2 = MathF.Max(p1.X, p2.X);
                    MergeOverlapInterval(coordinates, x1, x2);
                }
                else if (p1OnEdge)
                {
                    coordinates.Add(p1.X);
                }
                else if (!p2OnEdge)
                {
                    float dy = p2.Y - p1.Y;
                    if (MathF.Abs(dy) > 1e-6f)
                    {
                        float t = (edgeY - p1.Y) / dy;
                        if (t >= 0 && t <= 1)
                        {
                            float x = p1.X + t * (p2.X - p1.X);
                            coordinates.Add(x);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 检测路径与垂直边（X = edgeX）的交点，将 Y 坐标追加到列表中。
        /// </summary>
        private static void CollectVerticalCoordinates(
            List<SKPoint> points, float edgeX, float tolerance, List<float> coordinates)
        {
            for (int i = 0; i < points.Count - 1; i++)
            {
                var p1 = points[i];
                var p2 = points[i + 1];

                bool p1OnEdge = MathF.Abs(p1.X - edgeX) < tolerance;
                bool p2OnEdge = MathF.Abs(p2.X - edgeX) < tolerance;

                if (p1OnEdge && p2OnEdge)
                {
                    float y1 = MathF.Min(p1.Y, p2.Y);
                    float y2 = MathF.Max(p1.Y, p2.Y);
                    MergeOverlapInterval(coordinates, y1, y2);
                }
                else if (p1OnEdge)
                {
                    coordinates.Add(p1.Y);
                }
                else if (!p2OnEdge)
                {
                    float dx = p2.X - p1.X;
                    if (MathF.Abs(dx) > 1e-6f)
                    {
                        float t = (edgeX - p1.X) / dx;
                        if (t >= 0 && t <= 1)
                        {
                            float y = p1.Y + t * (p2.Y - p1.Y);
                            coordinates.Add(y);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 计算图形轮廓与单条 AABB 边的交点。
        /// 沿路径采样，检测路径段是否与目标边相交或重合。
        /// </summary>
        private static AABBEdgeIntersectionResult ComputeIntersectionsForEdge(
            SKPath path,
            float edgeValue,
            bool isHorizontal,
            float tolerance)
        {
            using var measure = new SKPathMeasure(path, false, 1f);
            float pathLength = measure.Length;
            if (pathLength < 0.001f)
                return default;

            // 采样步长：路径长度的 1/200
            float step = pathLength / 200f;
            var samplePoints = new List<SKPoint>();

            for (float d = 0; d <= pathLength + 0.001f; d += step)
            {
                float sampleD = MathF.Min(d, pathLength);
                if (measure.GetPosition(sampleD, out var pt))
                    samplePoints.Add(pt);
            }

            // 处理闭合路径
            if (samplePoints.Count >= 2)
            {
                var first = samplePoints[0];
                var last = samplePoints[samplePoints.Count - 1];
                float dSq = (first.X - last.X) * (first.X - last.X) +
                            (first.Y - last.Y) * (first.Y - last.Y);
                if (dSq > 0.01f)
                    samplePoints.Add(first);
            }

            if (samplePoints.Count < 2)
                return default;

            // 检测路径段与目标边的交点 / 重合
            if (isHorizontal)
            {
                return ComputeHorizontalIntersections(samplePoints, edgeValue, tolerance);
            }
            else
            {
                return ComputeVerticalIntersections(samplePoints, edgeValue, tolerance);
            }
        }

        /// <summary>
        /// 检测路径与水平边（Y = edgeValue）的交点。
        /// </summary>
        private static AABBEdgeIntersectionResult ComputeHorizontalIntersections(
            List<SKPoint> points,
            float edgeY,
            float tolerance)
        {
            var intersectionXs = new List<float>();

            for (int i = 0; i < points.Count - 1; i++)
            {
                var p1 = points[i];
                var p2 = points[i + 1];

                bool p1OnEdge = MathF.Abs(p1.Y - edgeY) < tolerance;
                bool p2OnEdge = MathF.Abs(p2.Y - edgeY) < tolerance;

                if (p1OnEdge && p2OnEdge)
                {
                    // 线段在边上 → 重合
                    float x1 = MathF.Min(p1.X, p2.X);
                    float x2 = MathF.Max(p1.X, p2.X);
                    // 合并相邻/重叠的区间
                    MergeOverlapInterval(intersectionXs, x1, x2);
                }
                else if (p1OnEdge)
                {
                    intersectionXs.Add(p1.X);
                }
                else if (p2OnEdge)
                {
                    // 只在 p2 在边上时才记录（避免与 p1OnEdge 重复）
                }
                else
                {
                    // 检测线段是否跨越目标边
                    float dy = p2.Y - p1.Y;
                    if (MathF.Abs(dy) > 1e-6f)
                    {
                        float t = (edgeY - p1.Y) / dy;
                        if (t >= 0 && t <= 1)
                        {
                            float x = p1.X + t * (p2.X - p1.X);
                            intersectionXs.Add(x);
                        }
                    }
                }
            }

            return AnalyzeIntersectionsX(intersectionXs, edgeY);
        }

        /// <summary>
        /// 检测路径与垂直边（X = edgeValue）的交点。
        /// </summary>
        private static AABBEdgeIntersectionResult ComputeVerticalIntersections(
            List<SKPoint> points,
            float edgeX,
            float tolerance)
        {
            var intersectionYs = new List<float>();

            for (int i = 0; i < points.Count - 1; i++)
            {
                var p1 = points[i];
                var p2 = points[i + 1];

                bool p1OnEdge = MathF.Abs(p1.X - edgeX) < tolerance;
                bool p2OnEdge = MathF.Abs(p2.X - edgeX) < tolerance;

                if (p1OnEdge && p2OnEdge)
                {
                    // 线段在边上 → 重合
                    float y1 = MathF.Min(p1.Y, p2.Y);
                    float y2 = MathF.Max(p1.Y, p2.Y);
                    MergeOverlapInterval(intersectionYs, y1, y2);
                }
                else if (p1OnEdge)
                {
                    intersectionYs.Add(p1.Y);
                }
                else
                {
                    float dx = p2.X - p1.X;
                    if (MathF.Abs(dx) > 1e-6f)
                    {
                        float t = (edgeX - p1.X) / dx;
                        if (t >= 0 && t <= 1)
                        {
                            float y = p1.Y + t * (p2.Y - p1.Y);
                            intersectionYs.Add(y);
                        }
                    }
                }
            }

            return AnalyzeIntersectionsY(intersectionYs, edgeX);
        }

        /// <summary>
        /// 将重合区间合并到列表中（处理相邻/重叠区间）。
        /// 存储格式：依次存储区间的起点和终点 [start1, end1, start2, end2, ...]。
        /// </summary>
        private static void MergeOverlapInterval(List<float> intervals, float start, float end)
        {
            if (MathF.Abs(end - start) < 0.001f)
                return; // 忽略太短的区间

            if (intervals.Count < 2)
            {
                intervals.Add(start);
                intervals.Add(end);
                return;
            }

            // 尝试与最后一个区间合并
            float lastStart = intervals[intervals.Count - 2];
            float lastEnd = intervals[intervals.Count - 1];

            if (MathF.Abs(start - lastEnd) < 0.01f || start < lastEnd)
            {
                // 与最后一个区间重叠/相邻 → 合并
                intervals[intervals.Count - 1] = MathF.Max(lastEnd, end);
            }
            else
            {
                intervals.Add(start);
                intervals.Add(end);
            }
        }

        /// <summary>
        /// 分析水平方向交点列表，返回单点或重合段结果。
        /// </summary>
        private static AABBEdgeIntersectionResult AnalyzeIntersectionsX(
            List<float> intersectionXs, float edgeY)
        {
            // 过滤重合区间
            var overlaps = new List<(float Start, float End)>();
            var singlePoints = new List<float>();

            for (int i = 0; i < intersectionXs.Count; i += 2)
            {
                if (i + 1 < intersectionXs.Count &&
                    MathF.Abs(intersectionXs[i + 1] - intersectionXs[i]) > 0.001f)
                {
                    // 这是一个区间（重合段）
                    overlaps.Add((intersectionXs[i], intersectionXs[i + 1]));
                }
                else
                {
                    // 这是一个单点
                    singlePoints.Add(intersectionXs[i]);
                }
            }

            // 优先返回重合段
            if (overlaps.Count > 0)
            {
                var resultPoints = new List<SKPoint>();
                foreach (var (s, e) in overlaps)
                {
                    float mid = (s + e) / 2f;
                    resultPoints.Add(new SKPoint(s, edgeY));
                    resultPoints.Add(new SKPoint(mid, edgeY));
                    resultPoints.Add(new SKPoint(e, edgeY));
                }
                return new AABBEdgeIntersectionResult(
                    SinglePoint: null,
                    OverlapPoints: resultPoints);
            }

            // 去重单点
            var unique = singlePoints
                .GroupBy(x => MathF.Round(x / 0.001f))
                .Select(g => g.First())
                .ToList();

            if (unique.Count == 1)
            {
                return new AABBEdgeIntersectionResult(
                    SinglePoint: new SKPoint(unique[0], edgeY),
                    OverlapPoints: null);
            }

            // 多个单点：取首尾中点（视为隐含的重合段）
            if (unique.Count > 1)
            {
                float minX = unique.Min();
                float maxX = unique.Max();
                return new AABBEdgeIntersectionResult(
                    SinglePoint: null,
                    OverlapPoints: new List<SKPoint>
                    {
                        new SKPoint(minX, edgeY),
                        new SKPoint((minX + maxX) / 2f, edgeY),
                        new SKPoint(maxX, edgeY)
                    });
            }

            return default;
        }

        /// <summary>
        /// 分析垂直方向交点列表，返回单点或重合段结果。
        /// </summary>
        private static AABBEdgeIntersectionResult AnalyzeIntersectionsY(
            List<float> intersectionYs, float edgeX)
        {
            var overlaps = new List<(float Start, float End)>();
            var singlePoints = new List<float>();

            for (int i = 0; i < intersectionYs.Count; i += 2)
            {
                if (i + 1 < intersectionYs.Count &&
                    MathF.Abs(intersectionYs[i + 1] - intersectionYs[i]) > 0.001f)
                {
                    overlaps.Add((intersectionYs[i], intersectionYs[i + 1]));
                }
                else
                {
                    singlePoints.Add(intersectionYs[i]);
                }
            }

            if (overlaps.Count > 0)
            {
                var resultPoints = new List<SKPoint>();
                foreach (var (s, e) in overlaps)
                {
                    float mid = (s + e) / 2f;
                    resultPoints.Add(new SKPoint(edgeX, s));
                    resultPoints.Add(new SKPoint(edgeX, mid));
                    resultPoints.Add(new SKPoint(edgeX, e));
                }
                return new AABBEdgeIntersectionResult(
                    SinglePoint: null,
                    OverlapPoints: resultPoints);
            }

            var unique = singlePoints
                .GroupBy(y => MathF.Round(y / 0.001f))
                .Select(g => g.First())
                .ToList();

            if (unique.Count == 1)
            {
                return new AABBEdgeIntersectionResult(
                    SinglePoint: new SKPoint(edgeX, unique[0]),
                    OverlapPoints: null);
            }

            if (unique.Count > 1)
            {
                float minY = unique.Min();
                float maxY = unique.Max();
                return new AABBEdgeIntersectionResult(
                    SinglePoint: null,
                    OverlapPoints: new List<SKPoint>
                    {
                        new SKPoint(edgeX, minY),
                        new SKPoint(edgeX, (minY + maxY) / 2f),
                        new SKPoint(edgeX, maxY)
                    });
            }

            return default;
        }



    }
}
