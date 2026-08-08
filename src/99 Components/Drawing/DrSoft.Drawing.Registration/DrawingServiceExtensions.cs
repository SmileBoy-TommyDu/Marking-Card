using DrSoft.Drawing.Contracts;
using DrSoft.Drawing.Controls;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Controls.Rendering;
using DrSoft.Drawing.Controls.Service;
using DrSoft.Drawing.Controls.ViewModels;
using DrSoft.Drawing.Controls.Views;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace DrSoft.Drawing.Registration
{
    public static class DrawingServiceExtensions
    {
        public static IServiceCollection RegisterDrawingTools(
            this IServiceCollection services)
        {
            // 弹框服务
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<DialogWindow>();
            services.AddSingleton<ToolTextPopupViewModel>();
            services.AddSingleton<ExtendNodePopupViewModel>();
            services.AddSingleton<MoveNodePopupViewModel>();

            // 渲染服务
            services.AddRenderersFromAssembly(Assembly.Load("DrSoft.Drawing.Controls"));

            // 画布服务
            services.AddSingleton<MultiCanvas>();
            services.AddSingleton<CanvasViewModel>();

            // 对外服务
            services.AddSingleton<ICanvasService, CanvasService>();
            services.AddSingleton<ILayerService, LayerService>();
            services.AddSingleton<IShapeService, ShapeService>();
            services.AddSingleton<IDrawingService, DrawingService>();

            return services;
        }

        /// <summary>
        /// 添加渲染器
        /// </summary>
        /// <param name="services"></param>
        /// <param name="assembly"></param>
        /// <returns></returns>
        public static IServiceCollection AddRenderersFromAssembly(
        this IServiceCollection services,
        Assembly assembly)
        {
            var rendererTypes = assembly
                .GetTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false }
                         && t.GetCustomAttribute<RendererForAttribute>() != null
                         && typeof(IRenderer).IsAssignableFrom(t))
                .ToList();

            foreach (var type in rendererTypes)
            {
                services.AddSingleton(typeof(IRenderer), type);
                Console.WriteLine($"[IOC] 自动注册渲染器: {type.Name}");
            }

            services.AddSingleton<RendererDispatcher>();
            return services;
        }
    }
}
