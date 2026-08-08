using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.MarkCard.Model.MarkCommand
{
    /// <summary>
    /// 下发激光延时命令（开光延时、关光延时）
    /// </summary>
    public class ModifyLaserDelayCommand : IMarkCommand
    {
        public MarkCommandType MarkCommandType => MarkCommandType.ModifyLaserDelay;

        /// <summary>
        /// 开光延时，单位为微秒
        /// </summary>
        public int LaserOnDelay { get; set; } = 100;

        /// <summary>
        /// 关光延时，单位为微秒
        /// </summary>
        public int LaserOffDelay { get; set; }
    }
}
