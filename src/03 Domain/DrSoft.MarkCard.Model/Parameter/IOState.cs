
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.MarkCard.Model.Parameter
{
    public record IOState : ParameterBase
    {
        public bool[] Inputs { get; set; } = new bool[16];
        public bool[] Outputs { get; set; } = new bool[16];
        public bool[] Lasers { get; set; } = new bool[6];
    }
}
