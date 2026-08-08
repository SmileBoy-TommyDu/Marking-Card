namespace DrSoft.Drawing.Event.Tool
{
    /// <summary>
    /// 节点编辑模式状态变化事件。
    /// 用于同步“是否处于节点编辑”以及当前持久子模式。
    /// </summary>
    public record EditNodesModeChangedEvent() : IEvent
    {
        public bool IsEditing { get; init; }
        public NodeEditSubMode SubMode { get; init; } = NodeEditSubMode.None;
        public bool HasSelectedMoveNode { get; init; }
        public bool CanExtendSelectedPathNodes { get; init; }
        public bool CanConnectSelectedPathNodes { get; init; }
    }
}
