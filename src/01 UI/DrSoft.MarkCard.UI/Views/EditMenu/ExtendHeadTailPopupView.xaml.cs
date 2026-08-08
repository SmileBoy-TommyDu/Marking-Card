using DrSoft.MarkCard.UI.ViewModes.EditMenu;
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

namespace DrSoft.MarkCard.UI.Views.EditMenu
{
    /// <summary>
    /// ExtendHeadTailPopupView.xaml 的交互逻辑
    /// </summary>
    public partial class ExtendHeadTailPopupView : UserControl
    {
        public ExtendHeadTailPopupView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is ExtendHeadTailPopupViewModel vm)
            {
                vm.LoadDataCommand.Execute(null);
            }
        }
    }
}
