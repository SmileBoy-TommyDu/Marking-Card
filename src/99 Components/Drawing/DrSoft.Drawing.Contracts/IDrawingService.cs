namespace DrSoft.Drawing.Contracts
{
    public interface IDrawingService
    {
        ICanvasService CanvasService { get; }
        ILayerService Layers { get; }
        IShapeService Shapes { get; }
    }
}
