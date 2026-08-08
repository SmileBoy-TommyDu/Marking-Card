using System.ComponentModel;

namespace DrSoft.Drawing.Model
{
    public enum ShapeType
    {
        None=-1,

        [Description("点")]
        Point,

        [Description("曲线")]
        Line,

        [Description("曲线")]
        PolyLine,

        [Description("矩形")]
        Rectangle,

        [Description("圆")]
        Circle,

        [Description("多边形")]
        Polygon,

        [Description("圆弧")]
        Arc,

        [Description("曲线")]
        Bezier,

        [Description("字体")]
        Text,

        [Description("组合")]
        Combination,

        [Description("群组")]
        Group,

        [Description("填充")]
        Hatch,

        //三次贝塞尔路径
        [Description("曲线")]
        CubicPath,

        [Description("曲线")]
        ArbitraryCurve,
    }
}
