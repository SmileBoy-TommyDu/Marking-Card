using DrSoft.MarkCard.UI.ViewModes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DrSoft.MarkCard.UI.Views
{
    /// <summary>
    /// IOView.xaml 的交互逻辑
    /// </summary>
    public partial class IOView : UserControl
    {
        public IOView()
        {
            InitializeComponent();
            DataContext = App.GetService<IOViewModel>();

            // 界面可见/隐藏时启停轮询
            IsVisibleChanged += OnIsVisibleChanged;
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (DataContext is not IOViewModel vm) return;

            if ((bool)e.NewValue)
                vm.StartPolling();
            else
                vm.StopPolling();
        }

        /// <summary>鼠标进入指示灯：将 Description 显示到底部说明栏</summary>
        private void OnLedMouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is Led led && DataContext is IOViewModel vm)
            {
                vm.IODescription = led.Description;
            }
        }

        /// <summary>鼠标离开指示灯：清空说明栏</summary>
        private void OnLedMouseLeave(object sender, MouseEventArgs e)
        {
            if (DataContext is IOViewModel vm)
            {
                vm.IODescription = string.Empty;
            }
        }
    }
}
