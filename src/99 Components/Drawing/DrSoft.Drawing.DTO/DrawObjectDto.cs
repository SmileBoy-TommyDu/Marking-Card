using DrSoft.Drawing.Model;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace DrSoft.Drawing.DTO
{
    public class DrawObjectDto
    {
        public DrawObjectDto() { UId = UniqueIdGenerator.NextId(); }
        private List<Point2D>? _points;
        private List<Point2D>? _outlinePoints;
        private List<Point2D>? _intersectionSkipPoints;
        private List<DrawObjectDto>? _children;

        public int UId { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool Direction { get; set; } = false;// 是否有方向（如箭头线）

        //是否显示加工路径
        public bool ShowJumpLine { get; set; } = true;

        public PenDto Pen { get; set; } = new PenDto(DrawingColorDto.Black) { Width = 0.25f };
        public bool IsVisible { get; set; } = true;
        public bool IsSelected { get; set; }
        public bool IsLocked { get; set; }
        public int LayerId { get; set; }
        public ShapeType Type { get; set; }
        public List<Point2D> Points { get => _points ??= new(); set => _points = value; }
        public List<Point2D> OutlinePoints { get => _outlinePoints ??= new(); set => _outlinePoints = value; }
        public Point2D SharpCenter { get; set; }
        public Point2D RotationCenter { get; set; }

        public (Point2D[] Corners, Point2D Center) OBBInfo { get; set; }

        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; } = 0;
        public float Height { get; set; } = 0;
        public float Rotation { get; set; } = 0; // 角度制，顺时针为正
        public float ScaleX { get; set; } = 1;
        public float ScaleY { get; set; } = 1;
        public float SkewX { get; set; } = 0; // 倾斜角度
        public float SkewY { get; set; } = 0;

        //public float Width2 { get; set; } = 0;
        //public float Height2 { get; set; } = 0;

        //public Point2D SharpCenter2 { get; set; }
        //public Point2D RotationCenter2 { get; set; }

        public bool IsClockwise { get; set; } = true; // 激光加工方向：true=顺时针，false=逆时针

        /// <summary>
        /// 相交镂空点（世界坐标）：跳点功能检测到与后续图形的交点后
        /// 记在前一个图形上，打标指令生成时在这些点附近抬笔，避免重复打标。
        /// </summary>
        public List<Point2D> IntersectionSkipPoints { get => _intersectionSkipPoints ??= new(); set => _intersectionSkipPoints = value; }

        /// <summary>
        /// 相交镂空圈半径（毫米），作为单个镂空点形成的缺口半径。
        /// </summary>
        public float IntersectionSkipRadius { get; set; } = 0.5f;

        public int ChildrenCount { get; set; } = 0;
        public List<DrawObjectDto> Children { get => _children ??= new(); set => _children = value; }
        public IReadOnlyList<Point2D>? PointsOrNull => _points;
        public IReadOnlyList<Point2D>? OutlinePointsOrNull => _outlinePoints;
        public IReadOnlyList<Point2D>? IntersectionSkipPointsOrNull => _intersectionSkipPoints;
        public IReadOnlyList<DrawObjectDto>? ChildrenOrNull => _children;

        public List<DrawObjectDto> GetLeafDrawObjects()
        {
            if (_children == null || _children.Count == 0)
            {
                return new List<DrawObjectDto> { this };
            }

            return _children
                .Where(child => child != null)
                .SelectMany(child => child.GetLeafDrawObjects())
                .ToList();
        }
    }

    public class UniqueIdGenerator
    {
        private static readonly long EpochTicks = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
        private static int _count = CreateInitialCount();

        private static int CreateInitialCount()
        {
            long currentTicks = DateTime.UtcNow.Ticks;
            return (int)((currentTicks - EpochTicks) / 10000000);
        }

        public static int NextId()
        {
            return Interlocked.Increment(ref _count);
        }
    }
}
