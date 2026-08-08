namespace DrSoft.MarkCard.Model.EditMenu
{
    public record ExtendHeadTailSettingsModel : ParameterBase, IMarkingParameter
    {
       
        public float HeadExtendLength { get; set; }
        public float TailExtendLength { get; set; }
    }
}
