using System.Diagnostics;
using System.Windows;
using DrSoft.Drawing.Contracts;
using DrSoft.Drawing.Controls.Algorithm;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Controls.Rendering;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using SkiaSharp;
using static DrSoft.Drawing.Controls.Rendering.HatchRenderHelper;

namespace DrSoft.Drawing.Controls.DrawShapes
{
    public class DrawPolygon : DrawObject, IHatchable, IPolygonShapeData
    {
        // CenterX/CenterY/ChildShapes 由基类处理
        // 存储局部坐标的点（相对于中心点）
        private List<Point2D> _localPoints = null;

        // 基准尺寸，用于控制点拖动时的比例缩放
        private float _baseWidth = 0;
        private float _baseHeight = 0;
        private bool _suppressSharpCenterPropagation = false;
        /// <summary>边数（正多边形）或顶点数（五角星）</summary>
        public int SideCount { get; set; } = 5;

        /// <summary>true = 五角星；false = 正多边形</summary>
        public bool IsStar { get; set; } = false;

        /// <summary>
        /// 获取多边形可见轮廓的世界坐标点（含旋转/缩放/倾斜变换），
        /// 与 DrawRectangle.GetVertices() 采用相同的坐标模型：
        /// 先计算局部坐标点（_localPoints × 缩放比），再通过 GetTransformMatrix 映射到世界坐标。
        /// </summary>
        public override List<Point2D> OutlinePoints
        {
            get
            {
                if (_localPoints == null || _localPoints.Count < 3)
                    return Points.Select(p => new Point2D(p.X, p.Y)).ToList();

                var transformMatrix = GetTransformMatrix();
                var result = new List<Point2D>(_localPoints.Count);
                for (int i = 0; i < _localPoints.Count; i++)
                {
                    //// 局部坐标（相对于 SharpCenter，已缩放）
                    var localPt = new SKPoint(_localPoints[i].X, _localPoints[i].Y);
                    // 通过变换矩阵映射到世界坐标（含旋转/缩放/倾斜）
                    var worldPt = transformMatrix.MapPoint(localPt);
                    result.Add(new Point2D(worldPt.X, worldPt.Y));
                }
                return result;
            }
            set => throw new NotImplementedException();
        }

        // 无参构造函数，供 AutoMapper 映射使用
        public DrawPolygon() : base()
        {
            UId = UniqueIdGenerator.NextId();
            Points = new List<SKPoint>();
            Type = ShapeType.Polygon;
        }

        public DrawPolygon(List<SKPoint> points) : this()
        {
            Points = points;
            UpdateSetProperty(points);
        }

        public DrawPolygon(List<Point2D> points) : this(points?.Select(p => new SKPoint(p.X, p.Y)).ToList()) { }

        public override void UpdateSetProperty(List<SKPoint> worldPoints)
        {
            if (worldPoints == null || worldPoints.Count < 3) return;
            Points = worldPoints;
            Type = ShapeType.Polygon;
            // 计算多边形的边界框（世界坐标）
            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;

            foreach (var point in worldPoints)
            {
                if (point.X < minX) minX = point.X;
                if (point.X > maxX) maxX = point.X;
                if (point.Y < minY) minY = point.Y;
                if (point.Y > maxY) maxY = point.Y;
            }

            // 计算中心点（世界坐标）
            float centerX = (float)((minX + maxX) / 2);
            float centerY = (float)((minY + maxY) / 2);

            // 计算宽度和高度

            // 记录基准尺寸（用于后续控制点拖动时的比例缩放）
            float newWidth = (float)(maxX - minX);
            float newHeight = (float)(maxY - minY);
            _baseWidth = newWidth;
            _baseHeight = newHeight;

            // 设置中心点

            // 将世界坐标的点转换为局部坐标（相对于中心点）
            _localPoints = new List<Point2D>();
            foreach (var point in worldPoints)
            {
                _localPoints.Add(new Point2D(point.X - centerX, point.Y - centerY));
            }
        }

        public override bool HitTest(SKPoint p, float tol = 6.0f)
        {
            if (Points == null || Points.Count < 3)
                return false;
            return base.HitTest(p, tol);
        }

        public override bool IntersectsWith(SKRect rect)
        {
            if (Points == null || Points.Count < 3)
                return false;

            return base.IntersectsWith(rect);
        }


        protected override void OnCommittedMatrixChanged()
        {
            SyncWorldPointsFromMatrix();
        }

        public override IShape Clone()
        {
            var clone = new DrawPolygon
            {
                HatchParamInfo = HatchParamInfo,
                SideCount = SideCount,
                IsStar = IsStar,
            };

            if (Points != null)
            {
                clone.Points = new List<SKPoint>(Points.Count);
                foreach (var point in Points)
                {
                    clone.Points.Add(new SKPoint(point.X, point.Y));
                }
            }

            if (_localPoints != null)
            {
                clone._localPoints = new List<Point2D>(_localPoints.Count);
                foreach (var point in _localPoints)
                {
                    clone._localPoints.Add(new Point2D(point.X, point.Y));
                }
            }

            clone._baseWidth = _baseWidth;
            clone._baseHeight = _baseHeight;

            return FinalizeClone(clone);
        }

        /// <summary>
        /// 获取多边形的路径（局部坐标）
        /// </summary>
        public override SKPath GetPath()
        {
            var path = new SKPath();
            FillPath(path);
            return path;
        }

        protected override void FillPath(SKPath path)
        {
            if (_localPoints == null || _localPoints.Count < 3)
                return;

            // 同步更新世界坐标点，保持数据一致性

            // 移动到第一个点
            var firstPoint = _localPoints[0];
            float firstX = firstPoint.X;
            float firstY = firstPoint.Y;
            path.MoveTo(firstX, firstY);

            // 连接所有点
            for (int i = 1; i < _localPoints.Count; i++)
            {
                var point = _localPoints[i];
                float x = point.X;
                float y = point.Y;
                path.LineTo(x, y);
            }

            // 闭合路径（多边形）
            path.Close();

            // 同步更新 Points，保持数据一致性（缩放后的世界坐标，不含旋转）
            // 旋转由渲染管线通过 GetTransformMatrix 单独应用，不应编入 Points。
        }


        #region 多边形点生成工具

        /// <summary>
        /// 生成正多边形顶点列表（世界坐标）。
        /// 以 center 为圆心，radius 为外接圆半径，第一个顶点在正上方（-π/2）。
        /// </summary>
        private void SyncWorldPointsFromMatrix()
        {
            if (_localPoints == null || _localPoints.Count < 3)
                return;

            var worldPoints = new List<SKPoint>(_localPoints.Count);
            for (int i = 0; i < _localPoints.Count; i++)
            {
                var localPoint = new SKPoint(_localPoints[i].X, _localPoints[i].Y);
                worldPoints.Add(this.Matrix.MapPoint(localPoint));
            }

            Points = worldPoints;
        }

        public static List<SKPoint> GenerateRegularPolygonPoints(SKPoint center, float radius, int sides)
        {
            if (sides < 3) sides = 3;
            var pts = new List<SKPoint>(sides);
            double angleStep = 2 * Math.PI / sides;
            double startAngle = Math.PI / 2; // 第一个顶点在正上方（世界坐标Y轴向上）
            for (int i = 0; i < sides; i++)
            {
                double a = startAngle + i * angleStep;
                pts.Add(new SKPoint(
                    center.X + radius * (float)Math.Cos(a),
                    center.Y + radius * (float)Math.Sin(a)));
            }
            return pts;
        }

        /// <summary>
        /// 生成尖角星顶点列表（世界坐标）。
        /// points 个外顶点交错内顶点，内接圆半径 = outerRadius * 0.382（黄金分割）。
        /// </summary>
        public static List<SKPoint> GenerateStarPoints(SKPoint center, float outerRadius, int points)
        {
            if (points < 3) points = 3;
            float innerRadius = outerRadius * 0.382f; // 黄金分割比内接圆
            var pts = new List<SKPoint>(points * 2);
            double angleStep = Math.PI / points; // 外/内顶点交替间隔
            double startAngle = Math.PI / 2; // 第一个外顶点在正上方（世界坐标Y轴向上）
            for (int i = 0; i < points * 2; i++)
            {
                double a = startAngle + i * angleStep;
                float r = (i % 2 == 0) ? outerRadius : innerRadius;
                pts.Add(new SKPoint(
                    center.X + r * (float)Math.Cos(a),
                    center.Y + r * (float)Math.Sin(a)));
            }
            return pts;
        }

        /// <summary>
        /// 修改当前多边形的边数/顶点数和类型，保持中心和外接圆半径不变。
        /// 用于 属性面板 套用时将封闭数重新生成冠点并更新局部坐标。
        /// </summary>
        public void AdjustShape(int sideCount, PolygonType polygonType)
        {
            AdjustShape(sideCount, polygonType == PolygonType.Star);
        }

        /// <summary>
        /// 修改当前多边形的边数/顶点数和类型，保持外接圆半径不变。
        /// 重新生成顶点后以边界框中心更新 SharpCenter，确保选择框正确框选图形。
        /// </summary>
        public void AdjustShape(int sideCount, bool isStar)
        {
            SideCount = Math.Max(3, sideCount);
            IsStar = isStar;

            _suppressSharpCenterPropagation = true;
            try
            {
                // 计算真实外接圆中心和半径。
                // 对于奇数边正多边形/星形，边界框中心偏移于外接圆中心，
                // 直接用 max(distance to local origin) 会导致每次套用时半径膨胀。
                // 正确做法：先求所有顶点的几何中心（centroid）= 外接圆中心，
                // 再求 centroid 到最远顶点的距离 = 真实外接圆半径。
                SKPoint trueCenter;
                float trueRadius;
                if (_localPoints?.Count > 0)
                {
                    float cx = 0, cy = 0;
                    foreach (var p in _localPoints) { cx += p.X; cy += p.Y; }
                    cx /= _localPoints.Count;
                    cy /= _localPoints.Count;

                    // 真实外接圆中心 = 世界坐标中的 centroid
                    trueCenter = new SKPoint(SharpCenter.X + cx, SharpCenter.Y + cy);
                    // 真实外接圆半径 = centroid 到最远顶点的距离
                    trueRadius = _localPoints.Max(p => (float)Math.Sqrt(
                        (p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy)));
                }
                else
                {
                    trueCenter = SharpCenter;
                    trueRadius = Math.Min(Width, Height) / 2f;
                }
                if (trueRadius <= 0) trueRadius = 5f;

                // 以真实外接圆中心生成新顶点，保持多边形大小和位置不变
                List<SKPoint> newPoints = IsStar
                    ? GenerateStarPoints(trueCenter, trueRadius, SideCount)
                    : GenerateRegularPolygonPoints(trueCenter, trueRadius, SideCount);

                // 计算新边界框及其中心
                float minX = newPoints[0].X, maxX = newPoints[0].X;
                float minY = newPoints[0].Y, maxY = newPoints[0].Y;
                foreach (var pt in newPoints)
                {
                    if (pt.X < minX) minX = pt.X;
                    if (pt.X > maxX) maxX = pt.X;
                    if (pt.Y < minY) minY = pt.Y;
                    if (pt.Y > maxY) maxY = pt.Y;
                }

                // 以边界框中心作为新的 SharpCenter，
                // 保证 GetLocalBounds() 返回的对称矩形与实际内容对齐，选择框正确框选图形。
                float newCenterX = (minX + maxX) / 2f;
                float newCenterY = (minY + maxY) / 2f;

                _baseWidth = Width;
                _baseHeight = Height;

                // 局部坐标相对于新的 SharpCenter（边界框中心），
                // 与 UpdateSetProperty 保持一致
                _localPoints = new List<Point2D>(newPoints.Count);
                foreach (var pt in newPoints)
                {
                    _localPoints.Add(new Point2D(pt.X - newCenterX, pt.Y - newCenterY));
                }

                Points = newPoints;
            }
            finally
            {
                _suppressSharpCenterPropagation = false;
            }
        }

        #endregion

        #region 多边形填充
        // 填充
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

        /// <summary>
        /// 多边形直线填充：采用与 DrawBezier 一致的“点到边距离 ≥ margin”的精确约束扫描线算法。
        /// 不再执行 ShrinkPolygon 的顺手重方向偏移，在自相交/凹多边形/尖角 等场景下仍能
        /// 稳定地在所有边（包括自相交 bowtie 波领约束的两条内部交叉边）仅仅推方向内侧软反边界约束 margin。
        /// </summary>
        public HatchPatternObjects CreateHatchPattern()
        {
            if (HatchParamInfo == null) return new HatchPatternObjects();
            // 1. 获取基础数据
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
            if (hatchInfo.LineSpacing <= 0 || _localPoints == null || _localPoints.Count < 3)
                return result;

            return hatchInfo.FillTypeIndex switch
            {
                0 => GenerateScanlineFill(hatchInfo),      // S型单向 / 弓字型双向 / 优化弓字
                1 => GenerateScanlineFill(hatchInfo),      // S型单向 / 弓字型双向 / 优化弓字
                2 => GenerateConcentricLineFill(hatchInfo),
                3 => GenerateSpiralLineFill(hatchInfo),
                _ => new List<(SKPoint, SKPoint)>(),      // 其他
            };
        }
        /// <summary>
        /// 扫描线填充：将多边形按 -angle 旋转使填充线水平。对每行 y：
        /// ① odd-even 规则求所有边的 x 交点得到实心填充段；
        /// ② 求每条边的 margin-胶囊（Minkowski 和）与扫描线的 x 区间并集作为禁区；
        /// ③ 实心段 减去 禁区 即得到每点都距所有边 ≥ margin 的子区间，自然解决 bowtie 等自相交/凹多边形中左右两侧 margin
        /// 不同时有效的问题。
        /// </summary>
        private List<(SKPoint Start, SKPoint End)> GenerateScanlineFill(
             HatchParamDto info)
        {
            var result = new List<(SKPoint, SKPoint)>();
            if (info == null || _localPoints == null || _localPoints.Count < 3) return result;

            if (Points == null || Points.Count < 3) return result;

            //        SKPoint[] polygon = _localPoints
            //.Select(p => new SKPoint((float)p.X, (float)p.Y))
            //.ToArray();

            var polygon = Points.Select(p => new SKPoint(p.X, p.Y)).ToArray();

            if (info.LineSpacing <= 0 || polygon.Length < 3) return result;

            double angleDeg = info.StartAngle;
            float margin = (float)info.Margin;
            float spacing = (float)info.LineSpacing;
            float extension = (float)info.Extension;
            bool reverseAll = info.ReverseFillLine;
            // FillTypeIndex：0 = S型单向，1 = S型双向（逆行反向）
            bool bidirectional = info.FillTypeIndex == 1;
            bool relativeToAngle = info.RelativeToAngle;
            //double rad = -angleDeg * Math.PI / 180.0;
            //double rad = -(relativeToAngle ? info.StartAngle : info.StartAngle + Rotation) * Math.PI / 180.0;
            double rad = -(relativeToAngle ? info.StartAngle + Rotation : info.StartAngle) * Math.PI / 180.0;
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
            if (minY >= maxY) return result;

            // AverageDistribute ：将 LineSpacing 作为目标值，重算间距使扫描线在 [minY, maxY]
            // 区间均等分布；将 span 平均分成 nGaps 份，生成 nGaps-1 条填充线，
            // 使“边界→首线 / 线间 / 尾线→边界”的间距全部相等 = span / nGaps
            float startOffset = spacing / 2f;
            float yLimit = maxY;
            if (info.AverageDistribute)
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
            var segs = new List<(float A, float B)>(8);

            int lineIndex = 0;
            for (float y = minY + startOffset; y < yLimit; y += spacing, lineIndex++)
            {
                // 1) 求扫描线与多边形边的交点，odd-even 配对得实心填充段（自然处理自相交）
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

                // 2) 求扫描线与每条边 margin-胶囊 的 x 区间并集（禁区）
                forbidden.Clear();
                if (margin > 0)
                {
                    for (int i = 0; i < n; i++)
                    {
                        var p1 = rotated[i];
                        var p2 = rotated[(i + 1) % n];
                        if (TrySegmentCapsuleXRange(p1.X, p1.Y, p2.X, p2.Y, y, margin, out float fMin, out float fMax))
                            forbidden.Add((fMin, fMax));
                    }
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
                segs.Clear();
                for (int i = 0; i + 1 < xs.Count; i += 2)
                {
                    float a = xs[i], b = xs[i + 1];
                    if (b <= a) continue;
                    float cur = a;
                    for (int k = 0; k < forbidden.Count; k++)
                    {
                        var (fs, fe) = forbidden[k];
                        if (fe <= cur) continue;
                        if (fs >= b) break;
                        if (fs > cur) segs.Add((cur, fs));
                        if (fe > cur) cur = fe;
                        if (cur >= b) break;
                    }
                    if (cur < b) segs.Add((cur, b));
                }
                if (segs.Count == 0) continue;

                // 4) Extension 延伸
                if (extension != 0f)
                {
                    for (int si = 0; si < segs.Count; si++)
                    {
                        var (a, b) = segs[si];
                        a -= extension;
                        b += extension;
                        if (b > a) segs[si] = (a, b);
                    }
                }

                // 5) 确定本行方向：S型双向时奇数行翻转，叠加全局 ReverseFillLine
                bool reverseLine = reverseAll;
                if (bidirectional && (lineIndex & 1) == 1) reverseLine = !reverseLine;

                // 6) 旋转回局部坐标系并输出
                foreach (var (a, b) in segs)
                {
                    float sx = reverseLine ? b : a;
                    float ex = reverseLine ? a : b;
                    float bsx = (float)(sx * cosBack - y * sinBack);
                    float bsy = (float)(sx * sinBack + y * cosBack);
                    float bex = (float)(ex * cosBack - y * sinBack);
                    float bey = (float)(ex * sinBack + y * cosBack);
                    result.Add((new SKPoint(bsx, bsy), new SKPoint(bex, bey)));
                }
            }

            return result;
        }

        private List<(SKPoint Start, SKPoint End)> GenerateConcentricLineFill(HatchParamDto info)
        {
            return new List<(SKPoint Start, SKPoint End)>();
        }

        private List<(SKPoint Start, SKPoint End)> GenerateSpiralLineFill(HatchParamDto info)
        {
            return new List<(SKPoint Start, SKPoint End)>();
        }
        /// <summary>
        /// 水平扫描线 y = y 与线段 P1P2 的 margin-胶囊 交集 x 区间。胶囊为凸集，与直线交集必为单个区间。
        /// </summary>
        private static bool TrySegmentCapsuleXRange(float p1x, float p1y, float p2x, float p2y,
                                                    float y, float margin,
                                                    out float xMin, out float xMax)
        {
            xMin = float.MaxValue;
            xMax = float.MinValue;
            bool any = false;

            float dx = p2x - p1x;
            float dy = p2y - p1y;
            float L2 = dx * dx + dy * dy;

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

            // 端点 P1 处的半圆帽
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
            // 端点 P2 处的半圆帽
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
            // 线段中间的垂直条带
            {
                float nx = -dy / L;
                float B = (y - p1y) * (dx / L);

                float stripMin, stripMax;
                bool stripActive = true;
                if (Math.Abs(nx) < 1e-9f)
                {
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
        /// <summary>
        /// 对单条填充线应用 Extension：正值从两端向外延长，负值向内收缩。
        /// 若收缩后长度≤ 0 则返回 false，表示该填充线不存在。
        /// </summary>
        private static bool TryApplyLineExtension(SKPoint s, SKPoint e, float extension,
            out SKPoint newStart, out SKPoint newEnd)
        {
            if (extension == 0f) { newStart = s; newEnd = e; return true; }
            float dx = e.X - s.X, dy = e.Y - s.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len <= 1e-6f) { newStart = s; newEnd = e; return extension > 0f; }
            if (len + 2f * extension <= 1e-6f) { newStart = default; newEnd = default; return false; }
            float ux = dx / len, uy = dy / len;
            newStart = new SKPoint(s.X - ux * extension, s.Y - uy * extension);
            newEnd = new SKPoint(e.X + ux * extension, e.Y + uy * extension);
            return true;
        }
        #endregion

        // ── ISnapshotable ──────────────────────────────────────────────────

        public override IShapeMemento CaptureSnapshot()
        {
            return new DrawPolygonMemento(this);
        }

        protected class DrawPolygonMemento : DrawObjectMemento
        {
            private readonly int _sideCount;
            private readonly bool _isStar;
            // 捕获 DrawPolygon 内部状态，确保撤销/重做时 _localPoints/_baseWidth/_baseHeight 与 SharpCenter 一致
            private readonly List<Point2D> _localPoints;
            private readonly float _baseWidth;
            private readonly float _baseHeight;

            public DrawPolygonMemento(DrawPolygon poly) : base(poly)
            {
                _sideCount = poly.SideCount;
                _isStar = poly.IsStar;

                // 深拷贝 _localPoints（内部状态，用于比例缩放计算）
                if (poly._localPoints != null)
                {
                    _localPoints = new List<Point2D>(poly._localPoints.Count);
                    for (int i = 0; i < poly._localPoints.Count; i++)
                        _localPoints.Add(new Point2D(poly._localPoints[i].X, poly._localPoints[i].Y));
                }
                else
                {
                    _localPoints = null;
                }
                _baseWidth = poly._baseWidth;
                _baseHeight = poly._baseHeight;
            }

            protected override void RestoreDerived()
            {
                if (Shape is DrawPolygon poly)
                {
                    poly.SideCount = _sideCount;
                    poly.IsStar = _isStar;

                    // 直接恢复内部状态，避免 UpdateSetProperty 从旧 Points 重算 _localPoints
                    // 导致 _localPoints 相对的中心与 RestoreTransform 覆盖的 SharpCenter 不一致
                    if (_localPoints != null)
                    {
                        poly._localPoints = new List<Point2D>(_localPoints.Count);
                        for (int i = 0; i < _localPoints.Count; i++)
                            poly._localPoints.Add(new Point2D(_localPoints[i].X, _localPoints[i].Y));
                    }
                    else
                    {
                        poly._localPoints = null;
                    }
                    poly._baseWidth = _baseWidth;
                    poly._baseHeight = _baseHeight;
                }
            }
        }
    }
}
