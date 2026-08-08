namespace DrSoft.Drawing.Event
{
    public sealed class CommandCapabilityChangedEvent : IEvent
    {
        public SelectionCapabilities Capabilities { get; init; } = SelectionCapabilities.Empty;
    }
}
