namespace DrSoft.Drawing.Model
{
    /// <summary>
    /// 外框样式：0=实线, 1=短虚线, 2=点虚线, 3=无外框。
    /// 与 UI 层 OutlineStyleIndex 语义一致，避免 SKPaint 泄露到对外接口。
    /// </summary>
    public enum OutlineStyle
    {
        Solid   = 0,
        Dashed  = 1,
        Dotted  = 2,
        None    = 3
    }

    public enum LineStyle
    {
        /// <summary>
        /// 实线
        /// </summary>
        Solid = 0,
        /// <summary>
        /// 短虚线
        /// </summary>
        Dashed = 1,
        /// <summary>
        /// 点虚线
        /// </summary>
        Dotted = 2
    }

    /// <summary>
    /// 图形数据契约（只读）。
    /// <para>
    /// 只暴露几何数据，不含任何渲染行为（无 SKCanvas、无 SKPaint）。
    /// 打标卡组件通过此接口读取图形数据，零拷贝、零转换，且无法修改图形工具内部状态。
    /// </para>
    /// </summary>
    public interface IShapeData
    {
        // ── 标识 ──────────────────────────────
        int       UId      { get; }
        string    Name     { get; }
        int       LayerId  { get; }
        ShapeType Type     { get; }

        // ── 几何（世界坐标，单位：毫米）───────────
        float     X        { get; }   // 包围盒左边界
        float     Y        { get; }   // 包围盒上边界
        float     Width    { get; }
        float     Height   { get; }
        float     CenterX  { get; }
        float     CenterY  { get; }

        // ── 变换 ──────────────────────────────
        float     Rotation { get; }   // 角度制，顺时针为正
        float     ScaleX   { get; }
        float     ScaleY   { get; }
        float     SkewX    { get; }
        float     SkewY    { get; }

        // ── 外观 ──────────────────────────────
        /// <summary>
        /// 外框颜色。null 表示使用图层共享颜色（无自定义外框颜色）。
        /// </summary>
        DrawingColor? OutlineColor { get; }

        /// <summary>
        /// 外框样式（实线 / 短虚线 / 点虚线 / 无外框）。
        /// </summary>
        OutlineStyle OutlineStyle { get; }

        // ── 加工语义 ──────────────────────────
        bool      IsClockwise { get; }  // 激光行进方向
        bool      IsVisible   { get; }
        bool      IsLocked    { get; }

        // ── 打标指令生成所需数据 ───────────────────
        /// <summary>
        /// 图形轮廓点序列（世界坐标，单位 mm）。
        /// 文字等复合图形用 NaN 分隔多段轮廓。
        /// </summary>
        IReadOnlyList<(float X, float Y)> OutlinePoints { get; }

        /// <summary>
        /// 相交镂空跳点坐标列表（打标时在这些点附近抬笔）。
        /// 无跳点时返回空序列。
        /// </summary>
        IReadOnlyList<(float X, float Y)> IntersectionSkipPoints { get; }

        /// <summary>相交镂空圈半径（mm）。</summary>
        float IntersectionSkipRadius { get; }

        /// <summary>
        /// 自交跳点数量：IntersectionSkipPoints 前 N 个为自交点。
        /// 自交点仅裁剪“under”线段（路径中第二次经过的那条），
        /// “over”线段（第一次经过的）保持连续。
        /// </summary>
        int SelfIntersectionSkipCount { get; }

        /// <summary>
        /// 子图形（Group / Combination / Hatch 等容器图形使用）。
        /// 叶子图形返回空序列。
        /// </summary>
        IReadOnlyList<IShapeData> ChildShapes { get; }
    }

    // ─────────────────────────────────────────────────────────
    // 各图形类型的扩展数据契约
    // 打标卡通过 switch pattern matching 访问类型特有属性
    // ─────────────────────────────────────────────────────────

    /// <summary>圆 / 椭圆数据契约</summary>
    public interface ICircleShapeData : IShapeData
    {
        float RadiusX   { get; }
        float RadiusY   { get; }
        bool  IsEllipse { get; }
    }

    /// <summary>直线数据契约（起点 + 终点已包含在 Points 中）</summary>
    public interface ILineShapeData : IShapeData { }

    /// <summary>折线 / 多段线数据契约</summary>
    public interface IPolyLineShapeData : IShapeData
    {
        IReadOnlyList<(float X, float Y)> Vertices { get; }

        public LineStyle LineStyle { get; }

        /// <summary>是否为闭合折线（末尾自动连回起点）</summary>
        bool IsClosed { get; }

        /// <summary>
        /// 虚线输出线段列表（世界坐标，单位 mm）。
        /// 当 OutputAsDashed 为 true 时，由 BuildMarkingJob 阶段根据 CurveParameter 的
        /// 多组 A/B 循环迭代算法预计算并写入；为 null 或空表示未启用虚线输出。
        /// </summary>
        IReadOnlyList<((float X, float Y) Start, (float X, float Y) End)> DashSegments { get; set; }
    }

    public interface IPolygonShapeData : IShapeData
    {
        int SideCount { get; }

        /// <summary>true = 五角星；false = 正多边形</summary>
        bool IsStar { get; }
    }

    /// <summary>矩形数据契约</summary>
    public interface IRectangleShapeData : IShapeData
    {
        float CornerRadiusTopLeft     { get; }
        float CornerRadiusTopRight    { get; }
        float CornerRadiusBottomRight { get; }
        float CornerRadiusBottomLeft  { get; }

        float ChamferTopLeft     { get; }
        float ChamferTopRight    { get; }
        float ChamferBottomRight { get; }
        float ChamferBottomLeft  { get; }
    }

    /// <summary>圆弧数据契约</summary>
    public interface IArcShapeData : IShapeData
    {
        float Radius     { get; }   // 外接圆半径（圆弧为圆形时等于 RadiusX/RadiusY）
        float RadiusX    { get; }   // X 方向半径（支持椭圆弧）
        float RadiusY    { get; }   // Y 方向半径（支持椭圆弧）
        float StartAngle { get; }   // 角度制
        float SweepAngle { get; }   // 角度制，正为顺时针
        float EndAngle {  get; }
        float CircumcircleCenterX { get; } // 圆心 X 坐标
        float CircumcircleCenterY { get; } // 圆心 Y 坐标
        float StartX { get; }
        float StartY { get; }
        float EndX { get; }
        float EndY { get; }
    }

    /// <summary>文字数据契约</summary>
    public interface ITextShapeData : IShapeData
    {
        string Text             { get; }
        string FontFamily       { get; }
        float  FontSize         { get; }
        bool   IsBold           { get; }
        bool   IsItalic         { get; }
        float  LineHeight       { get; }
        float  CharacterSpacing { get; }
    }

    /// <summary>点数据契约</summary>
    public interface IDotShapeData : IShapeData { }

    /// <summary>贝塞尔曲线数据契约</summary>
    public interface IBezierShapeData : IShapeData
    {
        IReadOnlyList<(float X, float Y)> ControlPoints { get; }

        /// <summary>是否为闭合曲线</summary>
        bool IsClosed { get; }
    }

    /// <summary>自由曲线数据契约</summary>
    public interface IArbitraryCurveShapeData : IShapeData
    {
        /// <summary>是否为闭合曲线</summary>
        bool IsClosed { get; }
    }
}
