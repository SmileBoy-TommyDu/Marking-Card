using ConfigModel = DrSoft.MarkCard.Model.Config.Config;
using DrSoft.MarkCard.UI.ViewModes.Config;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DrSoft.MarkCard.UI.Views.Config
{
    /// <summary>
    /// ConfigDialog.xaml 的交互逻辑
    /// </summary>
    public partial class ConfigDialog : Window
    {
        public ConfigDialog(ConfigModel config)
        {
            InitializeComponent();
            DataContext = new ConfigDialogViewModel(config);
        }

        private void ConfigTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is ConfigDialogViewModel vm && e.NewValue is TreeViewItem item)
            {
                string header = item.Header.ToString() ?? string.Empty;
                vm.SelectNodeCommand.Execute(header);
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }


    }
}
