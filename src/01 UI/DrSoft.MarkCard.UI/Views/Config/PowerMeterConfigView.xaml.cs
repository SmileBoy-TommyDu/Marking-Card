using DrSoft.MarkCard.UI.ViewModes.Config;
using System.Windows.Controls;

namespace DrSoft.MarkCard.UI.Views.Config
{
    /// <summary>
    /// PowerMeterConfigView.xaml 的交互逻辑
    /// </summary>
    public partial class PowerMeterConfigView : UserControl
    {
        public PowerMeterConfigView()
        {
            InitializeComponent();
            Unloaded += PowerMeterConfigView_Unloaded;
        }

        private void PowerMeterConfigView_Unloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is PowerMeterConfigViewModel vm)
            {
                vm.Dispose();
            }
        }
    }
}
