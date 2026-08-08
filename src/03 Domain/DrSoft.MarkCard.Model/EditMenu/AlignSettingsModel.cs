namespace DrSoft.MarkCard.Model.EditMenu
{
    public class AlignSettingsModel
    {
        /// <summary>旧字段，兼容单次对齐调用</summary>
        public AlignType AlignType { get; set; }

        /// <summary>水平方向对齐类型（None 表示不进行水平对齐）</summary>
        public AlignType HorizontalAlignType { get; set; }

        /// <summary>垂直方向对齐类型（None 表示不进行垂直对齐）</summary>
        public AlignType VerticalAlignType { get; set; }

        public AlignStandard AlignStandard { get; set; }    
    }

    public enum AlignType
    { 
        None,
        Left,
        Center,
        Right,
        Top,
        Middle,
        Bottom
    }

    public enum AlignStandard
    {
        None,
        LastChooseOne,
        PageEdge,
        PageCenter,
        Baseline,
    }
}
