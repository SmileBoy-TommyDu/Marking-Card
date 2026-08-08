namespace DrSoft.Drawing.Event
{
    /// <summary>
    /// 工具激活状态变化事件，用于跨项目通信
    /// </summary>
    public record ToolActiveChangedEvent() : IEvent
    {
        /// <summary>
        /// 工具名称（如 "Node", "Select" 等）
        /// </summary>
        public string ToolName { get; init; }

        /// <summary>
        /// 工具是否被激活（true=激活，false=取消激活）
        /// </summary>
        public bool IsActive { get; init; }
    }
}
