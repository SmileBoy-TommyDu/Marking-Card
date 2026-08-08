using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrSoft.Drawing.Event;
using Microsoft.Win32;
using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using ConfigModel = DrSoft.MarkCard.Model.Config.Config;

namespace DrSoft.MarkCard.UI.ViewModes.Config
{
    public partial class ImportExportConfigViewModel : ObservableObject
    {
        private readonly ConfigModel _config;

        [ObservableProperty]
        private string _filePath = string.Empty;

        public ImportExportConfigViewModel(ConfigModel config)
        {
            _config = config;
            _filePath = ConfigModel.Config_Path;
        }

        [RelayCommand]
        private void Import()
        {
            var dlg = new OpenFileDialog
            {
                DefaultExt = ".json",
                Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                CheckFileExists = true
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var json = File.ReadAllText(dlg.FileName);
                var imported = JsonSerializer.Deserialize<ConfigModel>(json);
                if (imported == null)
                {
                    EventBus.Instance?.Publish(new ToastMessageEvent("所选文件不是有效的配置文件。", ToastType.Warning));
                    return;
                }

                FilePath = dlg.FileName;
                File.Copy(dlg.FileName, ConfigModel.Config_Path, overwrite: true);
                
                EventBus.Instance?.Publish(new ToastMessageEvent("配置已导入，重启应用后生效。", ToastType.Info));
            }
            catch (Exception ex)
            {
                EventBus.Instance?.Publish(new ToastMessageEvent($"导入失败：{ ex.Message }", ToastType.Error));
                
            }
        }

        [RelayCommand]
        private void Export()
        {
            var dlg = new SaveFileDialog
            {
                FileName = "config.json",
                DefaultExt = ".json",
                Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                AddExtension = true
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                FilePath = dlg.FileName;
                var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(dlg.FileName, json);
                EventBus.Instance?.Publish(new ToastMessageEvent("配置已导出。", ToastType.Info));
                //MessageBox.Show("配置已导出。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                EventBus.Instance?.Publish(new ToastMessageEvent($"导出失败：{ex.Message}", ToastType.Error));
                //MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
