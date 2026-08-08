
using DrSoft.Drawing.Event;
using DrSoft.MarkCard.Event.Tool;
using Newtonsoft.Json.Linq;
using System.Windows;
using System.Windows.Controls;

namespace DrSoft.MarkCard.UI.Views.Tool
{
    /// <summary>
    /// ToolButtonControl.xaml 的交互逻辑
    /// </summary>
    public partial class ToolButtonControl : UserControl
    {
        public static readonly DependencyProperty IsCheckedProperty =
                DependencyProperty.Register("Checked", typeof(bool), typeof(ToolButtonControl),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
        public static readonly DependencyProperty IsEnabledProperty =
                DependencyProperty.Register("Enabled", typeof(bool), typeof(ToolButtonControl),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
        public string ToolTip { get => (string)GetValue(ToolTipProperty); set => SetValue(ToolTipProperty, value); }
        public bool IsChecked { get => (bool)GetValue(IsCheckedProperty); set => SetValue(IsCheckedProperty, value); }

        public bool IsEnabled { get => (bool)GetValue(IsEnabledProperty); set => SetValue(IsEnabledProperty, value); }

        private readonly IEventBus _eventBus;

        public bool CommandTriggered { get; set; } = false;

        public ToolButtonControl(string toolTip, Object content)
        {
            InitializeComponent();

            _eventBus = EventBus.Instance;
            ToolTip = toolTip;
            ToggleBtn.Content = content;
        }

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == IsCheckedProperty)
            {
                ToggleBtn.IsChecked = IsChecked;
            }
            else if (e.Property == IsEnabledProperty)
            {
                ToggleBtn.IsEnabled = IsEnabled;
            }
            else if (e.Property == ToolTipProperty)
            {
                ToggleBtn.ToolTip = ToolTip;
            }
        }

        private void ToggleBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!IsEnabled) return;

            CommandTriggered = true;
            IsChecked = ToggleBtn.IsChecked == true;

            _eventBus.Publish(new ToolButtonClickedEvent
            {
                ToolTip = ToolTip,
                IsChecked = IsChecked
            });
        }
    }
}
