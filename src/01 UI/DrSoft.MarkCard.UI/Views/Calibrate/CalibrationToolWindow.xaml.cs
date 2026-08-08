using DrSoft.MarkCard.Model.Config;
using DrSoft.MarkCard.UI.ViewModes.Calibrate;
using System.Windows;
using System.Windows.Input;


namespace DrSoft.MarkCard.UI.Views.Calibrate
{
    public partial class CalibrationToolWindow : Window
    {
        public CalibrationToolWindow(ScanHeadConfig scanHeadConfig)
        {
            InitializeComponent();
            var vm = new CalibrationToolViewModel(scanHeadConfig);
            DataContext = vm;
            vm.CalibrateProcessViewModel.CloseAction = () => this.Close();
            vm.GalvoParamViewModel.CloseAction = () => this.Close();
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
