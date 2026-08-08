using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.MarkCard.Model.MarkCommand
{
    /// <summary>
    /// 下发功率命令
    /// </summary>
    public class ModifyPowerCommand : IMarkCommand
    {
        public MarkCommandType MarkCommandType => MarkCommandType.ModifyPower;

        /// <summary>
        /// 功率值（单位：百分比或设备约定单位）
        /// </summary>
        public double Power { get; set; }
    }
}
