using System.ComponentModel;
using System.Windows.Controls;
using DrSoft.MarkCard.UI.ViewModes;

namespace DrSoft.MarkCard.UI.Views
{
    /// <summary>
    /// LayerOutputIO.xaml 的交互逻辑
    /// </summary>
    public partial class LayerOutputIOView : UserControl
    {
        public LayerOutputIOView()
        {
            InitializeComponent();
            DataContext =  App.GetService<LayerOutputIOViewModel>();
        }
    }
}
