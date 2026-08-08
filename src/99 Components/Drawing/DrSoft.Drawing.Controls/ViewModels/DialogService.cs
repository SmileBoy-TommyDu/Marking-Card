using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Controls.Views;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using Application = System.Windows.Application;

namespace DrSoft.Drawing.Controls.ViewModels
{
    /// <summary>
    /// 弹框服务实现
    /// </summary>
    public class DialogService : IDialogService
    {
        private readonly IServiceProvider _serviceProvider;

        public DialogService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// 显示弹框，不关心返回值
        /// </summary>
        public async Task ShowDialogAsync<TViewModel>(Action<TViewModel>? configure = null)
            where TViewModel : DialogViewModelBase<object?>
        {
            var result = await ShowDialogAsync<TViewModel, object?>(configure);
        }

        /// <summary>
        /// 显示弹框并等待返回值
        /// </summary>
        public async Task<TResult?> ShowDialogAsync<TViewModel, TResult>(Action<TViewModel>? configure = null)
            where TViewModel : DialogViewModelBase<TResult>
        {
            // 从依赖注入容器中获取 ViewModel 实例
            var viewModel = _serviceProvider.GetRequiredService<TViewModel>();

            // 执行弹框前的准备工作（重置状态等）
            viewModel.OnPrepareForDialog();

            // 执行配置委托，允许调用方初始化 ViewModel
            configure?.Invoke(viewModel);

            // 创建弹框窗口
            var dialogWindow = new DialogWindow();
            dialogWindow.DataContext = viewModel;

            // 将 ViewModel 的窗口高度传递给 Window 的依赖属性
            dialogWindow.WindowHeight = viewModel.WindowHeight;

            // 设置 Owner 为当前活动窗口或主窗口
            dialogWindow.Owner = Application.Current.MainWindow;
            // 可选：设置居中
            dialogWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            // 使用 TaskCompletionSource 将异步弹框转换为可等待的 Task
            var tcs = new TaskCompletionSource<TResult?>();

            //// 订阅 ViewModel 的关闭事件
            //void CloseHandler(TResult? result)
            //{
            //    viewModel.CloseRequested -= CloseHandler;
            //    // 在 CloseHandler 或 OnConfirm 中
            //    if (dialogWindow != null && dialogWindow.IsLoaded && dialogWindow.IsVisible)
            //    {
            //        dialogWindow.DialogResult = true;
            //        dialogWindow.Close();
            //    }
            //    tcs.SetResult(result);
            //}
            //viewModel.CloseRequested += CloseHandler;


            // 标记是否已经处理过关闭
            bool isClosed = false;

            // 订阅 ViewModel 的关闭事件
            void CloseHandler(TResult? result)
            {
                if (isClosed) return;
                isClosed = true;

                viewModel.CloseRequested -= CloseHandler;

                try
                {
                    // 关闭窗口
                    if (dialogWindow != null && dialogWindow.IsLoaded && dialogWindow.IsVisible)
                    {
                        dialogWindow.DialogResult = true;
                        dialogWindow.Close();
                    }

                    // 设置结果（允许 null）
                    tcs.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }
            viewModel.CloseRequested += CloseHandler;

            // 处理窗口直接关闭（点击 X 或按 Alt+F4）
            void WindowClosedHandler(object? sender, EventArgs e)
            {
                if (isClosed) return;
                isClosed = true;

                dialogWindow.Closed -= WindowClosedHandler;
                viewModel.CloseRequested -= CloseHandler;

                // 窗口被直接关闭，返回 default(TResult)
                // 使用 TrySetResult 避免重复设置
                tcs.TrySetResult(default);
            }
            dialogWindow.Closed += WindowClosedHandler;

            // 显示弹框并等待结果
            dialogWindow.ShowDialog();
            return await tcs.Task;
        }
    }
}
