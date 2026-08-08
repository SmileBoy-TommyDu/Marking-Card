using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Model;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace DrSoft.Drawing.Controls.DrawShapes
{
    public class DrawDot : DrawObject, IDotShapeData
    {
        private SKPoint _dotPos = new SKPoint();
        public bool IsAnchorX { get; set; }
        public bool IsAnchorY { get; set; }
        // IDotShapeData 无额外属性，CenterX/CenterY/ChildShapes 由基类处理
        // 点的半径（直径 = Pen.StrokeWidth * 2）
        private float? _radius;
        public float Radius
        {
            get => _radius ?? 2 / (2 * (DocumentContext.Instance.ActiveCanvas?.Viewport?.Scale ?? 1));
            set => _radius = value;
        }

        public override List<Point2D> OutlinePoints { get => Points.Select(it => new Point2D(it.X, it.Y)).ToList(); set => throw new NotImplementedException(); }

        public override SKPath GetPath()
        {
            var path = new SKPath();
            FillPath(path);
            return path;
        }

        protected override void FillPath(SKPath path)
        {
            // 点在本地坐标系中的位置为(0,0)，半径为Radius
            // 绘制一个圆形路径
            //path.AddCircle(0, 0, Radius);
            path.AddCircle(_dotPos.X, _dotPos.Y, Radius);
        }

        // 无参构造函数，供 AutoMapper 映射使用
        public DrawDot()
        {
            this.UId = UniqueIdGenerator.NextId();
            Points = new List<SKPoint>();
            Type = ShapeType.Point;
            Pen.Style = SKPaintStyle.Fill;
        }

        public DrawDot(SKPoint point, bool isDxf = false) : this()
        {
            if (isDxf)
            {
                InitializeFromDxfPoints(point);
                return;
            }

            Points = new List<SKPoint> { point };
            UpdateSetProperty(Points);
        }

        private void InitializeFromDxfPoints(SKPoint point)
        {
            Points = new List<SKPoint> { point };
            UpdateSetProperty(Points);
            RestoreTransformCommandSnapshot(new TransformCommandSnapshot(
                SKMatrix.CreateTranslation(point.X, point.Y),
                0f,
                1f,
                1f,
                0f,
                0f,
                point,
                SKPoint.Empty,
                SKPoint.Empty));
            Translate(point.X, point.Y, true);
        }

        protected override void OnCommittedMatrixChanged()
        {
            _dotPos = Matrix.MapPoint(new SKPoint(0, 0));

            Points.Clear();
            OutlinePoints.Clear();
            Points.Add(_dotPos);
            OutlinePoints.Add(new Point2D(_dotPos.X, _dotPos.Y));
        }

        public DrawDot(Point2D point) : this(new SKPoint(point.X, point.Y)) { }

        public override (SKPoint[] Corners, SKPoint Center) GetOBB()
        {
            SKPoint center = _dotPos;
            // 3. 构造出这个矩形的四个角点
            SKPoint[] corners = new SKPoint[]
            {
                new(center.X, center.Y),
                new(center.X, center.Y),
                new(center.X, center.Y),
                new(center.X, center.Y),
            };

            return (corners, center);
        }
        public override SKRect GetAABB()
        {
            return GetOBB().Corners.ToRect();
        }

        public override (SKPoint[] Corners, SKPoint Center) GetAABB2()
        {
            return GetOBB();
        }

        public override (SKPoint[] Corners, SKPoint Center) GetPreviewAABB()
        {
            SKPoint center;
            // 1. 获取基础矩阵
            SKMatrix finalMatrix = IsAnchorX && IsAnchorY ? Matrix : TotalPreviewMatrix;

            // 2. 如果只锚定了其中一个轴，直接修改矩阵的平移分量
            if (IsAnchorX)
            {
                finalMatrix.ScaleX = 1; // 强制抹除 X 轴的平移
            }
            if (IsAnchorY)
            {
                finalMatrix.ScaleY = 1; // 强制抹除 Y 轴的平移
            }
            center = finalMatrix.MapPoint(new SKPoint(0, 0));

            // 3. 构造出这个矩形的四个角点
            SKPoint[] corners = new SKPoint[]
            {
                new(center.X, center.Y),
                new(center.X, center.Y),
                new(center.X, center.Y),
                new(center.X, center.Y),
            };

            return (corners, center);
        }

        public override (SKPoint[] Corners, SKPoint Center) GetPreviewOBB()
        {
            return GetPreviewAABB();
        }

        public override void UpdateSetProperty(List<SKPoint> points)
        {
            if (points == null || points.Count < 1)
            {
                throw new ArgumentException("绘制点需要至少1个点!");
            }

            Points = points;
            Type = ShapeType.Point;
        }

        public override bool HitTest(SKPoint p, float tolerance = 6.0f)
        {
            if (Points.Count == 0) return false;

            var distance = SKPoint.Distance(p, _dotPos);
            if (distance < Radius) return true;
            return Math.Abs(distance - Radius) <= tolerance;
        }

        public override bool IntersectsWith(SKRect rect)
        {
            if (Points.Count == 0) return false;

            return base.IntersectsWith(rect);
        }

        public override IShape Clone()
        {
            var clonedPoints = new List<SKPoint>();
            if (Points != null)
            {
                foreach (var point in Points)
                {
                    clonedPoints.Add(point);
                }
            }

            var clone = clonedPoints.Count > 0
                ? new DrawDot(clonedPoints.First())
                : new DrawDot();

            return FinalizeClone(clone);
        }

        internal override List<IShape> CreatePartitionShapes(
            float pw,
            float ph,
            float stepX,
            float stepY,
            Func<List<List<SKPoint>>, SKRect, List<List<SKPoint>>>? clipContours = null)
        {
            return new List<IShape> { this };
        }
    }
}
