using CommunityToolkit.Mvvm.Input;
using DrSoft.Drawing.Controls.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.Drawing.Controls.Interface
{
    /// <summary>
    /// 弹框服务接口
    /// </summary>
    public interface IDialogService
    {
        /// <summary>
        /// 显示弹框，不关心返回值
        /// </summary>
        Task ShowDialogAsync<TViewModel>(Action<TViewModel>? configure = null)
            where TViewModel : DialogViewModelBase<object?>;

        /// <summary>
        /// 显示弹框并等待返回值
        /// </summary>
        Task<TResult?> ShowDialogAsync<TViewModel, TResult>(Action<TViewModel>? configure = null)
            where TViewModel : DialogViewModelBase<TResult>;
    }
}
