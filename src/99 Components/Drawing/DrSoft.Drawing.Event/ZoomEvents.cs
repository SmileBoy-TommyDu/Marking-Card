namespace DrSoft.Drawing.Event
{
    /// <summary>
    /// 视口状态变化事件。
    /// 由 ToolZoom 发布，统一承载缩放百分比与 ZoomBack 可用性，避免同一视口变化拆成多条弱关联消息。
    /// </summary>
    public record ViewportChangedEvent() : IEvent
    {
        public double ZoomPercent { get; init; }
        public bool CanZoomBack { get; init; }
    }
}
