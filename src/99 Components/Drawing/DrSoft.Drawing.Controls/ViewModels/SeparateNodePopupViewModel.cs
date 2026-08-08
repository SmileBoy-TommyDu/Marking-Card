using CommunityToolkit.Mvvm.ComponentModel;

namespace DrSoft.Drawing.Controls.ViewModels
{
    /// <summary>
    /// 分离节点对话框结果
    /// </summary>
    public class SeparateNodeDialogResult
    {
        public float Distance { get; set; }
        public bool Confirmed { get; set; }
    }

    /// <summary>
    /// 分离节点对话框 ViewModel。
    /// 用户输入分离距离（mm），确认后激活分离节点模式。
    /// </summary>
    public partial class SeparateNodePopupViewModel : DialogViewModelBase<SeparateNodeDialogResult>
    {
        [ObservableProperty] private float _inputDistance = 2.0f;

        // 构造时将自定义的 View（UserControl）赋值给 Content
        public SeparateNodePopupViewModel()
        {
            Content = new Views.SeparateNodePopupView() { DataContext = this };
        }

        protected internal override void OnPrepareForDialog()
        {
            // 重置状态
            InputDistance = 2.0f;
        }

        /// <summary>
        /// 设置初始距离值，对话框打开时显示在输入框中。
        /// </summary>
        public void SetInitialDistance(float distance)
        {
            InputDistance = distance;
        }

        protected override SeparateNodeDialogResult? GetConfirmResult()
        {
            return new SeparateNodeDialogResult { Distance = InputDistance, Confirmed = true };
        }

        protected override SeparateNodeDialogResult? GetCancelResult()
        {
            return new SeparateNodeDialogResult { Distance = 0, Confirmed = false };
        }
    }
}
