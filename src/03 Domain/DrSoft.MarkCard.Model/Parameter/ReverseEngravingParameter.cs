namespace DrSoft.MarkCard.Model.Parameter
{
    public record ReverseEngravingParameter : ParameterBase
    {
        public bool Enabled { get; set; }
        public double Top { get; set; }
        public double Bottom { get; set; }
        public double Left { get; set; }
        public double Right { get; set; }
    }
}
