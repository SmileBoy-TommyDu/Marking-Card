namespace DrSoft.MarkCard.Model.EditMenu
{
    public class DistributionSettingsModel
    {
        /// <summary>
        /// 兼容旧代码，单一分布类型
        /// </summary>
        public DistributionType DistributionType { get; set; }

        /// <summary>
        /// 水平分布类型（None 表示不进行水平分布）
        /// </summary>
        public DistributionType HorizontalDistributionType { get; set; }

        /// <summary>
        /// 垂直分布类型（None 表示不进行垂直分布）
        /// </summary>
        public DistributionType VerticalDistributionType { get; set; }

        public DistributionStandard DistributionStandard { get; set; }
    }

    public enum DistributionType
    {
        None,
        AlignLeftDistribute,
        AlignCenterDistribute,
        AlignRightDistribute,
        AlignHorizontalSpaceDistribute,
        AlignTopDistribute,
        AlignMiddleDistribute,
        AlignBottomDistribute,
        AlignVerticalSpaceDistribute,
    }

    public enum DistributionStandard
    {
        None,
        SelectArea,
        CanvasArea
    }
}

