using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;

namespace DrSoft.Drawing.Event
{
    /// <summary>
    /// 图层/群组/图形选中事件
    /// </summary>
    public record NodeSelectedEvent() : IEvent
    {
        /// 画布ID
        public int CanvasId { get; set; }

        /// 选中维度
        public NodeType NodeType { get; set; }

        /// 选中的图形
        public NodeSelectedSummary Summary { get; set; } = new NodeSelectedSummary();
    }

    public record NodeSelectedSummary
    {
        /// <summary>
        /// 选中总数
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// 包含子级总数
        /// </summary>
        public int TotalCountWithChildren { get; set; }

        /// <summary>
        /// 仅当选中对象为单个文本时，提供该文本的详细信息以供参数面板使用
        /// </summary>
        public DrawObjectDto? EditingObject { get; set; }

        /// <summary>
        /// 当前选中图形的类型，如果是多个类型，则为 null；参数面板可根据该信息决定显示哪些参数项
        /// </summary>
        public ShapeType? UniformType { get; set; }

        /// <summary>
        /// 被选中图形ID集合
        /// </summary>
        public List<int> SelectionIds { get; set; } = new List<int>();
    }


    /// <summary>
    /// 节点类型枚举
    /// </summary>
    public enum NodeType
    {
        Canvas,
        /// <summary>图层</summary>
        Layer,
        /// <summary>群组</summary>
        Group,
        /// <summary>组合</summary>
        Combination,
        /// <summary>图形</summary>
        Shape,
        /// <summary> 填满 </summary>
        Hatch,
    }
}
