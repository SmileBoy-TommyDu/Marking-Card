using DrSoft.Drawing.Controls;
using DrSoft.Drawing.Controls.Algorithm;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Controls.Rendering;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Windows.Shapes;
using static DrSoft.Drawing.Controls.Rendering.HatchRenderHelper;

namespace DrSoft.Drawing.Controls.DrawShapes
{
    public class DrawPolyLines : DrawObject, IHatchable, IPolyLineShapeData, IClosable
    {
        IReadOnlyList<(float X, float Y)> IPolyLineShapeData.Vertices =>
            Points.Select(p => (p.X, p.Y)).ToArray();
        // CenterX/CenterY/ChildShapes 由基类处理
        /// <summary>
        /// 0:实线;1：短虚线;2：点虚线；
        /// </summary>
        public LineStyle LineStyle { get; set; }
        // 存储局部坐标的点（相对于中心点）
        private List<Point2D> _localPoints = null;
        public SKPoint Center { get; set; }
        /// <summary>
        /// 是否为闭合折线（末尾自动连回起点）
        /// </summary>
        public bool IsClosed { get; set; } = false;

        /// <summary>
        /// 虚线输出线段列表（世界坐标，单位 mm）。
        /// 当 OutputAsDashed 为 true 时由 BuildMarkingJob 阶段预计算写入。
        /// </summary>
        public IReadOnlyList<((float X, float Y) Start, (float X, float Y) End)> DashSegments { get; set; }

        // 基准尺寸不再使用——FillPath 直接用 _localPoints 渲染（无缩放），
        // 与 UpdateLocalPointsInPlace 统一为同一套坐标转换逻辑。
        // 保留字段以防反序列化兼容，但不再参与渲染计算。
        private float _baseWidth = 0;
        private float _baseHeight = 0;

        /// <summary>
        /// 获取折线可见轮廓的世界坐标点（含旋转/缩放/倾斜变换），
        /// 与 DrawPolygon.OutlinePoints 采用相同的坐标模型：
        /// 先计算局部坐标点（_localPoints × 缩放比），再通过 GetTransformMatrix 映射到世界坐标。
        /// Points 只包含平移+缩放（不含旋转），OutlinePoints 包含全部矩阵变换。
        /// </summary>
        public override List<Point2D> OutlinePoints
        {
            get
            {
                if (_localPoints == null || _localPoints.Count == 0)
                    return Points.Select(p => new Point2D(p.X, p.Y)).ToList();

                var transformMatrix = GetTransformMatrix();
                var result = new List<Point2D>(_localPoints.Count);
                for (int i = 0; i < _localPoints.Count; i++)
                {
                    var localPt = new SKPoint((float)(_localPoints[i].X), (float)(_localPoints[i].Y));
                    var worldPt = transformMatrix.MapPoint(localPt);
                    result.Add(new Point2D(worldPt.X, worldPt.Y));
                }
                return result;
            }
            set => throw new NotImplementedException();
        }

        // 无参构造函数，供 AutoMapper 映射使用
        public DrawPolyLines() : base()
        {
            UId = UniqueIdGenerator.NextId();
            Points = new List<SKPoint>();
            Type = ShapeType.PolyLine;
        }

        public DrawPolyLines(List<SKPoint> points, bool isDxf = false) : this()
        {
            if (isDxf)
            {
                InitializeFromDxfPoints(points);
                return;
            }

            Points.AddRange(points);
            UpdateSetProperty(Points);
        }
        public DrawPolyLines(List<Point2D> points, bool isDxf = false) : this(points.Select(p => new SKPoint((float)p.X, (float)p.Y)).ToList(), isDxf) { }


        private void InitializeFromDxfPoints(List<SKPoint> points)
        {
            if (points == null || points.Count < 2)
            {
                return;
            }

            var worldPoints = points as List<SKPoint> ?? points.ToList();
            Points = worldPoints;
            Type = ShapeType.PolyLine;

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

            float centerX = (float)((minX + maxX) / 2);
            float centerY = (float)((minY + maxY) / 2);
            Center = new SKPoint(centerX, centerY);

            float newWidth = (float)(maxX - minX);
            float newHeight = (float)(maxY - minY);

            _localPoints = new List<Point2D>(worldPoints.Count);
            foreach (var point in worldPoints)
            {
                _localPoints.Add(new Point2D(point.X - centerX, point.Y - centerY));
            }

            _baseWidth = newWidth;
            _baseHeight = newHeight;

            RestoreTransformCommandSnapshot(new TransformCommandSnapshot(
                SKMatrix.CreateTranslation(Center.X, Center.Y),
                0f,
                1f,
                1f,
                0f,
                0f,
                Center,
                SKPoint.Empty,
                SKPoint.Empty));
        }
        public override void UpdateSetProperty(List<SKPoint> worldPoints)
        {
            if (worldPoints == null || worldPoints.Count < 2) return;

            Points = worldPoints;
            Type = ShapeType.PolyLine;

            // 计算折线的边界框（世界坐标）
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
            Center = new SKPoint(centerX, centerY);

            // 计算宽度和高度
            float newWidth = (float)(maxX - minX);
            float newHeight = (float)(maxY - minY);

            // 重要：先更新 _localPoints，再设置 Width/Height
            // 因为 Width/Height 的 setter 会触发 OnPropertyChanged，
            // 而 OnPropertyChanged 会使用 _localPoints 重新计算 Points
            _localPoints = new List<Point2D>();
            foreach (var point in worldPoints)
            {
                _localPoints.Add(new Point2D(point.X - centerX, point.Y - centerY));
            }

            // 记录基准尺寸（用于后续控制点拖动时的比例缩放）
            _baseWidth = newWidth;
            _baseHeight = newHeight;
            Translate(Center.X - SharpCenter.X, Center.Y - SharpCenter.Y, true);
        }

        /// <summary>
        /// 节点拖动专用更新：只更新被拖动节点的局部坐标，<b>不改变矩阵</b>。
        /// </summary>
        public void UpdateLocalPointsInPlace(int changedIndex, SKPoint newWorldPos)
        {
            if (_localPoints == null || changedIndex < 0 || changedIndex >= _localPoints.Count)
                return;

            var worldPoints = Points != null
                ? new List<SKPoint>(Points)
                : new List<SKPoint>();
            if (changedIndex >= worldPoints.Count)
                return;

            worldPoints[changedIndex] = newWorldPos;
            RebuildFromWorldPointsPreserveLinearTransform(worldPoints);
        }

        /// <summary>
        /// 批量更新所有点的局部坐标（用于节点插入/删除/批量变换等场景）。
        /// 用逆矩阵正确转换，不能用 Points[i] - SharpCenter（有旋转/缩放时不成立）。
        /// </summary>
        public void UpdateLocalPointsInPlace(List<SKPoint> worldPoints)
        {
            if (worldPoints == null || worldPoints.Count < 2) return;

            RebuildFromWorldPointsPreserveLinearTransform(worldPoints);
        }

        private void RebuildFromWorldPointsPreserveLinearTransform(IReadOnlyList<SKPoint> worldPoints)
        {
            if (worldPoints == null || worldPoints.Count < 2)
                return;

            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            for (int i = 0; i < worldPoints.Count; i++)
            {
                var point = worldPoints[i];
                minX = Math.Min(minX, point.X);
                maxX = Math.Max(maxX, point.X);
                minY = Math.Min(minY, point.Y);
                maxY = Math.Max(maxY, point.Y);
            }

            var center = new SKPoint(
                (minX + maxX) / 2f,
                (minY + maxY) / 2f);

            var linearTransform = ExtractLinearTransform(GetTransformMatrix());
            if (!linearTransform.TryInvert(out var inverseLinearTransform))
            {
                UpdateSetProperty(new List<SKPoint>(worldPoints));
                return;
            }

            var rebuiltLocalPoints = new List<Point2D>(worldPoints.Count);
            for (int i = 0; i < worldPoints.Count; i++)
            {
                var point = worldPoints[i];
                var localPoint = inverseLinearTransform.MapPoint(new SKPoint(
                    point.X - center.X,
                    point.Y - center.Y));
                rebuiltLocalPoints.Add(new Point2D(localPoint.X, localPoint.Y));
            }

            Points = new List<SKPoint>(worldPoints);
            Type = ShapeType.PolyLine;
            Center = center;
            _localPoints = rebuiltLocalPoints;
            _baseWidth = maxX - minX;
            _baseHeight = maxY - minY;

            var currentSnapshot = CaptureTransformCommandSnapshot();
            var updatedMatrix = new SKMatrix
            {
                ScaleX = currentSnapshot.Matrix.ScaleX,
                SkewX = currentSnapshot.Matrix.SkewX,
                TransX = center.X,
                SkewY = currentSnapshot.Matrix.SkewY,
                ScaleY = currentSnapshot.Matrix.ScaleY,
                TransY = center.Y,
                Persp0 = currentSnapshot.Matrix.Persp0,
                Persp1 = currentSnapshot.Matrix.Persp1,
                Persp2 = currentSnapshot.Matrix.Persp2
            };

            RestoreTransformCommandSnapshot(new TransformCommandSnapshot(
                updatedMatrix,
                currentSnapshot.Rotation,
                currentSnapshot.ScaleX,
                currentSnapshot.ScaleY,
                currentSnapshot.SkewX,
                currentSnapshot.SkewY,
                center,
                currentSnapshot.ScaleAnchorPoint,
                currentSnapshot.RotationCenterLocal));
        }

        private static SKMatrix ExtractLinearTransform(SKMatrix matrix)
        {
            return new SKMatrix
            {
                ScaleX = matrix.ScaleX,
                SkewX = matrix.SkewX,
                TransX = 0f,
                SkewY = matrix.SkewY,
                ScaleY = matrix.ScaleY,
                TransY = 0f,
                Persp0 = 0f,
                Persp1 = 0f,
                Persp2 = 1f
            };
        }

        public override bool HitTest(SKPoint p, float tol = 6.0f)
        {
            if (Points == null || Points.Count < 2)
                return false;

            return base.HitTest(p, tol);
        }

        internal override void ReverseDirection()
        {
            Points.Reverse();
            UpdateSetProperty(Points);
        }

        /// <summary>
        /// 返回局部坐标系中的真实包围盒（基于 _localPoints）。
        /// 节点拖动后 SharpCenter 不变但点集偏心，需用真实局部坐标而非对称矩形。
        /// </summary>
        public override SKRect GetLocalBounds()
        {
            if (_localPoints == null || _localPoints.Count == 0)
                return base.GetLocalBounds();

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in _localPoints)
            {
                float x = p.X;
                float y = p.Y;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
            return new SKRect(minX, minY, maxX, maxY);
        }

        public override bool IntersectsWith(SKRect rect)
        {
            if (Points == null || Points.Count < 2)
                return false;

            return base.IntersectsWith(rect);
        }

        protected override void OnCommittedMatrixChanged()
        {
            SyncWorldPointsFromMatrix();
        }

        public override IShape Clone()
        {
            var clone = new DrawPolyLines
            {
                IsClosed = IsClosed,
                LineStyle = LineStyle,
                HatchParamInfo = HatchParamInfo,
                Center = Center,
                DashSegments = DashSegments,
                _baseWidth = _baseWidth,
                _baseHeight = _baseHeight,
            };

            if (Points != null)
            {
                clone.Points = new List<SKPoint>(Points);
            }

            if (_localPoints != null)
            {
                clone._localPoints = new List<Point2D>(_localPoints.Count);
                foreach (var point in _localPoints)
                {
                    clone._localPoints.Add(new Point2D(point.X, point.Y));
                }
            }

            return FinalizeClone(clone);
        }

        /// <summary>
        /// 获取折线的路径（局部坐标）
        /// </summary>
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
            var firstPoint = _localPoints[0];
            path.MoveTo(firstPoint.X, firstPoint.Y);

            for (int i = 1; i < _localPoints.Count; i++)
            {
                var point = _localPoints[i];
                path.LineTo(point.X, point.Y);
            }

            if (IsClosed)
                path.Close();
        }

        private void SyncWorldPointsFromMatrix()
        {
            if (_localPoints == null || _localPoints.Count == 0)
                return;

            var transformMatrix = GetTransformMatrix();
            var worldPoints = new List<SKPoint>(_localPoints.Count);
            for (int i = 0; i < _localPoints.Count; i++)
            {
                var localPoint = new SKPoint(_localPoints[i].X, _localPoints[i].Y);
                worldPoints.Add(transformMatrix.MapPoint(localPoint));
            }

            Points = worldPoints;
        }

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
            if (hatchInfo.LineSpacing <= 0)
                throw new ArgumentException("填充间隔参数为0");
            if (_localPoints == null || _localPoints.Count < 3)
                throw new Exception("直线段无法填充");

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
            bool relativeToAngle = info.RelativeToAngle;

            // 与 GetPath 一致的 Width/Height 缩放：确保填充底层几何 == 渲染几何。
            // 这里的缩放只设为 “按控制点拖动/改 Width/Height ” 的比例缩放；
            // Rotation/Skew/ScaleX-ScaleY/Translation 都由 FillLineStyleEmitter.Convert 
            // 通过 GetTransformMatrix 统一应用，不在此处重复处理。
            //float scaleX = _baseWidth > 0.001f ? Width / _baseWidth : 1f;
            //float scaleY = _baseHeight > 0.001f ? Height / _baseHeight : 1f;

            //SKPoint[] polygon = _localPoints
            //    .Select(p => new SKPoint((float)(p.X * scaleX), (float)(p.Y * scaleY)))
            //    .ToArray();

            var polygon = Points.Select(p => new SKPoint(p.X, p.Y)).ToArray();

            if (info.LineSpacing <= 0 || polygon.Length < 3) return result;

            double angleDeg = info.StartAngle;
            float margin = (float)info.Margin;
            float spacing = (float)info.LineSpacing;
            float extension = (float)info.Extension;
            bool reverseAll = info.ReverseFillLine;
            // FillTypeIndex：0 = S型单向，1 = S型双向（逆行反向）
            bool bidirectional = info.FillTypeIndex == 1;
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
        #endregion


        // ── ISnapshotable ──────────────────────────────────────────────────

        public override IShapeMemento CaptureSnapshot()
        {
            return new DrawPolyLinesMemento(this);
        }

        protected class DrawPolyLinesMemento : DrawObjectMemento
        {
            private readonly bool _isClosed;
            private readonly LineStyle _lineType;
            private readonly List<Point2D> _localPoints;
            private readonly float _baseWidth;
            private readonly float _baseHeight;

            public DrawPolyLinesMemento(DrawPolyLines poly) : base(poly)
            {
                _isClosed = poly.IsClosed;
                _lineType = poly.LineStyle;

                if (poly._localPoints != null)
                {
                    _localPoints = new List<Point2D>(poly._localPoints.Count);
                    for (int i = 0; i < poly._localPoints.Count; i++)
                    {
                        var localPoint = poly._localPoints[i];
                        _localPoints.Add(new Point2D(localPoint.X, localPoint.Y));
                    }
                }

                _baseWidth = poly._baseWidth;
                _baseHeight = poly._baseHeight;
            }

            protected override void RestoreGeometry()
            {
                if (Shape is not DrawPolyLines poly)
                    return;

                var restoredPoints = new List<SKPoint>(_points.Count);
                for (int i = 0; i < _points.Count; i++)
                {
                    restoredPoints.Add(_points[i]);
                }

                poly.Points = restoredPoints;
                poly.Type = ShapeType.PolyLine;

                if (_localPoints != null)
                {
                    var restoredLocalPoints = new List<Point2D>(_localPoints.Count);
                    for (int i = 0; i < _localPoints.Count; i++)
                    {
                        var localPoint = _localPoints[i];
                        restoredLocalPoints.Add(new Point2D(localPoint.X, localPoint.Y));
                    }

                    poly._localPoints = restoredLocalPoints;
                }
                else
                {
                    poly._localPoints = null;
                }

                poly._baseWidth = _baseWidth;
                poly._baseHeight = _baseHeight;
            }

            protected override void RestoreDerived()
            {
                if (Shape is DrawPolyLines poly)
                {
                    poly.IsClosed = _isClosed;
                    poly.LineStyle = _lineType;
                }
            }
        }
    }
}
