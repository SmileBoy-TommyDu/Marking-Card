using DrSoft.Drawing.Event;

namespace DrSoft.Drawing.Event
{
    public class BaseEvent<T>:IEvent
    {
        public string EventName { get; set; }
        public T Data { get; set; }
    }
}
