using System;
using System.Collections.Generic;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.Algorithm
{
    /// <summary>
    /// 圆弧弓形区域回字形边框算法
    /// </summary>
    public class ArcChordRingAlgorithm
    {
        public class ArcParam
        {
            public SKPoint Center { get; set; }
            public float Radius { get; set; }
            public float StartAngle { get; set; }
            public float SweepAngle { get; set; }

            public ArcParam(SKPoint center, float radius, float startAngle, float sweepAngle)
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

            public float ChordLength
            {
                get
                {
                    float dx = EndPoint.X - StartPoint.X;
                    float dy = EndPoint.Y - StartPoint.Y;
                    return (float)Math.Sqrt(dx * dx + dy * dy);
                }
            }

            public SKPoint ChordDirection
            {
                get
                {
                    float dx = EndPoint.X - StartPoint.X;
                    float dy = EndPoint.Y - StartPoint.Y;
                    float len = ChordLength;
                    if (len < 0.001f) return new SKPoint(1, 0);
                    return new SKPoint(dx / len, dy / len);
                }
            }

            public SKPoint NormalToArc
            {
                get
                {
                    SKPoint chordDir = ChordDirection;
                    SKPoint normal1 = new SKPoint(-chordDir.Y, chordDir.X);
                    SKPoint normal2 = new SKPoint(chordDir.Y, -chordDir.X);

                    float midAngle = StartAngle + SweepAngle / 2;
                    float midRad = midAngle * (float)Math.PI / 180;
                    SKPoint arcDir = new SKPoint((float)Math.Cos(midRad), (float)Math.Sin(midRad));

                    float dot1 = normal1.X * arcDir.X + normal1.Y * arcDir.Y;
                    float dot2 = normal2.X * arcDir.X + normal2.Y * arcDir.Y;

                    return dot1 > dot2 ? normal1 : normal2;
                }
            }
        }

        public class RingParams
        {
            public float RingSpacing { get; set; } = 15f;
            public float StartMarginToArc { get; set; } = 5f;
            public float StartMarginToChord { get; set; } = 5f;
            public int MaxRings { get; set; } = 10;
            public float MinRadius { get; set; } = 5f;
        }

        private ArcParam arc;
        private SKPath result;

        public ArcChordRingAlgorithm(ArcParam arc)
        {
            this.arc = arc;
            this.result = new SKPath();
        }

        public SKPath GenerateRingPath(RingParams ringParams)
        {
            result.Reset();

            float currentRadius = arc.Radius;
            float currentChordOffset = 0;
            int ringIndex = 0;

            while (ringIndex < ringParams.MaxRings)
            {
                float arcOffset = (ringIndex == 0) ? ringParams.StartMarginToArc : ringParams.RingSpacing;
                float newRadius = currentRadius - arcOffset;
                if (newRadius <= ringParams.MinRadius) break;

                float chordOffset = (ringIndex == 0) ? ringParams.StartMarginToChord : ringParams.RingSpacing;
                float newChordOffset = currentChordOffset + chordOffset;

                SKPoint normal = arc.NormalToArc;
                SKPoint chordStart = new SKPoint(
                    arc.StartPoint.X + normal.X * newChordOffset,
                    arc.StartPoint.Y + normal.Y * newChordOffset
                );
                SKPoint chordEnd = new SKPoint(
                    arc.EndPoint.X + normal.X * newChordOffset,
                    arc.EndPoint.Y + normal.Y * newChordOffset
                );

                SKPoint pointA, pointB;
                if (!GetLineCircleIntersections(chordStart, chordEnd, arc.Center, newRadius, out pointA, out pointB))
                    break;

                // 获取交点在圆上的角度（原始角度，不归一化）
                float angleA = GetAngleOnCircle(pointA, arc.Center);
                float angleB = GetAngleOnCircle(pointB, arc.Center);

                // 获取圆弧的起始终止角度
                float startAngle, sweepAngle;
                if (!GetArcAngles(angleA, angleB, out startAngle, out sweepAngle))
                    break;

                // 绘制圆弧
                SKRect rect = new SKRect(
                    arc.Center.X - newRadius,
                    arc.Center.Y - newRadius,
                    arc.Center.X + newRadius,
                    arc.Center.Y + newRadius
                );
                result.AddArc(rect, startAngle, sweepAngle);

                // 绘制弦
                result.MoveTo(pointA);
                result.LineTo(pointB);

                currentRadius = newRadius;
                currentChordOffset = newChordOffset;
                ringIndex++;
            }

            return result;
        }

        /// <summary>
        /// 根据两个交点的角度，确定圆弧的起始终止角度
        /// </summary>
        private bool GetArcAngles(float angleA, float angleB, out float startAngle, out float sweepAngle)
        {
            startAngle = 0;
            sweepAngle = 0;

            float a = angleA;
            float b = angleB;
            float originalStart = arc.StartAngle;
            float originalSweep = arc.SweepAngle;
            bool isCounterClockwise = originalSweep > 0;

            if (isCounterClockwise)
            {
                // 逆时针：角度递增
                if (a < b)
                {
                    startAngle = a;
                    sweepAngle = b - a;
                }
                else
                {
                    startAngle = a;
                    sweepAngle = (360 - a) + b;
                }
                // 确保 sweepAngle 为正
                if (sweepAngle < 0) sweepAngle += 360;
            }
            else
            {
                // 顺时针：角度递减
                // 关键修复：对于顺时针圆弧，我们需要从较大的角度走到较小的角度
                if (a > b)
                {
                    startAngle = b;
                    sweepAngle = a - b;
                }
                else
                {
                    // a < b 的情况，说明圆弧跨越了0°边界
                    // 例如：从 10° 顺时针到 350°，实际路径是 10→0→350
                    startAngle = a;
                    sweepAngle = -(b - a);  // 负值表示顺时针
                                            // 但这里需要正确处理跨越边界的情况

                    // 重新计算：顺时针从 angleA 到 angleB 跨越0°时
                    // 实际扫掠角 = -( (360 - angleA) + angleB )
                    startAngle = angleA;
                    sweepAngle = -((360 - angleA) + angleB);
                }

                // 特殊情况：顺时针时，如果 a > b，但差值很大，可能也是跨越边界
                // 修正：确保 sweepAngle 为负且绝对值不超过360
                if (sweepAngle > 0) sweepAngle = -sweepAngle;

                // 如果扫掠角绝对值大于180，取另一边（较短路径）
                if (Math.Abs(sweepAngle) > 180)
                {
                    if (sweepAngle > 0)
                        sweepAngle = sweepAngle - 360;
                    else
                        sweepAngle = sweepAngle + 360;
                }
            }

            // 归一化起始角度到 0-360
            startAngle = NormalizeAngle(startAngle);

            return Math.Abs(sweepAngle) > 0.01f;
        }

        /// <summary>
        /// 求直线与圆的交点
        /// </summary>
        private bool GetLineCircleIntersections(SKPoint p1, SKPoint p2, SKPoint center, float radius,
                                                 out SKPoint i1, out SKPoint i2)
        {
            i1 = i2 = new SKPoint();

            float x1 = p1.X - center.X;
            float y1 = p1.Y - center.Y;
            float x2 = p2.X - center.X;
            float y2 = p2.Y - center.Y;

            float dx = x2 - x1;
            float dy = y2 - y1;
            float a = dx * dx + dy * dy;
            if (a < 0.0001f) return false;

            float b = 2 * (x1 * dx + y1 * dy);
            float c = x1 * x1 + y1 * y1 - radius * radius;

            float delta = b * b - 4 * a * c;
            if (delta < 0) return false;

            float sqrtDelta = (float)Math.Sqrt(delta);
            float t1 = (-b - sqrtDelta) / (2 * a);
            float t2 = (-b + sqrtDelta) / (2 * a);

            bool has1 = false, has2 = false;
            if (t1 >= 0 && t1 <= 1)
            {
                i1 = new SKPoint(p1.X + t1 * dx, p1.Y + t1 * dy);
                has1 = true;
            }
            if (t2 >= 0 && t2 <= 1 && Math.Abs(t2 - t1) > 0.0001f)
            {
                if (has1)
                {
                    i2 = new SKPoint(p1.X + t2 * dx, p1.Y + t2 * dy);
                    has2 = true;
                }
                else
                {
                    i1 = new SKPoint(p1.X + t2 * dx, p1.Y + t2 * dy);
                    has1 = true;
                }
            }

            return has1 && has2;
        }

        private float GetAngleOnCircle(SKPoint point, SKPoint center)
        {
            float dx = point.X - center.X;
            float dy = point.Y - center.Y;
            return (float)(Math.Atan2(dy, dx) * 180 / Math.PI);
        }

        private float NormalizeAngle(float angle)
        {
            angle = angle % 360;
            if (angle < 0) angle += 360;
            return angle;
        }
    }
}