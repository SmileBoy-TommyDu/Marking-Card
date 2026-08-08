using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Controls.ViewModels;
using DrSoft.Drawing.Controls.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DrSoft.Drawing.Controls
{
    public class DrawingContext
    {
        /// <summary>
        /// 画布控件
        /// </summary>
        public DrawingCanvasControl CanvasControl { get; }

        /// <summary>
        /// 图层面板控件（不绑定 DataContext，由外部动态设置）
        /// </summary>
        public LayerViewView LayerControl { get; }

        internal CanvasViewModel ViewModel { get; }

        private DrawingContext(DrawingCanvasControl canvas,
                                      LayerViewView layers,
                                      CanvasViewModel vm)
        {
            CanvasControl = canvas;
            LayerControl = layers;
            ViewModel = vm;
        }

        public static DrawingContext Create(IServiceProvider provider)
        {
            var canvasViewModel = provider.GetService<CanvasViewModel>();
            if (canvasViewModel == null)
            {
                throw new ArgumentNullException(nameof(canvasViewModel));
            }

            var canvas = new DrawingCanvasControl(canvasViewModel);

            // 创建 LayerViewView，并绑定当前活动文档的 LayerViewViewModel
            var layerViewView = new LayerViewView(((DrawingCanvas)canvasViewModel.MultiCanvas.Context.ActiveCanvas).LayerViewViewModel!);

            return new DrawingContext(canvas, layerViewView, canvas.ViewModel!);
        }
    }
}
