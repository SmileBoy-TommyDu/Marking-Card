using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.Algorithm
{
    /// <summary>
    /// 圆弧直线填充算法类 - 弓形区域填充（最终优化版）
    /// 完全解决：1.拖动后填充线数量变化问题 2.角落空白问题 3.外部多余三角形问题 4.多余圆心方向线段
    /// </summary>
    public class ArcLineAlgorithm
    {
        /// <summary>
        /// 圆弧参数
        /// </summary>
        public class ArcParameter
        {
            public SKPoint Center { get; set; }
            public float Radius { get; set; }
            public float StartAngle { get; set; }
            public float SweepAngle { get; set; }

            public ArcParameter(SKPoint center, float radius, float startAngle, float sweepAngle)
            {
                Center = center;
                Radius = radius;
                StartAngle = startAngle;
                SweepAngle = sweepAngle;
            }

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

            public SKPoint ChordMidPoint => new SKPoint(
                (StartPoint.X + EndPoint.X) / 2,
                (StartPoint.Y + EndPoint.Y) / 2
            );

            public SKPoint ArcMidPoint
            {
                get
                {
                    float midAngle = StartAngle + SweepAngle / 2;
                    float rad = midAngle * (float)Math.PI / 180;
                    return new SKPoint(
                        Center.X + Radius * (float)Math.Cos(rad),
                        Center.Y + Radius * (float)Math.Sin(rad)
                    );
                }
            }

            public float Sagitta
            {
                get
                {
                    float distToChord = DistanceFromCenterToChord;
                    return Radius - distToChord;
                }
            }

            public float DistanceFromCenterToChord
            {
                get
                {
                    float x1 = StartPoint.X, y1 = StartPoint.Y;
                    float x2 = EndPoint.X, y2 = EndPoint.Y;
                    float cx = Center.X, cy = Center.Y;

                    float area = Math.Abs((x2 - x1) * (cy - y1) - (cx - x1) * (y2 - y1));
                    float chordLength = ChordLength;

                    return chordLength < 0.001f ? 0 : area / chordLength;
                }
            }

            public float ChordLength
            {
                get
                {
                    float dx = EndPoint.X - StartPoint.X;
                    float dy = EndPoint.Y - StartPoint.Y;
                    return (float)Math.Sqrt(dx * dx + dy * dy);
                }
            }

            public SKRect GetLocalBounds()
            {
                float minX = Math.Min(StartPoint.X, EndPoint.X);
                float maxX = Math.Max(StartPoint.X, EndPoint.X);
                float minY = Math.Min(StartPoint.Y, EndPoint.Y);
                float maxY = Math.Max(StartPoint.Y, EndPoint.Y);

                minX = Math.Min(minX, ArcMidPoint.X);
                maxX = Math.Max(maxX, ArcMidPoint.X);
                minY = Math.Min(minY, ArcMidPoint.Y);
                maxY = Math.Max(maxY, ArcMidPoint.Y);

                return new SKRect(minX, minY, maxX, maxY);
            }
        }

        public enum FillType
        {
            ParallelLines,
            RadialLines,
            ConcentricArcs,
            Grid
        }

        public class FillOptions
        {
            public float Margin { get; set; } = 10f;
            public float Spacing { get; set; } = 12f;
            public float LineAngle { get; set; } = 0f;
            public FillType Type { get; set; } = FillType.ParallelLines;
            public bool AdaptiveSpacing { get; set; } = false;
            public int TargetLineCount { get; set; } = 20;
            public float MinSpacing { get; set; } = 2f;
            public float MaxSpacing { get; set; } = 50f;
            public bool FillCorners { get; set; } = true;
        }

        private ArcParameter arc;
        private FillOptions options;
        private SKMatrix localToWorld;
        private SKMatrix worldToLocal;
        private SKRect localBounds;
        private float localSagitta;
        private float localChordLength;
        private SKPoint localStartPoint;
        private SKPoint localEndPoint;
        private SKPoint localCenter;
        private float localRadius;
        private float chordAngle;

        public ArcLineAlgorithm(ArcParameter arc, FillOptions options)
        {
            this.arc = arc;
            this.options = options ?? new FillOptions();
            InitializeLocalCoordinateSystem();
        }

        public ArcLineAlgorithm(ArcParameter arc, float margin, float spacing,
                                float lineAngle, SKPoint referencePoint, FillType fillType = FillType.ParallelLines)
            : this(arc, new FillOptions
            {
                Margin = margin,
                Spacing = spacing,
                LineAngle = lineAngle,
                Type = fillType
            })
        {
        }

        private void InitializeLocalCoordinateSystem()
        {
            localBounds = arc.GetLocalBounds();
            localSagitta = arc.Sagitta;
            localChordLength = arc.ChordLength;
            localStartPoint = arc.StartPoint;
            localEndPoint = arc.EndPoint;
            localCenter = arc.Center;
            localRadius = arc.Radius;

            chordAngle = (float)Math.Atan2(
                localEndPoint.Y - localStartPoint.Y,
                localEndPoint.X - localStartPoint.X
            );

            SKPoint chordMid = new SKPoint(
                (localStartPoint.X + localEndPoint.X) / 2,
                (localStartPoint.Y + localEndPoint.Y) / 2
            );

            localToWorld = SKMatrix.CreateTranslation(chordMid.X, chordMid.Y);
            localToWorld = localToWorld.PreConcat(SKMatrix.CreateRotation(chordAngle));

            worldToLocal = SKMatrix.CreateRotation(-chordAngle);
            worldToLocal = worldToLocal.PreConcat(SKMatrix.CreateTranslation(-chordMid.X, -chordMid.Y));
        }

        public SKPath GenerateFillPath()
        {
            switch (options.Type)
            {
                case FillType.ParallelLines:
                    return GenerateParallelLinesFinal();
                case FillType.RadialLines:
                    return GenerateRadialLinesFinal();
                case FillType.ConcentricArcs:
                    return GenerateConcentricArcsFinal();
                case FillType.Grid:
                    return GenerateGridLinesFinal();
                default:
                    return GenerateParallelLinesFinal();
            }
        }

        #region 最终版平行线填充

        private SKPath GenerateParallelLinesFinal()
        {
            var path = new SKPath();

            float actualSpacing = options.AdaptiveSpacing ? CalculateAdaptiveSpacing() : options.Spacing;

            float halfChord = localChordLength / 2;
            float sagitta = localSagitta;

            float startY = options.Margin;
            float endY = sagitta - options.Margin;

            if (startY >= endY) return path;

            float angleRad = options.LineAngle * (float)Math.PI / 180;
            SKPoint lineDirLocal = new SKPoint((float)Math.Cos(angleRad), (float)Math.Sin(angleRad));
            SKPoint normalLocal = new SKPoint(-lineDirLocal.Y, lineDirLocal.X);

            float minProj = float.MaxValue, maxProj = float.MinValue;

            var boundaryPoints = GetBoundaryPointsInLocal();

            foreach (var pt in boundaryPoints)
            {
                float proj = pt.X * normalLocal.X + pt.Y * normalLocal.Y;
                minProj = Math.Min(minProj, proj);
                maxProj = Math.Max(maxProj, proj);
            }

            minProj += options.Margin;
            maxProj -= options.Margin;

            if (minProj >= maxProj) return path;

            float expandFactor = sagitta * 2;
            var addedSegments = new HashSet<string>();

            float step = actualSpacing;
            if (options.FillCorners) step = actualSpacing * 0.8f;

            for (float proj = minProj; proj <= maxProj + 0.01f; proj += step)
            {
                SKPoint lineStartLocal = new SKPoint(
                    -expandFactor * lineDirLocal.X + normalLocal.X * proj,
                    -expandFactor * lineDirLocal.Y + normalLocal.Y * proj
                );
                SKPoint lineEndLocal = new SKPoint(
                    expandFactor * lineDirLocal.X + normalLocal.X * proj,
                    expandFactor * lineDirLocal.Y + normalLocal.Y * proj
                );

                SKPoint lineStart = TransformLocalToWorld(lineStartLocal);
                SKPoint lineEnd = TransformLocalToWorld(lineEndLocal);

                var intersections = GetLineWithBoundaryIntersectionsFinal(lineStart, lineEnd);

                for (int i = 0; i < intersections.Count - 1; i += 2)
                {
                    if (i + 1 < intersections.Count)
                    {
                        string key = GetSegmentKey(intersections[i], intersections[i + 1]);
                        if (!addedSegments.Contains(key))
                        {
                            path.MoveTo(intersections[i]);
                            path.LineTo(intersections[i + 1]);
                            addedSegments.Add(key);
                        }
                    }
                }
            }

            if (options.FillCorners)
            {
                FillCornersFinal(path, actualSpacing);
            }

            return path;
        }

        private List<SKPoint> GetBoundaryPointsInLocal()
        {
            var points = new List<SKPoint>();
            float halfChord = localChordLength / 2;
            float sagitta = localSagitta;

            points.Add(new SKPoint(-halfChord, 0));
            points.Add(new SKPoint(halfChord, 0));
            points.Add(new SKPoint(0, sagitta));

            int arcSamples = Math.Max(30, (int)(Math.Abs(arc.SweepAngle) / 3));
            for (int i = 0; i <= arcSamples; i++)
            {
                float t = i / (float)arcSamples;
                float angle = arc.StartAngle + arc.SweepAngle * t;
                float rad = angle * (float)Math.PI / 180;

                SKPoint worldPoint = new SKPoint(
                    arc.Center.X + arc.Radius * (float)Math.Cos(rad),
                    arc.Center.Y + arc.Radius * (float)Math.Sin(rad)
                );
                SKPoint localPoint = TransformWorldToLocal(worldPoint);
                points.Add(localPoint);
            }

            return points;
        }

        /// <summary>
        /// 修复版角落填充 - 只填充弓形内部，不产生外部三角形和圆心方向线段
        /// </summary>
        private void FillCornersFinal(SKPath path, float spacing)
        {
            float halfChord = localChordLength / 2;
            float sagitta = localSagitta;

            // 减小角落填充半径，只填充真正需要填充的小区域
            float cornerRadius = Math.Min(spacing * 0.8f, Math.Min(halfChord * 0.3f, sagitta * 0.3f));
            if (cornerRadius < 0.1f) return;

            // 填充靠近弦的角落区域（平行于弦的方向）
            FillCornerAlongChord(path, -halfChord, 0, cornerRadius, spacing, true);
            FillCornerAlongChord(path, halfChord, 0, cornerRadius, spacing, false);

            // 填充靠近圆弧的角落区域
            FillCornerAlongArc(path, -halfChord, 0, cornerRadius, spacing, true);
            FillCornerAlongArc(path, halfChord, 0, cornerRadius, spacing, false);
        }

        /// <summary>
        /// 沿着弦方向填充角落（平行于弦的短线）
        /// </summary>
        private void FillCornerAlongChord(SKPath path, float cornerX, float cornerY, float radius, float spacing, bool isLeft)
        {
            float halfChord = localChordLength / 2;
            float sagitta = localSagitta;

            int lineCount = Math.Max(2, (int)(radius / spacing * 2));

            for (int i = 1; i <= lineCount; i++)
            {
                float t = i / (float)lineCount;
                float offset = radius * t;

                // 生成平行于弦的短线（在Y方向上有轻微偏移）
                float y = offset * 0.3f;
                if (y > sagitta * 0.3f) continue;

                float x1 = cornerX + (isLeft ? -offset : offset) * 0.5f;
                float x2 = cornerX + (isLeft ? -offset * 0.8f : offset * 0.8f);

                // 确保线段在弓形内部
                if (y > sagitta) continue;

                float maxXAtY = halfChord * (1 - y / sagitta);
                if (Math.Abs(x1) > maxXAtY + 0.1f || Math.Abs(x2) > maxXAtY + 0.1f) continue;

                SKPoint p1 = TransformLocalToWorld(new SKPoint(x1, y));
                SKPoint p2 = TransformLocalToWorld(new SKPoint(x2, y));

                if (IsPointInSegmentFinal(p1) && IsPointInSegmentFinal(p2))
                {
                    path.MoveTo(p1);
                    path.LineTo(p2);
                }
            }
        }

        /// <summary>
        /// 沿着圆弧方向填充角落（沿着圆弧的短弧线）
        /// </summary>
        private void FillCornerAlongArc(SKPath path, float cornerX, float cornerY, float radius, float spacing, bool isLeft)
        {
            float halfChord = localChordLength / 2;
            float sagitta = localSagitta;

            int arcCount = Math.Max(2, (int)(radius / spacing * 2));

            for (int i = 1; i <= arcCount; i++)
            {
                float t = i / (float)arcCount;
                float offset = radius * t;

                // 生成沿着圆弧方向的小弧段
                float startY = offset * 0.2f;
                float endY = offset * 0.5f;

                if (endY > sagitta * 0.5f) continue;

                float startX = cornerX + (isLeft ? -offset * 0.3f : offset * 0.3f);
                float endX = cornerX + (isLeft ? -offset * 0.6f : offset * 0.6f);

                SKPoint p1 = TransformLocalToWorld(new SKPoint(startX, startY));
                SKPoint p2 = TransformLocalToWorld(new SKPoint(endX, endY));

                if (IsPointInSegmentFinal(p1) && IsPointInSegmentFinal(p2))
                {
                    path.MoveTo(p1);
                    path.LineTo(p2);
                }
            }
        }

        #endregion

        #region 径向线填充

        private SKPath GenerateRadialLinesFinal()
        {
            var path = new SKPath();

            float sagitta = localSagitta;
            float startDistance = options.Margin;
            float endDistance = sagitta - options.Margin;

            if (startDistance >= endDistance) return path;

            float actualSpacing = options.AdaptiveSpacing ? CalculateAdaptiveSpacing() : options.Spacing;
            float step = actualSpacing;
            if (options.FillCorners) step = actualSpacing * 0.7f;

            float halfChord = localChordLength / 2;

            for (float distance = startDistance; distance <= endDistance + 0.01f; distance += step)
            {
                float clampedDistance = Math.Min(distance, endDistance);

                float t = clampedDistance / sagitta;
                float currentHalfWidth = halfChord * (1 - t);

                SKPoint startLocal = new SKPoint(-currentHalfWidth, clampedDistance);
                SKPoint endLocal = new SKPoint(currentHalfWidth, clampedDistance);

                SKPoint startWorld = TransformLocalToWorld(startLocal);
                SKPoint endWorld = TransformLocalToWorld(endLocal);

                var intersections = GetLineArcIntersectionsFinal(startWorld, endWorld);

                if (intersections.Count >= 2)
                {
                    path.MoveTo(intersections[0]);
                    path.LineTo(intersections[1]);
                }
            }

            return path;
        }

        #endregion

        #region 同心圆弧填充

        private SKPath GenerateConcentricArcsFinal()
        {
            var path = new SKPath();

            float sagitta = localSagitta;
            float startDistance = options.Margin;
            float endDistance = sagitta - options.Margin;

            if (startDistance >= endDistance) return path;

            float actualSpacing = options.AdaptiveSpacing ? CalculateAdaptiveSpacing() : options.Spacing;
            float step = actualSpacing;
            if (options.FillCorners) step = actualSpacing * 0.7f;

            float distToChord = arc.DistanceFromCenterToChord;

            for (float distance = startDistance; distance <= endDistance + 0.01f; distance += step)
            {
                float clampedDistance = Math.Min(distance, endDistance);

                float currentRadius = (float)Math.Sqrt(
                    distToChord * distToChord +
                    (sagitta - clampedDistance) * (sagitta - clampedDistance)
                );

                if (currentRadius < arc.Radius && currentRadius > 0)
                {
                    SKRect rect = new SKRect(
                        arc.Center.X - currentRadius,
                        arc.Center.Y - currentRadius,
                        arc.Center.X + currentRadius,
                        arc.Center.Y + currentRadius
                    );

                    float currentStartAngle, currentSweepAngle;
                    GetArcAnglesAtDistanceFinal(clampedDistance, out currentStartAngle, out currentSweepAngle);

                    if (Math.Abs(currentSweepAngle) > 0.1f)
                    {
                        path.AddArc(rect, currentStartAngle, currentSweepAngle);
                    }
                }
            }

            return path;
        }

        #endregion

        #region 网格填充

        private SKPath GenerateGridLinesFinal()
        {
            var path = new SKPath();

            var optionsH = new FillOptions
            {
                Margin = options.Margin,
                Spacing = options.Spacing,
                LineAngle = 0,
                Type = FillType.ParallelLines,
                AdaptiveSpacing = options.AdaptiveSpacing,
                TargetLineCount = options.TargetLineCount,
                MinSpacing = options.MinSpacing,
                MaxSpacing = options.MaxSpacing,
                FillCorners = options.FillCorners
            };
            var algorithmH = new ArcLineAlgorithm(arc, optionsH);
            path.AddPath(algorithmH.GenerateFillPath());

            var optionsV = new FillOptions
            {
                Margin = options.Margin,
                Spacing = options.Spacing,
                LineAngle = 90,
                Type = FillType.ParallelLines,
                AdaptiveSpacing = options.AdaptiveSpacing,
                TargetLineCount = options.TargetLineCount,
                MinSpacing = options.MinSpacing,
                MaxSpacing = options.MaxSpacing,
                FillCorners = options.FillCorners
            };
            var algorithmV = new ArcLineAlgorithm(arc, optionsV);
            path.AddPath(algorithmV.GenerateFillPath());

            return path;
        }

        #endregion

        #region 辅助方法

        private SKPoint TransformLocalToWorld(SKPoint localPoint)
        {
            float x = localPoint.X;
            float y = localPoint.Y;
            float worldX = localToWorld.TransX + x * localToWorld.ScaleX + y * localToWorld.SkewX;
            float worldY = localToWorld.TransY + x * localToWorld.SkewY + y * localToWorld.ScaleY;
            return new SKPoint(worldX, worldY);
        }

        private SKPoint TransformWorldToLocal(SKPoint worldPoint)
        {
            float x = worldPoint.X - localToWorld.TransX;
            float y = worldPoint.Y - localToWorld.TransY;
            float localX = x * worldToLocal.ScaleX + y * worldToLocal.SkewX;
            float localY = x * worldToLocal.SkewY + y * worldToLocal.ScaleY;
            return new SKPoint(localX, localY);
        }

        private bool IsPointInSegmentFinal(SKPoint worldPoint)
        {
            SKPoint localPoint = TransformWorldToLocal(worldPoint);
            float halfChord = localChordLength / 2;
            float sagitta = localSagitta;
            float x = localPoint.X;
            float y = localPoint.Y;

            if (y < -0.1f) return false;
            if (y > sagitta + 0.1f) return false;

            float maxX = halfChord * (1 - y / sagitta);
            if (Math.Abs(x) > maxX + 0.1f) return false;

            float dxToCenter = worldPoint.X - arc.Center.X;
            float dyToCenter = worldPoint.Y - arc.Center.Y;
            float distanceToCenter = (float)Math.Sqrt(dxToCenter * dxToCenter + dyToCenter * dyToCenter);

            if (distanceToCenter > arc.Radius + 0.1f) return false;

            float angle = (float)(Math.Atan2(dyToCenter, dxToCenter) * 180 / Math.PI);
            if (angle < 0) angle += 360;

            float startAngle = NormalizeAngle(arc.StartAngle);
            float endAngle = NormalizeAngle(arc.EndAngle);

            bool inArcAngle;
            if (arc.SweepAngle > 0)
            {
                if (startAngle <= endAngle)
                    inArcAngle = angle >= startAngle - 0.5f && angle <= endAngle + 0.5f;
                else
                    inArcAngle = angle >= startAngle - 0.5f || angle <= endAngle + 0.5f;
            }
            else
            {
                if (endAngle <= startAngle)
                    inArcAngle = angle >= endAngle - 0.5f && angle <= startAngle + 0.5f;
                else
                    inArcAngle = angle >= endAngle - 0.5f || angle <= startAngle + 0.5f;
            }

            if (!inArcAngle)
            {
                float chordSide = (arc.EndPoint.X - arc.StartPoint.X) * (worldPoint.Y - arc.StartPoint.Y) -
                                  (arc.EndPoint.Y - arc.StartPoint.Y) * (worldPoint.X - arc.StartPoint.X);

                float centerSide = (arc.EndPoint.X - arc.StartPoint.X) * (arc.Center.Y - arc.StartPoint.Y) -
                                   (arc.EndPoint.Y - arc.StartPoint.Y) * (arc.Center.X - arc.StartPoint.X);

                return Math.Sign(chordSide) == Math.Sign(centerSide);
            }

            return true;
        }

        private List<SKPoint> GetLineWithBoundaryIntersectionsFinal(SKPoint lineStart, SKPoint lineEnd)
        {
            var intersections = new List<SKPoint>();

            var arcIntersections = GetLineArcIntersectionsFinal(lineStart, lineEnd);
            intersections.AddRange(arcIntersections);

            var chordIntersections = GetLineSegmentIntersectionsFinal(lineStart, lineEnd, arc.StartPoint, arc.EndPoint);
            intersections.AddRange(chordIntersections);

            intersections = RemoveDuplicatePoints(intersections);
            intersections.Sort((a, b) =>
            {
                float da = GetDistance(lineStart, a);
                float db = GetDistance(lineStart, b);
                return da.CompareTo(db);
            });

            return intersections;
        }

        private List<SKPoint> GetLineArcIntersectionsFinal(SKPoint p1, SKPoint p2)
        {
            var intersections = new List<SKPoint>();

            SKPoint dir = new SKPoint(p2.X - p1.X, p2.Y - p1.Y);
            float a = dir.X * dir.X + dir.Y * dir.Y;
            if (Math.Abs(a) < 1e-6f) return intersections;

            float b = 2 * (dir.X * (p1.X - arc.Center.X) + dir.Y * (p1.Y - arc.Center.Y));
            float c = (p1.X - arc.Center.X) * (p1.X - arc.Center.X) +
                      (p1.Y - arc.Center.Y) * (p1.Y - arc.Center.Y) - arc.Radius * arc.Radius;

            float delta = b * b - 4 * a * c;

            if (delta >= 0)
            {
                float sqrtDelta = (float)Math.Sqrt(delta);
                float t1 = (-b - sqrtDelta) / (2 * a);
                float t2 = (-b + sqrtDelta) / (2 * a);

                if (t1 >= 0 && t1 <= 1)
                {
                    SKPoint point = new SKPoint(p1.X + t1 * dir.X, p1.Y + t1 * dir.Y);
                    if (IsPointOnArcFinal(point))
                        intersections.Add(point);
                }

                if (t2 >= 0 && t2 <= 1 && Math.Abs(t2 - t1) > 1e-6f)
                {
                    SKPoint point = new SKPoint(p1.X + t2 * dir.X, p1.Y + t2 * dir.Y);
                    if (IsPointOnArcFinal(point))
                        intersections.Add(point);
                }
            }

            return intersections;
        }

        private List<SKPoint> GetLineSegmentIntersectionsFinal(SKPoint lineStart, SKPoint lineEnd,
                                                                SKPoint segStart, SKPoint segEnd)
        {
            var intersections = new List<SKPoint>();

            float x1 = lineStart.X, y1 = lineStart.Y;
            float x2 = lineEnd.X, y2 = lineEnd.Y;
            float x3 = segStart.X, y3 = segStart.Y;
            float x4 = segEnd.X, y4 = segEnd.Y;

            float denom = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
            if (Math.Abs(denom) < 1e-6f) return intersections;

            float t = ((x1 - x3) * (y3 - y4) - (y1 - y3) * (x3 - x4)) / denom;
            float u = -((x1 - x2) * (y1 - y3) - (y1 - y2) * (x1 - x3)) / denom;

            if (t >= 0 && t <= 1 && u >= 0 && u <= 1)
            {
                SKPoint point = new SKPoint(x1 + t * (x2 - x1), y1 + t * (y2 - y1));
                intersections.Add(point);
            }

            return intersections;
        }

        private bool IsPointOnArcFinal(SKPoint point)
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

        private void GetArcAnglesAtDistanceFinal(float distanceFromChord, out float startAngle, out float sweepAngle)
        {
            float sagitta = localSagitta;
            float distToChord = arc.DistanceFromCenterToChord;

            float currentRadius = (float)Math.Sqrt(
                distToChord * distToChord +
                (sagitta - distanceFromChord) * (sagitta - distanceFromChord)
            );

            float halfChordAngle = (float)Math.Asin((localChordLength / 2) / currentRadius);
            float midAngle = GetMidArcAngleFinal();

            startAngle = midAngle - halfChordAngle * 180 / (float)Math.PI;
            sweepAngle = 2 * halfChordAngle * 180 / (float)Math.PI;

            startAngle = NormalizeAngle(startAngle);
        }

        private float GetMidArcAngleFinal()
        {
            float midAngle = arc.StartAngle + arc.SweepAngle / 2;
            return NormalizeAngle(midAngle);
        }

        private float CalculateAdaptiveSpacing()
        {
            float featureSize = localSagitta;
            if (featureSize <= 0) return options.Spacing;

            float adaptiveSpacing = featureSize / options.TargetLineCount;
            return Math.Max(options.MinSpacing, Math.Min(options.MaxSpacing, adaptiveSpacing));
        }

        private float GetDistance(SKPoint p1, SKPoint p2)
        {
            float dx = p1.X - p2.X;
            float dy = p1.Y - p2.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private List<SKPoint> RemoveDuplicatePoints(List<SKPoint> points)
        {
            var result = new List<SKPoint>();
            foreach (var point in points)
            {
                bool duplicate = false;
                foreach (var existing in result)
                {
                    if (Math.Abs(existing.X - point.X) < 0.01f && Math.Abs(existing.Y - point.Y) < 0.01f)
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (!duplicate)
                    result.Add(point);
            }
            return result;
        }

        private string GetSegmentKey(SKPoint p1, SKPoint p2)
        {
            float x1 = (float)Math.Round(p1.X, 2);
            float y1 = (float)Math.Round(p1.Y, 2);
            float x2 = (float)Math.Round(p2.X, 2);
            float y2 = (float)Math.Round(p2.Y, 2);

            if (x1 > x2 || (Math.Abs(x1 - x2) < 0.01f && y1 > y2))
            {
                return $"{x2:F2},{y2:F2}|{x1:F2},{y1:F2}";
            }
            return $"{x1:F2},{y1:F2}|{x2:F2},{y2:F2}";
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