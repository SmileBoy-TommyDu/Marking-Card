
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.MarkCard.Model.Parameter
{
    public record SystemParam : ParameterBase
    {
        public double HeadSpeedMax { get; set; } = 8000;
        public double HeadSpeedMin { get; set; } = 0.1;
        public double Power100 { get; set; } = 100;
        public double Power0 { get; set; } = 0;
        public double FreqMax { get; set; } = 100;
        public double FreqMin { get; set; } = 0.1;
        public int CountMax { get; set; } = 10000;
        public int CountMin { get; set; } = 0;
        public double PointTimeMax { get; set; } = 10000;
        public double PointTimeMin { get; set; } = 0.001;
    }
}
