using DrSoft.Drawing.DTO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.Drawing.DTO
{
    public class DrawPolygonDto : DrawObjectDto
    {
        public int SideCount { get; set; } = 5;

        /// <summary>true = 五角星；false = 正多边形</summary>
        public bool IsStar { get; set; } = false;
    }
}
