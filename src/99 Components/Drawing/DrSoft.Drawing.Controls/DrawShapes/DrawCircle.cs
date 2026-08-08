using DrSoft.Drawing.Controls.Algorithm;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Controls.Rendering;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using SkiaSharp;
using System;
using System.Windows.Media.Media3D;
using static DrSoft.Drawing.Controls.Rendering.HatchRenderHelper;

namespace DrSoft.Drawing.Controls.DrawShapes
{
    /// <summary>
    /// 圆 / 椭圆图形。
    /// 实现 <see cref="ICircleShapeData"/> 只读数据契约，打标卡可直接读取几何数据，无需 DTO 转换。
    /// </summary>
    public class DrawCircle : DrawObject, IHatchable, ICircleShapeData
    {
        // 基础几何属性
        public List<Point2D>? _vertices;
        public bool IsEllipse { get; set; } = false;
        public float RadiusX
        {
            get;
            set;
        } = 0; // 圆的半径（X轴方向）--暴露给外部使用，方便获取圆的半径
        public float RadiusY { get; set; } = 0; // 圆的半径（Y轴方向）

        // ── ICircleShapeData 特有属性：RadiusX / RadiusY / IsEllipse 已作为公共属性存在，自动满足接口 ──
        // CenterX / CenterY 显式实现（基类 DrawObject 同名实现会被此覆盖，语义一致）
        float IShapeData.CenterX => SharpCenter.X;
        float IShapeData.CenterY => SharpCenter.Y;
        // ChildShapes：由基类 DrawObject.GetChildShapeData() 返回空列表，无需重写

        // 无参构造函数，供 AutoMapper 映射使用


        public float DrawingRadiusX { get; private set; } = 0; // 圆的半径（X轴方向）--渲染内部使用
        public float DrawingRadiusY { get; private set; } = 0; // 圆的半径（Y轴方向）

        public DrawCircle() : base()
        {
            UId = UniqueIdGenerator.NextId();
            Points = new List<SKPoint>();
            Type = ShapeType.Circle;
            IsClockwise = true; // 默认顺时针
        }

        public DrawCircle(List<SKPoint> points, bool isDxf = false, float dxfRatio = 1f) : this()
        {
            if (points == null || points.Count < 2)
            {
                throw new ArgumentException("绘制圆需要2个点!");
            }

            if (isDxf)
            {
                InitializeFromDxfPoints(points, dxfRatio);
                return;
            }

            UpdateSetProperty(points);
        }
        public DrawCircle(List<Point2D> points, bool isDxf = false, float dxfRatio = 1f) : this(points.Select(p => new SKPoint((float)p.X, (float)p.Y)).ToList(), isDxf, dxfRatio) { }

        public DrawCircle(Point2D center, float radius) : this(new List<Point2D> { center, new Point2D(center.X + radius, center.Y) }, true) { }

        internal override List<IShape> CreateCurveChildren()
        {
            var children = new List<IShape>();

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
                IsClosed = true,
                Pen = Pen,
                Name = $"{Name}_折线"
            };
            children.Add(polyLine);
            return children;
        }

        /// <summary>
        /// DXF 导入初始化：从已变换的绝对世界坐标点初始化圆/椭圆属性。
        /// points[0] = 圆心（世界坐标），points[1] = 主轴端点（世界坐标，即长轴方向边缘点）。
        /// ratio = 短轴/长轴比例（DXF 组码40），正圆时 ratio=1。
        /// 从主轴向量反算 Rotation，从 ratio 反算 RadiusY。
        /// </summary>
        private void InitializeFromDxfPoints(List<SKPoint> points, float ratio = 1f)
        {
            var center = points[0];

            // 主轴端点：圆心到该点的距离 = 长半径 RadiusX
            float dx = points[1].X - center.X;
            float dy = points[1].Y - center.Y;
            DrawingRadiusX = MathF.Sqrt(dx * dx + dy * dy);

            // 从主轴向量反算旋转角度（度）。
            // 局部椭圆长轴沿局部 +X，Rotate()/矩阵约定：CreateRotationDegrees(θ)
            // 将 (1,0) 映射到 (cosθ, sinθ)，故 θ = atan2(dy, dx)。
            float rotDeg = 0f;
            if (DrawingRadiusX > 0.001f)
                rotDeg = MathF.Atan2(dy, dx) * 180f / MathF.PI;

            // 短轴半径 = 长轴半径 × ratio（DXF 组码40）
            DrawingRadiusY = DrawingRadiusX * ratio;
            IsEllipse = Math.Abs(DrawingRadiusX - DrawingRadiusY) > float.Epsilon;

            //Width = RadiusX * 2;
            //Height = RadiusY * 2;
            Points = points;
            Type = ShapeType.Circle;

            // ★ 关键修复：旋转必须烘焙进变换矩阵（渲染与导出均以 Matrix 为准），
            // 仅设置 Rotation 属性不会影响 Matrix，会导致旋转丢失。
            // 先平移到圆心，再绕圆心旋转，使状态与 UI 交互旋转的椭圆完全一致。
            var matrix = SKMatrix.CreateRotationDegrees(rotDeg, 0, 0)
                .PostConcat(SKMatrix.CreateTranslation(center.X, center.Y));
            RestoreTransformCommandSnapshot(new TransformCommandSnapshot(
                matrix,
                rotDeg,
                1f,
                1f,
                0f,
                0f,
                center,
                SKPoint.Empty,
                SKPoint.Empty));
        }

        internal void AdjustGeometry(float centerX, float centerY, float radiusX, float radiusY)
        {
            if (IsLocked)
                return;

            bool isTargetCircle = MathF.Abs(radiusX - radiusY) < float.Epsilon;

            if (isTargetCircle && (SkewX != 0 || SkewY != 0))
            {
                // ── 步骤 1: 使用传入的半径值 ──
                float targetRadius = radiusX;  // 或 radiusY，因为二者相等

                // ── 步骤 2: 更新 DrawCircle 特有属性 ──
                DrawingRadiusX = targetRadius;
                DrawingRadiusY = targetRadius;
                IsEllipse = false;

                // ── 步骤 3: 更新 Points 定义点 ──
                Points.Clear();
                Points.Add(new SKPoint(centerX, centerY));
                Points.Add(new SKPoint(centerX + targetRadius, centerY));

                var newMatrix = SKMatrix.CreateIdentity();
                newMatrix = newMatrix.PostConcat(SKMatrix.CreateTranslation(centerX, centerY));

                // ── 步骤 4: 设置矩阵 ──
                SetMatrixInternal(newMatrix);
                ResetSkewProperties();
            }
            else
            {
                // 获取当前状态用于测量半轴
                var m = Matrix;
                var origin = m.MapPoint(new SKPoint(0f, 0f));
                var xEnd = m.MapPoint(new SKPoint(DrawingRadiusX, 0f));
                var yEnd = m.MapPoint(new SKPoint(0f, DrawingRadiusY));
                float curX = SKPoint.Distance(origin, xEnd);
                float curY = SKPoint.Distance(origin, yEnd);

                // ── 椭圆的原有逻辑保持不变 ──
                float scaleX = curX > 1e-4f ? radiusX / curX : 1f;
                float scaleY = curY > 1e-4f ? radiusY / curY : 1f;

                // 沿图形自身方向缩放（沿椭圆长/短轴方向），旋转椭圆也能正确改变半轴。
                Scale(scaleX, scaleY, origin, GetWorldRotationRad(), commit: true);
            }

            // 最后：调整中心位置到目标坐标
            float cx = centerX - SharpCenter.X;
            float cy = centerY - SharpCenter.Y;
            Translate(cx, cy, true);
        }

        protected override void OnCommittedMatrixChanged()
        {
            // RadiusX/RadiusY 对外表示椭圆的“真实半轴”（长/短轴半径），而非包围盒半宽高。
            // 若用 Width/2、Height/2（旋转后 AABB 包围盒），旋转椭圆的值会趋近相等，
            // 参数面板读回后再作为半轴回填给 AdjustGeometry，会把椭圆逐步逼成圆。
            // 这里直接从矩阵测量沿局部长/短轴方向的世界长度（对旋转不敏感）。
            var m = Matrix;
            var origin = m.MapPoint(new SKPoint(0f, 0f));
            var xEnd = m.MapPoint(new SKPoint(DrawingRadiusX, 0f));
            var yEnd = m.MapPoint(new SKPoint(0f, DrawingRadiusY));
            RadiusX = SKPoint.Distance(origin, xEnd);
            RadiusY = SKPoint.Distance(origin, yEnd);
        }

        //public override SKRect GetLocalBounds()
        //{
        //    using var path = GetPath();
        //    if (path == null || path.IsEmpty)
        //        return base.GetLocalBounds();

        //    return path.TightBounds;
        //}

        // 更新属性方法，类似DrawRectangle和DrawPolyLines的实现
        //public override void UpdateSetProperty(List<SKPoint> points)
        //{
        //    if (points == null || points.Count < 2)
        //        return;
        //    var center = points[0];
        //    var edge = points[1];
        //    float centerX = (float)center.X;
        //    float centerY = (float)center.Y;

        //    // 更新图形属性
        //    Points = points;
        //    Type = ShapeType.Circle;
        //    DrawingRadiusX = SKPoint.Distance(center, edge);
        //    DrawingRadiusY = DrawingRadiusX;
        //    IsEllipse = Math.Abs(width - height) > float.Epsilon;
        //}

        public override void UpdateSetProperty(List<SKPoint> points)
        {
            var startPoint = points[0];
            var endPoint = points[1];
            var bounds = ResolveBounds(startPoint, endPoint);

            float width = bounds.Right - bounds.Left;
            float height = bounds.Top - bounds.Bottom;
            float centerX = (bounds.Left + bounds.Right) / 2f;
            float centerY = (bounds.Top + bounds.Bottom) / 2f;
            DrawingRadiusX = width / 2f;
            DrawingRadiusY = height / 2f;

            var centerPoint = new SKPoint(centerX, centerY);
            var edgePoint = new SKPoint(centerX + DrawingRadiusX, centerY);

            if (Points.Count >= 2)
            {
                Points[0] = centerPoint;
                Points[1] = edgePoint;
            }
            else
            {
                Points.Clear();
                Points.Add(centerPoint);
                Points.Add(edgePoint);
            }

            IsEllipse = Math.Abs(width - height) > float.Epsilon;
            Translate(centerPoint.X - SharpCenter.X, centerPoint.Y - SharpCenter.Y, true);
        }

        internal (float Left, float Right, float Bottom, float Top) ResolveBounds(SKPoint startPoint, SKPoint endPoint)
        {
            float left = Math.Min(startPoint.X, endPoint.X);
            float right = Math.Max(startPoint.X, endPoint.X);
            float bottom = Math.Min(startPoint.Y, endPoint.Y);
            float top = Math.Max(startPoint.Y, endPoint.Y);

            bool shouldLockCircle = DocumentContext.Instance.IsShiftPressed();
            if (shouldLockCircle)
            {
                float width = right - left;
                float height = top - bottom;
                float size = Math.Max(width, height);

                if (endPoint.X >= startPoint.X)
                {
                    right = left + size;
                }
                else
                {
                    left = right - size;
                }

                if (endPoint.Y >= startPoint.Y)
                {
                    top = bottom + size;
                }
                else
                {
                    bottom = top - size;
                }
            }

            return (left, right, bottom, top);
        }

        // 获取SKPath用于绘制（局部坐标）
        public override SKPath GetPath()
        {
            var path = new SKPath();
            FillPath(path);
            return path;
        }

        protected override void FillPath(SKPath path)
        {
            IsEllipse = Math.Abs(DrawingRadiusX - DrawingRadiusY) > float.Epsilon;
            if (IsEllipse)
            {
                // 在局部坐标系中绘制椭圆
                // 局部坐标系中，中心点是原点
                var halfWidth = (float)DrawingRadiusX;
                var halfHeight = (float)DrawingRadiusY;

                // 注意：在SKRect中，Top是较小的Y值，Bottom是较大的Y值
                // 但在局部坐标系中，Y轴向上为正，所以需要转换
                var top = halfHeight;    // 较大的Y值（上方）
                var bottom = -halfHeight; // 较小的Y值（下方）
                var left = -halfWidth;
                var right = halfWidth;

                path.AddOval(new SKRect(left, bottom, right, top));
            }
            else
            {
                // 在局部坐标系中绘制圆形
                // 局部坐标系中，中心点是原点
                var radius = (float)DrawingRadiusX;
                path.AddCircle(0, 0, radius);
            }
        }

        public override bool HitTest(SKPoint p, float tol = 6.0f)
        {
            if (Points == null || Points.Count < 2)
                return false;

            return base.HitTest(p, tol);
        }

        public override bool IntersectsWith(SKRect rect)
        {
            if (Points == null || Points.Count < 2)
                return false;

            // 检查圆心是否在矩形内
            if (rect.Contains(SharpCenter))
                return true;

            return base.IntersectsWith(rect);
        }

        public override IShape Clone()
        {
            var clone = new DrawCircle()
            {
                HatchParamInfo = HatchParamInfo,
                IsEllipse = IsEllipse,
                RadiusX = RadiusX,
                RadiusY = RadiusY,
                DrawingRadiusX = DrawingRadiusX,
                DrawingRadiusY = DrawingRadiusY,
            };

            if (Points != null)
            {
                clone.Points = new List<SKPoint>(Points);
            }

            return FinalizeClone(clone);
        }

        #region 填充
        // 填充
        public HatchParamDto? HatchParamInfo { get; set; }
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

        //public HatchPatternObjects CreateHatchPattern()
        //{
        //    // TODO: 根据 FillInfo 计算填充线段，并缓存结果以提升性能。当前返回空结果。
        //    if (HatchParamInfo == null) return new HatchPatternObjects();

        //    // 1. 获取基础数据（Extension / ReverseFillLine 已在 GetFillLines 内部处理）
        //    var fillLines = GetFillLines(HatchParamInfo);

        //    // 2. 进行映射变换
        //    var drawObjects = GetConvertObjects(fillLines, HatchParamInfo);
        //    return new HatchPatternObjects { HatchObjects = drawObjects };
        //}

        public HatchPatternObjects CreateHatchPattern()
        {
            // TODO: 根据 FillInfo 计算填充线段，并缓存结果以提升性能。当前返回空结果。
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
        /// 获取填充线段。返回的线段在**局部坐标系**中（中心为原点，未应用变换）。
        /// 根据 FillTypeIndex 分发到不同的填充算法。
        /// </summary>
        public List<(SKPoint Start, SKPoint End)> GetFillLines(HatchParamDto hatchInfo)
        {
            var fillLines = new List<(SKPoint Start, SKPoint End)>();
            IsEllipse = Math.Abs(DrawingRadiusX - DrawingRadiusY) > float.Epsilon;

            return hatchInfo.FillTypeIndex switch
            {
                0 => GetScanlineFillLines(hatchInfo),     // S型单向 / 弓字型双向 / 优化弓字
                1 => GetScanlineFillLines(hatchInfo),     // S型单向 / 弓字型双向 / 优化弓字
                //2 => GetConcentricFillLines(hatchInfo),   // 同心圆/椭圆
                2 => GetConcentricFillLines2(hatchInfo),   // 同心圆/椭圆（新方法，支持方向类型）
                3 => GetSpiralFillLines(hatchInfo),       // 螺旋线
                _ => new List<(SKPoint, SKPoint)>(),
            };
        }

        /// <summary>
        /// 扫描线填充（S 型单向 / 弓字型双向 / 优化弓字）。
        /// 在世界空间中直接扫描：世界填充线 → 拉回局部求交 → 交点映射回世界。
        /// 图形倾斜只影响边框，填充线在世界空间中保持指定角度不变。
        /// </summary>
        private List<(SKPoint Start, SKPoint End)> GetScanlineFillLines(HatchParamDto hatchInfo)
        {
            var fillLines = new List<(SKPoint Start, SKPoint End)>();

            if (hatchInfo.LineSpacing <= 0)
                return fillLines;

            float rx = DrawingRadiusX;
            float ry = IsEllipse ? DrawingRadiusY : DrawingRadiusX;

            // 应用边距（内缩，在局部空间缩半轴）
            float innerRx = rx - (float)hatchInfo.Margin;
            float innerRy = ry - (float)hatchInfo.Margin;
            if (innerRx <= 0 || innerRy <= 0)
                return fillLines;

            // FillTypeIndex：0 = S型单向，1 = S型双向（逆行反向）
            bool bidirectional = hatchInfo.FillTypeIndex == 1;
            float extension = (float)hatchInfo.Extension;
            bool reverseAll = hatchInfo.ReverseFillLine;

            // ── 世界空间填充线方向和法线 ──
            // RelativeToAngle：true = 填充角度跟随图形旋转，false = 绝对世界角度
            bool relativeToAngle = hatchInfo.RelativeToAngle;
            float fillAngleDeg = (float)hatchInfo.StartAngle;
            if (relativeToAngle)
                fillAngleDeg += Rotation;
            float fillAngleRad = fillAngleDeg * MathF.PI / 180f;
            // 世界空间法线（填充线的垂直方向，用于步进）
            float normRad = fillAngleRad + MathF.PI / 2;
            float nx = MathF.Cos(normRad);
            float ny = MathF.Sin(normRad);
            // 世界空间填充线方向（用于排序和延伸）
            float dx = MathF.Cos(fillAngleRad);
            float dy = MathF.Sin(fillAngleRad);

            // ── 变换矩阵参数 ──
            var matrix = GetTransformMatrix();
            float mSx = matrix.ScaleX, mSy = matrix.ScaleY;
            float mKx = matrix.SkewX, mKy = matrix.SkewY;
            float mTx = matrix.TransX, mTy = matrix.TransY;

            // ── 拉回法线：M^T · N_world，用于将世界扫描线方程拉回局部空间 ──
            // 世界线: N_world · (P - C_world) = proj
            // 展开: N_world · P = N_world · C_world + proj
            // 代入 P = M·P_local: (M^T·N) · P_local + N·T = N·C_world + proj
            // 由于 C_world = M·(0,0) = T，所以 N·T 抵消，得: A·x + B·y = proj
            float A = mSx * nx + mKy * ny;  // 局部线方程 A*x + B*y = proj 的系数
            float B = mKx * nx + mSy * ny;

            // ── 世界空间投影范围（proj 是相对于中心的有符号距离）──
            // N_world · (P_world(θ) - C_world) = (A·innerRx)·cosθ + (B·innerRy)·sinθ
            // 范围为 [-halfRange, +halfRange]
            float halfRange = MathF.Sqrt(
                (A * innerRx) * (A * innerRx) + (B * innerRy) * (B * innerRy));
            float minProj = -halfRange;
            float maxProj = halfRange;

            // ── 扫描步进（从中心 proj=0 向两侧展开）──
            float spacing = (float)hatchInfo.LineSpacing;
            float startProj = 0f;  // 从中心开始
            float projLimit = maxProj;

            // AverageDistribute：将 span 均分成 nGaps 份，边界→首线、线间、尾线→边界间距相等
            if (hatchInfo.AverageDistribute && maxProj > minProj)
            {
                float span = maxProj - minProj;
                int nGaps = Math.Max(2, (int)Math.Round(span / spacing));
                spacing = span / nGaps;
                startProj = minProj + spacing;
                projLimit = maxProj - spacing * 0.5f;
            }
            else
            {
                while (startProj >= minProj) startProj -= spacing;
                startProj += spacing;
            }

            int lineIndex = 0;
            for (float proj = startProj; proj <= projLimit; proj += spacing, lineIndex++)
            {
                // 局部线方程: A*x + B*y = proj（N·T 项已抵消）
                float C = proj;

                // 在局部空间求直线与轴对齐椭圆的交点
                List<SKPoint> localIntersections;
                if (IsEllipse)
                    localIntersections = IntersectLineEllipseAtOrigin(innerRx, innerRy, A, B, C);
                else
                    localIntersections = IntersectLineCircleAtOrigin(innerRx, A, B, C);

                if (localIntersections.Count >= 2)
                {
                    // 将交点映射回世界空间
                    var worldPts = new List<SKPoint>(localIntersections.Count);
                    foreach (var lp in localIntersections)
                        worldPts.Add(matrix.MapPoint(lp));

                    // 沿世界填充线方向排序
                    worldPts.Sort((a, b) =>
                    {
                        float ta = a.X * dx + a.Y * dy;
                        float tb = b.X * dx + b.Y * dy;
                        return ta.CompareTo(tb);
                    });

                    // 本行方向
                    bool reverseLine = reverseAll;
                    if (bidirectional && (lineIndex & 1) == 1) reverseLine = !reverseLine;

                    // 两两配对
                    for (int i = 0; i < worldPts.Count - 1; i += 2)
                    {
                        SKPoint p1 = worldPts[i];
                        SKPoint p2 = worldPts[i + 1];

                        if (extension != 0f)
                        {
                            float edx = p2.X - p1.X, edy = p2.Y - p1.Y;
                            float len = MathF.Sqrt(edx * edx + edy * edy);
                            if (len + 2f * extension <= 1e-6f) continue;
                            if (len > 1e-6f)
                            {
                                float ux = edx / len, uy = edy / len;
                                p1 = new SKPoint(p1.X - ux * extension, p1.Y - uy * extension);
                                p2 = new SKPoint(p2.X + ux * extension, p2.Y + uy * extension);
                            }
                            else if (extension <= 0f)
                            {
                                continue;
                            }
                        }

                        fillLines.Add(reverseLine ? (p2, p1) : (p1, p2));
                    }
                }
            }

            return fillLines;
        }

        /// <summary>
        /// 同心圆/椭圆填充（支持方向类型和变动起点）。
        /// 从外圈向内逐圈生成圆/椭圆轮廓线段，圈距由 FillRingSpacing 控制。
        /// 方向类型：
        ///   0 = 向内（固定起点-右侧0度）
        ///   1 = 向外（固定起点-右侧0度）
        ///   2 = 向内（变动起点-四个象限循环）
        ///   3 = 向外（变动起点-四个象限循环）
        ///   4 = 向内再向外（变动起点-四个象限循环）
        ///   5 = 向外再向内（变动起点-四个象限循环）
        /// </summary>
        private List<(SKPoint Start, SKPoint End)> GetConcentricFillLines2(HatchParamDto hatchInfo)
        {
            var result = new List<(SKPoint Start, SKPoint End)>();

            float spacing = hatchInfo.RingSpacing > 0 ? (float)hatchInfo.RingSpacing : (float)hatchInfo.LineSpacing;
            if (spacing <= 0)
                return result;

            float margin = (float)hatchInfo.Margin;
            bool reverseAll = hatchInfo.ReverseFillLine;
            // 方向：0=向内（固定起点）、1=向外（固定起点）、2=向内（变动起点）、3=向外（变动起点）、4=向内再向外（变动起点）、5=向外再向内（变动起点）
            int directionType = hatchInfo.DirectionTypeIndex;

            // 起始半径（应用 margin 后）
            float maxRx = DrawingRadiusX - margin;
            float maxRy = (IsEllipse ? DrawingRadiusY : DrawingRadiusX) - margin;

            if (maxRx <= 0 || maxRy <= 0)
                return result;

            // 计算最大圈数
            float minRadius = Math.Min(maxRx, maxRy);
            int maxPossibleTurns = (int)(minRadius / spacing);
            if (maxPossibleTurns < 1) maxPossibleTurns = 1;

            int totalTurns = hatchInfo.InternalRings > 0
                ? Math.Min(hatchInfo.InternalRings, maxPossibleTurns)
                : maxPossibleTurns;

            // 收集所有圈的椭圆数据
            var ellipses = new List<EllipseInfo>();
            float currentRx = maxRx;
            float currentRy = maxRy;
            int turnIndex = 0;

            while (currentRx > 0 && currentRy > 0 && turnIndex <= totalTurns)
            {
                ellipses.Add(new EllipseInfo
                {
                    RadiusX = currentRx,
                    RadiusY = currentRy,
                    TurnIndex = turnIndex
                });

                currentRx -= spacing;
                currentRy -= spacing;
                turnIndex++;
            }

            if (ellipses.Count == 0) return result;

            // 根据方向类型生成线段
            switch (directionType)
            {
                case 0: // 向内（固定起点-右侧0度）
                    for (int i = 0; i < ellipses.Count; i++)
                    {
                        if (i == 0 && margin == 0) continue;
                        AddEllipseSegments(result, ellipses[i], startAngle: 0, reverse: reverseAll);
                    }
                    break;

                case 1: // 向外（固定起点-右侧0度）- 从内向外输出
                    for (int i = ellipses.Count - 1; i >= 0; i--)
                    {
                        if (i == 0 && margin == 0) continue;
                        AddEllipseSegments(result, ellipses[i], startAngle: 0, reverse: reverseAll);
                    }
                    break;

                case 2: // 向内（变动起点-四个象限循环：0°, 90°, 180°, 270°）
                    for (int i = 0; i < ellipses.Count; i++)
                    {
                        if (i == 0 && margin == 0) continue;
                        int startAngle = (i % 4); // 0°, 90°, 180°, 270° 循环
                        AddEllipseSegments(result, ellipses[i], startAngle: startAngle, reverse: reverseAll);
                    }
                    break;

                case 3: // 向外（变动起点-四个象限循环）- 从内向外输出
                    for (int i = ellipses.Count - 1; i >= 0; i--)
                    {
                        if (i == 0 && margin == 0) continue;
                        int startAngle = (i % 4);
                        AddEllipseSegments(result, ellipses[i], startAngle: startAngle, reverse: reverseAll);
                    }
                    break;

                case 4: // 向内再向外（变动起点-四个象限循环）
                        // 第一段：向内
                    for (int i = 0; i < ellipses.Count; i++)
                    {
                        if (i == 0 && margin == 0) continue;
                        int startAngle = (i % 4);
                        AddEllipseSegments(result, ellipses[i], startAngle: startAngle, reverse: reverseAll);
                    }
                    // 第二段：向外（跳过最内圈避免重复）
                    for (int i = ellipses.Count - 2; i >= 0; i--)
                    {
                        if (i == 0 && margin == 0) continue;
                        int startAngle = (i % 4);
                        AddEllipseSegments(result, ellipses[i], startAngle: startAngle, reverse: reverseAll);
                    }
                    break;

                case 5: // 向外再向内（变动起点-四个象限循环）
                        // 第一段：向外
                    for (int i = ellipses.Count - 1; i >= 0; i--)
                    {
                        if (i == 0 && margin == 0) continue;
                        int startAngle = (i % 4);
                        AddEllipseSegments(result, ellipses[i], startAngle: startAngle, reverse: reverseAll);
                    }
                    // 第二段：向内（跳过最外圈避免重复）
                    for (int i = 1; i < ellipses.Count; i++)
                    {
                        if (i == 0 && margin == 0) continue;
                        int startAngle = (i % 4);
                        AddEllipseSegments(result, ellipses[i], startAngle: startAngle, reverse: reverseAll);
                    }
                    break;

                default:
                    // 默认向内（固定起点）
                    for (int i = 0; i < ellipses.Count; i++)
                    {
                        if (i == 0 && margin == 0) continue;
                        AddEllipseSegments(result, ellipses[i], startAngle: 0, reverse: reverseAll);
                    }
                    break;
            }

            // ✅ 将局部坐标转换到世界坐标
            var matrix = GetTransformMatrix();

            for (int i = 0; i < result.Count; i++)
            {
                var startWorld = matrix.MapPoint(result[i].Item1);
                var endWorld = matrix.MapPoint(result[i].Item2);
                result[i] = ((startWorld, endWorld));
            }

            return result;
        }

        /// <summary>
        /// 椭圆信息
        /// </summary>
        private class EllipseInfo
        {
            public float RadiusX { get; set; }
            public float RadiusY { get; set; }
            public int TurnIndex { get; set; }
        }

        /// <summary>
        /// 添加椭圆/圆形轮廓线段
        /// </summary>
        /// <param name="result">结果列表</param>
        /// <param name="ellipse">椭圆信息</param>
        /// <param name="startAngle">起始角度（度）</param>
        /// <param name="reverse">是否反向绘制</param>
        private void AddEllipseSegments(List<(SKPoint Start, SKPoint End)> result, EllipseInfo ellipse, int startAngle, bool reverse)
        {
            const int segments = 72; // 每圈离散段数，可根据需要调整

            var points = new List<SKPoint>();

            // 生成椭圆上的点（逆时针方向）
            for (int i = 0; i <= segments; i++)
            {
                double angleRad = (reverse ? -1 : 1) * 2 * Math.PI * i / segments;
                float x = ellipse.RadiusX * (float)Math.Cos(angleRad);
                float y = ellipse.RadiusY * (float)Math.Sin(angleRad);
                points.Add(new SKPoint(x, y));
            }

            // 根据起始角度重新排列点集
            var reorderedPoints = ReorderPointsByStartAngle(points, startAngle);

            // 根据方向生成线段
            //if (reverse)
            //{
            //    // 反向：从尾到头
            //    for (int i = reorderedPoints.Count - 1; i > 0; i--)
            //    {
            //        result.Add((reorderedPoints[i], reorderedPoints[i - 1]));
            //    }
            //}
            //else
            //{
            //    // 正向：从头到尾
            //    for (int i = 0; i < reorderedPoints.Count - 1; i++)
            //    {
            //        result.Add((reorderedPoints[i], reorderedPoints[i + 1]));
            //    }
            //}

            // 正向：从头到尾
            for (int i = 0; i < reorderedPoints.Count - 1; i++)
            {
                result.Add((reorderedPoints[i], reorderedPoints[i + 1]));
            }

            // 关键：连接最后一个点和第一个点，形成闭合环
            if (reorderedPoints.Count >= 2)
            {
                result.Add((reorderedPoints[reorderedPoints.Count - 1], reorderedPoints[0]));
            }
        }

        /// <summary>
        /// 根据起始角度重新排列点集
        /// </summary>
        private List<SKPoint> ReorderPointsByStartAngle(List<SKPoint> points, int startIndex)
        {
            if (points == null || points.Count == 0)
                return new List<SKPoint>();

            var reordered = new List<(SKPoint, SKPoint)>();
            int n = points.Count;
            startIndex = startIndex % n;  // 处理索引越界
            if (startIndex < 0)
                startIndex += n;  // 处理负索引

            // 从 startIndex 处断开，将后半部分和前半部分拼接
            return points.Skip(startIndex).Concat(points.Take(startIndex)).ToList();





            //if (points == null || points.Count == 0)
            //    return new List<SKPoint>();

            //// 将起始角度转换为弧度
            //double startAngleRad = startAngle * Math.PI / 180.0;

            //// 找到离起始角度最近的点作为起点
            //int startIndex = 0;
            //double minAngleDiff = double.MaxValue;

            //for (int i = 0; i < points.Count; i++)
            //{
            //    double angle = Math.Atan2(points[i].Y, points[i].X);
            //    if (angle < 0) angle += 2 * Math.PI;

            //    double diff = Math.Abs(angle - startAngleRad);
            //    if (diff < minAngleDiff)
            //    {
            //        minAngleDiff = diff;
            //        startIndex = i;
            //    }
            //}

            //// 从 startIndex 处断开，将后半部分和前半部分拼接
            //var reordered = new List<SKPoint>();
            //for (int i = startIndex; i < points.Count; i++)
            //    reordered.Add(points[i]);
            //for (int i = 0; i < startIndex; i++)
            //    reordered.Add(points[i]);

            //return reordered;
        }

        /// <summary>
        /// 螺旋线填充（真正的阿基米德螺旋线）。
        /// 螺旋前进的方向为逆时针（与圆的逆时针方向一致）。
        /// DirectionTypeIndex:
        ///   0 = 向内（外→内）
        ///   1 = 向外（内→外）
        ///   2 = 向内再向外（外→内→外）
        ///   3 = 向外再向内（内→外→内）
        /// </summary>
        private List<(SKPoint Start, SKPoint End)> GetSpiralFillLines(HatchParamDto hatchInfo)
        {
            var result = new List<(SKPoint Start, SKPoint End)>();

            float spacing = hatchInfo.RingSpacing > 0 ? (float)hatchInfo.RingSpacing : (float)hatchInfo.LineSpacing;
            if (spacing <= 0)
                return result;

            float margin = (float)hatchInfo.Margin;
            float maxRx = DrawingRadiusX - margin;
            float maxRy = (IsEllipse ? DrawingRadiusY : DrawingRadiusX) - margin;

            if (maxRx <= 0 || maxRy <= 0)
                return result;

            // 螺旋方向类型：0=向内，1=向外，2=向内再向外，3=向外再向内
            int directionType = hatchInfo.DirectionTypeIndex;

            // 计算可容纳的最大圈数
            int autoTurnsX = (int)(maxRx / spacing);
            int autoTurnsY = (int)(maxRy / spacing);
            int maxPossibleTurns = Math.Min(autoTurnsX, autoTurnsY);
            if (maxPossibleTurns < 1) maxPossibleTurns = 1;

            int totalTurns = hatchInfo.InternalRings > 0
                ? Math.Min(hatchInfo.InternalRings, maxPossibleTurns)
                : maxPossibleTurns;

            float finalRx = maxRx - totalTurns * spacing;
            float finalRy = maxRy - totalTurns * spacing;
            if (finalRx < 0) finalRx = 0;
            if (finalRy < 0) finalRy = 0;

            const int segmentsPerTurn = 72;
            double angleStep = 2 * Math.PI / segmentsPerTurn;

            // 根据方向类型执行不同的螺旋逻辑
            switch (directionType)
            {
                case 0: // 向内（外→内）
                    AddSpiralInward(result, maxRx, maxRy, finalRx, finalRy, spacing, margin, totalTurns, angleStep);
                    break;
                case 1: // 向外（内→外）
                    AddSpiralOutward(result, maxRx, maxRy, finalRx, finalRy, spacing, margin, totalTurns, angleStep);
                    break;
                case 2: // 向内再向外（外→内→外）
                    AddSpiralInwardThenOutward(result, maxRx, maxRy, finalRx, finalRy, spacing, margin, totalTurns, angleStep);
                    break;
                case 3: // 向外再向内（内→外→内）
                    AddSpiralOutwardThenInward(result, maxRx, maxRy, finalRx, finalRy, spacing, margin, totalTurns, angleStep);
                    break;
                default: // 默认向内
                    AddSpiralInward(result, maxRx, maxRy, finalRx, finalRy, spacing, margin, totalTurns, angleStep);
                    break;
            }

            // ✅ 将局部坐标转换到世界坐标
            var matrix = GetTransformMatrix();

            for (int i = 0; i < result.Count; i++)
            {
                var startWorld = matrix.MapPoint(result[i].Item1);
                var endWorld = matrix.MapPoint(result[i].Item2);
                result[i] = ((startWorld, endWorld));
            }

            return result;
        }

        /// <summary>
        /// 向内螺旋（外→内）
        /// </summary>
        private void AddSpiralInward(List<(SKPoint Start, SKPoint End)> result,
            float maxRx, float maxRy, float finalRx, float finalRy,
            float spacing, float margin, int totalTurns, double angleStep)
        {
            // 最外层封闭圈
            if (margin > 0)
            {
                AddClosingRing(result, maxRx, maxRy);
            }

            AddSpiralCore(result, maxRx, maxRy, finalRx, finalRy, spacing, totalTurns, angleStep, isInward: true);

            // 内层封闭圈
            if (finalRx > 0 && finalRy > 0)
            {
                AddClosingRing(result, finalRx, finalRy);
            }
        }

        /// <summary>
        /// 向外螺旋（内→外）
        /// </summary>
        private void AddSpiralOutward(List<(SKPoint Start, SKPoint End)> result,
            float maxRx, float maxRy, float finalRx, float finalRy,
            float spacing, float margin, int totalTurns, double angleStep)
        {
            // 内层起始封闭圈
            if (finalRx > 0 && finalRy > 0 && finalRx < maxRx)
            {
                AddClosingRing(result, finalRx, finalRy);
            }

            AddSpiralCore(result, maxRx, maxRy, finalRx, finalRy, spacing, totalTurns, angleStep, isInward: false);

            // 外层封闭圈
            if (maxRx > 0 && maxRy > 0)
            {
                AddClosingRing(result, maxRx, maxRy);
            }
        }

        /// <summary>
        /// 向内再向外螺旋（外→内→外）
        /// </summary>
        private void AddSpiralInwardThenOutward(List<(SKPoint Start, SKPoint End)> result,
            float maxRx, float maxRy, float finalRx, float finalRy,
            float spacing, float margin, int totalTurns, double angleStep)
        {
            // 最外层封闭圈
            if (margin > 0)
            {
                AddClosingRing(result, maxRx, maxRy);
            }

            // 第一段：向内螺旋（外→内）
            AddSpiralCore(result, maxRx, maxRy, finalRx, finalRy, spacing, totalTurns, angleStep, isInward: true);

            // 内层中继封闭圈（螺旋转折点）
            if (finalRx > 0 && finalRy > 0)
            {
                AddClosingRing(result, finalRx, finalRy);
            }

            // 第二段：向外螺旋（内→外）
            AddSpiralCore(result, maxRx, maxRy, finalRx, finalRy, spacing, totalTurns, angleStep, isInward: false);

            // 最外层封闭圈
            if (maxRx > 0 && maxRy > 0)
            {
                AddClosingRing(result, maxRx, maxRy);
            }
        }

        /// <summary>
        /// 向外再向内螺旋（内→外→内）
        /// </summary>
        private void AddSpiralOutwardThenInward(List<(SKPoint Start, SKPoint End)> result,
            float maxRx, float maxRy, float finalRx, float finalRy,
            float spacing, float margin, int totalTurns, double angleStep)
        {
            // 内层起始封闭圈
            if (finalRx > 0 && finalRy > 0 && finalRx < maxRx)
            {
                AddClosingRing(result, finalRx, finalRy);
            }

            // 第一段：向外螺旋（内→外）
            AddSpiralCore(result, maxRx, maxRy, finalRx, finalRy, spacing, totalTurns, angleStep, isInward: false);

            // 外层中继封闭圈（螺旋转折点）
            if (maxRx > 0 && maxRy > 0)
            {
                AddClosingRing(result, maxRx, maxRy);
            }

            // 第二段：向内螺旋（外→内）
            AddSpiralCore(result, maxRx, maxRy, finalRx, finalRy, spacing, totalTurns, angleStep, isInward: true);

            // 内层封闭圈
            if (finalRx > 0 && finalRy > 0)
            {
                AddClosingRing(result, finalRx, finalRy);
            }
        }

        /// <summary>
        /// 螺旋线核心生成算法
        /// </summary>
        private void AddSpiralCore(List<(SKPoint Start, SKPoint End)> result,
            float maxRx, float maxRy, float finalRx, float finalRy,
            float spacing, int totalTurns, double angleStep, bool isInward)
        {
            if (IsEllipse)
            {
                // ── 椭圆螺旋线（逆时针，角度递增）──
                double startAngle = 0;
                double endAngle = totalTurns * 2 * Math.PI;

                SKPoint? prev = null;
                for (double a = startAngle; a <= endAngle + angleStep * 0.5; a += angleStep)
                {
                    double t = a / (2 * Math.PI);  // 已完成圈数

                    float rx, ry;
                    if (isInward)
                    {
                        // 向内：半径从大到小
                        rx = maxRx - (float)t * spacing;
                        ry = maxRy - (float)t * spacing;
                        if (rx < finalRx) rx = finalRx;
                        if (ry < finalRy) ry = finalRy;
                    }
                    else
                    {
                        // 向外：半径从小到大
                        rx = finalRx + (float)t * spacing;
                        ry = finalRy + (float)t * spacing;
                        if (rx > maxRx) rx = maxRx;
                        if (ry > maxRy) ry = maxRy;
                    }

                    float x = rx * (float)Math.Cos(a);
                    float y = ry * (float)Math.Sin(a);
                    var pt = new SKPoint(x, y);

                    if (prev.HasValue)
                        result.Add((prev.Value, pt));
                    prev = pt;
                }
            }
            else
            {
                // ── 圆形阿基米德螺旋线（逆时针，角度递增）──
                // 使用极坐标方程：r(θ) = r0 + a·θ
                // 其中 a = spacing / (2π)
                double a = spacing / (2 * Math.PI);

                double startAngle, endAngle;

                if (isInward)
                {
                    // 向内：从外圈出发，角度递增，半径递减
                    startAngle = 0;
                    endAngle = (maxRx - finalRx) / a;
                }
                else
                {
                    // 向外：从内圈出发，角度递增，半径递增
                    startAngle = 0;
                    endAngle = (maxRx - finalRx) / a;
                }

                SKPoint? prev = null;
                double currentAngle = startAngle;

                while (currentAngle <= endAngle + angleStep * 0.5)
                {
                    double r;
                    if (isInward)
                    {
                        r = maxRx - a * currentAngle;
                        if (r < finalRx) r = finalRx;
                    }
                    else
                    {
                        r = finalRx + a * currentAngle;
                        if (r > maxRx) r = maxRx;
                    }

                    float x = (float)(r * Math.Cos(currentAngle));
                    float y = (float)(r * Math.Sin(currentAngle));
                    var pt = new SKPoint(x, y);

                    if (prev.HasValue)
                        result.Add((prev.Value, pt));
                    prev = pt;

                    currentAngle += angleStep;  // 角度始终递增，产生逆时针轨迹
                }
            }
        }

        /// <summary>
        /// 添加一个封闭的圆/椭圆圈
        /// </summary>
        private void AddClosingRing(List<(SKPoint Start, SKPoint End)> lines, float rx, float ry)
        {
            const int segments = 72;
            SKPoint prev = default;
            for (int i = 0; i <= segments; i++)
            {
                double angle = 2 * Math.PI * i / segments;
                float x = rx * (float)Math.Cos(angle);
                float y = ry * (float)Math.Sin(angle);
                var pt = new SKPoint(x, y);
                if (i > 0) lines.Add((prev, pt));
                prev = pt;
            }
        }

        #region 局部坐标系填充辅助方法

        /// <summary>
        /// 获取圆形在法向上的投影范围（局部坐标系）
        /// </summary>
        private void GetCircleProjectionRangeLocal(float radius, float normX, float normY,
            out float minProj, out float maxProj)
        {
            // 圆心在原点
            minProj = -radius;
            maxProj = radius;
        }

        /// <summary>
        /// 获取椭圆在法向上的投影范围（局部坐标系）
        /// </summary>
        private void GetEllipseProjectionRangeLocal(float rx, float ry,
            float normX, float normY, out float minProj, out float maxProj)
        {
            // 极值半径 = sqrt(rx²·nx² + ry²·ny²)
            float radius = (float)Math.Sqrt(rx * rx * normX * normX + ry * ry * normY * normY);
            minProj = -radius;
            maxProj = radius;
        }

        /// <summary>
        /// 求直线与圆的交点（局部坐标系）
        /// </summary>
        private List<SKPoint> GetLineCircleIntersectionsLocal(float cx, float cy, float radius,
            float normX, float normY, float proj, float dirX, float dirY)
        {
            var result = new List<SKPoint>();

            // 圆心到直线的距离
            float centerProj = cx * normX + cy * normY;  // = 0
            float d = Math.Abs(proj - centerProj);

            if (d >= radius) return result;

            // 半弦长
            float h = (float)Math.Sqrt(radius * radius - d * d);

            // 垂足
            float footX = cx + (proj - centerProj) * normX;
            float footY = cy + (proj - centerProj) * normY;

            // 两个交点
            result.Add(new SKPoint(footX - h * dirX, footY - h * dirY));
            result.Add(new SKPoint(footX + h * dirX, footY + h * dirY));

            return result;
        }

        /// <summary>
        /// 求直线与椭圆的交点（局部坐标系，椭圆无旋转）
        /// </summary>
        private List<SKPoint> GetLineEllipseIntersectionsLocal(float cx, float cy, float rx, float ry,
            float normX, float normY, float proj, float dirX, float dirY)
        {
            var result = new List<SKPoint>();

            // 计算垂足
            float centerProj = cx * normX + cy * normY;  // = 0
            float footX = cx + (proj - centerProj) * normX;
            float footY = cy + (proj - centerProj) * normY;

            // 在局部坐标系中，椭圆方程: x²/rx² + y²/ry² = 1
            // 代入参数方程求解 t
            float a = (dirX * dirX) / (rx * rx) + (dirY * dirY) / (ry * ry);
            float b = 2 * (footX * dirX / (rx * rx) + footY * dirY / (ry * ry));
            float c = (footX * footX) / (rx * rx) + (footY * footY) / (ry * ry) - 1;

            float delta = b * b - 4 * a * c;
            if (delta < 1e-6f) return result;

            float sqrtDelta = (float)Math.Sqrt(delta);
            float t1 = (-b - sqrtDelta) / (2 * a);
            float t2 = (-b + sqrtDelta) / (2 * a);

            // 计算交点
            result.Add(new SKPoint(footX + t1 * dirX, footY + t1 * dirY));
            result.Add(new SKPoint(footX + t2 * dirX, footY + t2 * dirY));

            return result;
        }
        /// <summary>
        /// 求直线 A*x + B*y = C 与圆心在原点的圆的交点。
        /// 直线方程由世界扫描线拉回得到，无需求方向向量。
        /// </summary>
        private static List<SKPoint> IntersectLineCircleAtOrigin(
            float radius, float A, float B, float C)
        {
            var result = new List<SKPoint>();
            float L2 = A * A + B * B;
            if (L2 < 1e-12f) return result;

            float d = MathF.Abs(C) / MathF.Sqrt(L2);  // 原点到直线的距离
            if (d >= radius - 1e-6f)
            {
                if (d <= radius + 1e-6f)
                {
                    // 相切：一个交点
                    float fx = A * C / L2;
                    float fy = B * C / L2;
                    result.Add(new SKPoint(fx, fy));
                }
                return result;
            }

            float h = MathF.Sqrt(radius * radius - d * d);  // 半弦长
            float footX = A * C / L2;
            float footY = B * C / L2;
            float invL = 1f / MathF.Sqrt(L2);
            float tx = -B * invL;  // 沿直线方向的单位向量
            float ty = A * invL;

            result.Add(new SKPoint(footX - h * tx, footY - h * ty));
            result.Add(new SKPoint(footX + h * tx, footY + h * ty));
            return result;
        }

        /// <summary>
        /// 求直线 A*x + B*y = C 与圆心在原点的轴对齐椭圆的交点。
        /// 椭圆方程: x²/rx² + y²/ry² = 1。
        /// 直线方程由世界扫描线拉回得到。
        /// </summary>
        private static List<SKPoint> IntersectLineEllipseAtOrigin(
            float rx, float ry, float A, float B, float C)
        {
            var result = new List<SKPoint>();

            // 选择主方向求解以避免除以零
            if (MathF.Abs(B) > MathF.Abs(A))
            {
                // 求解 y = (C - A*x)/B，代入椭圆
                // (B²*ry² + A²*rx²)*x² - 2*A*C*rx²*x + rx²*(C² - B²*ry²) = 0
                float a = B * B * ry * ry + A * A * rx * rx;
                float b = -2f * A * C * rx * rx;
                float c = rx * rx * (C * C - B * B * ry * ry);

                float delta = b * b - 4f * a * c;
                if (delta < 0) return result;

                float sqrtD = MathF.Sqrt(delta);
                float x1 = (-b - sqrtD) / (2f * a);
                float x2 = (-b + sqrtD) / (2f * a);
                result.Add(new SKPoint(x1, (C - A * x1) / B));
                if (delta > 1e-9f)
                    result.Add(new SKPoint(x2, (C - A * x2) / B));
            }
            else
            {
                // 求解 x = (C - B*y)/A，代入椭圆
                // (A²*rx² + B²*ry²)*y² - 2*B*C*ry²*y + ry²*(C² - A²*rx²) = 0
                float a = A * A * rx * rx + B * B * ry * ry;
                float b = -2f * B * C * ry * ry;
                float c = ry * ry * (C * C - A * A * rx * rx);

                float delta = b * b - 4f * a * c;
                if (delta < 0) return result;

                float sqrtD = MathF.Sqrt(delta);
                float y1 = (-b - sqrtD) / (2f * a);
                float y2 = (-b + sqrtD) / (2f * a);
                result.Add(new SKPoint((C - B * y1) / A, y1));
                if (delta > 1e-9f)
                    result.Add(new SKPoint((C - B * y2) / A, y2));
            }

            return result;
        }
        #endregion
        #endregion

        // ── ISnapshotable ──────────────────────────────────────────────────

        public override IShapeMemento CaptureSnapshot()
        {
            return new DrawCircleMemento(this);
        }

        protected class DrawCircleMemento : DrawObjectMemento
        {
            private readonly float _radiusX;
            private readonly float _radiusY;
            private readonly bool _isEllipse;

            public DrawCircleMemento(DrawCircle circle) : base(circle)
            {
                _radiusX = circle.RadiusX;
                _radiusY = circle.RadiusY;
                _isEllipse = circle.IsEllipse;
            }

            /// <summary>
            /// DrawCircle 的 UpdateSetProperty 会从 Points 重算 RadiusX/Y、Width、Height、SharpCenter，
            /// 因此恢复时直接赋值 Points，跳过 UpdateSetProperty，避免覆盖 RestoreTransform 恢复的变换属性。
            /// </summary>
            protected override void RestoreGeometry()
            {
                if (_points != null)
                {
                    var pointsCopy = new List<SKPoint>(_points.Count);
                    for (int i = 0; i < _points.Count; i++)
                        pointsCopy.Add(_points[i]);
                    Shape.Points = pointsCopy;
                }
                else
                {
                    Shape.Points = new List<SKPoint>();
                }
            }

            protected override void RestoreDerived()
            {
                if (Shape is DrawCircle circle)
                {
                    circle.RadiusX = _radiusX;
                    circle.RadiusY = _radiusY;
                    circle.IsEllipse = _isEllipse;
                }
            }
        }
    }
}
