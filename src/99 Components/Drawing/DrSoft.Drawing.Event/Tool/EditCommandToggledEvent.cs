namespace DrSoft.Drawing.Event.Tool
{
    /// <summary>
    /// Edit 命令切换事件（由 EditPathNodesToolViewModel 发布，CanvasViewModel 订阅）
    /// </summary>
    public record EditCommandToggledEvent() : IEvent
    {
        /// <summary>
        /// 是否选中（true=选中，false=取消）
        /// </summary>
        public bool IsChecked { get; init; }
    }
}
