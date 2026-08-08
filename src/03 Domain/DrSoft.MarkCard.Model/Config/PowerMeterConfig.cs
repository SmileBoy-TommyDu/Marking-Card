using DrSoft.MarkCard.Model.Enum;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.MarkCard.Model.Config
{
    /// <summary>
    /// 功率计配置类
    /// </summary>
    public class PowerMeterConfig
    {
        /// <summary>
        /// 功率计型号
        /// </summary>
        public PowerMeterModel PowerMeterModel { get; set; }

        public ConnectType ConnectType { get; set; } = ConnectType.Com;

        public string? ConnectString { get; set; }
    }
}
