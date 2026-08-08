using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.MarkCard.Model.MarkCommand
{
    public class ModifyFrequencyAndPulsesWidthCommand : IMarkCommand
    {
        public MarkCommandType MarkCommandType => MarkCommandType.ModifyFrequencyAndPulsesWidth;
        public float Frequency { get; set; }

        public float PulsesWidth { get; set; }
    }
}
