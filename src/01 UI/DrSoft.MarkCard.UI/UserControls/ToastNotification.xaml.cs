using DrSoft.Drawing.Event;
using System.Windows;
using System.Windows.Controls;

namespace DrSoft.MarkCard.UI.UserControls
{
    public partial class ToastNotification : UserControl
    {
        public ToastNotificationViewModel ViewModel { get; }

        public ToastNotification()
        {
            ViewModel = new ToastNotificationViewModel();
            InitializeComponent();
            this.DataContext = ViewModel;
        }

        public void Show(string message, ToastType type = ToastType.Info, int durationSeconds = 3)
        {
            ViewModel.Show(message, type, durationSeconds);
        }

        public void Hide()
        {
            ViewModel.Hide();
        }
    }
}
