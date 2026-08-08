namespace DrSoft.Drawing.Event
{
    public class ToastMessageEvent : IEvent
    {
        public string Message { get; }
        public ToastType Type { get; }

        public ToastMessageEvent(string message, ToastType type)
        {
            Message = message;
            Type = type;
        }
    }
    public enum ToastType
    {
        Info,
        Error,
        Warning
    }
}
