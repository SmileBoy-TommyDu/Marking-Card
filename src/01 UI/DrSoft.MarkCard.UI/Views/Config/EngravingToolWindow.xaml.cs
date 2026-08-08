using System.Windows;
using DrSoft.MarkCard.UI.ViewModes.Config;

namespace DrSoft.MarkCard.UI.Views.Config
{
    /// <summary>
    /// EngravingToolWindow.xaml 的交互逻辑
    /// </summary>
    public partial class EngravingToolWindow : Window
    {
        public EngravingToolWindow()
        {
            InitializeComponent();
            DataContext = App.GetService<EngravingToolViewModel>();
        }
    }
}
