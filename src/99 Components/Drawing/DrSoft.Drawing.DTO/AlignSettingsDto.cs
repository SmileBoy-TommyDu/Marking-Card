namespace DrSoft.Drawing.DTO
{
    /// <summary>
    /// 对齐设置参数
    /// </summary>
    public class AlignSettingsDto
    {
        /// <summary>
        /// 对齐类型（兼容旧代码，单次对齐调用时使用）
        /// </summary>
        public AlignTypeDto AlignType { get; set; }

        /// <summary>
        /// 水平方向对齐类型（None 表示不进行水平对齐）
        /// </summary>
        public AlignTypeDto HorizontalAlignType { get; set; }

        /// <summary>
        /// 垂直方向对齐类型（None 表示不进行垂直对齐）
        /// </summary>
        public AlignTypeDto VerticalAlignType { get; set; }

        /// <summary>
        /// 对齐基准
        /// </summary>
        public AlignStandardDto AlignStandard { get; set; }
    }

    /// <summary>
    /// 对齐类型枚举
    /// </summary>
    public enum AlignTypeDto
    {
        None,
        Left,       // 左对齐
        Center,     // 水平居中
        Right,      // 右对齐
        Top,        // 顶部对齐
        Middle,     // 垂直居中
        Bottom      // 底部对齐
    }

    /// <summary>
    /// 对齐基准枚举
    /// </summary>
    public enum AlignStandardDto
    {
        None,
        LastChooseOne,  // 最后所选对象
        PageEdge,       // 页面边缘
        PageCenter,     // 页面中心
        Baseline,       // 基线
        CanvasArea      // 画布区域
    }
}
