
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.MarkCard.Model.Parameter
{
    public record GalvoConfig : ParameterBase
    {

        /// <summary>
        /// 打标卡类型，默认为RTC6
        /// </summary>
        public MarkCardType MarkCardType { get; set; }
        public uint MarkCardNo { get; set; } = 1;
        public uint LensNo { get; set; } = 1;
        public double ScaleX { get; set; } = 100.0;
        public double ScaleY { get; set; } = 100.0;
        public double OffsetX { get; set; } = 0.00;
        public double OffsetY { get; set; } = 0.00;
        public double Rotation { get; set; } = 0.00;
    }
}
