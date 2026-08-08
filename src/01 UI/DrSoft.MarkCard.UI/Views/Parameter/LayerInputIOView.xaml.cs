using DrSoft.MarkCard.UI.ViewModes;
using System.Windows.Controls;

namespace DrSoft.MarkCard.UI.Views
{
    /// <summary>
    /// LayerInputIO.xaml 的交互逻辑
    /// </summary>
    public partial class LayerInputIOView : UserControl
    {
        public LayerInputIOView()
        {
            InitializeComponent();
            DataContext = App.GetService<LayerInputIOViewModel>();
        }
    }
}
