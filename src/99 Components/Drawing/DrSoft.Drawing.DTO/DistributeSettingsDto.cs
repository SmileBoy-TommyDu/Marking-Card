namespace DrSoft.Drawing.DTO
{
    /// <summary>
    /// 分布设置参数
    /// </summary>
    public class DistributeSettingsDto
    {
        /// <summary>
        /// 分布类型（兼容旧代码，取水平或垂直中较后设置的那个）
        /// </summary>
        public DistributeTypeDto DistributeType { get; set; }

        /// <summary>
        /// 水平方向分布类型（None 表示不进行水平分布）
        /// </summary>
        public DistributeTypeDto HorizontalDistributeType { get; set; }

        /// <summary>
        /// 垂直方向分布类型（None 表示不进行垂直分布）
        /// </summary>
        public DistributeTypeDto VerticalDistributeType { get; set; }

        /// <summary>
        /// 分布区域基准：选取范围 or 画布范围
        /// </summary>
        public DistributeStandardDto DistributeStandard { get; set; }
    }

    public enum DistributeTypeDto
    {
        None,

        //左边界水平分布
        AlignLeftDistribute,
        //中心点水平分布
        AlignCenterDistribute,

        //右边界水平分布
        AlignRightDistribute,

        //水平间距分布
        AlignHorizontalSpaceDistribute,

        //上边界垂直分布
        AlignTopDistribute,
        //中心点垂直分布
        AlignMiddleDistribute,

        //下边界垂直分布
        AlignBottomDistribute,

        //垂直间距分布
        AlignVerticalSpaceDistribute,
    }

    public enum DistributeStandardDto
    {
        None,
        SelectArea,
        CanvasArea
    }
}
