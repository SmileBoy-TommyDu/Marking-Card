using CommunityToolkit.Mvvm.ComponentModel;
using DrSoft.Drawing.DTO;
using DrSoft.MarkCard.UI.ViewModes;
using System.Windows.Controls;

namespace DrSoft.MarkCard.UI.Views
{
    /// <summary>
    /// ShapeParamView.xaml 的交互逻辑
    /// </summary>
    public partial class ShapeParamView : UserControl
    {
 

        public ShapeParamView()
        {
            InitializeComponent();
            DataContext = App.GetService<ShapeParamViewModel>();
        }

       
    }
}
