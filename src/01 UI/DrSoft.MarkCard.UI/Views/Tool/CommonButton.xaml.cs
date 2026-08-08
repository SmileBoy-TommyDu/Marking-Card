using DrSoft.Drawing.Event;
using DrSoft.MarkCard.Event.Tool;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace DrSoft.MarkCard.UI.Views.Tool
{
    /// <summary>
    /// UserControl1.xaml 的交互逻辑
    /// </summary>
    public partial class CommonButton : UserControl
    {
        public string ToolTip { get; set; } 
        private readonly IEventBus _eventBus;
        public bool CommandTriggered { get; set; } = false;

        public bool IsChecked
        {
            get => StateButtonBtn?.IsChecked ?? false;
            set
            {
                if (StateButtonBtn != null)
                {
                    StateButtonBtn.IsChecked = value;
                }
            }
        }

        public CommonButton(string toolTip, string disableFilePath, string enableFilePath)
        {
            InitializeComponent();

            _eventBus = EventBus.Instance;

            StateButtonBtn.Margin = new Thickness(0, 5, 0, 0);
            StateButtonBtn.ToolTip = toolTip;
            StateButtonBtn.NormalIcon = new BitmapImage(new Uri(enableFilePath, UriKind.Relative));
            StateButtonBtn.DisabledIcon = new BitmapImage(new Uri(disableFilePath, UriKind.Relative));
            StateButtonBtn.Width = 32;
            StateButtonBtn.Height = 32;
            StateButtonBtn.IconHeight = 32;
            StateButtonBtn.IconWidth = 32;

            ToolTip = toolTip;
        }

        private void StateButton_Click(object sender, RoutedEventArgs e)
        {
            if (!StateButtonBtn.IsEnabled) return;

            CommandTriggered = true;
            StateButtonBtn.IsChecked = !StateButtonBtn.IsChecked;
            IsChecked = StateButtonBtn.IsChecked;

            _eventBus.Publish(new ToolButtonClickedEvent
            {
                ToolTip = ToolTip,
                IsChecked = IsChecked
            });
        }
    }
}
