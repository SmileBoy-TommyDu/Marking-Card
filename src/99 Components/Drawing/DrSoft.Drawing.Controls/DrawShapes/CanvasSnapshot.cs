namespace DrSoft.Drawing.Controls.DrawShapes
{
    /// <summary>
    /// 提供给外部
    /// </summary>
    public class CanvasSnapshot
    {
        public int Id { get;  set; }
        public string? Name { get;  set; }
        public IEnumerable<DrawingLayer> Layers { get;  set; } = Enumerable.Empty<DrawingLayer>();
    }
}
