using DrSoft.MarkCard.UI.ViewModes;
using DrSoft.MarkCard.UI.ViewModes.EditMenu;
using System.Windows.Controls;

namespace DrSoft.MarkCard.UI.Views.EditMenu
{
    /// <summary>
    /// SkyWritingPopupView.xaml 的交互逻辑
    /// </summary>
    public partial class SkyWritingPopupView : UserControl
    {
        public SkyWritingPopupView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is SkyWritingPopupViewModel vm)
            {
                vm.LoadDataCommand.Execute(null);
            }
        }
    }
}
