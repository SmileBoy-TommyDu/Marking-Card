using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.MarkCard.Model.MarkCommand
{
    /// <summary>
    /// 园形打标命令（包括圆弧）
    /// </summary>
    public class MarkCircleCommand : IMarkCommand
    {
        public MarkCommandType MarkCommandType => MarkCommandType.MarkCircle;

        /// <summary>
        /// 加工起点
        /// </summary>
        public PointF StartPoint { get; set; }

        /// <summary>
        /// 中心点
        /// </summary>
        public PointF Center { get; set; }

        /// <summary>
        /// 半径
        /// </summary>
        public float Radius { get; set; }

        /// <summary>
        /// 角度，单位为度，顺时针为负，逆时针为正
        /// </summary>
        public float Angle { get; set; }
    
    }
}
