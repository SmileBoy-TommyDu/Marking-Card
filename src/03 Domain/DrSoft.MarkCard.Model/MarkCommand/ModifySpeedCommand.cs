using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.MarkCard.Model.MarkCommand
{
    /// <summary>
    /// 下发速度命令（打标速度、跳转速度）
    /// </summary>
    public class ModifySpeedCommand : IMarkCommand
    {
        public MarkCommandType MarkCommandType => MarkCommandType.ModifySpeed;

        /// <summary>
        /// 跳转速度
        /// </summary>
        public double JumpSpeed { get; set; }

        /// <summary>
        /// 打标速度
        /// </summary>
        public double MarkSpeed { get; set; }
    }
}
