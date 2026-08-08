using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using DrSoft.Drawing.Controls.Algorithm;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Controls.Rendering;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using SkiaSharp;
using static DrSoft.Drawing.Controls.Rendering.HatchRenderHelper;

namespace DrSoft.Drawing.Controls.DrawShapes
{
    /// <summary>
    /// 贝塞尔曲线图形
    /// </summary>
    public class DrawBezier : DrawObject, IHatchable, IClosable, IBezierShapeData
    {
        // ── IBezierShapeData：ControlPoints 代理到 Points（SKPoint → (float X, float Y)）────
        IReadOnlyList<(float X, float Y)> IBezierShapeData.ControlPoints =>
            Points.Select(p => (p.X, p.Y)).ToArray();
        // CenterX/CenterY/ChildShapes 由基类处理

        public bool IsClosed { get; set; } = false;

        // 存储局部坐标的点（相对于 SharpCenter）
        private List<SKPoint> _localPoints = null;

        // 基准尺寸，用于控制点拖动时的比例缩放


        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="points">控制点列表</param>
        public DrawBezier() : base()
        {
            UId = UniqueIdGenerator.NextId();
            Points = new List<SKPoint>();
            Type = ShapeType.Bezier;
        }

        public DrawBezier(List<SKPoint> points) : this()
        {
            if (points == null)
                return;

            Points = new List<SKPoint>(points);
            if (Points.Count >= 2)
            {
                UpdateSetProperty(Points);
            }
        }
        public DrawBezier(List<Point2D> points) : this(points.Select(p => new SKPoint(p.X, p.Y)).ToList()) { }

        /// <summary>
        /// 控制点列表
        /// </summary>
        public IReadOnlyList<SKPoint> ControlPoints => Points.AsReadOnly();

        public override List<Point2D> OutlinePoints
        {
            get
            {
                // 使用 GetWorldAnchorPoints()（包含旋转/缩放变换的世界坐标）插值，
                // 而不是直接用 Points（仅含平移，不含旋转/缩放），
                // 确保导出和打标数据包含完整变换信息。
                var worldPoints = GetWorldAnchorPoints();
                return CurveInterpolation.FlattenPath(worldPoints).Select(it => new Point2D(it.X, it.Y)).ToList();
            }
            set => throw new NotImplementedException();
        }

        internal override List<IShape> CreateCurveChildren()
        {
            var children = new List<IShape>();

            if (Points == null || Points.Count < 2)
                return children;

            using var localPath = GetPath();
            if (localPath == null || localPath.IsEmpty)
                return children;

            using var worldPath = new SKPath();
            worldPath.AddPath(localPath, GetTransformMatrix());

            var points = SampleWorldPathToPolylinePoints(worldPath);
            if (points.Count < 2)
                return children;

            var polyLine = new DrawPolyLines(points)
            {
                IsClosed = IsClosed,
                Pen = Pen,
                Name = $"{Name}_折线"
            };
            children.Add(polyLine);
            return children;
        }

        public override bool HitTest(SKPoint p, float tol = 6.0f)
        {
            if (Points.Count < 2) return false;

            return base.HitTest(p, tol);
        }

        public override bool IntersectsWith(SKRect rect)
        {
            return true;
        }

        internal override void ReverseDirection()
        {
            if (_localPoints != null && _localPoints.Count >= 2)
            {
                _localPoints.Reverse();
                SyncWorldPointsFromMatrix();
                return;
            }

            Points.Reverse();
            UpdateSetProperty(Points);
        }

        protected override void OnCommittedMatrixChanged()
        {
            SyncWorldPointsFromMatrix();
        }

        public override IShape Clone()
        {
            var clone = new DrawBezier()
            {
                IsClosed = IsClosed,
                HatchParamInfo = HatchParamInfo,
            };

            if (Points != null)
            {
                clone.Points = new List<SKPoint>(Points);
            }

            if (_localPoints != null)
            {
                clone._localPoints = new List<SKPoint>(_localPoints.Count);
                foreach (var point in _localPoints)
                {
                    clone._localPoints.Add(new SKPoint(point.X, point.Y));
                }
            }

            return FinalizeClone(clone);
        }

        public override void UpdateSetProperty(List<SKPoint> worldPoints)
        {
            Points = worldPoints ?? new List<SKPoint>();
            Type = ShapeType.Bezier;

            if (Points.Count < 2)
            {
                _localPoints = new List<SKPoint>();
                return;
            }

            if (HasCommittedMatrix())
            {
                var inverse = Matrix.Invert();
                _localPoints = new List<SKPoint>(Points.Count);
                foreach (var point in Points)
                {
                    _localPoints.Add(inverse.MapPoint(point));
                }
                return;
            }

            var center = GetPointsCenter(Points);
            _localPoints = new List<SKPoint>(Points.Count);
            foreach (var point in Points)
            {
                _localPoints.Add(new SKPoint(point.X - center.X, point.Y - center.Y));
            }
        }

        public List<SKPoint> GetWorldAnchorPoints()
        {
            if (_localPoints == null || _localPoints.Count == 0)
            {
                if (Points == null || Points.Count == 0) return new List<SKPoint>();
                UpdateSetProperty(Points);
            }

            if (!HasCommittedMatrix())
                return new List<SKPoint>(Points);

            return _localPoints!.Select(p => Matrix.MapPoint(p)).ToList();
        }
        internal override List<(SKPoint P1, SKPoint P2)> SamplePathToSegments(float step = 0.5f)
        {
            var result = new List<(SKPoint, SKPoint)>();
            try
            {
                using var localPath = GetPath();
                if (localPath == null || localPath.IsEmpty)
                    return result;

                // 将局部路径变换到世界坐标，确保跳点检测时与 IntersectionSkipPoints 坐标系一致
                var matrix = GetTransformMatrix();
                using var worldPath = new SKPath(localPath);
                worldPath.Transform(matrix);

                using var measure = new SKPathMeasure(worldPath, false, 1f);
                do
                {
                    float length = measure.Length;
                    if (length <= 0) continue;

                    int count = Math.Max(2, (int)Math.Ceiling(length / step) + 1);
                    SKPoint prev = SKPoint.Empty;

                    for (int i = 0; i < count; i++)
                    {
                        float d = length * i / (count - 1);
                        if (!measure.GetPosition(d, out var pos)) continue;
                        if (i > 0) result.Add((prev, pos));
                        prev = pos;
                    }
                } while (measure.NextContour());
            }
            catch
            {
            }

            return result;
        }

        public override SKPath GetPath()
        {
            var path = new SKPath();
            FillPath(path);
            return path;
        }

        protected override void FillPath(SKPath path)
        {
            if (_localPoints == null || _localPoints.Count == 0)
                return;

            // 直接用 _localPoints 构建 Catmull-Rom 路径（局部坐标），
            // 渲染时由 GetTransformMatrix() 变换到世界坐标。
            // 不在 GetPath/FillPath 内部做 Width/Height 缩放——
            // 缩放由 OnPropertyChanged 在 Width/Height 变化时同步更新 _localPoints 处理。
            // 这样确保旋转围绕原点（=SharpCenter）正确旋转，不会偏移。
            CurveInterpolation.FillCatmullRomPath(path, _localPoints);

            if (IsClosed && _localPoints.Count >= 2)
            {
                path.Close();
            }
        }

        private void SyncWorldPointsFromMatrix()
        {
            if (_localPoints == null || _localPoints.Count == 0)
                return;

            var worldPoints = new List<SKPoint>(_localPoints.Count);
            foreach (var point in _localPoints)
            {
                worldPoints.Add(Matrix.MapPoint(point));
            }

            Points = worldPoints;
        }

        private static SKPoint GetPointsCenter(IReadOnlyList<SKPoint> points)
        {
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;

            for (int i = 0; i < points.Count; i++)
            {
                var point = points[i];
                if (point.X < minX) minX = point.X;
                if (point.X > maxX) maxX = point.X;
                if (point.Y < minY) minY = point.Y;
                if (point.Y > maxY) maxY = point.Y;
            }

            return new SKPoint((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
        }

        private bool HasCommittedMatrix()
        {
            return !Matrix.Equals(SKMatrix.Identity);
        }
        //public override SKRect GetLocalBounds()
        //{
        //    if ((_localPoints == null || _localPoints.Count < 2) && Points?.Count >= 2)
        //    {
        //        UpdateSetProperty(Points);
        //    }

        //    using var path = GetPath();
        //    if (path == null || path.IsEmpty)
        //        return base.GetLocalBounds();

        //    return path.TightBounds;
        //}

        #region 填充
        public HatchParamDto HatchParamInfo { get; set; }
        public List<DrawObject> ExpandHatchObject(List<(SKPoint Start, SKPoint End)> hatchLineObjects)
        {
            if (HatchParamInfo == null) throw new ArgumentNullException("填充参数为null！");
            List<DrawObject> result = new List<DrawObject>();
            switch (HatchParamInfo.FillTypeIndex)
            {
                case 0:
                    throw new Exception("实线无需解析！");
                case 1:
                    result.AddRange(HatchRenderHelper.ExpandToDashGeometry(DashRenderType.Dash, hatchLineObjects,
                        HatchRenderHelper.GetDashParameters(HatchParamInfo.FillTypeIndex), SKColor.Parse(HatchParamInfo.FillColor), Name));
                    break;

                case 2:
                    result.AddRange(HatchRenderHelper.ExpandToDashGeometry(DashRenderType.Dot, hatchLineObjects,
                   HatchRenderHelper.GetDashParameters(HatchParamInfo.FillTypeIndex), SKColor.Parse(HatchParamInfo.FillColor), Name));
                    break;
            }

            return result;
        }

        // 填充
        //public HatchPatternObjects CreateHatchPattern()
        //{
        //    if (HatchParamInfo == null) return new HatchPatternObjects();

        //    // 1. 获取基础数据（Extension / ReverseFillLine 已在 GetFillLines 内部处理）
        //    var fillLines = GetFillLines(HatchParamInfo);

        //    var drawObjects = GetConvertObjects(fillLines, HatchParamInfo);
        //    return new HatchPatternObjects { HatchObjects = drawObjects };
        //}

        public HatchPatternObjects CreateHatchPattern()
        {
            if (HatchParamInfo == null) return new HatchPatternObjects();

            // 1. 获取基础数据（Extension / ReverseFillLine 已在 GetFillLines 内部处理）
            var fillLines = GetFillLines(HatchParamInfo);

            var drawObjects = FillLineStyleEmitter.Convert3(fillLines, HatchParamInfo, Name);
            return new HatchPatternObjects
            {
                HatchObjects = drawObjects,
                HatchLineObjects = fillLines,
            };
        }

        /// <summary>
        /// 获取填充线段。返回的线段在**本地坐标系**
        /// 中心为原点，x∈[-W/2, W/2]，y∈[-H/2, H/2]（Y 轴向上）。
        /// 根据 FillTypeIndex 分发到不同的填充算法。
        /// </summary>
        public List<(SKPoint Start, SKPoint End)> GetFillLines(HatchParamDto hatchInfo)
        {
            var result = new List<(SKPoint, SKPoint)>();
            if (hatchInfo.LineSpacing <= 0)
                return result;

            // 确保 _localPoints 已初始化
            if (_localPoints == null || _localPoints.Count < 2)
            {
                if (Points == null || Points.Count < 2)
                    return result;
                UpdateSetProperty(Points);
                if (_localPoints == null || _localPoints.Count < 2)
                    return result;
            }

            return hatchInfo.FillTypeIndex switch
            {
                0 => GetScanlineFillLines(hatchInfo),      // S型单向 / 弓字型双向 / 优化弓字
                1 => GetScanlineFillLines(hatchInfo),      // S型单向 / 弓字型双向 / 优化弓字
                2 => GetConcentricFillLines(hatchInfo),   // 回字形
                3 => GetSpiralFillLines(hatchInfo),
                _ => new List<(SKPoint, SKPoint)>(),      // 其他
            };
        }
        /// <summary>
        /// 获取填充线段。将贝塞尔曲线离散为多边形后，使用扫描线算法生成填充线。
        /// 返回的线段在**局部坐标系**中（相对于 SharpCenter）。
        /// 使用与 GetPath() 一致的缩放比例，确保填充线与曲线轮廓几何对齐。
        /// </summary>
        public List<(SKPoint Start, SKPoint End)> GetScanlineFillLines(HatchParamDto hatchInfo)
        {
            var result = new List<(SKPoint, SKPoint)>();

            // 确保 _localPoints 已初始化
            if (_localPoints == null || _localPoints.Count < 2)
            {
                if (Points == null || Points.Count < 2)
                    return result;
                //UpdateSetProperty(Points);
                if (_localPoints == null || _localPoints.Count < 2)
                    return result;
            }

            var points = Matrix.MapPoints(_localPoints.ToArray());
            // _localPoints 已在 OnPropertyChanged 中同步缩放，直接使用
            var flattenedPoints = CurveInterpolation.FlattenPath(points);
            //var flattenedPoints = CurveInterpolation.FlattenPath(_localPoints);
            if (flattenedPoints.Count < 3)
                return result;

            // 离散点已在局部坐标系中（相对于中心点），无需再减去 SharpCenter
            var localPoints = flattenedPoints.Select(p => new SKPoint(p.X, p.Y)).ToArray();

            // 直接在原多边形（未闭合曲线隐含首尾连线）上做扫描线填充，
            // 通过"到每条边的距离≥margin"的约束来保证填充区域处处与曲线保持
            // margin 的偏移，同时对自相交、尖角、极值等情形均鲁棒。
            result.AddRange(GenerateScanlineFill(localPoints, hatchInfo));
            return result;
        }

        private List<(SKPoint Start, SKPoint End)> GetConcentricFillLines(HatchParamDto info)
        {
            return new List<(SKPoint Start, SKPoint End)>();
        }

        private List<(SKPoint Start, SKPoint End)> GetSpiralFillLines(HatchParamDto info)
        {
            return new List<(SKPoint Start, SKPoint End)>();
        }

        /// <summary>
        /// 在多边形上做水平扫描线填充（使用 odd-even 规则），并对每条扫描线的
        /// 每个填充段执行“到边距离≥margin”的约束裁剪：减去所有多边形边的
        /// margin-胶囊（capsule）与扫描线交集组成的禁区区间并集。
        /// 该方法不依赖多边形偏移，对高曲率极值、尖角、自相交点均天然鲁棒。
        /// </summary>
        private List<(SKPoint Start, SKPoint End)> GenerateScanlineFill(SKPoint[] polygon, HatchParamDto hatchInfo)
        {
            var result = new List<(SKPoint, SKPoint)>();
            if (hatchInfo.LineSpacing <= 0 || polygon.Length < 3)
                return result;

            float margin = (float)hatchInfo.Margin;
            // Extension 延伸（沿填充线方向两端各延长 extension；负值收缩，<=0 丢弃）
            // ReverseFillLine 全局反向
            float extension = (float)hatchInfo.Extension;
            bool reverseAll = hatchInfo.ReverseFillLine;
            bool relativeToAngle = hatchInfo.RelativeToAngle;
            // 旋转多边形使填充方向水平
            //double rad = -hatchInfo.StartAngle * Math.PI / 180.0;
            //double rad = -(relativeToAngle ? hatchInfo.StartAngle : hatchInfo.StartAngle + Rotation) * Math.PI / 180.0;
            //double rad = -(relativeToAngle ? hatchInfo.StartAngle : hatchInfo.StartAngle - Rotation) * Math.PI / 180.0;
            double rad = -(relativeToAngle ? hatchInfo.StartAngle + Rotation : hatchInfo.StartAngle) * Math.PI / 180.0;
            double cos = Math.Cos(rad), sin = Math.Sin(rad);
            int n = polygon.Length;
            var rotated = new SKPoint[n];
            for (int i = 0; i < n; i++)
            {
                rotated[i] = new SKPoint(
                    (float)(polygon[i].X * cos - polygon[i].Y * sin),
                    (float)(polygon[i].X * sin + polygon[i].Y * cos));
            }

            float minY = rotated.Min(p => p.Y);
            float maxY = rotated.Max(p => p.Y);

            // AverageDistribute ：将 LineSpacing 作为目标值，重算间距使扫描线在 [minY, maxY]
            // 区间均等分布；将 span 平均分成 nGaps 份，生成 nGaps-1 条填充线，
            // 使“边界→首线 / 线间 / 尾线→边界”的间距全部相等 = span / nGaps
            float spacing = (float)hatchInfo.LineSpacing;
            float startOffset = spacing / 2f;
            float yLimit = maxY;
            if (hatchInfo.AverageDistribute && maxY > minY)
            {
                float span = maxY - minY;
                int nGaps = Math.Max(2, (int)Math.Round(span / spacing));
                spacing = span / nGaps;
                startOffset = spacing;
                yLimit = maxY - spacing * 0.5f;
            }

            double cosBack = Math.Cos(-rad), sinBack = Math.Sin(-rad);
            var xs = new List<float>(16);
            var forbidden = new List<(float Start, float End)>(n);
            int lineIndex = 0;
            for (float y = minY + startOffset; y < yLimit; y += spacing, lineIndex++)
            {
                // 1) 求扫描线与多边形边的交点，odd-even 配对得到实心填充段
                xs.Clear();
                for (int i = 0; i < n; i++)
                {
                    var p1 = rotated[i];
                    var p2 = rotated[(i + 1) % n];
                    if ((p1.Y <= y && p2.Y > y) || (p2.Y <= y && p1.Y > y))
                    {
                        float t = (y - p1.Y) / (p2.Y - p1.Y);
                        xs.Add(p1.X + t * (p2.X - p1.X));
                    }
                }
                if (xs.Count < 2) continue;
                xs.Sort();

                // 2) 求扫描线与每条边 margin-胶囊的 x 区间并集（禁区）
                forbidden.Clear();
                if (margin > 0)
                {
                    for (int i = 0; i < n; i++)
                    {
                        var p1 = rotated[i];
                        var p2 = rotated[(i + 1) % n];
                        if (TrySegmentCapsuleXRange(p1.X, p1.Y, p2.X, p2.Y, y, margin, out float fMin, out float fMax))
                        {
                            forbidden.Add((fMin, fMax));
                        }
                    }
                    // 按起点排序并合并重叠区间
                    if (forbidden.Count > 1)
                    {
                        forbidden.Sort((a, b) => a.Start.CompareTo(b.Start));
                        int w = 0;
                        for (int r = 1; r < forbidden.Count; r++)
                        {
                            if (forbidden[r].Start <= forbidden[w].End)
                            {
                                if (forbidden[r].End > forbidden[w].End)
                                    forbidden[w] = (forbidden[w].Start, forbidden[r].End);
                            }
                            else
                            {
                                w++;
                                forbidden[w] = forbidden[r];
                            }
                        }
                        forbidden.RemoveRange(w + 1, forbidden.Count - w - 1);
                    }
                }

                // 3) 从每个实心填充段中减去禁区，输出剩余的子区间
                for (int i = 0; i + 1 < xs.Count; i += 2)
                {
                    float segStart = xs[i];
                    float segEnd = xs[i + 1];
                    if (segEnd <= segStart) continue;

                    float cur = segStart;
                    for (int k = 0; k < forbidden.Count; k++)
                    {
                        var (fs, fe) = forbidden[k];
                        if (fe <= cur) continue;
                        if (fs >= segEnd) break;
                        if (fs > cur)
                        {
                            AddRotatedLine(result, cur, fs, y, cosBack, sinBack, extension, reverseAll);
                        }
                        if (fe > cur) cur = fe;
                        if (cur >= segEnd) break;
                    }
                    if (cur < segEnd)
                    {
                        AddRotatedLine(result, cur, segEnd, y, cosBack, sinBack, extension, reverseAll);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 将旋转坐标系下的线段 (x1..x2, y) 反旋转回局部坐标系并追加到结果中。
        /// 同时在旋转系内应用 Extension 延伸（沿 x 轴两端各延长 extension，负值收缩，<=0 丢弃）与 ReverseFillLine 全局反向。
        /// </summary>
        private void AddRotatedLine(List<(SKPoint Start, SKPoint End)> result,
                                    float x1, float x2, float y,
                                    double cosBack, double sinBack,
                                    float extension, bool reverseLine)
        {
            if (x2 <= x1) return;
            if (extension != 0f)
            {
                x1 -= extension;
                x2 += extension;
                if (x2 <= x1) return;
            }
            var s = new SKPoint(
                (float)(x1 * cosBack - y * sinBack),
                (float)(x1 * sinBack + y * cosBack));
            var e = new SKPoint(
                (float)(x2 * cosBack - y * sinBack),
                (float)(x2 * sinBack + y * cosBack));
            if (reverseLine)
                result.Add((e, s));
            else
                result.Add((s, e));
        }

        /// <summary>
        /// 计算水平扫描线 y = y 与线段 P1P2 的 margin-胶囊（capsule）的交集 x 区间。
        /// 胶囊即线段与半径为 margin 的圆盘的 Minkowski 和，是凸集，因此与任意直线的
        /// 交集必为单个区间。返回 true 表示有交，并输出 [xMin, xMax]；返回 false 表示不相交。
        /// </summary>
        private bool TrySegmentCapsuleXRange(float p1x, float p1y, float p2x, float p2y,
                                             float y, float margin,
                                             out float xMin, out float xMax)
        {
            xMin = float.MaxValue;
            xMax = float.MinValue;
            bool any = false;

            float dx = p2x - p1x;
            float dy = p2y - p1y;
            float L2 = dx * dx + dy * dy;

            // 退化：线段就是单点
            if (L2 < 1e-12f)
            {
                float ddy0 = y - p1y;
                if (Math.Abs(ddy0) > margin) return false;
                float dd0 = (float)Math.Sqrt(margin * margin - ddy0 * ddy0);
                xMin = p1x - dd0;
                xMax = p1x + dd0;
                return true;
            }

            float L = (float)Math.Sqrt(L2);

            // 端点 P1 处的半圆帽与扫描线交集
            {
                float ddy = y - p1y;
                if (Math.Abs(ddy) <= margin)
                {
                    float dd = (float)Math.Sqrt(margin * margin - ddy * ddy);
                    if (p1x - dd < xMin) xMin = p1x - dd;
                    if (p1x + dd > xMax) xMax = p1x + dd;
                    any = true;
                }
            }

            // 端点 P2 处的半圆帽与扫描线交集
            {
                float ddy = y - p2y;
                if (Math.Abs(ddy) <= margin)
                {
                    float dd = (float)Math.Sqrt(margin * margin - ddy * ddy);
                    if (p2x - dd < xMin) xMin = p2x - dd;
                    if (p2x + dd > xMax) xMax = p2x + dd;
                    any = true;
                }
            }

            // 线段中间部分的垂直条带（沿边法线的 margin 侧向威延，并受限于垂直投影参数 t∈[0,1]）
            // 单位法线 (nx, ny) = (-dy/L, dx/L)
            // 垂直有符号距离约束：|(x-p1x)*nx + (y-p1y)*ny| ≤ margin
            // 投影参数约束：t = ((x-p1x)*dx + (y-p1y)*dy) / L² ∈ [0, 1]
            //                 ⇔ (x-p1x)*dx ∈ [-(y-p1y)*dy, L² - (y-p1y)*dy]
            {
                float nx = -dy / L;
                float B = (y - p1y) * (dx / L);

                float stripMin, stripMax;
                bool stripActive = true;
                if (Math.Abs(nx) < 1e-9f)
                {
                    // 近水平边：条带条件简化为 |y-p1y| ≤ margin
                    if (Math.Abs(y - p1y) > margin) stripActive = false;
                    stripMin = float.MinValue;
                    stripMax = float.MaxValue;
                }
                else
                {
                    float s1 = (-margin - B) / nx;
                    float s2 = (margin - B) / nx;
                    stripMin = Math.Min(s1, s2);
                    stripMax = Math.Max(s1, s2);
                }

                if (stripActive)
                {
                    float tMin, tMax;
                    if (Math.Abs(dx) < 1e-9f)
                    {
                        // 近竖直边：t∈[0,1] 对 x 无约束，但需 y 在 [p1y, p2y] 之间
                        float yMn = Math.Min(p1y, p2y);
                        float yMx = Math.Max(p1y, p2y);
                        if (y < yMn || y > yMx) stripActive = false;
                        tMin = float.MinValue;
                        tMax = float.MaxValue;
                    }
                    else
                    {
                        float yDyTerm = (y - p1y) * dy;
                        float t1 = -yDyTerm / dx;
                        float t2 = (L2 - yDyTerm) / dx;
                        tMin = Math.Min(t1, t2);
                        tMax = Math.Max(t1, t2);
                    }

                    if (stripActive)
                    {
                        float fMinRel = Math.Max(stripMin, tMin);
                        float fMaxRel = Math.Min(stripMax, tMax);
                        if (fMinRel <= fMaxRel)
                        {
                            float absMin = fMinRel + p1x;
                            float absMax = fMaxRel + p1x;
                            if (absMin < xMin) xMin = absMin;
                            if (absMax > xMax) xMax = absMax;
                            any = true;
                        }
                    }
                }
            }

            return any;
        }
        #endregion

        // ── ISnapshotable ──────────────────────────────────────────────────

        public override IShapeMemento CaptureSnapshot()
        {
            return new DrawBezierMemento(this);
        }

        protected class DrawBezierMemento : DrawObjectMemento
        {
            private readonly bool _isClosed;
            // 保存 _localPoints 以便正确恢复旋转/缩放后的图形。
            // RestoreGeometry 中 UpdateSetProperty 从变换后的 Points 计算的 _localPoints 是错误的，
            // 需要在 RestoreDerived 中用正确的 _localPoints 覆盖。
            private readonly List<SKPoint>? _localPoints;

            public DrawBezierMemento(DrawBezier bezier) : base(bezier)
            {
                _isClosed = bezier.IsClosed;
                if (bezier._localPoints != null)
                {
                    _localPoints = new List<SKPoint>(bezier._localPoints.Count);
                    foreach (var p in bezier._localPoints)
                        _localPoints.Add(new SKPoint(p.X, p.Y));
                }
            }

            protected override void RestoreDerived()
            {
                if (Shape is DrawBezier bezier)
                {
                    bezier.IsClosed = _isClosed;
                    // 恢复正确的 _localPoints，确保旋转/缩放后的图形渲染正确
                    if (_localPoints != null)
                    {
                        bezier._localPoints = new List<SKPoint>(_localPoints.Count);
                        foreach (var p in _localPoints)
                            bezier._localPoints.Add(new SKPoint(p.X, p.Y));
                    }
                }
            }
        }
    }
}
