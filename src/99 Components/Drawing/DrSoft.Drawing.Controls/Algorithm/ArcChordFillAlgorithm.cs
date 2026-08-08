using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.Algorithm
{
    /// <summary>
    /// 圆弧弓形区域直线填充算法 - 基于边界偏移
    /// 修复小弧度时的角落溢出问题
    /// 支持三种圆弧定义方式：圆心+半径+角度、三点圆弧
    /// </summary>
    public class ArcChordFillAlgorithm
    {
        /// <summary>
        /// 圆弧定义类型
        /// </summary>
        public enum ArcDefineType
        {
            CenterRadius,   // 圆心+半径+角度
            ThreePoints     // 三点圆弧
        }

        public class ArcParam
        {
            // 通用属性
            public ArcDefineType DefineType { get; set; }

            // 圆心+半径方式
            public SKPoint Center { get; set; }
            public float Radius { get; set; }
            public float StartAngle { get; set; }
            public float SweepAngle { get; set; }

            // 三点方式
            public SKPoint Point1 { get; set; }  // 起点
            public SKPoint Point2 { get; set; }  // 中间点
            public SKPoint Point3 { get; set; }  // 终点

            // ========== 构造函数 ==========

            /// <summary>
            /// 使用圆心+半径+角度方式创建圆弧
            /// </summary>
            public ArcParam(SKPoint center, float radius, float startAngle, float sweepAngle)
            {
                DefineType = ArcDefineType.CenterRadius;
                Center = center;
                Radius = radius;
                StartAngle = startAngle;
                SweepAngle = sweepAngle;
            }

            /// <summary>
            /// 使用三点方式创建圆弧
            /// </summary>
            public ArcParam(SKPoint p1, SKPoint p2, SKPoint p3)
            {
                DefineType = ArcDefineType.ThreePoints;
                Point1 = p1;
                Point2 = p2;
                Point3 = p3;

                // 计算圆弧参数
                ComputeArcFromThreePoints();
            }

            /// <summary>
            /// 从三点计算圆弧参数
            /// </summary>
            private void ComputeArcFromThreePoints()
            {
                // 计算两条弦的中垂线交点得到圆心
                // 弦1: P1 -> P2
                // 弦2: P2 -> P3

                float x1 = Point1.X, y1 = Point1.Y;
                float x2 = Point2.X, y2 = Point2.Y;
                float x3 = Point3.X, y3 = Point3.Y;

                // 计算弦1的中点和斜率
                float midX1 = (x1 + x2) / 2;
                float midY1 = (y1 + y2) / 2;
                float dx1 = x2 - x1;
                float dy1 = y2 - y1;

                // 计算弦2的中点和斜率
                float midX2 = (x2 + x3) / 2;
                float midY2 = (y2 + y3) / 2;
                float dx2 = x3 - x2;
                float dy2 = y3 - y2;

                // 处理垂线斜率
                float k1 = -dx1 / dy1;  // 弦1中垂线的斜率
                float k2 = -dx2 / dy2;  // 弦2中垂线的斜率

                float centerX, centerY;

                if (Math.Abs(dy1) < 0.001f) // 弦1水平
                {
                    // 弦1水平，中垂线垂直
                    centerX = midX1;
                    // 弦2中垂线方程: y = k2(x - midX2) + midY2
                    centerY = k2 * (centerX - midX2) + midY2;
                }
                else if (Math.Abs(dy2) < 0.001f) // 弦2水平
                {
                    centerX = midX2;
                    centerY = k1 * (centerX - midX1) + midY1;
                }
                else
                {
                    // 一般情况：两中垂线交点
                    // 方程1: y = k1(x - midX1) + midY1
                    // 方程2: y = k2(x - midX2) + midY2
                    // 解出 x: k1(x - midX1) + midY1 = k2(x - midX2) + midY2
                    // k1*x - k1*midX1 + midY1 = k2*x - k2*midX2 + midY2
                    // (k1 - k2)*x = k1*midX1 - k2*midX2 + midY2 - midY1
                    if (Math.Abs(k1 - k2) < 0.001f)
                    {
                        // 平行线，三点共线，使用中点作为圆心（退化为直线）
                        centerX = midX1;
                        centerY = midY1;
                    }
                    else
                    {
                        centerX = (k1 * midX1 - k2 * midX2 + midY2 - midY1) / (k1 - k2);
                        centerY = k1 * (centerX - midX1) + midY1;
                    }
                }

                Center = new SKPoint(centerX, centerY);
                Radius = (float)Math.Sqrt(Math.Pow(Point1.X - centerX, 2) + Math.Pow(Point1.Y - centerY, 2));

                // 计算起始角度和扫掠角度
                float startRad = (float)Math.Atan2(Point1.Y - centerY, Point1.X - centerX);
                float endRad = (float)Math.Atan2(Point3.Y - centerY, Point3.X - centerX);
                float midRad = (float)Math.Atan2(Point2.Y - centerY, Point2.X - centerX);

                StartAngle = startRad * 180 / (float)Math.PI;
                float endAngle = endRad * 180 / (float)Math.PI;
                float midAngle = midRad * 180 / (float)Math.PI;

                // 归一化角度
                StartAngle = NormalizeAngle(StartAngle);
                endAngle = NormalizeAngle(endAngle);
                midAngle = NormalizeAngle(midAngle);

                // 判断方向并计算扫掠角
                float sweep1 = GetSweepAngle(StartAngle, midAngle);
                float sweep2 = GetSweepAngle(midAngle, endAngle);

                // 如果方向一致，取总和；否则方向不一致时调整
                if (Math.Sign(sweep1) == Math.Sign(sweep2))
                {
                    SweepAngle = sweep1 + sweep2;
                }
                else
                {
                    // 中点不在起点和终点之间，需要调整
                    SweepAngle = GetSweepAngle(StartAngle, endAngle);
                    // 验证中点是否在圆弧上
                    float testSweep = GetSweepAngle(StartAngle, midAngle);
                    if (Math.Abs(testSweep) > Math.Abs(SweepAngle))
                    {
                        SweepAngle = -SweepAngle;
                    }
                }

                // 确保扫掠角绝对值不超过360
                if (Math.Abs(SweepAngle) > 360)
                    SweepAngle = SweepAngle > 0 ? SweepAngle - 360 : SweepAngle + 360;
            }

            /// <summary>
            /// 计算从 start 到 end 的扫掠角（带方向）
            /// </summary>
            private float GetSweepAngle(float start, float end)
            {
                float sweep = end - start;
                if (sweep > 180)
                    sweep = sweep - 360;
                else if (sweep < -180)
                    sweep = sweep + 360;
                return sweep;
            }

            private float NormalizeAngle(float angle)
            {
                angle = angle % 360;
                if (angle < 0) angle += 360;
                return angle;
            }

            // ========== 计算属性 ==========

            public float EndAngle => StartAngle + SweepAngle;
            public float StartRad => StartAngle * (float)Math.PI / 180;
            public float EndRad => EndAngle * (float)Math.PI / 180;

            public SKPoint StartPoint => new SKPoint(
                Center.X + Radius * (float)Math.Cos(StartRad),
                Center.Y + Radius * (float)Math.Sin(StartRad)
            );

            public SKPoint EndPoint => new SKPoint(
                Center.X + Radius * (float)Math.Cos(EndRad),
                Center.Y + Radius * (float)Math.Sin(EndRad)
            );

            public float ChordLength
            {
                get
                {
                    float dx = EndPoint.X - StartPoint.X;
                    float dy = EndPoint.Y - StartPoint.Y;
                    return (float)Math.Sqrt(dx * dx + dy * dy);
                }
            }

            public float SignedDistanceToChord
            {
                get
                {
                    float x1 = StartPoint.X, y1 = StartPoint.Y;
                    float x2 = EndPoint.X, y2 = EndPoint.Y;
                    float cx = Center.X, cy = Center.Y;
                    return ((x2 - x1) * (cy - y1) - (cx - x1) * (y2 - y1)) / ChordLength;
                }
            }

            public bool IsMajor => Math.Abs(SweepAngle) > 180;
            public float Sagitta => IsMajor ? Radius + Math.Abs(SignedDistanceToChord) : Radius - Math.Abs(SignedDistanceToChord);

            /// <summary>
            /// 获取圆弧中点坐标
            /// </summary>
            public SKPoint MidPoint
            {
                get
                {
                    float midAngle = StartAngle + SweepAngle / 2;
                    float midRad = midAngle * (float)Math.PI / 180;
                    return new SKPoint(
                        Center.X + Radius * (float)Math.Cos(midRad),
                        Center.Y + Radius * (float)Math.Sin(midRad)
                    );
                }
            }
        }

        public class FillParams
        {
            public float LineAngle { get; set; } = 0f;
            public float Spacing { get; set; } = 10f;
            public float MarginToArc { get; set; } = 5f;
            public float MarginToChord { get; set; } = 5f;
            public SKPoint ReferencePoint { get; set; }
            public bool Bidirectional { get; set; } = false;
            /// <summary>
            /// 将扫描线在 [minProj, maxProj] 区间均等分布；实际间距 ≈ span/round(span/Spacing)。
            /// </summary>
            public bool AverageDistribute { get; set; } = false;
            /// <summary>
            /// 填充线延伸：正值沿填充线方向两端各延长 Extension，负值收缩；
            /// 延伸后长度 <= 0 的线段丢弃。
            /// </summary>
            public float Extension { get; set; } = 0f;
            /// <summary>
            /// 全局反向：与 Bidirectional 的奇行翻转叠加。
            /// </summary>
            public bool ReverseFillLine { get; set; } = false;
        }

        private ArcParam arc;
        private FillParams param;
        private SKPath result;

        public ArcChordFillAlgorithm(ArcParam arc, FillParams param)
        {
            this.arc = arc;
            this.param = param;
            this.result = new SKPath();
        }

        // 在 ArcChordFillAlgorithm 类中添加以下方法

        /// <summary>
        /// 获取填充线段列表（不通过路径转换）
        /// </summary>
        public List<(SKPoint Start, SKPoint End)> GetFillLines()
        {
            var lines = new List<(SKPoint Start, SKPoint End)>();
            result.Reset();

            // 1. 内缩圆弧：半径减去 MarginToArc
            float shrunkRadius = arc.Radius - param.MarginToArc;
            if (shrunkRadius <= 0) return lines;

            // 2. 内缩弦：向圆弧方向平移 MarginToChord
            SKPoint chordStart = arc.StartPoint;
            SKPoint chordEnd = arc.EndPoint;

            SKPoint chordDir = new SKPoint(chordEnd.X - chordStart.X, chordEnd.Y - chordStart.Y);
            float chordLen = arc.ChordLength;
            if (chordLen < 0.001f) return lines;
            chordDir = new SKPoint(chordDir.X / chordLen, chordDir.Y / chordLen);

            SKPoint normal = GetNormalToArc(chordDir);

            SKPoint shrunkChordStart = new SKPoint(
                chordStart.X + normal.X * param.MarginToChord,
                chordStart.Y + normal.Y * param.MarginToChord
            );
            SKPoint shrunkChordEnd = new SKPoint(
                chordEnd.X + normal.X * param.MarginToChord,
                chordEnd.Y + normal.Y * param.MarginToChord
            );

            // 3. 生成填充线段
            FillLinesInShrunkRegion(shrunkRadius, shrunkChordStart, shrunkChordEnd, lines);

            return lines;
        }

        /// <summary>
        /// 生成填充线段（直接添加到列表）
        /// </summary>
        private void FillLinesInShrunkRegion(float shrunkRadius, SKPoint shrunkChordStart, SKPoint shrunkChordEnd,
            List<(SKPoint Start, SKPoint End)> lines)
        {
            float lineAngleRad = param.LineAngle * (float)Math.PI / 180;
            SKPoint lineDir = new SKPoint((float)Math.Cos(lineAngleRad), (float)Math.Sin(lineAngleRad));
            SKPoint perpDir = new SKPoint(-lineDir.Y, lineDir.X);

            SKPoint chordMid = new SKPoint(
                (shrunkChordStart.X + shrunkChordEnd.X) / 2,
                (shrunkChordStart.Y + shrunkChordEnd.Y) / 2
            );

            SKPoint refPoint = (param.ReferencePoint.X == 0 && param.ReferencePoint.Y == 0)
                ? chordMid : param.ReferencePoint;

            // 获取边界点用于计算投影范围
            var boundaryPoints = GetShrunkBoundaryPoints(shrunkRadius, shrunkChordStart, shrunkChordEnd);

            float minProj = float.MaxValue, maxProj = float.MinValue;
            foreach (var p in boundaryPoints)
            {
                float proj = (p.X - refPoint.X) * perpDir.X + (p.Y - refPoint.Y) * perpDir.Y;
                minProj = Math.Min(minProj, proj);
                maxProj = Math.Max(maxProj, proj);
            }

            float expandLength = shrunkRadius * 3;
            var addedSegments = new HashSet<string>();

            // AverageDistribute ：将 Spacing 作为目标值，重算间距使扫描线在
            // [minProj, maxProj] 区间均等分布；将 span 平均分成 nGaps 份，生成
            // nGaps-1 条填充线，使“边界→首线 / 线间 / 尾线→边界”的间距全部相等 = span / nGaps
            float spacing = param.Spacing;
            float startProj = minProj;
            float projLimit = maxProj + 0.01f;
            if (param.AverageDistribute && maxProj > minProj)
            {
                float span = maxProj - minProj;
                int nGaps = Math.Max(2, (int)Math.Round(span / spacing));
                spacing = span / nGaps;
                startProj = minProj + spacing;
                projLimit = maxProj - spacing * 0.5f;
            }

            int lineIndex = 0;
            for (float proj = startProj; proj <= projLimit; proj += spacing, lineIndex++)
            {
                SKPoint lineStart = new SKPoint(
                    refPoint.X + lineDir.X * expandLength + perpDir.X * proj,
                    refPoint.Y + lineDir.Y * expandLength + perpDir.Y * proj
                );
                SKPoint lineEnd = new SKPoint(
                    refPoint.X - lineDir.X * expandLength + perpDir.X * proj,
                    refPoint.Y - lineDir.Y * expandLength + perpDir.Y * proj
                );

                // 使用精确的边界交点计算
                var intersections = GetPreciseIntersections(
                    lineStart, lineEnd, shrunkRadius, shrunkChordStart, shrunkChordEnd);

                for (int i = 0; i < intersections.Count - 1; i += 2)
                {
                    if (i + 1 < intersections.Count)
                    {
                        SKPoint p1 = intersections[i];
                        SKPoint p2 = intersections[i + 1];

                        // Extension 延伸：沿交点连线方向两端各延长 Extension（负值收缩，<=0 丢弃）
                        if (param.Extension != 0f)
                        {
                            float dx = p2.X - p1.X, dy = p2.Y - p1.Y;
                            float len = (float)Math.Sqrt(dx * dx + dy * dy);
                            if (len + 2f * param.Extension <= 1e-6f) continue;
                            if (len > 1e-6f)
                            {
                                float ux = dx / len, uy = dy / len;
                                p1 = new SKPoint(p1.X - ux * param.Extension, p1.Y - uy * param.Extension);
                                p2 = new SKPoint(p2.X + ux * param.Extension, p2.Y + uy * param.Extension);
                            }
                            else if (param.Extension <= 0f)
                            {
                                continue;
                            }
                        }

                        string key = GetSegmentKey(p1, p2);
                        if (!addedSegments.Contains(key))
                        {
                            // 本行方向：S型双向时奇数行翻转，叠加全局 ReverseFillLine
                            bool reverseLine = param.ReverseFillLine;
                            if (param.Bidirectional && (lineIndex & 1) == 1) reverseLine = !reverseLine;

                            if (reverseLine)
                            {
                                lines.Add((p2, p1));
                            }
                            else
                            {
                                lines.Add((p1, p2));
                            }
                            addedSegments.Add(key);
                        }
                    }
                }
            }
        }

        public SKPath GenerateFillPath()
        {
            result.Reset();

            // 1. 内缩圆弧：半径减去 MarginToArc
            float shrunkRadius = arc.Radius - param.MarginToArc;
            if (shrunkRadius <= 0) return result;

            // 2. 内缩弦：向圆弧方向平移 MarginToChord
            SKPoint chordStart = arc.StartPoint;
            SKPoint chordEnd = arc.EndPoint;

            SKPoint chordDir = new SKPoint(chordEnd.X - chordStart.X, chordEnd.Y - chordStart.Y);
            float chordLen = arc.ChordLength;
            if (chordLen < 0.001f) return result;
            chordDir = new SKPoint(chordDir.X / chordLen, chordDir.Y / chordLen);

            SKPoint normal = GetNormalToArc(chordDir);

            SKPoint shrunkChordStart = new SKPoint(
                chordStart.X + normal.X * param.MarginToChord,
                chordStart.Y + normal.Y * param.MarginToChord
            );
            SKPoint shrunkChordEnd = new SKPoint(
                chordEnd.X + normal.X * param.MarginToChord,
                chordEnd.Y + normal.Y * param.MarginToChord
            );

            // 3. 在内缩弓形区域内填充（使用精确的边界交点计算）
            FillInShrunkRegionPrecise(shrunkRadius, shrunkChordStart, shrunkChordEnd);

            return result;
        }

        private SKPoint GetNormalToArc(SKPoint chordDir)
        {
            SKPoint normal1 = new SKPoint(-chordDir.Y, chordDir.X);
            SKPoint normal2 = new SKPoint(chordDir.Y, -chordDir.X);

            float midAngle = arc.StartAngle + arc.SweepAngle / 2;
            float midRad = midAngle * (float)Math.PI / 180;
            SKPoint arcDir = new SKPoint((float)Math.Cos(midRad), (float)Math.Sin(midRad));

            float dot1 = normal1.X * arcDir.X + normal1.Y * arcDir.Y;
            float dot2 = normal2.X * arcDir.X + normal2.Y * arcDir.Y;

            return dot1 > dot2 ? normal1 : normal2;
        }

        /// <summary>
        /// 精确填充内缩区域 - 解决小弧度角落溢出问题
        /// </summary>
        private void FillInShrunkRegionPrecise(float shrunkRadius, SKPoint shrunkChordStart, SKPoint shrunkChordEnd)
        {
            float lineAngleRad = param.LineAngle * (float)Math.PI / 180;
            SKPoint lineDir = new SKPoint((float)Math.Cos(lineAngleRad), (float)Math.Sin(lineAngleRad));
            SKPoint perpDir = new SKPoint(-lineDir.Y, lineDir.X);

            SKPoint chordMid = new SKPoint(
                (shrunkChordStart.X + shrunkChordEnd.X) / 2,
                (shrunkChordStart.Y + shrunkChordEnd.Y) / 2
            );

            SKPoint refPoint = (param.ReferencePoint.X == 0 && param.ReferencePoint.Y == 0)
                ? chordMid : param.ReferencePoint;

            // 获取边界点用于计算投影范围
            var boundaryPoints = GetShrunkBoundaryPoints(shrunkRadius, shrunkChordStart, shrunkChordEnd);

            float minProj = float.MaxValue, maxProj = float.MinValue;
            foreach (var p in boundaryPoints)
            {
                float proj = (p.X - refPoint.X) * perpDir.X + (p.Y - refPoint.Y) * perpDir.Y;
                minProj = Math.Min(minProj, proj);
                maxProj = Math.Max(maxProj, proj);
            }

            float expandLength = shrunkRadius * 3;
            var addedSegments = new HashSet<string>();

            for (float proj = minProj; proj <= maxProj + 0.01f; proj += param.Spacing)
            {
                SKPoint lineStart = new SKPoint(
                    refPoint.X + lineDir.X * expandLength + perpDir.X * proj,
                    refPoint.Y + lineDir.Y * expandLength + perpDir.Y * proj
                );
                SKPoint lineEnd = new SKPoint(
                    refPoint.X - lineDir.X * expandLength + perpDir.X * proj,
                    refPoint.Y - lineDir.Y * expandLength + perpDir.Y * proj
                );

                // 使用精确的边界交点计算
                var intersections = GetPreciseIntersections(
                    lineStart, lineEnd, shrunkRadius, shrunkChordStart, shrunkChordEnd);

                for (int i = 0; i < intersections.Count - 1; i += 2)
                {
                    if (i + 1 < intersections.Count)
                    {
                        string key = GetSegmentKey(intersections[i], intersections[i + 1]);
                        if (!addedSegments.Contains(key))
                        {
                            result.MoveTo(intersections[i]);
                            result.LineTo(intersections[i + 1]);
                            addedSegments.Add(key);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 精确获取直线与内缩弓形区域的交点
        /// 关键：只取同时满足"在圆弧内侧"和"在弦内侧"的点
        /// </summary>
        private List<SKPoint> GetPreciseIntersections(
            SKPoint lineStart, SKPoint lineEnd,
            float radius, SKPoint chordStart, SKPoint chordEnd)
        {
            var allCandidates = new List<SKPoint>();

            // 与内缩圆弧的交点
            var arcIntersections = GetLineCircleIntersections(lineStart, lineEnd, arc.Center, radius);
            foreach (var p in arcIntersections)
            {
                // 检查是否在圆弧角度范围内
                if (IsPointOnArcAngle(p))
                    allCandidates.Add(p);
            }

            // 与内缩弦的交点
            var chordIntersection = GetLineSegmentIntersection(lineStart, lineEnd, chordStart, chordEnd);
            if (chordIntersection.HasValue)
                allCandidates.Add(chordIntersection.Value);

            // 去重
            allCandidates = RemoveDuplicatePoints(allCandidates);

            if (allCandidates.Count < 2) return new List<SKPoint>();

            // 按直线参数排序
            allCandidates.Sort((a, b) =>
            {
                float da = GetDistance(lineStart, a);
                float db = GetDistance(lineStart, b);
                return da.CompareTo(db);
            });

            // 核心修复：过滤掉不在有效区域内的交点对
            // 有效区域 = 圆弧内侧 && 弦内侧
            var validIntersections = new List<SKPoint>();

            for (int i = 0; i < allCandidates.Count - 1; i++)
            {
                SKPoint p1 = allCandidates[i];
                SKPoint p2 = allCandidates[i + 1];

                // 取线段中点判断是否在有效区域内
                SKPoint mid = new SKPoint((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2);

                bool insideArc = IsInsideShrunkArc(mid, radius);
                bool insideChord = IsInsideShrunkChord(mid, chordStart, chordEnd);

                // 只有中点同时满足两个条件时，这条线段才有效
                if (insideArc && insideChord)
                {
                    validIntersections.Add(p1);
                    validIntersections.Add(p2);
                }
            }

            // 去重并排序
            validIntersections = RemoveDuplicatePoints(validIntersections);
            validIntersections.Sort((a, b) =>
            {
                float da = GetDistance(lineStart, a);
                float db = GetDistance(lineStart, b);
                return da.CompareTo(db);
            });

            return validIntersections;
        }

        /// <summary>
        /// 判断点是否在内缩圆弧内侧（即距离圆心 <= 半径）
        /// </summary>
        private bool IsInsideShrunkArc(SKPoint point, float radius)
        {
            float dx = point.X - arc.Center.X;
            float dy = point.Y - arc.Center.Y;
            float dist = (float)Math.Sqrt(dx * dx + dy * dy);
            return dist <= radius + 0.1f;
        }

        /// <summary>
        /// 判断点是否在内缩弦的内侧（即弦的圆弧侧）
        /// </summary>
        private bool IsInsideShrunkChord(SKPoint point, SKPoint chordStart, SKPoint chordEnd)
        {
            // 计算点到弦所在直线的有向距离
            float x1 = chordStart.X, y1 = chordStart.Y;
            float x2 = chordEnd.X, y2 = chordEnd.Y;

            float area = (x2 - x1) * (point.Y - y1) - (point.X - x1) * (y2 - y1);

            // 计算圆弧侧的方向
            float midAngle = arc.StartAngle + arc.SweepAngle / 2;
            float midRad = midAngle * (float)Math.PI / 180;
            SKPoint arcDir = new SKPoint((float)Math.Cos(midRad), (float)Math.Sin(midRad));

            // 弦中点到圆弧中点的方向
            SKPoint chordMid = new SKPoint((x1 + x2) / 2, (y1 + y2) / 2);
            SKPoint toArc = new SKPoint(arc.Center.X + arc.Radius * arcDir.X - chordMid.X,
                                         arc.Center.Y + arc.Radius * arcDir.Y - chordMid.Y);

            // 判断点的侧向是否与圆弧方向一致
            float chordSide = (x2 - x1) * (point.Y - y1) - (point.X - x1) * (y2 - y1);
            float arcSide = (x2 - x1) * (toArc.Y) - (toArc.X) * (y2 - y1);

            return Math.Sign(chordSide) == Math.Sign(arcSide);
        }

        private List<SKPoint> GetShrunkBoundaryPoints(float radius, SKPoint chordStart, SKPoint chordEnd)
        {
            var points = new List<SKPoint>();

            points.Add(chordStart);
            points.Add(chordEnd);

            int segments = Math.Max(60, (int)(Math.Abs(arc.SweepAngle) / 2));
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float angle = arc.StartAngle + arc.SweepAngle * t;
                float rad = angle * (float)Math.PI / 180;

                points.Add(new SKPoint(
                    arc.Center.X + radius * (float)Math.Cos(rad),
                    arc.Center.Y + radius * (float)Math.Sin(rad)
                ));
            }

            return points;
        }

        private bool IsPointOnArcAngle(SKPoint point)
        {
            float angle = (float)(Math.Atan2(point.Y - arc.Center.Y, point.X - arc.Center.X) * 180 / Math.PI);
            if (angle < 0) angle += 360;

            float startAngle = NormalizeAngle(arc.StartAngle);
            float endAngle = NormalizeAngle(arc.EndAngle);

            if (arc.SweepAngle > 0)
            {
                if (startAngle <= endAngle)
                    return angle >= startAngle - 0.1f && angle <= endAngle + 0.1f;
                else
                    return angle >= startAngle - 0.1f || angle <= endAngle + 0.1f;
            }
            else
            {
                if (endAngle <= startAngle)
                    return angle >= endAngle - 0.1f && angle <= startAngle + 0.1f;
                else
                    return angle >= endAngle - 0.1f || angle <= startAngle + 0.1f;
            }
        }

        #region 辅助方法

        private List<SKPoint> GetLineCircleIntersections(SKPoint p1, SKPoint p2, SKPoint center, float radius)
        {
            var intersections = new List<SKPoint>();

            SKPoint dir = new SKPoint(p2.X - p1.X, p2.Y - p1.Y);
            float a = dir.X * dir.X + dir.Y * dir.Y;
            if (Math.Abs(a) < 1e-6f) return intersections;

            float b = 2 * (dir.X * (p1.X - center.X) + dir.Y * (p1.Y - center.Y));
            float c = (p1.X - center.X) * (p1.X - center.X) +
                      (p1.Y - center.Y) * (p1.Y - center.Y) - radius * radius;

            float delta = b * b - 4 * a * c;

            if (delta >= 0)
            {
                float sqrtDelta = (float)Math.Sqrt(delta);
                float t1 = (-b - sqrtDelta) / (2 * a);
                float t2 = (-b + sqrtDelta) / (2 * a);

                if (t1 >= 0 && t1 <= 1)
                    intersections.Add(new SKPoint(p1.X + t1 * dir.X, p1.Y + t1 * dir.Y));

                if (t2 >= 0 && t2 <= 1 && Math.Abs(t2 - t1) > 1e-6f)
                    intersections.Add(new SKPoint(p1.X + t2 * dir.X, p1.Y + t2 * dir.Y));
            }

            return intersections;
        }

        private SKPoint? GetLineSegmentIntersection(SKPoint lineStart, SKPoint lineEnd, SKPoint segStart, SKPoint segEnd)
        {
            float x1 = lineStart.X, y1 = lineStart.Y;
            float x2 = lineEnd.X, y2 = lineEnd.Y;
            float x3 = segStart.X, y3 = segStart.Y;
            float x4 = segEnd.X, y4 = segEnd.Y;

            float denom = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
            if (Math.Abs(denom) < 1e-6f) return null;

            float t = ((x1 - x3) * (y3 - y4) - (y1 - y3) * (x3 - x4)) / denom;
            float u = -((x1 - x2) * (y1 - y3) - (y1 - y2) * (x1 - x3)) / denom;

            if (t >= 0 && t <= 1 && u >= 0 && u <= 1)
                return new SKPoint(x1 + t * (x2 - x1), y1 + t * (y2 - y1));

            return null;
        }

        private string GetSegmentKey(SKPoint p1, SKPoint p2)
        {
            float x1 = (float)Math.Round(p1.X, 2);
            float y1 = (float)Math.Round(p1.Y, 2);
            float x2 = (float)Math.Round(p2.X, 2);
            float y2 = (float)Math.Round(p2.Y, 2);

            if (x1 > x2 || (Math.Abs(x1 - x2) < 0.01f && y1 > y2))
                return $"{x2:F2},{y2:F2}|{x1:F2},{y1:F2}";
            return $"{x1:F2},{y1:F2}|{x2:F2},{y2:F2}";
        }

        private List<SKPoint> RemoveDuplicatePoints(List<SKPoint> points)
        {
            var result = new List<SKPoint>();
            foreach (var p in points)
            {
                bool dup = false;
                foreach (var e in result)
                {
                    if (Math.Abs(e.X - p.X) < 0.01f && Math.Abs(e.Y - p.Y) < 0.01f)
                    { dup = true; break; }
                }
                if (!dup) result.Add(p);
            }
            return result;
        }

        private float GetDistance(SKPoint p1, SKPoint p2)
        {
            float dx = p1.X - p2.X;
            float dy = p1.Y - p2.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private float NormalizeAngle(float angle)
        {
            angle = angle % 360;
            if (angle < 0) angle += 360;
            return angle;
        }

        #endregion
    }
}