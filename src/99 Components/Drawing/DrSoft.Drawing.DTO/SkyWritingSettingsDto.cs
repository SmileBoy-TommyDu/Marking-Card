using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.Drawing.DTO
{
    public class SkyWritingSettingsDto
    {
        public uint SkyWritingModel { get; set; }

        public double DelayTime { get; set; }
        public int SwitchCompensation { get; set; }
        public int RunInTime { get; set; }
        public int RunOutTime { get; set; }
        public float ExtremeAngle { get; set; }
    }
}
