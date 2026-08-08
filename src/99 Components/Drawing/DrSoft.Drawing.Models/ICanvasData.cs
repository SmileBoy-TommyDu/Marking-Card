namespace DrSoft.Drawing.Model
{
    /// <summary>
    /// 画布数据契约（只读）。
    /// 打标卡通过此接口获取画布内的图层和图形数据，零拷贝，无需 DTO 转换。
    /// </summary>
    public interface ICanvasData
    {
        int    Id   { get; }
        string Name { get; }

        /// <summary>图层列表（仅可见图层建议由调用方过滤）</summary>
        IReadOnlyList<ILayerData> Layers { get; }
    }

    /// <summary>
    /// 图层数据契约（只读）。
    /// </summary>
    public interface ILayerData
    {
        int    UId       { get; }
        string Name      { get; }
        bool   IsVisible { get; }
        bool   IsLocked  { get; }
        string Color     { get; }

        /// <summary>
        /// 图层内所有顶层图形（不含子图形，子图形通过 IShapeData.ChildShapes 访问）。
        /// </summary>
        IReadOnlyList<IShapeData> Shapes { get; }
    }
}
