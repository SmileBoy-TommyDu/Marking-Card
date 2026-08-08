using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.MarkCard.Model.Enum
{
    public class EnumValueAttriute : Attribute
    {
        /// <summary>
        /// 名称
        /// </summary>
        public string Description { get; set; }
        /// <summary>
        /// 值
        /// </summary>
        public int Value { get; set; }

        public EnumValueAttriute(string description, int value)
        {
            Description = description;
            Value = value;
        }
    }
}
