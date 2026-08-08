using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.MarkCard.Model.MarkCommand
{
    public class MarkLineCommand :  IMarkCommand
    {

        public MarkCommandType MarkCommandType => MarkCommandType.MarkLine; 

        //public PointF StartPoint { get; set; }

        public PointF EndPoint { get; set; }
    }
}
