namespace DrSoft.MarkCard.Model.Parameter
{
    public record OutlineParameter : ParameterBase
    {
        public string OutlineColor { get; set; } = "#000000"; // 十六进制颜色
        public int OutlineStyleIndex { get; set; } = 0;
    }
}
