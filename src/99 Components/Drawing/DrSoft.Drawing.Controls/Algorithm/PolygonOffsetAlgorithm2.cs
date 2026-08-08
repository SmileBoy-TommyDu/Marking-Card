using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.Algorithm
{
    public class PolygonOffsetAlgorithm2
    {
        public enum FillDirection
        {
            Inward,
            Outward
        }

        public class FillOptions
        {
            public float Spacing { get; set; } = 10f;
            public FillDirection Direction { get; set; } = FillDirection.Inward;
            public float MinSize { get; set; } = 1f;
            public bool ClosePath { get; set; } = true;
            public bool FillCenter { get; set; } = true;
        }

        #region 公共接口方法


        #endregion

        #region 矩形填充


        #endregion

        #region 多边形填充




        private static List<SKPoint> ShrinkPolygonByEdgeTranslation(List<SKPoint> vertices, float offset)
        {
            int n = vertices.Count;
            if (n < 3) return new List<SKPoint>();

            var newEdges = new List<Tuple<SKPoint, SKPoint>>();

            for (int i = 0; i < n; i++)
            {
                var p1 = vertices[i];
                var p2 = vertices[(i + 1) % n];

                var edge = new SKPoint(p2.X - p1.X, p2.Y - p1.Y);
                float edgeLen = (float)Math.Sqrt(edge.X * edge.X + edge.Y * edge.Y);

                if (edgeLen < 0.0001f) continue;

                var normal = GetInwardNormal(vertices, i, edge, edgeLen);

                var newP1 = new SKPoint(p1.X + normal.X * offset, p1.Y + normal.Y * offset);
                var newP2 = new SKPoint(p2.X + normal.X * offset, p2.Y + normal.Y * offset);

                newEdges.Add(new Tuple<SKPoint, SKPoint>(newP1, newP2));
            }

            if (newEdges.Count < 3) return new List<SKPoint>();

            var newVertices = new List<SKPoint>();
            for (int i = 0; i < newEdges.Count; i++)
            {
                var edge1 = newEdges[i];
                var edge2 = newEdges[(i + 1) % newEdges.Count];

                var intersection = GetLineIntersection(
                    edge1.Item1, edge1.Item2,
                    edge2.Item1, edge2.Item2
                );

                if (intersection.HasValue)
                {
                    newVertices.Add(intersection.Value);
                }
            }

            return CleanAndValidatePolygon(newVertices);
        }

        private static SKPoint GetInwardNormal(List<SKPoint> vertices, int index, SKPoint edge, float edgeLen)
        {
            var p1 = vertices[index];
            var p2 = vertices[(index + 1) % vertices.Count];

            var dir = new SKPoint(edge.X / edgeLen, edge.Y / edgeLen);

            var normal1 = new SKPoint(-dir.Y, dir.X);
            var normal2 = new SKPoint(dir.Y, -dir.X);

            float midX = (p1.X + p2.X) / 2;
            float midY = (p1.Y + p2.Y) / 2;
            var midPoint = new SKPoint(midX, midY);

            var testPoint1 = new SKPoint(midPoint.X + normal1.X, midPoint.Y + normal1.Y);
            var testPoint2 = new SKPoint(midPoint.X + normal2.X, midPoint.Y + normal2.Y);

            bool isInside1 = IsPointInPolygon(testPoint1, vertices);
            bool isInside2 = IsPointInPolygon(testPoint2, vertices);

            return isInside1 ? normal1 : normal2;
        }

        private static bool IsPointInPolygon(SKPoint point, List<SKPoint> polygon)
        {
            bool inside = false;
            int n = polygon.Count;

            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var pi = polygon[i];
                var pj = polygon[j];

                bool intersect = ((pi.Y > point.Y) != (pj.Y > point.Y)) &&
                    (point.X < (pj.X - pi.X) * (point.Y - pi.Y) / (pj.Y - pi.Y) + pi.X);

                if (intersect) inside = !inside;
            }
            return inside;
        }

        private static SKPoint? GetLineIntersection(SKPoint p1, SKPoint p2, SKPoint p3, SKPoint p4)
        {
            float denominator = (p1.X - p2.X) * (p3.Y - p4.Y) - (p1.Y - p2.Y) * (p3.X - p4.X);

            if (Math.Abs(denominator) < 0.0001f)
                return null;

            float t = ((p1.X - p3.X) * (p3.Y - p4.Y) - (p1.Y - p3.Y) * (p3.X - p4.X)) / denominator;
            float x = p1.X + t * (p2.X - p1.X);
            float y = p1.Y + t * (p2.Y - p1.Y);

            return new SKPoint(x, y);
        }

        private static List<SKPoint> CleanAndValidatePolygon(List<SKPoint> vertices)
        {
            if (vertices.Count < 3) return new List<SKPoint>();

            var result = new List<SKPoint>();
            float epsilon = 0.01f;

            for (int i = 0; i < vertices.Count; i++)
            {
                var curr = vertices[i];
                if (float.IsNaN(curr.X) || float.IsNaN(curr.Y) ||
                    float.IsInfinity(curr.X) || float.IsInfinity(curr.Y))
                    continue;
                result.Add(curr);
            }

            if (result.Count < 3) return new List<SKPoint>();

            var finalResult = new List<SKPoint>();
            for (int i = 0; i < result.Count; i++)
            {
                var curr = result[i];
                var next = result[(i + 1) % result.Count];

                float dx = curr.X - next.X;
                float dy = curr.Y - next.Y;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy);

                if (dist > epsilon)
                {
                    finalResult.Add(curr);
                }
            }

            return finalResult.Count >= 3 ? finalResult : new List<SKPoint>();
        }

        #endregion

        #region 回字形填充

        private static int FillRectangularRingInward(List<SKPoint> outerVertices, List<SKPoint> innerVertices, SKPath path, FillOptions options, List<List<PointF>> fillRings)
        {
            if (outerVertices.Count < 3 || innerVertices.Count < 3) return 0;

            var currentOuter = new List<SKPoint>(outerVertices);
            var currentInner = new List<SKPoint>(innerVertices);
            int circleCount = 0;

            // 先添加原始回字形
            AddPolygonToPath(currentOuter, path, options.ClosePath);
            AddPolygonToPath(currentInner, path, options.ClosePath);
            AddPolygonRingToResult(currentOuter, fillRings);
            AddPolygonRingToResult(currentInner, fillRings);

            while (true)
            {
                if (currentOuter.Count < 3 || currentInner.Count < 3)
                    break;

                var newOuter = ShrinkPolygonByEdgeTranslation(currentOuter, options.Spacing);
                var newInner = ExpandPolygonByEdgeTranslation(currentInner, options.Spacing);

                if (newOuter.Count < 3 || newInner.Count < 3)
                    break;

                float outerArea = GetPolygonArea(newOuter);
                float innerArea = GetPolygonArea(newInner);

                // 检查内外边框是否交叉
                if (outerArea <= innerArea + options.MinSize * options.MinSize)
                {
                    // 填充中间剩余区域
                    FillRemainingRingArea(currentOuter, currentInner, newOuter, newInner, path, options, fillRings);
                    break;
                }

                AddPolygonToPath(newOuter, path, options.ClosePath);
                AddPolygonToPath(newInner, path, options.ClosePath);
                AddPolygonRingToResult(newOuter, fillRings);
                AddPolygonRingToResult(newInner, fillRings);

                currentOuter = newOuter;
                currentInner = newInner;
                circleCount++;

                if (circleCount > 500)
                    break;
            }
            return circleCount;
        }

        /// <summary>
        /// 填充内外边框之间的剩余区域（渐进式填充，不留空白）
        /// </summary>
        private static void FillRemainingRingArea(List<SKPoint> oldOuter, List<SKPoint> oldInner,
            List<SKPoint> newOuter, List<SKPoint> newInner, SKPath path, FillOptions options, List<List<PointF>> fillRings)
        {
            if (oldOuter.Count < 3 || oldInner.Count < 3) return;

            // 计算中心点
            var center = GetPolygonCenter(oldOuter);

            // 计算原始外边框和内边框的平均半径
            float oldOuterRadius = GetAverageRadius(oldOuter, center);
            float oldInnerRadius = GetAverageRadius(oldInner, center);
            float newOuterRadius = GetAverageRadius(newOuter, center);
            float newInnerRadius = GetAverageRadius(newInner, center);

            // 计算总共需要填充的层数
            float totalSteps = Math.Max(1, (oldOuterRadius - oldInnerRadius) / options.Spacing);
            float step = (oldOuterRadius - oldInnerRadius) / totalSteps;

            float currentRadius = oldOuterRadius;

            // 从外向内渐进填充，确保没有空白
            for (int i = 1; i < totalSteps; i++)
            {
                currentRadius -= step;

                if (currentRadius <= oldInnerRadius + step)
                    break;

                // 计算当前半径对应的缩放比例
                float scale = currentRadius / oldOuterRadius;
                var midLayer = ScalePolygon(oldOuter, center, scale);

                if (midLayer.Count >= 3 && GetPolygonArea(midLayer) > options.MinSize * options.MinSize)
                {
                    // 检查这个中间层是否已经添加过（避免重复）
                    bool alreadyAdded = false;
                    if (fillRings.Count > 0)
                    {
                        var lastRing = fillRings.Last();
                        if (lastRing.Count == midLayer.Count)
                        {
                            float diff = 0;
                            for (int j = 0; j < midLayer.Count; j++)
                            {
                                diff += Math.Abs(lastRing[j].X - midLayer[j].X) + Math.Abs(lastRing[j].Y - midLayer[j].Y);
                            }
                            if (diff < 0.1f)
                                alreadyAdded = true;
                        }
                    }

                    if (!alreadyAdded)
                    {
                        AddPolygonToPath(midLayer, path, options.ClosePath);
                        AddPolygonRingToResult(midLayer, fillRings);
                    }
                }
            }

            // 添加内边框作为最后一层
            if (GetPolygonArea(oldInner) > options.MinSize * options.MinSize)
            {
                AddPolygonToPath(oldInner, path, options.ClosePath);
                AddPolygonRingToResult(oldInner, fillRings);
            }
        }

        /// <summary>
        /// 计算多边形顶点到中心点的平均半径
        /// </summary>
        private static float GetAverageRadius(List<SKPoint> vertices, SKPoint center)
        {
            if (vertices.Count == 0) return 0;
            float sum = 0;
            foreach (var v in vertices)
            {
                sum += Distance(v, center);
            }
            return sum / vertices.Count;
        }

        /// <summary>
        /// 缩放多边形
        /// </summary>
        private static List<SKPoint> ScalePolygon(List<SKPoint> vertices, SKPoint center, float scale)
        {
            var result = new List<SKPoint>();
            foreach (var v in vertices)
            {
                var scaled = new SKPoint(
                    center.X + (v.X - center.X) * scale,
                    center.Y + (v.Y - center.Y) * scale
                );
                result.Add(scaled);
            }
            return result;
        }

        private static List<SKPoint> ExpandPolygonByEdgeTranslation(List<SKPoint> vertices, float offset)
        {
            int n = vertices.Count;
            if (n < 3) return new List<SKPoint>();

            var newEdges = new List<Tuple<SKPoint, SKPoint>>();

            for (int i = 0; i < n; i++)
            {
                var p1 = vertices[i];
                var p2 = vertices[(i + 1) % n];

                var edge = new SKPoint(p2.X - p1.X, p2.Y - p1.Y);
                float edgeLen = (float)Math.Sqrt(edge.X * edge.X + edge.Y * edge.Y);

                if (edgeLen < 0.0001f) continue;

                var normal = GetInwardNormal(vertices, i, edge, edgeLen);
                var outwardNormal = new SKPoint(-normal.X, -normal.Y);

                var newP1 = new SKPoint(p1.X + outwardNormal.X * offset, p1.Y + outwardNormal.Y * offset);
                var newP2 = new SKPoint(p2.X + outwardNormal.X * offset, p2.Y + outwardNormal.Y * offset);

                newEdges.Add(new Tuple<SKPoint, SKPoint>(newP1, newP2));
            }

            if (newEdges.Count < 3) return new List<SKPoint>();

            var newVertices = new List<SKPoint>();
            for (int i = 0; i < newEdges.Count; i++)
            {
                var edge1 = newEdges[i];
                var edge2 = newEdges[(i + 1) % newEdges.Count];

                var intersection = GetLineIntersection(
                    edge1.Item1, edge1.Item2,
                    edge2.Item1, edge2.Item2
                );

                if (intersection.HasValue)
                {
                    newVertices.Add(intersection.Value);
                }
            }

            return CleanAndValidatePolygon(newVertices);
        }

        #endregion

        #region 辅助方法
        private static float GetPolygonArea(List<SKPoint> vertices)
        {
            if (vertices.Count < 3) return 0;
            float area = 0;
            for (int i = 0; i < vertices.Count; i++)
            {
                var curr = vertices[i];
                var next = vertices[(i + 1) % vertices.Count];
                area += curr.X * next.Y - next.X * curr.Y;
            }
            return Math.Abs(area) / 2;
        }

        private static SKPoint GetPolygonCenter(List<SKPoint> vertices)
        {
            if (vertices.Count == 0) return new SKPoint(0, 0);
            float sumX = 0, sumY = 0;
            foreach (var v in vertices)
            {
                sumX += v.X;
                sumY += v.Y;
            }
            return new SKPoint(sumX / vertices.Count, sumY / vertices.Count);
        }

        private static float Distance(SKPoint p1, SKPoint p2)
        {
            float dx = p1.X - p2.X;
            float dy = p1.Y - p2.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private static void AddPolygonRingToResult(List<SKPoint> vertices, List<List<PointF>> fillRings)
        {
            if (fillRings == null || vertices.Count < 3) return;
            var ring = new List<PointF>();
            foreach (var v in vertices)
            {
                ring.Add(new PointF(v.X, v.Y));
            }
            ring.Add(new PointF(vertices[0].X, vertices[0].Y));
            fillRings.Add(ring);
        }

        private static void AddPolygonToPath(List<SKPoint> vertices, SKPath path, bool close)
        {
            if (vertices.Count < 3) return;
            path.MoveTo(vertices[0]);
            for (int i = 1; i < vertices.Count; i++)
            {
                path.LineTo(vertices[i]);
            }
            if (close)
                path.Close();
        }

        #endregion
    }
}