using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Utility;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace DrSoft.MarkCard.UI.UserControls
{
    public partial class ToastNotificationViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _message = string.Empty;

        [ObservableProperty]
        private bool _isVisible;

        [ObservableProperty]
        private ToastType _toastType;

        private CancellationTokenSource? _autoHideCts;
        private readonly DispatcherTimer _hideTimer;

        public ToastNotificationViewModel()
        {
            _hideTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _hideTimer.Tick += (s, e) =>
            {
                _hideTimer.Stop();
                Hide();
            };
        }

        public void Show(string message, ToastType type = ToastType.Info, int durationSeconds = 3)
        {
            _autoHideCts?.Cancel();
            _autoHideCts = new CancellationTokenSource();

            Message = message;
            ToastType = type;
            IsVisible = true;

            _hideTimer.Interval = TimeSpan.FromSeconds(durationSeconds);
            _hideTimer.Stop();
            _hideTimer.Start();
        }

        public void Hide()
        {
            IsVisible = false;
            _hideTimer.Stop();
        }

        [RelayCommand]
        private void Close()
        {
            Hide();
        }
    }


    //public class ToastMessageEvent: IEvent
    //{
    //    public string Message { get; }
    //    public ToastType Type { get; }

    //    public ToastMessageEvent(string message, ToastType type)
    //    {
    //        Message = message;
    //        Type = type;
    //    }
    //}
    //public enum ToastType
    //{
    //    Info,
    //    Error
    //}
}
