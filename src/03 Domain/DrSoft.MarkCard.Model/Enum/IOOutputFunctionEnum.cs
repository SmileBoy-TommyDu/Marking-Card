using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.MarkCard.Model.Enum
{
    public enum IOOutputFunctionEnum
    {
        [Description("未定义")]
        None,

        [Description("准备就绪")]
        Ready,

        [Description("打标结束")]
        MarkEnd,
        
        [Description("打标中")]
        MarkRunning
        
       
        
        //[Description("错误报警")]
        //Error
        
        
        
      
    }
}
