using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrSoft.MarkCard.Model.Config;

namespace DrSoft.MarkCard.UI.ViewModes.Config
{
    public partial class LogSettingsViewModel : ObservableObject
    {
        private readonly SystemConfig _config;

        [ObservableProperty]
        private string _logFilePath;

        [ObservableProperty]
        private string _drMarkPath;

        public LogSettingsViewModel(SystemConfig config)
        {
            _config = config;
            _logFilePath = config.LogFilePath;
            _drMarkPath = config.DrMarkPath;
        }

        [RelayCommand]
        private void BrowseLogFile()
        {
            // TODO: 实现文件夹浏览功能
            // 可以使用 System.Windows.Forms.FolderBrowserDialog 或第三方库
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                LogFilePath = dialog.SelectedPath;
                _config.LogFilePath = LogFilePath;
            }
        }

        [RelayCommand]
        private void BrowseDrMarkPath()
        {
            // TODO: 实现文件夹浏览功能
            // 可以使用 System.Windows.Forms.FolderBrowserDialog 或第三方库
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                DrMarkPath = dialog.SelectedPath;
                _config.DrMarkPath = DrMarkPath;
            }
        }
    }
}
