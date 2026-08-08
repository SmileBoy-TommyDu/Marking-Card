using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.Drawing.Event
{
    public class DxfReportMsgEvent : IEvent
    {
        public double ProgressValue { get; init; } = 0;

        public string? ShowTxt { get; init; } = string.Empty;
    }
}
