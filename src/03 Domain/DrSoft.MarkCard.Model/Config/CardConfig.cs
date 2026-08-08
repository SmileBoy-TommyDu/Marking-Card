using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DrSoft.MarkCard.Model.Enum;

namespace DrSoft.MarkCard.Model.Config
{
    public class CardConfig
    {

        /// <summary>
        /// 打标卡类型，默认为RTC6
        /// </summary>
        public MarkCardType MarkCardType { get; set; }


        /// <summary>
        /// 是否主卡（多卡同步）
        /// </summary>
        public bool IsMaster { get; set; } = true;

        /// <summary>
        /// 打标超时时间(ms)
        /// </summary>
        public int MarkingTimeout { get; set; } = 1000;

        /// <summary>
        /// 初始化超时时间(ms)
        /// </summary>
        public int InitTimeout { get; set; } = 1000;

        /// <summary>
        /// 激活该类型打标卡
        /// </summary>
        public bool IsActive { get; set; } = false;

        /// <summary>
        /// 是否启用IO触发打标
        /// </summary>
        public bool EnableIOTrigger { get; set; } = false;

        /// <summary>
        /// 连接方式
        /// </summary>
        public ConnectType ConnectionType { get; set; } = ConnectType.PCIe;

        /// <summary>
        /// IO触发模式（上升沿、下降沿）
        /// </summary>
        public IOTriggerType IOTriggerType { get; set; } = IOTriggerType.FallingEdge;

        /// <summary>
        /// 打标卡数量
        /// </summary>
        public uint CardCount { get; set; } = 1;

        /// <summary>
        /// 打标卡头数量列表，列表元素表示对应打标卡的扫描头数量
        /// </summary>
        //public List<uint> ScanHeadCount { get; set; } = new List<uint>();

        ///// <summary>
        ///// 打标卡连接列表，列表元素表示对应打标卡的连接字符串（如IP地址或COM端口）
        ///// </summary>
        //public List<string> ConnectList { get; set; } = new List<string>();

        public List<CardDescConfig> CardDescConfigs { get; set; } = new List<CardDescConfig>();

        //柏楚打标卡配置文件路径
        public string CardConfigFliePath { get; set; }

    }

    public class  CardDescConfig
    {
        public uint ScanHeadCount { get; set; } = 1;

        public string ConnectStr { get; set; } = string.Empty;

        public bool IsMaster { get; set; } = true;


    }
}
