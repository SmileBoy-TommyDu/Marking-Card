using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.MarkCard.Model.MarkCommand
{
    public class MarkEllipseCommand : IMarkCommand
    {
        public MarkCommandType MarkCommandType => MarkCommandType.MarkEllipse;



        /// <summary>
        /// 中心点
        /// </summary>
        public PointF Center { get; set; }

        /// <summary>
        /// 长半径
        /// </summary>
        public double MajorRadius { get; set; } = 0;

        /// <summary>
        /// 短半径
        /// </summary>
        public double MinorRadius { get; set; } = 0;

        /// <summary>
        /// 长半径与X轴的夹角，单位为度，顺时针为负，逆时针为正
        /// </summary>
        public double Alpha { get; set; } = 0;

        /// <summary>
        /// 起始角度（度），0 表示从长轴方向开始
        /// </summary>
        public double StartAngle { get; set; } = 0;

        /// <summary>
        /// 扫掠角度（度），正值为逆时针，负值为顺时针。
        /// 默认 360 表示完整椭圆。
        /// </summary>
        public double SweepAngle { get; set; } = 360;
    }
}
