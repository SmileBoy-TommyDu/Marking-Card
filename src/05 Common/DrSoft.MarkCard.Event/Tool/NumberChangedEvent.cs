using DrSoft.Drawing.Event;

namespace DrSoft.MarkCard.Event.Tool
{
    public class NumberChangedEvent : IEvent
    {
        public string ToolTip { get; set; }
        public float Value { get; set; }
    }
}
