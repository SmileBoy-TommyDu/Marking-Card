using DrSoft.Drawing.Contracts;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;

namespace DrSoft.MarkCard.Model
{
    /// <summary>
    /// 矩形图形参数
    /// </summary>
    public record RectangleParameter : ShapeParameterBase, IShapeParameter
    {
        //public enum CornerMode { Round, Chamfer }
        public override ShapeType Type => ShapeType.Rectangle;
        public string Unit { get; set; } = "%";
        // public CornerMode Mode { get; set; } = CornerMode.Round;
        public int Mode { get; set; } = 1;
        public bool AllCornersSame { get; set; } = false;
        public double TopLeft { get; set; } = 0.000;
        public double TopRight { get; set; } = 0.000;
        public double BottomLeft { get; set; } = 0.000;
        public double BottomRight { get; set; } = 0.000;

    }

    public record ArcParameter : ShapeParameterBase, IShapeParameter
    {
        public override ShapeType Type => ShapeType.Arc;
        // 弧心
        public double CenterX { get; set; }
        public double CenterY { get; set; }

        // 半径
        public double RadiusX { get; set; }
        public double RadiusY { get; set; }
        public bool IsEqualRadius { get; set; } = false;

        // 起始点
        public double StartX { get; set; }
        public double StartY { get; set; }
        public double StartAngle { get; set; }

        public double MiddleX { get; set; }
        public double MiddleY { get; set; }
        public double MiddleAngle { get; set; }

        // 终止点
        public double EndX { get; set; }
        public double EndY { get; set; }
        public double EndAngle { get; set; }
    }

    public record CurveParameter : ShapeParameterBase, IShapeParameter
    {
        public override ShapeType Type => ShapeType.PolyLine;

        // 基础设定
        public bool IsClosedPath { get; set; } = false;      // 封闭形路径
        
        // 虚线设定
        public DashSettingParameter DashSettings { get; set; } = new DashSettingParameter();
    }

    /// <summary>
    /// 每组虚线的 A/B 数据
    /// </summary>
    public record DashGroupData
    {
        public double A { get; set; } = 1.0;
        public double B { get; set; } = 0.5;
    }

    public record DashSettingParameter : ShapeParameterBase, IMarkingParameter
    {
        public override ShapeType Type => ShapeType.PolyLine;
        public bool OutputAsDashed { get; set; } = false;    // 将外框输出为虚线

        // 多组设定
        public int GroupCount { get; set; } = 1;             // 组数 (1~10)
        public int SelectedGroupIndex { get; set; } = 0;     // 当前选中组的索引 (0~N-1)
        public List<DashGroupData> DashGroups { get; set; } = new() { new DashGroupData() }; // 每组的A/B数据，默认1组

        // 虚线起始偏移 (C)
        public double DashC { get; set; } = 0.0;

        // 高级对齐与缩放
        public bool IsOddEvenAlign { get; set; } = true;    // 奇偶行对齐（默认启用）
        public double EvenRowOffset { get; set; } = 0.0;     // 偶数行偏移量
        public double StartPointOffset { get; set; } = 0.0;  // 起点偏位
        public double HorizontalScale { get; set; } = 100.0; // 水平缩放 (%)
        public double VerticalScale { get; set; } = 100.0;   // 垂直缩放 (%)
    }

    public record CircleParameter : ShapeParameterBase, IShapeParameter
    {
        public override ShapeType Type => ShapeType.Circle;
        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public double RadiusX { get; set; }
        public double RadiusY { get; set; }
        public bool IsEqualRadius { get; set; } = true;
    }

    public record TextParameter : ShapeParameterBase, IShapeParameter
    {
        public override ShapeType Type => ShapeType.Text;

        public string Text { get; set; }

        // 字体与样式
        public string CurrentFontFamily { get; set; }

        // 排版参数
        public double FontSize { get; set; } 

        public bool IsBold { get; set; }
        /// <summary>
        /// 倾斜 (Italic)
        /// </summary>
        public bool IsItalic { get; set; }
        /// <summary>
        /// 底线 (Underline)
        /// </summary>
        public bool IsUnderline { get; set; }
        public bool IsVerticalLayout { get; set; }
        /// <summary>
        /// 文字对齐 (Text Align)
        /// </summary>
        public int TextAlign { get; set; } 

        public double LineHeight { get; set; } 
        public double CharSpacing { get; set; }
        

    }


    public record PolygonParameter : ShapeParameterBase, IShapeParameter
    {
        public override ShapeType Type => ShapeType.Polygon;

        // 多边形类型：五角星或正多边形
        public PolygonType SubType { get; set; } = PolygonType.Star;

        // 边数或角数
        public int SideCount { get; set; } = 5;
    }
}
