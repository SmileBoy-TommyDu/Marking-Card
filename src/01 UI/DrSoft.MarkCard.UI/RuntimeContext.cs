using DrSoft.Drawing.Contracts;
using DrSoft.Drawing.DTO;

namespace DrSoft.MarkCard.UI
{
    public static class RuntimeContext
    {
        /// <summary>
        /// 当前激活的画布ID，可能为null表示没有激活的画布
        /// </summary>
        public static int ActiveCanvasId { get; set; }

        public static List<int> Selections { get; set; }

        /// <summary>
        /// 绘图服务实例
        /// </summary>
        public static IDrawingService DrawingService { get; set; }
    }
}
