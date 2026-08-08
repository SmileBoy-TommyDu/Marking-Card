using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.Drawing.Controls.Models
{
    public class CanvasRightClickCommand<T, TR>
    {
        public bool IsEnabled { get; set; }
        public Func<TR> FuncResult { get; set; }
        public Func<T,TR> FuncResultWithParamIn { get; set; }
    }
}
