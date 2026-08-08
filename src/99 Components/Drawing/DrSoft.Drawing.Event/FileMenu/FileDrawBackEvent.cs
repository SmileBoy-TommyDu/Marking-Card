using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.Drawing.Event.FileMenu
{
    public class FileDrawBackEvent : IEvent
    {
        /// <summary>
        /// 画布ID
        /// </summary>

        public int Id { get; init; }

        public FileOrderEnum Order { get; init; }
    }
}
