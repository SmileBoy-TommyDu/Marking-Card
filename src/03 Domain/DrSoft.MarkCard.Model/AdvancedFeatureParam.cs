using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.MarkCard.Model
{
    public class AdvancedFeatureParam
    {

        /// <summary>
        /// Sky Writing Model 0:不使用天书 1:标准模式 2:时间优化模式 3:智能切换模式
        /// </summary>
        public uint SkyWritingModel { get; set; }

        public double DelayTime { get; set; }
        public int LaserOnDelay { get; set; }
        public int RunInTime { get; set; }
        public int RunOutTime { get; set; }
        public float ExtremeAngle { get; set; }

        //进入补偿长度 单位mm
        public float RunInCompensationLength { get; set; }

        //退出补偿长度 单位mm
        public float RunOutCompensationLength { get;set; }

    }
}
