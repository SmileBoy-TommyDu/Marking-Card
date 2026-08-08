using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.MarkCard.Model.MarkCommand
{
    /// <summary>
    /// 下发扫描延时命令（打标延时、跳转延时、转角延时）
    /// </summary>
    public class ModifyScannerDelayCommand : IMarkCommand
    {
        public MarkCommandType MarkCommandType => MarkCommandType.ModifyScannerDelay;

        /// <summary>
        /// 打标延时，单位为微秒
        /// </summary>
        public int MarkDelay { get; set; }

        /// <summary>
        /// 跳转延时，单位为微秒
        /// </summary>
        public int JumpDelay { get; set; }

        /// <summary>
        /// 转角延时，单位为微秒
        /// </summary>
        public int CornerDelay { get; set; }
    }
}
