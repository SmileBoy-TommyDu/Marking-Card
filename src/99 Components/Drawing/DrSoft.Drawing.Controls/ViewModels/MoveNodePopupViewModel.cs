using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace DrSoft.Drawing.Controls.ViewModels
{
    /// <summary>
    /// 移动节点对话框结果
    /// </summary>
    public class MoveNodeDialogResult
    {
        public float NewX { get; set; }
        public float NewY { get; set; }
        public bool Confirmed { get; set; }
    }

    /// <summary>
    /// 移动节点坐标输入对话框 ViewModel。
    /// 用户输入目标 X、Y 坐标，确认后将节点移动到指定位置。
    /// </summary>
    public partial class MoveNodePopupViewModel : DialogViewModelBase<MoveNodeDialogResult>
    {
        [ObservableProperty] private float _inputX = 0;
        [ObservableProperty] private float _inputY = 0;

        // 构造时将自定义的 View（UserControl）赋值给 Content
        public MoveNodePopupViewModel()
        {
            Content = new Views.MoveNodePopupView() { DataContext = this };
        }

        protected internal override void OnPrepareForDialog()
        {
            // 重置状态
            InputX = 0;
            InputY = 0;
        }

        /// <summary>
        /// 设置初始坐标值（当前节点位置），对话框打开时显示在输入框中。
        /// </summary>
        public void SetInitialPosition(float x, float y)
        {
            InputX = x;
            InputY = y;
        }

        protected override MoveNodeDialogResult? GetConfirmResult()
        {
            return new MoveNodeDialogResult { NewX = InputX, NewY = InputY, Confirmed = true };
        }

        protected override MoveNodeDialogResult? GetCancelResult()
        {
            return new MoveNodeDialogResult { NewX = 0, NewY = 0, Confirmed = false };
        }
    }
}