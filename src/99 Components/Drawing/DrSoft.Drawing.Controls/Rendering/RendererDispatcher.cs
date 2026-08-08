using System.Diagnostics;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Model;
using DrSoft.Drawing.Rendering;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.Rendering
{
    public class RendererDispatcher
    {
        private readonly IEnumerable<IRenderer> _renderers;
        private readonly Dictionary<Type, IRenderer?> _rendererCache = new();

        public RendererDispatcher(IEnumerable<IRenderer> renderers)
            => _renderers = renderers;

        public void Render(IShape shape, SKCanvas canvas, IViewport viewport, SKPaintCache cache)
        {
            var type = shape.GetType();
            if (!_rendererCache.TryGetValue(type, out var renderer))
            {
                renderer = _renderers.FirstOrDefault(r => r.CanRender(shape));
                _rendererCache[type] = renderer;
            }
            renderer?.Render(shape, canvas, viewport, cache);
            /*if (shape is IHatchable hatchable && hatchable.HatchParamInfo != null)
                //renderer?.RenderHatch(shape, canvas, viewport, cache);
                renderer?.RenderHatch(shape, hatchable, canvas, viewport, cache);*/
        }

        public void PreviewRender(IShape shape, SKCanvas canvas, SKPaint strokePaint, SKPaintCache cache)
        {
            var type = shape.GetType();
            if (!_rendererCache.TryGetValue(type, out var renderer))
            {
                renderer = _renderers.FirstOrDefault(r => r.CanRender(shape));
                _rendererCache[type] = renderer;
            }
            renderer?.PreviewRender(shape, canvas, strokePaint, cache);
        }

        //public void RenderHatch(IShape shape, SKCanvas canvas, IViewport viewport, SKPaintCache cache)
        //{
        //    var renderer = _renderers.FirstOrDefault(r => r.CanRender(shape));
        //    renderer?.RenderHatch(shape, canvas, viewport,cache);
        //}
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class RendererForAttribute : Attribute
    {
        public Type ShapeType { get; }

        public RendererForAttribute(Type shapeType)
        {
            ShapeType = shapeType
                ?? throw new ArgumentNullException(nameof(shapeType));
        }
    }
}
