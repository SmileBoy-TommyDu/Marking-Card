
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.MarkCard.Model.Parameter
{
    public  record DrawingBoardParameter: ParameterBase
    {
        // 原点

        public double OriginX { get; set; } = 0.00;

     
        public double OriginY  { get; set; } = 0.00;

        // 画板设置（W/H，带锁）

        public double BoardW { get; set; } = 100.00;

      
        public double BoardH  { get; set; } = 100.00;

        public bool IsBoardLocked { get; set; } = true;

        // 微调移动

        public double MicroMoveX { get; set; } = 0.100;

  
        public double MicroMoveY   { get; set; } = 0.100;

        // 格点大小

        public double GridSizeW  { get; set; } = 100.00;


        public double GridSizeH { get; set; } = 100.00;

        public bool IsGridLocked { get; set; } = true;
    }
}
