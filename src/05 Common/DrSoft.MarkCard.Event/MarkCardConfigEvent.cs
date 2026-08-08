using DrSoft.Drawing.Event;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.MarkCard.Event
{
    public class MarkCardConfigEvent<T> : IEvent
    {
        public T? Data { get; init; }

        public string? EventName { get; init; }
    }
}
