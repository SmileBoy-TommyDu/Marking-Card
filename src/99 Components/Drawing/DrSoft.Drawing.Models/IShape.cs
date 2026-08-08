using SkiaSharp;

namespace DrSoft.Drawing.Model
{
    public interface IShape : ITransformService, IBoundable
    {
        int UId { get; set; }
        string Name { get; set; }
        int LayerId { get; set; }
        bool IsSelected { get; set; }
        bool CanTransform { get; }
        bool IsClockwise { get; set; }
        bool IsVisible { get; set; }
        bool IsLocked { get; set; }
        bool IsPathEditing { get; set; }
        List<SKPoint> PathNodes { get; set; }
        SKPaint Pen { get; set; }
        ShapeType Type { get; set; }
        public List<SKPoint> Points { get; set; }
        bool HitTest(SKPoint point, float tolerance = 6.0f);
        float GetDistanceToPath(SKPoint worldPoint);
        public abstract SKRect GetAABB();
        public abstract (SKPoint[] Corners, SKPoint Center) GetAABB2();
        public abstract (SKPoint[] Corners, SKPoint Center) GetOBB();
        public abstract float Width { get; }
        public abstract float Height { get; }
        public abstract SKPoint SharpCenter { get; }
        public abstract float Rotation { get; set; }
        public abstract float ScaleX { get; set; }
        public abstract float ScaleY { get; set; }
        public abstract float SkewX { get; set; }
        public abstract float SkewY { get; set; }
        public abstract void SetRotationCenter(SKPoint point);
        public SKPoint RotationCenter { get; }

        //public abstract SKPoint SkewCenter { get; set; }
        IShape Clone();
        // 对于复杂图形（如群组/组合/填充），Flatten方法将返回一个包含所有基本形状的列表，方便处理和渲染。
        IEnumerable<IShape> Flatten();
        void ApplyMirror(bool isHorizontal, SKPoint anchor, bool commit = false);
        float GetWorldRotationRad();
        /// <summary>
        /// Flatten() 返回的元素数量（含递归子级）。
        /// 叶子图形固定返回 1；容器图形返回所有递归子级总数。
        /// O(1) 懒缓存，避免 Flatten().Count() 的 O(n) 全量遍历。
        /// </summary>
        int FlattenCount { get; }
    }

    public class SelectChangedInfo
    {
        public int Count { get; set; }
        public bool AllPathEditing { get; set; }
        public bool IsSelectedMoveNode { get; set; }
        public int SelectedNodeCount { get; set; }
    }

    public record struct Pen(
    DrawingColor Color,
    float Width = 0.25f,
    PenStyle Style = PenStyle.Solid);

    public enum PenStyle { Solid, Dashed, Dotted };

    public record struct DrawingColor(
     byte R,
     byte G,
     byte B,
     byte A = 255)
    {
        public static DrawingColor Black => new(0, 0, 0);
        public static DrawingColor Gray => new(128, 128, 128);
        public static DrawingColor Blue => new(0, 100, 220);
        public static DrawingColor Red => new(220, 50, 50);
    }

    public record struct Point2D(
    float X,
    float Y)
    {
        public static Point2D Zero => new(0, 0);
        public double DistanceTo(Point2D other) =>
            Math.Sqrt(Math.Pow(X - other.X, 2) + Math.Pow(Y - other.Y, 2));
    }

    public record struct Rect2D(
       float X, // 左上角的X坐标
       float Y, // 左下角的Y坐标
       float Width, // 宽度（向右为正）
       float Height, // 高度（向上为正）
       float Rotation = 0,  // 旋转角度（弧度）
       List<Point2D>? Vertices = null)  // 可选：存储旋转后的顶点
    {
        public float Left => X;
        public float Top => Y;

        public float Right => X + Width;
        public float Bottom => Y + Height;
        
        // 中心点
        public Point2D Center
        {
            get
            {
                // 如果有顶点数据，使用顶点的平均值作为中心（更准确，特别是对于旋转图形）
                if (Vertices != null && Vertices.Count > 0)
                {
                    float sumX = 0, sumY = 0;
                    foreach (var vertex in Vertices)
                    {
                        sumX += vertex.X;
                        sumY += vertex.Y;
                    }
                    return new Point2D(sumX / Vertices.Count, sumY / Vertices.Count);
                }
                // 否则使用轴对齐矩形的中心
                return new Point2D(X + Width / 2, Y + Height / 2);
            }
        }
        
        //public bool Contains(Point2D p) => p.X >= X && p.X <= Right && p.Y >= Y && p.Y <= Bottom;
        //public bool IntersectsWith(Rect2D o) =>
        //    X < o.Right && Right > o.X && Y < o.Bottom && Bottom > o.Y;
        public static Rect2D FromPoints(Point2D a, Point2D b) =>
            new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
                Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        
        // 获取顶点列表（如果提供了Vertices则返回，否则计算轴对齐矩形的顶点）
        public List<Point2D> GetVertices()
        {
            if (Vertices != null && Vertices.Count >= 4)
                return Vertices;
            
            // 返回轴对齐矩形的四个顶点（左上、右上、右下、左下，顺时针顺序）
            return new List<Point2D>
            {
                new(X, Y),
                new(X + Width, Y),
                new(X + Width, Y + Height),
                new(X, Y + Height)
            };
        }

        public bool Contains(SKPoint p)
        {
            // 如果有顶点数据，使用多边形点包含测试
            if (Vertices != null && Vertices.Count >= 4)
            {
                return IsPointInPolygon(p, GetVertices());
            }

            // 否则使用轴对齐矩形的简单边界检查
            return p.X >= X && p.X <= Right && p.Y >= Y && p.Y <= Bottom;
        }

        public bool IntersectsWith(Rect2D o)
        {
            // 获取两个矩形的顶点
            var thisVertices = GetVertices();
            var otherVertices = o.GetVertices();

            // 使用分离轴定理进行精确检测
            return PolygonsIntersect(thisVertices, otherVertices);
        }

        // 判断点是否在多边形内部（使用射线投射算法）
        private bool IsPointInPolygon(SKPoint point, List<Point2D> vertices)
        {
            int intersections = 0;
            int vertexCount = vertices.Count;

            for (int i = 0; i < vertexCount; i++)
            {
                Point2D current = vertices[i];
                Point2D next = vertices[(i + 1) % vertexCount]; // 循环到第一个顶点

                // 检查水平射线是否与边相交
                if (((current.Y > point.Y) != (next.Y > point.Y)) &&
                    (point.X < (next.X - current.X) * (point.Y - current.Y) / (next.Y - current.Y) + current.X))
                {
                    intersections++;
                }
            }

            // 如果交点数量为奇数，则点在多边形内部
            return intersections % 2 == 1;
        }

        // 使用分离轴定理判断两个多边形是否相交
        private bool PolygonsIntersect(List<Point2D> polygon1, List<Point2D> polygon2)
        {
            // 收集所有潜在的分离轴
            var allAxes = new List<Point2D>();

            // 添加polygon1的所有边的垂直轴
            for (int i = 0; i < polygon1.Count; i++)
            {
                var p1 = polygon1[i];
                var p2 = polygon1[(i + 1) % polygon1.Count];
                var edge = new Point2D(p2.X - p1.X, p2.Y - p1.Y);
                // 垂直轴（逆时针旋转90度）
                var perpendicular = new Point2D(-edge.Y, edge.X);
                // 归一化
                var length = (float)Math.Sqrt(perpendicular.X * perpendicular.X + perpendicular.Y * perpendicular.Y);
                if (length > 0)
                {
                    perpendicular = new Point2D(perpendicular.X / length, perpendicular.Y / length);
                }
                allAxes.Add(perpendicular);
            }

            // 添加polygon2的所有边的垂直轴
            for (int i = 0; i < polygon2.Count; i++)
            {
                var p1 = polygon2[i];
                var p2 = polygon2[(i + 1) % polygon2.Count];
                var edge = new Point2D(p2.X - p1.X, p2.Y - p1.Y);
                // 垂直轴（逆时针旋转90度）
                var perpendicular = new Point2D(-edge.Y, edge.X);
                // 归一化
                var length = (float)Math.Sqrt(perpendicular.X * perpendicular.X + perpendicular.Y * perpendicular.Y);
                if (length > 0)
                {
                    perpendicular = new Point2D(perpendicular.X / length, perpendicular.Y / length);
                }
                allAxes.Add(perpendicular);
            }

            // 检查每个轴上的投影是否有重叠
            foreach (var axis in allAxes)
            {
                var projection1 = ProjectPolygonOntoAxis(polygon1, axis);
                var projection2 = ProjectPolygonOntoAxis(polygon2, axis);

                // 如果在任何轴上没有重叠，则多边形不相交
                if (!DoProjectionsOverlap(projection1, projection2))
                {
                    return false;
                }
            }

            // 如果在所有轴上都有重叠，则多边形相交
            return true;
        }

        private (double Min, double Max) ProjectPolygonOntoAxis(List<Point2D> polygon, Point2D axis)
        {
            double min = double.PositiveInfinity;
            double max = double.NegativeInfinity;

            foreach (var point in polygon)
            {
                // 计算点在轴上的投影（点积）
                double projection = point.X * axis.X + point.Y * axis.Y;
                min = Math.Min(min, projection);
                max = Math.Max(max, projection);
            }

            return (min, max);
        }

        private bool DoProjectionsOverlap((double Min, double Max) proj1, (double Min, double Max) proj2)
        {
            return proj1.Max >= proj2.Min && proj2.Max >= proj1.Min;
        }

    }
}
