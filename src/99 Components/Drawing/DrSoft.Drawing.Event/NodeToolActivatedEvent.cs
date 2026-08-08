namespace DrSoft.Drawing.Event
{
    /// <summary>
    /// Node 工具激活/取消事件（由 CanvasViewModel 发布，EditPathNodesToolViewModel 订阅）
    /// </summary>
    public record NodeToolActivatedEvent() : IEvent
    {
        /// <summary>
        /// 是否激活（true=激活，false=取消）
        /// </summary>
        public bool IsActive { get; init; }
    }
}
