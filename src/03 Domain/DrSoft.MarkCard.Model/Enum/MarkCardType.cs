using DrSoft.MarkCard.Model.Enum;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.MarkCard.Model
{
    /**
     * 枚举会提供给平台做位运算，请勿修改
     * 自动生成 markcards.drlic
     */
    public enum MarkCardType
    {
        [EnumValueAttriute("Scanlab", 1)]
        RTC6 = 0,
        [EnumValueAttriute("Eastern", 2)]
        PMC6 = 1,
        [EnumValueAttriute("柏楚", 4)]
        BoChu = 2
    }
}
