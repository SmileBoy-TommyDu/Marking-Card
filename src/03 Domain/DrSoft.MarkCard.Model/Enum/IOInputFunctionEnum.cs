using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.MarkCard.Model.Enum
{
    public enum IOInputFunctionEnum
    {
        [Description("未定义")]
        None,
        
        [Description("触发打标")]
        TriggerMark,
        
        [Description("暂停打标")]
        PauseMark,

        [Description("恢复打标")]
        ResumeMark,

        [Description("停止打标")]
        StopMark
        
    
     

        
        
    }
}
