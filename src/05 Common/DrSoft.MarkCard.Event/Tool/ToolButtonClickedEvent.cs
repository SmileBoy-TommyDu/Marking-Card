using DrSoft.Drawing.Event;

namespace DrSoft.MarkCard.Event.Tool
{
    public class ToolButtonClickedEvent : IEvent
    {
        public string ToolTip { get; set; }
        public bool IsChecked { get; set; }
    }
}
