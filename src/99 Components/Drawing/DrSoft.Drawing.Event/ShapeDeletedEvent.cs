using DrSoft.Drawing.Event;

namespace DrSoft.MarkCard.Event
{
    /// <summary>
    /// 图形或图层被删除时发布的事件，通知打标卡清理对应的加工参数。
    /// </summary>
    public class ShapeDeletedEvent : IEvent
    {
        /// <summary>画布ID</summary>
        public int CanvasId { get; init; }

        /// <summary>被删除的图形UId列表</summary>
        public List<int> EntityIds { get; init; } = new();

        /// <summary>是否为图层删除（true 表示删除了整个图层，EntityIds 包含该图层下所有图形）</summary>
        public bool IsLayerDeleted { get; init; }
    }
}
