using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.Drawing.Controls.Models
{
    /// <summary>
    /// 通知 View 打开右键菜单的消息
    /// </summary>
    public class OpenMenuMessage
    {
        /// <summary>
        /// 菜单弹出的屏幕相对坐标（相对于承载 SKMenu 的容器）
        /// </summary>
        public System.Windows.Point Position { get; }

        public OpenMenuMessage(System.Windows.Point position)
        {
            Position = position;
        }
    }

    public class CloseMenuMessage
    {
        public CloseMenuMessage()
        {
        }
    }
}
