using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DrSoft.MarkCard.Model.Enum;

namespace DrSoft.MarkCard.Model.Config
{
    public class IOConfig
    {
        /// <summary>
        /// 打标卡类型，默认为RTC6
        /// </summary>
        public MarkCardType MarkCardType { get; set; }

        public uint CardNo { get; set; }

        public bool EnableIO { get; set; } = false;
        public int InputCount { get; set; } = 16;
        public int OutputCount { get; set; } = 16;

        public IOOutputFunctionEnum[] OutputFunctions { get; set; } = new IOOutputFunctionEnum[16];

        public IOInputFunctionEnum[] InputFunctions { get; set; } = new IOInputFunctionEnum[16];

        public string[] InputCustomNames { get; set; } = new string[16];

        public string[] OutputCustomNames { get; set; } = new string[16];
    }
}
