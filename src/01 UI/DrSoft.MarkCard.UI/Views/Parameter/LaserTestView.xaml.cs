using DrSoft.MarkCard.UI.ViewModes;
using System.Windows.Controls;

namespace DrSoft.MarkCard.UI.Views
{
    /// <summary>
    /// LaserTestView.xaml 的交互逻辑
    /// </summary>
    public partial class LaserTestView : UserControl
    {
        public LaserTestView()
        {
            InitializeComponent();
            DataContext= App.GetService<LaserTestViewModel>();
        }
    }
}
