using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using DrSoft.Drawing.Contracts;
using DrSoft.Drawing.Controls.Algorithm;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Controls.Rendering;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using SkiaSharp;
using static DrSoft.Drawing.Controls.Rendering.HatchRenderHelper;

namespace DrSoft.Drawing.Controls.DrawShapes
{
    /// <summary>
    /// 圆弧图形（统一为标准椭圆弧模型：中心 + RadiusX/Y + 起止角度）。
    /// 三点构造时自动换算为标准参数。参考 DrawCircle 的 GetPath/FillPath/UpdateSetProperty 模式。
    /// 实现 <see cref="IArcShapeData"/> 只读数据契约。
    /// </summary>
    public class DrawArc : DrawObject, IHatchable, IArcShapeData
    {
        // ── IArcShapeData 显式实现 ──
        float IArcShapeData.Radius => (float)Radius;
        float IArcShapeData.RadiusX => (float)DrawingRadiusX;
        float IArcShapeData.RadiusY => (float)DrawingRadiusY;
        float IArcShapeData.StartAngle => (float)StartAngle;
        float IArcShapeData.SweepAngle => (float)SweepAngle;
        float IArcShapeData.EndAngle => (float)StartAngle + (float)SweepAngle;
        float IArcShapeData.CircumcircleCenterX => CircumcircleCenter.X;
        float IArcShapeData.CircumcircleCenterY => CircumcircleCenter.Y;
        float IArcShapeData.StartX => GetWorldPoints().Count > 0 ? GetWorldPoints()[0].X : 0f;
        float IArcShapeData.StartY => GetWorldPoints().Count > 0 ? GetWorldPoints()[0].Y : 0f;
        float IArcShapeData.EndX => GetWorldPoints().Count > 2 ? GetWorldPoints()[2].X : 0f;
        float IArcShapeData.EndY => GetWorldPoints().Count > 2 ? GetWorldPoints()[2].Y : 0f;
        float IShapeData.CenterX => SharpCenter.X;
        float IShapeData.CenterY => SharpCenter.Y;

        // ── 内部字段（参考 DrawCircle 的 DrawingRadiusX/Y） ──
        private float _drawingRadiusX = 0;   // 本地渲染半径 X（不随变换变）
        private float _drawingRadiusY = 0;   // 本地渲染半径 Y
        private float _arcStartAngle = 0;    // 起始角度（度）
        private float _arcSweepAngle = 0;    // 扫掠角度（度，正逆时针）
        public SKPoint localCenter;         //局部中心
        private bool _suppressPropertyPropagation = false;

        // ── 公共属性 ──
        public float DrawingRadiusX => _drawingRadiusX;
        public float DrawingRadiusY => _drawingRadiusY;
        public double RadiusX => _drawingRadiusX;
        public double RadiusY => _drawingRadiusY;
        public double Radius => (_drawingRadiusX + _drawingRadiusY) / 2.0;
        public double StartAngle => (_arcStartAngle - Rotation);
        public double SweepAngle => _arcSweepAngle;

        /// <summary>
        /// 椭圆中心的世界坐标。本地椭圆中心 = (-_unitBoundsCenterX*rx, -_unitBoundsCenterY*ry)，
        /// 经 Matrix 变换后得到世界坐标。
        /// </summary>
        public SKPoint CircumcircleCenter
        {
            get
            {
                return GetTransformMatrix().MapPoint(new SKPoint(0, 0));
            }
        }

        public SKPoint StartPoint => Points.Count > 0 ? Points[0] : SKPoint.Empty;
        public SKPoint MiddlePoint => Points.Count > 1 ? Points[1] : SKPoint.Empty;
        public SKPoint EndPoint => Points.Count > 2 ? Points[2] : SKPoint.Empty;
        public ArcType TypeOfArc => ArcType.CenterRadius;

        public SKPoint? PreviewLineEndPoint { get; set; }
        public SKPoint? PreviewLineEndPoint2 { get; set; }
        private List<SKPoint> _localPoints = new List<SKPoint>();

        // ── 构造函数 ──

        public DrawArc()
        {
            UId = UniqueIdGenerator.NextId();
            Type = ShapeType.Arc;
        }

        public DrawArc(SKPoint startPoint, SKPoint middlePoint, SKPoint endPoint, bool isDxf = false, float dxfRatio = 1f, bool useCenter = false) : this()
        {
            Points = new List<SKPoint> { startPoint, middlePoint, endPoint };
            if (isDxf)
            {
                InitializeFromDxfPoints(startPoint, middlePoint, endPoint, dxfRatio);
                return;
            }
            UpdateSetProperty(Points);
        }

        public DrawArc(Point2D startPoint, Point2D middlePoint, Point2D endPoint, bool isDxf = false, float dxfRatio = 1f, bool useCenter = false)
            : this(new SKPoint((float)startPoint.X, (float)startPoint.Y),
                   new SKPoint((float)middlePoint.X, (float)middlePoint.Y),
                   new SKPoint((float)endPoint.X, (float)endPoint.Y), isDxf, dxfRatio, useCenter)
        { }

        void InitializeFromDxfPoints(SKPoint startPoint, SKPoint middlePoint, SKPoint endPoint, float dxfRatio)
        {
            UpdateSetProperty(new List<SKPoint> { startPoint, middlePoint, endPoint });
            RestoreTransformCommandSnapshot(new TransformCommandSnapshot(
                SKMatrix.CreateTranslation(localCenter.X, localCenter.Y),
                0f,
                1f,
                1f,
                0f,
                0f,
                localCenter,
                SKPoint.Empty,
                SKPoint.Empty));
        }

        // ── 标准弧参数调整（属性面板编辑） ──

        internal GraphicResult AdjustArc(
            double cx, double cy,
            double rx, double ry,
            double sAngle, double eAngle)
        {
            if (IsLocked)
                return GraphicResult.Fail(GraphicErrorCode.ShapeLocked);

            _drawingRadiusX = (float)Math.Abs(rx);
            _drawingRadiusY = (float)Math.Abs(ry);
            _arcStartAngle = (float)sAngle;
            _arcSweepAngle = (float)eAngle;
            var circleCenter = new SKPoint((float)cx, (float)cy);
            Translate(circleCenter.X - CircumcircleCenter.X, circleCenter.Y - CircumcircleCenter.Y, true);

            ComputeUnitArcBounds(_arcStartAngle, _arcSweepAngle,
                out float minX, out float maxX, out float minY, out float maxY);


            // 更新 Points 为世界坐标三点（供序列化/预览使用）
            double sRad = sAngle * Math.PI / 180.0;
            double eRad = (sAngle + eAngle) * Math.PI / 180.0;
            double mRad = (sAngle + eAngle / 2) * Math.PI / 180.0;
            _suppressPropertyPropagation = true;
            try
            {
                Points = new List<SKPoint>
                {
                    new SKPoint((float)(cx + rx * Math.Cos(sRad)), (float)(cy + ry * Math.Sin(sRad))),
                    new SKPoint((float)(cx + rx * Math.Cos(mRad)), (float)(cy + ry * Math.Sin(mRad))),
                    new SKPoint((float)(cx + rx * Math.Cos(eRad)), (float)(cy + ry * Math.Sin(eRad)))
                };
            }
            finally { _suppressPropertyPropagation = false; }

            _bboxDirty = true;
            _cachedBoundingBox = null;
            return GraphicResult.Ok();
        }

        // ── GetPath / FillPath（参考 DrawCircle） ──

        public override SKPath GetPath()
        {
            var path = new SKPath();
            FillPath(path);
            return path;
        }

        /// <summary>
        /// 在本地坐标系中构建椭圆弧路径。椭圆中心偏移使 tight-bounds 中心对齐原点，
        /// 从而与 GetTransformMatrix / GetLocalBounds / ApplyCommittedBounds 机制一致。
        /// </summary>
        protected override void FillPath(SKPath path)
        {
            if (_drawingRadiusX <= 0 || _drawingRadiusY <= 0)
                return;
            ArcMath.FillEllipseArcPath(path, new SKPoint(0, 0),
                _drawingRadiusX, _drawingRadiusY, _arcStartAngle, _arcSweepAngle);
        }

        // ── UpdateSetProperty（参考 DrawCircle：从 Points 重算半径/角度） ──

        public override void UpdateSetProperty(List<SKPoint> points)
        {
            if (points == null || points.Count < 3)
                return;

            _suppressPropertyPropagation = true;
            try
            {
                Points = points;
                Type = ShapeType.Arc;



                if (Points.Count < 2)
                {
                    _localPoints = new List<SKPoint>();
                    return;
                }



                var center2 = GetPointsCenter(Points);
                _localPoints = new List<SKPoint>(Points.Count);
                foreach (var point in Points)
                {
                    _localPoints.Add(new SKPoint(point.X - center2.X, point.Y - center2.Y));
                }


                //if (HasCommittedMatrix())
                //{
                //    var inverse = Matrix.Invert();
                //    _localPoints = new List<SKPoint>(Points.Count);
                //    foreach (var point in Points)
                //    {
                //        _localPoints.Add(inverse.MapPoint(point));
                //    }
                //}




                var circ = ArcMath.Circumcircle(points[0], points[1], points[2]);
                if (!circ.HasValue)
                    return;

                var (center, radius) = circ.Value;
                _drawingRadiusX = radius;
                _drawingRadiusY = radius;
                localCenter = center;
                // 计算起始/扫掠角度（与 ArcMath.FillArcPath 一致）
                float a1 = MathF.Atan2(points[0].Y - center.Y, points[0].X - center.X);
                float am = MathF.Atan2(points[1].Y - center.Y, points[1].X - center.X);
                float a3 = MathF.Atan2(points[2].Y - center.Y, points[2].X - center.X);

                static float Norm(float a) => ((a % (2 * MathF.PI)) + 2 * MathF.PI) % (2 * MathF.PI);
                a1 = Norm(a1); am = Norm(am); a3 = Norm(a3);
                float CcwDist(float from, float to) => Norm(to - from);
                bool sweepCCW = CcwDist(a1, am) < CcwDist(a1, a3);
                float sweepRad = sweepCCW ? CcwDist(a1, a3) : -(2 * MathF.PI - CcwDist(a1, a3));

                _arcStartAngle = a1 * 180f / MathF.PI;
                _arcSweepAngle = sweepRad * 180f / MathF.PI;

                // Clear _localPoints so OnCommittedMatrixChanged does not
                // incorrectly recompute geometry from world-space Points.
                _localPoints = null;








                //Translate(localCenter.X - CircumcircleCenter.X, localCenter.Y - CircumcircleCenter.Y, true);


            }
            finally { _suppressPropertyPropagation = false; }
        }
        private bool HasCommittedMatrix()
        {
            return !Matrix.Equals(SKMatrix.Identity);
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
        protected override void OnCommittedMatrixChanged()
        {
            if (_localPoints == null || _localPoints.Count <= 2 || DocumentContext.Instance.IsDrawing)
                return;
            //Trace.WriteLine($"_localPoints:{(this._localPoints[0].X, this._localPoints[0].Y, this._localPoints[1].X, this._localPoints[1].Y, this._localPoints[2].X, this._localPoints[2].Y)}");

            //var worldPoints = new List<SKPoint>(_localPoints.Count);
            //foreach (var point in _localPoints)
            //{
            //    worldPoints.Add(Matrix.MapPoint(point));
            //}


            //Trace.WriteLine($"OriginPoints:{(Points[0].X, Points[0].Y, Points[1].X, Points[1].Y, Points[2].X, Points[2].Y)}");

            ////Points = worldPoints;
            //Trace.WriteLine($"WorldPoints:{(worldPoints[0].X, worldPoints[0].Y, worldPoints[1].X, worldPoints[1].Y, worldPoints[2].X, worldPoints[2].Y)}");


            List<SKPoint> mytest = new List<SKPoint>();
            foreach (var item in Points)
            {
                mytest.Add(new SKPoint(item.X, item.Y));
            }

            var mypoints = Matrix.MapPoints(mytest.ToArray());


            var circ = ArcMath.Circumcircle(mypoints[0], mypoints[1], mypoints[2]);
            if (!circ.HasValue)
                return;

            var (center, radius) = circ.Value;
            _drawingRadiusX = radius;
            _drawingRadiusY = radius;
            localCenter = center;
            // 计算起始/扫掠角度（与 ArcMath.FillArcPath 一致）
            float a1 = MathF.Atan2(mypoints[0].Y - center.Y, mypoints[0].X - center.X);
            float am = MathF.Atan2(mypoints[1].Y - center.Y, mypoints[1].X - center.X);
            float a3 = MathF.Atan2(mypoints[2].Y - center.Y, mypoints[2].X - center.X);

            static float Norm(float a) => ((a % (2 * MathF.PI)) + 2 * MathF.PI) % (2 * MathF.PI);
            a1 = Norm(a1); am = Norm(am); a3 = Norm(a3);
            float CcwDist(float from, float to) => Norm(to - from);
            bool sweepCCW = CcwDist(a1, am) < CcwDist(a1, a3);
            float sweepRad = sweepCCW ? CcwDist(a1, a3) : -(2 * MathF.PI - CcwDist(a1, a3));

            _arcStartAngle = a1 * 180f / MathF.PI;
            //var angle = a1 * 180f / MathF.PI;
            _arcSweepAngle = sweepRad * 180f / MathF.PI;


            _bboxDirty = true;
            _cachedBoundingBox = null;
        }

        // ── 三点弧更新（绘制过程实时预览） ──

        public void UpdateThreePointArc(SKPoint startPoint, SKPoint middlePoint, SKPoint endPoint)
        {
            Points = new List<SKPoint> { startPoint, middlePoint, endPoint };
            UpdateSetProperty(Points);
        }

        // ── 世界坐标三点（供 ArcRenderer/IArcShapeData 使用） ──

        public List<SKPoint> GetWorldPoints()
        {
            if (_drawingRadiusX <= 0 || _drawingRadiusY <= 0)
                return new List<SKPoint>();

            double sRad = _arcStartAngle * Math.PI / 180.0;
            double eRad = (_arcStartAngle + _arcSweepAngle) * Math.PI / 180.0;
            double mRad = (_arcStartAngle + _arcSweepAngle / 2) * Math.PI / 180.0;
            var matrix = GetTransformMatrix();
            float rx = _drawingRadiusX;
            float ry = _drawingRadiusY;
            return new List<SKPoint>
            {
                matrix.MapPoint(new SKPoint(((float)Math.Cos(sRad)) * rx, ((float)Math.Sin(sRad)) * ry)),
                matrix.MapPoint(new SKPoint(((float)Math.Cos(mRad)) * rx, ((float)Math.Sin(mRad)) * ry)),
                matrix.MapPoint(new SKPoint(((float)Math.Cos(eRad)) * rx, ((float)Math.Sin(eRad)) * ry))
            };
        }

        // ── OutlinePoints（采样弧线 + 矩阵变换） ──

        public override List<Point2D> OutlinePoints
        {
            get
            {
                if (_drawingRadiusX <= 0)
                    return Points.Select(p => new Point2D(p.X, p.Y)).ToList();

                var matrix = GetTransformMatrix();
                int segments = Math.Max(4, (int)Math.Ceiling(Math.Abs(_arcSweepAngle) / 3.0));
                var pts = new List<Point2D>(segments + 1);
                float step = _arcSweepAngle / segments;
                float rx = _drawingRadiusX;
                float ry = _drawingRadiusY;
                for (int i = 0; i <= segments; i++)
                {
                    float angle = _arcStartAngle + i * step;
                    float rad = angle * MathF.PI / 180f;
                    var localPt = new SKPoint(
                        (MathF.Cos(rad)) * rx,
                        (MathF.Sin(rad)) * ry);
                    var worldPt = matrix.MapPoint(localPt);
                    pts.Add(new Point2D(worldPt.X, worldPt.Y));
                }
                return pts;
            }
            set => throw new NotImplementedException();
        }

        public override SKRect GetLocalBounds() => base.GetLocalBounds();

        // ── 填充 ──
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

        public HatchPatternObjects CreateHatchPattern()
        {
            if (HatchParamInfo == null) return new HatchPatternObjects();
            var fillLines = GetFillLines(HatchParamInfo);
            var drawObjects = FillLineStyleEmitter.Convert3(fillLines, HatchParamInfo, Name);
            return new HatchPatternObjects
            {
                HatchObjects = drawObjects,
                HatchLineObjects = fillLines,
            };
        }

        public List<(SKPoint Start, SKPoint End)> GetFillLines(HatchParamDto hatchInfo)
        {
            return hatchInfo.FillTypeIndex switch
            {
                0 => GetScanlineFillLines(hatchInfo),
                1 => GetScanlineFillLines(hatchInfo),
                2 => GetConcentricFillLines(hatchInfo),
                3 => GetSpiralFillLines(hatchInfo),
                _ => new List<(SKPoint, SKPoint)>(),
            };
        }

        /// <summary>
        /// 获取本地坐标缩放因子（Width/GetLocalBounds().Width）。
        /// 填充线在世界坐标定义角度/间距，需换算到本地坐标。
        /// </summary>
        private (float scaleX, float scaleY) GetFillScaleFactors()
        {
            var localBounds = GetLocalBounds();
            float scaleX = localBounds.Width > 0.001f ? Width / localBounds.Width : 1f;
            float scaleY = localBounds.Height > 0.001f ? Height / localBounds.Height : 1f;
            return (scaleX, scaleY);
        }

        public List<(SKPoint Start, SKPoint End)> GetScanlineFillLines(HatchParamDto hatchInfo)
        {
            var fillLines = new List<(SKPoint Start, SKPoint End)>();
            if (hatchInfo.LineSpacing <= 0) return fillLines;
            if (_drawingRadiusX <= 0 || _drawingRadiusY <= 0) return fillLines;

            // World-space center and radius
            var matrix = GetTransformMatrix();
            var worldCenter = CircumcircleCenter;
            var worldEdgeX = matrix.MapPoint(new SKPoint(_drawingRadiusX, 0));
            float worldRadius = SKPoint.Distance(worldCenter, worldEdgeX);

            var arcParam = new ArcChordFillAlgorithm.ArcParam(
                center: worldCenter,
                radius: worldRadius,
                startAngle: (float)(_arcStartAngle + Rotation),
                sweepAngle: (float)SweepAngle);

            // World-space fill angle
            float targetAngleDeg = hatchInfo.RelativeToAngle
                ? (float)hatchInfo.StartAngle + Rotation
                : (float)hatchInfo.StartAngle;

            // World-space FillParams (ArcParam is already in world space)
            var fillParams = new ArcChordFillAlgorithm.FillParams
            {
                LineAngle = targetAngleDeg,
                Spacing = (float)hatchInfo.LineSpacing,
                MarginToArc = (float)hatchInfo.Margin,
                MarginToChord = (float)hatchInfo.Margin,
                ReferencePoint = worldCenter,
                Bidirectional = hatchInfo.FillTypeIndex == 1,
                AverageDistribute = hatchInfo.AverageDistribute,
                Extension = (float)hatchInfo.Extension,
                ReverseFillLine = hatchInfo.ReverseFillLine,
            };

            var algorithm = new ArcChordFillAlgorithm(arcParam, fillParams);
            return algorithm.GetFillLines();
        }

        private List<(SKPoint Start, SKPoint End)> GetConcentricFillLines(HatchParamDto info)
        {
            return new List<(SKPoint Start, SKPoint End)>();
        }

        private List<(SKPoint Start, SKPoint End)> GetSpiralFillLines(HatchParamDto info)
        {
            return new List<(SKPoint Start, SKPoint End)>();
        }

        //private List<(SKPoint Start, SKPoint End)> GetConcentricFillLines(HatchParamDto info)
        //{
        //    var result = new List<(SKPoint Start, SKPoint End)>();
        //    float spacing = info.RingSpacing > 0 ? (float)info.RingSpacing : (float)info.LineSpacing;
        //    if (spacing <= 0) return result;
        //    if (_drawingRadiusX <= 0 || _drawingRadiusY <= 0) return result;

        //    var center = new SKPoint(-localCenter.X * _drawingRadiusX, -localCenter.Y * _drawingRadiusY);
        //    float radius = _drawingRadiusX;
        //    float startAngleDeg = _arcStartAngle;
        //    float sweepAngleDeg = _arcSweepAngle;

        //    float margin = (float)info.Margin;
        //    float currentRadius = radius - margin;
        //    const int segmentsPerArc = 72;

        //    while (currentRadius > spacing * 0.5f)
        //    {
        //        float startRad = startAngleDeg * MathF.PI / 180;
        //        float endRad = (startAngleDeg + sweepAngleDeg) * MathF.PI / 180;

        //        SKPoint arcStart = new SKPoint(
        //            center.X + currentRadius * MathF.Cos(startRad),
        //            center.Y + currentRadius * MathF.Sin(startRad));
        //        float prevX = arcStart.X, prevY = arcStart.Y;
        //        for (int i = 1; i <= segmentsPerArc; i++)
        //        {
        //            float t = i / (float)segmentsPerArc;
        //            float ang = (startAngleDeg + sweepAngleDeg * t) * MathF.PI / 180;
        //            float x = center.X + currentRadius * MathF.Cos(ang);
        //            float y = center.Y + currentRadius * MathF.Sin(ang);
        //            result.Add((new SKPoint(prevX, prevY), new SKPoint(x, y)));
        //            prevX = x; prevY = y;
        //        }
        //        currentRadius -= spacing;
        //    }
        //    return result;
        //}

        // ── Clone ──

        public override IShape Clone()
        {
            var clone = new DrawArc();
            clone._drawingRadiusX = _drawingRadiusX;
            clone._drawingRadiusY = _drawingRadiusY;
            clone._arcStartAngle = _arcStartAngle;
            clone._arcSweepAngle = _arcSweepAngle;
            clone.HatchParamInfo = HatchParamInfo;
            if (Points != null)
                clone.Points = new List<SKPoint>(Points);
            return FinalizeClone(clone);
        }

        // ── 反向 ──

        internal override void ReverseDirection()
        {
            _arcStartAngle += _arcSweepAngle;
            _arcSweepAngle = -_arcSweepAngle;

            ComputeUnitArcBounds(_arcStartAngle, _arcSweepAngle,
                out float minX, out float maxX, out float minY, out float maxY);

            if (Points.Count >= 3)
            {
                var pts = new List<SKPoint>(Points);
                pts.Reverse(0, pts.Count);
                _suppressPropertyPropagation = true;
                try { Points = pts; }
                finally { _suppressPropertyPropagation = false; }
            }

            _bboxDirty = true;
            _cachedBoundingBox = null;
        }

        // ── 转曲线 ──

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
                IsClosed = false,
                Pen = Pen,
                Name = $"{Name}_折线"
            };
            children.Add(polyLine);
            return children;
        }

        // ── 快照 ──

        public override IShapeMemento CaptureSnapshot()
        {
            return new DrawArcMemento(this);
        }

        protected class DrawArcMemento : DrawObjectMemento
        {
            private readonly float _drawingRadiusX;
            private readonly float _drawingRadiusY;
            private readonly float _arcStartAngle;
            private readonly float _arcSweepAngle;

            public DrawArcMemento(DrawArc arc) : base(arc)
            {
                _drawingRadiusX = arc._drawingRadiusX;
                _drawingRadiusY = arc._drawingRadiusY;
                _arcStartAngle = arc._arcStartAngle;
                _arcSweepAngle = arc._arcSweepAngle;
            }

            /// <summary>
            /// 直接赋值 Points 并恢复弧参数，跳过 UpdateSetProperty 以避免
            /// 覆盖 RestoreTransform 恢复的变换属性。
            /// </summary>
            protected override void RestoreGeometry()
            {
                if (_points != null && _points.Count > 0)
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
                if (Shape is DrawArc arc)
                {
                    arc._drawingRadiusX = _drawingRadiusX;
                    arc._drawingRadiusY = _drawingRadiusY;
                    arc._arcStartAngle = _arcStartAngle;
                    arc._arcSweepAngle = _arcSweepAngle;
                    arc._bboxDirty = true;
                    arc._cachedBoundingBox = null;
                }
            }
        }

        // ── HitTest / IntersectsWith ──

        public override bool HitTest(SKPoint p, float tol = 6.0f)
        {
            if (Points == null || Points.Count < 3) return false;
            return base.HitTest(p, tol);
        }

        public override bool IntersectsWith(SKRect rect)
        {
            if (Points == null || Points.Count < 3) return false;
            return base.IntersectsWith(rect);
        }

        // ── 枚举 ──

        public enum ArcType
        {
            ThreePoint,
            CenterRadius
        }

        // ── 标准弧辅助方法 ──

        /// <summary>
        /// 计算单位圆上指定弧线的紧密边界
        /// </summary>
        private static void ComputeUnitArcBounds(float startAngleDeg, float sweepAngleDeg,
            out float minX, out float maxX, out float minY, out float maxY)
        {
            float sRad = startAngleDeg * MathF.PI / 180f;
            float eRad = (startAngleDeg + sweepAngleDeg) * MathF.PI / 180f;

            float sx = MathF.Cos(sRad), sy = MathF.Sin(sRad);
            float ex = MathF.Cos(eRad), ey = MathF.Sin(eRad);

            minX = MathF.Min(sx, ex); maxX = MathF.Max(sx, ex);
            minY = MathF.Min(sy, ey); maxY = MathF.Max(sy, ey);

            float[] criticals = { 0, 90, 180, 270 };
            foreach (var c in criticals)
            {
                if (IsAngleInArc(c, startAngleDeg, sweepAngleDeg))
                {
                    float cRad = c * MathF.PI / 180f;
                    float cx2 = MathF.Cos(cRad), cy2 = MathF.Sin(cRad);
                    if (cx2 < minX) minX = cx2;
                    if (cx2 > maxX) maxX = cx2;
                    if (cy2 < minY) minY = cy2;
                    if (cy2 > maxY) maxY = cy2;
                }
            }
        }

        private static bool IsAngleInArc(float angleDeg, float startDeg, float sweepDeg)
        {
            if (MathF.Abs(sweepDeg) >= 360f) return true;

            float s = ((startDeg % 360f) + 360f) % 360f;
            float e = (((startDeg + sweepDeg) % 360f) + 360f) % 360f;
            float a = ((angleDeg % 360f) + 360f) % 360f;

            if (sweepDeg > 0)
                return s < e ? (a > s && a < e) : (a > s || a < e);
            else
                return e < s ? (a > e && a < s) : (a > e || a < s);
        }
    }
}
