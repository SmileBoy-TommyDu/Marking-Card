using DrSoft.MarkCard.Model.Enum;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.MarkCard.Model.Config
{
    public class LaserConfig
    {

        /// <summary>
        /// 打标卡类型，默认为RTC6
        /// </summary>
        public MarkCardType MarkCardType { get; set; }

        public uint CardNo { get; set; }

        /// <summary>
        /// 激光型号 (IPG、Raycus等)
        /// </summary>
        public LaserModel LaserModel { get; set; }

        /// <summary>
        /// 激光器类型 (CO2\YAG)
        /// </summary>
        public LaserType LaserType { get; set; }

        /// <summary>
        /// 激光理论功率(w, 用于功率校准)
        /// </summary>
        public double TheoreticalPower { get; set; }

        /// <summary>
        /// 功率设定 (由零至满功率所需时间)
        /// </summary>
        public double PowerRampUpTime { get; set; }

        /// <summary>
        /// 功率设定 (稳定延迟时间)
        /// </summary>
        public double PowerStabilizationDelay { get; set; }
    }
}
