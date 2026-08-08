/*using ProtoBuf;

namespace DrSoft.Drawing.DTO
{
    /// <summary>
    /// 延迟与位移参数 DTO
    /// </summary>
    [ProtoContract]
    public record DelayParameterDto : MarkingParameterBaseDto
    {
        // --- 雕刻延迟参数 ---
        [ProtoMember(1)]
        public double StartDelay { get; set; } = 0.000;  // 起始点延迟
        [ProtoMember(2)]
        public double CornerDelay { get; set; } = 0.100; // 转角延迟
        [ProtoMember(3)]
        public double EndDelay { get; set; } = 0.300;    // 终止点延迟
        [ProtoMember(4)]
        public double EngraveDelay { get; set; } = 0.300; // 雕刻延迟

        // --- 位移参数 ---
        [ProtoMember(5)]
        public double JumpSpeed { get; set; } = 8000.0;  // 位移速度
        [ProtoMember(6)]
        public double JumpDelay { get; set; } = 3.000;   // 位移延迟
    }

    /// <summary>
    /// 矩形图形参数
    /// </summary>
    [ProtoContract]
    public record RectangleParameterDto : MarkingParameterBaseDto
    {
        public enum CornerMode { Round, Chamfer }
        [ProtoIgnore]
        public  ShapeType Type => ShapeType.Rectangle;
        [ProtoMember(7)]
        public string Unit { get; init; } = "%";
        [ProtoMember(8)]
        public CornerMode Mode { get; init; } = CornerMode.Round;
        [ProtoMember(9)]
        public bool AllCornersSame { get; init; } = false;
        [ProtoMember(10)]
        public double TopLeft { get; init; } = 0.000;
        [ProtoMember(11)]
        public double TopRight { get; init; } = 0.000;
        [ProtoMember(12)]
        public double BottomLeft { get; init; } = 0.000;
        [ProtoMember(13)]
        public double BottomRight { get; init; } = 0.000;

    }

    [ProtoContract]
    public record ArcParameterDto : MarkingParameterBaseDto
    {
        [ProtoIgnore]
        public  ShapeType Type => ShapeType.Arc;
        [ProtoMember(14)]
        public double CenterX { get; init; }
        [ProtoMember(15)]
        public double CenterY { get; init; }
        [ProtoMember(16)]
        public double RadiusX { get; init; }
        [ProtoMember(17)]
        public double RadiusY { get; init; }
        [ProtoMember(18)]
        public bool IsEqualRadius { get; init; } = true;
        [ProtoMember(19)]
        public double StartX { get; init; }
        [ProtoMember(20)]
        public double StartY { get; init; }
        [ProtoMember(21)]
        public double StartAngle { get; init; }
        [ProtoMember(22)]
        public double EndX { get; init; }
        [ProtoMember(23)]
        public double EndY { get; init; }
        [ProtoMember(24)]
        public double EndAngle { get; init; }
    }

    [ProtoContract]
    public record PolyLineParameterDto : MarkingParameterBaseDto
    {
        [ProtoIgnore]
        public  ShapeType Type => ShapeType.PolyLine;

        // 基础设定
        [ProtoMember(25)]
        public bool IsClosedPath { get; init; } = false;      // 封闭形路径
        [ProtoMember(26)]
        public bool OutputAsDashed { get; init; } = false;    // 将外框输出为虚线

        // 虚线参数 (A: 线段长度，B: 间隔长度，C: 起始偏移)
        [ProtoMember(27)]
        public double DashA { get; init; } = 1.0;
        [ProtoMember(28)]
        public double DashB { get; init; } = 1.0;
        [ProtoMember(29)]
        public double DashC { get; init; } = 0.0;

        // 高级对齐与缩放
        [ProtoMember(30)]
        public bool IsOddEvenAlign { get; init; } = false;    // 奇偶行对齐
        [ProtoMember(31)]
        public double EvenRowOffset { get; init; } = 0.0;     // 偶数行偏移量
        [ProtoMember(32)]
        public double StartPointOffset { get; init; } = 0.0;  // 起点偏位
        [ProtoMember(33)]
        public double HorizontalScale { get; init; } = 100.0; // 水平缩放 (%)
        [ProtoMember(34)]
        public double VerticalScale { get; init; } = 100.0;   // 垂直缩放 (%)
    }

    [ProtoContract]
    public record CircleParameterDto : MarkingParameterBaseDto
    {
        [ProtoIgnore]
        public  ShapeType Type => ShapeType.Circle;
        [ProtoMember(35)]
        public double CenterX { get; init; }
        [ProtoMember(36)]
        public double CenterY { get; init; }
        [ProtoMember(37)]
        public double RadiusX { get; init; }
        [ProtoMember(38)]
        public double RadiusY { get; init; }
        [ProtoMember(39)]
        public bool IsEqualRadius { get; init; } = true;
    }

    [ProtoContract]
    public record TextParameterDto : MarkingParameterBaseDto
    {
        [ProtoIgnore]
        public  ShapeType Type => ShapeType.Text;

        // 字体与样式
        [ProtoMember(40)]
        public string ChineseFont { get; init; }
        [ProtoMember(41)]
        public bool IsChineseBold { get; init; } = false;
        [ProtoMember(42)]
        public bool IsChineseItalic { get; init; } = false;

        [ProtoMember(43)]
        public string OtherFont { get; init; } = "T-";
        [ProtoMember(44)]
        public bool IsOtherBold { get; init; } = false;
        [ProtoMember(45)]
        public bool IsOtherItalic { get; init; } = false;

        // 排版参数
        [ProtoMember(46)]
        public double FontSize { get; init; } = 14;
        [ProtoMember(47)]
        public double CharSpacing { get; init; } = 0;
        [ProtoMember(48)]
        public double LineHeight { get; init; } = 14;
        [ProtoMember(49)]
        public double SlantAngle { get; init; } = 0;
    }

    public enum PolygonType { Star, Regular }
    [ProtoContract]
    public record PolygonParameterDto : MarkingParameterBaseDto
    {
        [ProtoIgnore]
        public  ShapeType Type => ShapeType.Polygon;

        // 多边形类型：五角星或正多边形
        [ProtoMember(50)]
        public PolygonType SubType { get; init; } = PolygonType.Star;

        // 边数或角数
        [ProtoMember(51)]
        public int SideCount { get; init; } = 5;
    }

    public enum CopyModeDto { Matrix, Circular }
    public record MatrixCopyParameterDto : MarkingParameterBaseDto
    {
        public CopyModeDto Mode { get; set; } = CopyModeDto.Matrix;

        #region 矩阵复制参数
        public int RowCount { get; init; } = 1;
        public double RowSpacing { get; init; } = 10.0;
        public int ColumnCount { get; init; } = 1;
        public double ColumnSpacing { get; init; } = 10.0;
        public int OrderIndex { get; init; } = 0; // 复制顺序图标索引
        #endregion

        #region 环状复制参数
        public double Radius { get; init; } = 50.0;
        public int Count { get; init; } = 4;
        public double StartAngle { get; init; } = 0;
        public double IntervalAngle { get; init; } = 90.0;
        public bool IsAverageDistribute { get; init; } = false;
        public bool IsObjectRotate { get; init; } = true;
        public bool IsCounterClockwise { get; init; } = true;
        #endregion
    }
}
*/