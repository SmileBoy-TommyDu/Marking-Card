using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.MarkCard.Model.Enum
{
    public enum IOTriggerType
    {
        //下降沿触发
        [Description("下降沿触发")]
        FallingEdge,

        [Description("上升沿触发")]
        RisingEdge,

       
    }
}
