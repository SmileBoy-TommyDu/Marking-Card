using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Model;
using DrSoft.Drawing.Rendering;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.Rendering
{
    /// <summary>
    /// 统一的形状渲染器接口
    /// </summary>
    public interface IRenderer
    {
        bool CanRender(IShape obj);

        /// <summary>
        /// 渲染形状对象
        /// </summary>
        void Render(IShape shape, SKCanvas canvas, IViewport viewport, SKPaintCache cache);

        /// <summary>
        /// 渲染预览效果
        /// </summary>
        void PreviewRender(IShape shape, SKCanvas canvas, SKPaint strokePaint, SKPaintCache cache);

        /// <summary>
        /// 渲染填充效果
        /// </summary>
        /// <param name="shape"></param>
        /// <param name="canvas"></param>
        /// <param name="cache"></param>
        //void RenderHatch(IShape shape, SKCanvas canvas, IViewport viewport, SKPaintCache cache) { }

        void RenderHatch(IShape shape, IHatchable Hatchable, SKCanvas canvas, IViewport viewport, SKPaintCache cache) { }
    }
}
