using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.MarkCard.Model.MarkCommand
{
    public class MarkPointCommand : IMarkCommand
    {
        public MarkCommandType MarkCommandType => MarkCommandType.MarkPoint;

        /// <summary>
        /// 持续时间，单位为微秒
        /// </summary>
        public double DotDuration {  get; set; }

        /// <summary>
        /// 位置
        /// </summary>
        public PointF Point {  get; set; }
    }
}
