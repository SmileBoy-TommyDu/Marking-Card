using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.Drawing.Controls.ViewModels
{
    /// <summary>
    /// 弹框 ViewModel 基类
    /// </summary>
    /// <typeparam name="TResult">弹框关闭时返回的结果类型</typeparam>
    public abstract class DialogViewModelBase<TResult> : ObservableValidator
    {
        private string _title = string.Empty;
        private object? _content;
        //private string _message = string.Empty;

        //// 弹框标题
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        private double _windowHeight = 400;
        public double WindowHeight
        {
            get => _windowHeight;
            set => SetProperty(ref _windowHeight, value);
        }

        //// 弹框提示信息
        //public string Message
        //{
        //    get => _message;
        //    set => SetProperty(ref _message, value);
        //}

        // 中间区域的内容（可以是任意对象：字符串、ViewModel、UIElement）
        public object? Content
        {
            get => _content;
            set => SetProperty(ref _content, value);
        }

        // 确认按钮文本
        public string ConfirmText { get; set; } = "确认";

        // 取消按钮文本
        public string CancelText { get; set; } = "取消";

        // 关闭弹框的事件，View 层订阅此事件
        public event Action<TResult?>? CloseRequested;

        /// <summary>
        /// 弹框即将显示前的准备工作，子类可重写以重置状态
        /// </summary>
        protected internal virtual void OnPrepareForDialog() { }
        
        // 确认命令
        public IRelayCommand ConfirmCommand => _confirmCommand ??= new RelayCommand(OnConfirm);
        private IRelayCommand? _confirmCommand;

        // 取消命令
        public IRelayCommand CancelCommand => _cancelCommand ??= new RelayCommand(OnCancel);
        private IRelayCommand? _cancelCommand;

        // 确认时调用，传递结果并关闭弹框
        protected virtual void OnConfirm()
        {
            var result = GetConfirmResult();
            CloseRequested?.Invoke(result);
        }

        // 取消时调用，传递默认结果并关闭弹框
        protected virtual void OnCancel()
        {
            var result = GetCancelResult();
            CloseRequested?.Invoke(result);
        }

        // 获取确认时的返回结果（子类可重写）
        protected abstract TResult? GetConfirmResult();

        // 获取取消时的返回结果（子类可重写）
        protected abstract TResult? GetCancelResult();
    }
}
