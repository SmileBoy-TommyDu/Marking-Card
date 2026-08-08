using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using System.Data;

namespace DrSoft.Drawing.Contracts
{
    // ── 图形接口：当前画布的图形操作 ──────────────────────────────
    public interface IShapeService :
        IShapeSelectionService,
        IShapeEditService,
        IShapeTransformService,
        IShapeStructureService,
        IShapeFillService,
        IShapeQueryService,
        IShapeAdjustService,
        IShapeMatrixCopyService,
        IShapeVectorService
    {
    }

    /// <summary>选择操作</summary>
    public interface IShapeSelectionService
    {
        GraphicResult SelectAll();
        GraphicResult SelectInvert();
        GraphicResult ClearSelection();
    }
    /// <summary>编辑操作（历史记录 / 剪贴板）</summary>
    public interface IShapeEditService
    {
        GraphicResult Undo();
        GraphicResult Redo();
        GraphicResult Copy();
        GraphicResult Cut();
        GraphicResult Paste(bool useMousePosition = true, bool suppressSelectionPublish = false);
        GraphicResult Delete();

        /// <summary>用新图形替换选中图形，返回新图形 ID</summary>
        GraphicResult Replace();

        GraphicResult EditNodes(bool turnOn);
        GraphicResult AddNodes(bool turnOn);
        GraphicResult DeleteNodes(bool turnOn);
        GraphicResult SeparateNodes(bool turnOn);
        GraphicResult MoveNodes(bool turnOn);
        GraphicResult ExtendNodes(bool turnOn);
        GraphicResult ConnectNodes(bool turnOn);
        GraphicResult SelectNodes(bool turnOn);

        GraphicResult ChangeSelectedState(int state);

        GraphicResult UpdateRotateCenterIcon(double x, double y, bool isShow);

        GraphicResult UpdateSkewCenterIcon(double x, double y, bool isShow);

        /// <summary>当前画布是否处于节点编辑模式（与 DocumentContext.IsNodeEditing 保持一致）</summary>
        bool IsNodeEditing { get; }
    }
    /// <summary>变换操作</summary>
    public interface IShapeTransformService
    {
        GraphicResult SetCenter(double cx, double cy);
        GraphicResult SetDimension(double width, double height);
        GraphicResult SetTranslate(double cx, double cy);
        GraphicResult SetScale(double cx, double cy, double scaleX, double scaleY);
        GraphicResult SetAbsoluteRotation(double cx, double cy, double angle);
        GraphicResult SetRotation(double cx, double cy, double angle);
        GraphicResult SetSkew(double skewX, double skewY);
        GraphicResult SetAbsoluteSkew(double cx, double cy, double skewX, double skewY);
        GraphicResult SetSkew(double cx, double cy, double skewX, double skewY);
        GraphicResult HorizontalMirror();
        GraphicResult VerticalMirror();
    }
    /// <summary>结构操作（组合 / 群组 / 图层 / 路径）</summary>
    public interface IShapeStructureService
    {
        /// <summary>组合为复合路径，返回新对象 ID</summary>
        GraphicResult Combine();

        /// <summary>拆解复合路径</summary>
        GraphicResult Break();

        /// <summary>群组</summary>
        GraphicResult Group();

        GraphicResult Ungroup();

        /// <summary>矢量合并（布尔运算类），返回新对象 ID</summary>
        GraphicResult VectorCombine();

        /// <summary>打散填充物件</summary>
        GraphicResult BreakFill();

        GraphicResult MoveToNewLayer();
        GraphicResult Reverse(bool isReverse);

        GraphicResult ConvertToCurve();
        GraphicResult ConvertToDot(ConvertToDotSettingsDto settings);

        GraphicResult Partition(double partWidth, double partHeight, double overlapX, double overlapY);
        GraphicResult Lock();
        GraphicResult ExtendHeadAndTail();

        GraphicResult Align(AlignSettingsDto settings);
        GraphicResult Distribute(DistributeSettingsDto settings);

        GraphicResult SetTextFont(FontSettingsDto fontSettings, string text = null, FontSettingsFields updatedFields = FontSettingsFields.All);
        GraphicResult SetSkyWriting(SkyWritingSettingsDto settings);
    }
    /// <summary>填充操作</summary>
    public interface IShapeFillService
    {
        /// <summary>填充，返回填充生成的线段数量</summary>
        GraphicResult<int> Fill(HatchParamDto param);

        /// <summary>重新填充</summary>
        GraphicResult<List<int>> Refill(HatchParamDto param);

        /// <summary>
        /// 获取选中填充对象的填充参数。
        /// 仅当选中 DrawingHatch 且其 HatchParamInfo 不为 null 时返回。
        /// </summary>
        GraphicResult<HatchParamDto?> GetHatchParam();
    }
    /// <summary>查询操作（只读，不产生副作用）</summary>
    public interface IShapeQueryService
    {
        /// <summary>获取当前活动画布中所有选中的图形（只读接口，零拷贝）</summary>
        GraphicResult<IReadOnlyList<IShapeData>> GetSelections();

        /// <summary>获取当前活动画布中所有图形（只读接口，零拷贝）</summary>
        GraphicResult<IReadOnlyList<IShapeData>> GetAllShapes();

        /// <summary>获取指定画布中所有图形（多画布批处理，只读接口）</summary>
        GraphicResult<IReadOnlyList<IShapeData>> GetAllShapes(int canvasId);
    }
    /// <summary>图形属性精细调整</summary>
    public interface IShapeAdjustService
    {
        GraphicResult AdjustRect(RoundMode mode, double lt, double rt, double rb, double lb);

        GraphicResult AdjustChamfer(RoundMode mode, double lt, double rt, double rb, double lb);

        GraphicResult AdjustCircle(double cx, double cy, double rx, double ry);

        GraphicResult AdjustArc(
            double cx, double cy,
            double rx, double ry,
            double startAngle, double endAngle);
        GraphicResult AdjustArcThreePoint(
         float p0x, float p0y,
         float p1x, float p1y,
         float p2x, float p2y);

        GraphicResult SetJumpPoint(JumpSettingsDto settings);

        GraphicResult AdjustPolygon(int SideCount, PolygonType polygonType);

        GraphicResult ClosePath();

        /// <summary>
        /// 设置选中图形的外框颜色和样式。外框颜色优先级高于图层颜色。
        /// </summary>
        /// <param name="outlineColor">外框颜色（十六进制），null 表示使用图层颜色</param>
        /// <param name="outlineStyleIndex">外框样式索引：0=实线, 1=短虚线, 2=点虚线, 3=无外框</param>
        GraphicResult SetOutlineStyle(string? outlineColor, int outlineStyleIndex);

    }
    /// <summary>
    /// 阵列复制操作
    /// </summary>
    public interface IShapeMatrixCopyService
    {
        GraphicResult MatrixCopy(int colunmnCount, double columnSpace, int rowCount, double rowSpace);

        GraphicResult CircleCopy(double Radius, int Count, double StartAngle, double IntervalAngle
            , bool IsAverageDistribute, bool IsObjectRotate, bool IsCounterClockwise);
    }

    public interface IShapeVectorService
    {
        GraphicResult Union();

        GraphicResult Intersect();
        GraphicResult Trim();
        GraphicResult KeepMain();
    }



    public enum ShapeChangeType
    {
        Position,    // 位置变更
        Dimension,   // 尺寸变更
        Rotation,    // 旋转变更
        Property,    // 属性变更（圆角/颜色等）
        Structure,   // 结构变更（节点增删、转曲等）
    }

    /// <summary>矩形圆角计算模式</summary>
    public enum RoundMode
    {
        /// <summary>按比例（%），相对于边长</summary>
        Percent,
        /// <summary>按绝对单位（mm）</summary>
        Unit,
    }
    public enum CornerMode { Round = 1, Chamfer }
    /// <summary>向量布尔组合操作类型</summary>
    public enum VectorCombineOperation
    {
        Union,        // 并集
        Subtract,     // 差集（前减后）
        Intersect,    // 交集
        Exclude,      // 排除（异或）
    }

    public enum PolygonType { Star, Regular }
}
