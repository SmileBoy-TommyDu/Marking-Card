using System;

namespace DrSoft.Drawing.Event
{
    public record ColorPickerRequestEvent : IEvent
    {
        public int LayerId { get; init; }
        public string? CurrentColor { get; init; }
    }
}
