using DrSoft.Drawing.Contracts;

namespace DrSoft.Drawing.Controls.Service
{
    /// <summary>
    /// 图形控制服务实现类。
    /// </summary>
    public class DrawingService : IDrawingService
    {
        public ICanvasService CanvasService { get; }
        public ILayerService Layers { get; }
        public IShapeService Shapes { get; }
        public DrawingService(ICanvasService canvasService, ILayerService layerService, IShapeService shapeService)
        {
            CanvasService = canvasService;
            Layers = layerService;
            Shapes = shapeService;
        }
    }
}