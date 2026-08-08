using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Model;
using DrSoft.Drawing.DTO;
using System.Collections.Generic;

namespace DrSoft.Drawing.Controls.DXFHelper.Parser
{
    // ================================================================
    // Entity models: sealed class with public fields (avoid property getter overhead)
    // Hot path creates tens of thousands per frame, keep small and simple
    // ================================================================

    public abstract class DxfEntity
    {
        public double X, Y;
        public string Layer = "0";
        public string Handle = "";
        public double Rotation;       // rotation angle (50)
        public string? XDataApp = null;
        public List<double> XDataDoubles = new();
        public bool InXData = false;  // parse state flag
    }
    public sealed class DxfText : DxfEntity
    {
        public double Height;         // text height (40)
        public string Text = "";      // text content (1)
        public DxfTextFontName FontName = DxfTextFontName.MicrosoftYaHei; // font name (7)
        public int HAlign = 0;        // horizontal alignment (72): 0=Left, 1=Center, 2=Right, 3=Aligned, 4=Middle, 5=Fit
        public int VAlign = 0;        // vertical alignment (73): 0=Baseline, 1=Bottom, 2=Middle, 3=Top
        public double Obliquing = 0;  // obliquing angle (51), degrees from vertical, 0=upright, positive=lean right
        public bool IsUnderline = false; // underline (from STYLE table or other source)
    }

    /// <summary>
    /// DXF MTEXT (multiline text) entity.
    /// Differences from TEXT:
    ///   - group 3 is text continuation (split when over 250 chars)
    ///   - group 1 is the last line of text
    ///   - text may contain formatting codes: \P (newline), {...} (font/color changes) etc.
    /// </summary>
    public sealed class DxfMText : DxfEntity
    {
        
        public double Height;         // text height (40)
        public string Text = "";      // text content (3+1 concatenated)

        public DxfTextFontName FontName = DxfTextFontName.MicrosoftYaHei; // font name (7)
        public int AttachmentPoint = 1; // attachment point (71): 1=TL,2=TC,3=TR,4=ML,5=MC,6=MR,7=BL,8=BC,9=BR
        public double Obliquing = 0;    // obliquing angle (extracted from {\Q...;} formatting), degrees
        public bool IsUnderline = false; // underline (detected from {\L ... \l} formatting codes)
    }

    public enum DxfTextFontName
    {
        Unknown = 0,
        Arial = 1,
        Calibri = 2,
        MicrosoftYaHei = 3,   // 微软雅黑
        SimSun = 4,            // 宋体
        SimHei = 5,            // 黑体
        FangSong = 6,          // 仿宋
        KaiTi = 7,             // 楷体
    }

    public static class DxfTextFontNameExtensions
    {
        /// <summary>
        /// 枚举值 → 字体字符串（用于 FontSettings.FontFamily 赋值）
        /// </summary>
        public static string ToFontString(this DxfTextFontName font) => font switch
        {
            DxfTextFontName.Arial => "Arial",
            DxfTextFontName.Calibri => "Calibri",
            DxfTextFontName.MicrosoftYaHei => "微软雅黑",
            DxfTextFontName.SimSun => "宋体",
            DxfTextFontName.SimHei => "黑体",
            DxfTextFontName.FangSong => "仿宋",
            DxfTextFontName.KaiTi => "楷体",
            _ => "微软雅黑",
        };

        /// <summary>
        /// DXF 字符串 → 枚举值（导入时解析 group 7）
        /// 支持中文名、英文名、以及 int 值字符串
        /// </summary>
        public static DxfTextFontName ParseFontName(string val)
        {
            if (string.IsNullOrWhiteSpace(val)) return DxfTextFontName.MicrosoftYaHei;

            // 优先尝试 int 解析（导出时存的是 int）
            if (int.TryParse(val.Trim(), out int intVal) && Enum.IsDefined(typeof(DxfTextFontName), intVal))
                return (DxfTextFontName)intVal;

            // 字符串匹配（兼容其他 CAD 导出的英文名/中文名）
            return val.Trim().ToLowerInvariant() switch
            {
                "arial" => DxfTextFontName.Arial,
                "calibri" => DxfTextFontName.Calibri,
                "microsoftyahei" or "microsoft yahei" or "微软雅黑" => DxfTextFontName.MicrosoftYaHei,
                "simsun" or "宋体" => DxfTextFontName.SimSun,
                "simhei" or "黑体" => DxfTextFontName.SimHei,
                "fangsong" or "仿宋" => DxfTextFontName.FangSong,
                "kaiti" or "楷体" => DxfTextFontName.KaiTi,
                _ => DxfTextFontName.MicrosoftYaHei,
            };
        }
    }
    public sealed class DxfLine : DxfEntity
    {
        public double X1, Y1, X2, Y2;
    }



    public sealed class DxfArc : DxfEntity
    {
        public double Cx, Cy, R, StartAngle, EndAngle;
      

        /// <summary>
        /// 当圆弧来自 LWPOLYLINE bulge 转换时，记录原始顶点的精确端点坐标。
        /// 用于在导入时避免从中心/半径/角度重新计算端点带来的浮点误差。
        /// NaN 表示未设置（此时按传统方式从角度计算端点）。
        /// </summary>
        public double ExactStartX = double.NaN, ExactStartY = double.NaN, ExactEndX = double.NaN, ExactEndY = double.NaN;
    }

    public sealed class DxfCircle : DxfEntity
    {
        public double Cx, Cy, R;
    }
    public sealed class DxfEllipse : DxfEntity
    {
        public double Cx, Cy;           // center point (10, 20)
        public double MajorAxisX, MajorAxisY;  // major axis endpoint offset (11, 21)
        public double Ratio;            // minor-to-major ratio (40)
        public double StartParam;       // start parameter (41)
        public double EndParam;         // end parameter (42)
    }

    public sealed class DxfRectangle : DxfEntity
    {
        public List<(double X, double Y)> Points = new List<(double X, double Y)>();   // 4个顶点
        public List<double>? Concor; // 圆角半径
        public List<double>? Chamfer; // 倒角半径

    }
    public sealed class DxfPoint : DxfEntity
    {
       
    }

    public sealed class DxfLwPolyline : DxfEntity
    {
        public bool Closed;
        public double Width;            // constant width (code 43)
        public List<LwVertex> Verts = new();

    }

    public struct LwVertex
    {
        public double X, Y, Bulge;
    }

    public sealed class DxfSpline : DxfEntity
    {
        public bool Closed;
        public int Degree;                    // 阶数 (组码 71)
        public List<(double X, double Y)> FitPoints = new(); // 拟合点 (组码 11/21)
    }

    /// <summary>
    /// DXF HATCH 填充实体
    /// 导出时存储填充线段和 HatchParam 参数；导入时重建 DrawingHatch
    /// </summary>
    public sealed class DxfHatch : DxfEntity
    {
     
        /// <summary>填充线段列表（每条线段由起点和终点组成）</summary>
        public List<((double X, double Y) Start, (double X, double Y) End)> Lines = new();

        // ── HatchParamDto 参数（通过 XDATA 存储与读取） ──
        public double Margin;               // 边距
        public double RingSpacing;           // 圈距
        public double LineSpacing;           // 间距
        public int Count;                    // 次数
        public double StartAngle;            // 起始角度
        public double IncrementalAngle;      // 累进角度
        public double Extension;             // 延伸
        public int FillTypeIndex;             // 形式
        public bool AverageDistribute;        // 平均分配
        public int InternalRings;             // 内圈数
        public int DirectionTypeIndex;        // 方向
        public bool RelativeToAngle;          // 相对物件角度
        public bool ReverseFillLine;          // 填满线反向

        // ── XDATA 解析状态 ──
        public string? XDataApp = null;
        public List<double> XDataDoubles = new();
        public bool InXData = false;
    }

    // BLOCK 定义（仅在解析阶段使用，展开后丢弃）
    internal sealed class BlockDef
    {
        public string Name   = "";
        public double BaseX, BaseY;     // code 10/20 in BLOCK header
        public List<DxfEntity> Entities = new();
    }
}
