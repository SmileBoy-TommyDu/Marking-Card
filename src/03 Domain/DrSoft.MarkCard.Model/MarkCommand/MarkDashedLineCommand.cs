using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.MarkCard.Model.MarkCommand
{
    public class MarkDashedLineCommand : IMarkCommand
    {
        public MarkCommandType MarkCommandType => MarkCommandType.MarkDashedLineCommand;

        /// <summary>
        /// 虚线起点（可选，用于日志/调试输出）
        /// </summary>
        public PointF? StartPoint { get; set; }

        /// <summary>
        /// 虚线终点（可选，用于日志/调试输出，取 DashArray 最后一个点）
        /// </summary>
        public PointF? EndPoint { get; set; }

        /// <summary>
        /// 虚线数组，每个元素表示实线段或空线段终点，实-空交替排列
        /// （如 [实线终点, 空白终点, 实线终点, 空白终点, ...]）
        /// </summary>
        public List<PointF> DashArray { get; set; }
    }
}
