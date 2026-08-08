using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DrSoft.Drawing.Event;
using DrSoft.MarkCard.UI.ViewModes.Calibrate;
using Microsoft.Win32;

namespace DrSoft.MarkCard.UI.Views.Calibrate
{
    public partial class CalibrationAnalysisView : UserControl
    {
        public CalibrationAnalysisView()
        {
            InitializeComponent();
        }

        private void DirectionTabControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (DataContext is CalibrationAnalysisViewModel vm && e.NewSize.Width > 0)
            {
                vm.ChartViewportWidth = e.NewSize.Width;
            }
        }

        private void HeatmapImage_MouseMove(object sender, MouseEventArgs e)
        {
            if (sender is Image img && DataContext is CalibrationAnalysisViewModel vm)
            {
                var pos = e.GetPosition(img);
                vm.UpdateHeatmapHover(pos.X, pos.Y, img.ActualWidth, img.ActualHeight);
            }
        }

        private void HeatmapImage_MouseLeave(object sender, MouseEventArgs e)
        {
            if (DataContext is CalibrationAnalysisViewModel vm)
            {
                vm.ClearHeatmapHover();
            }
        }

        private void ExportChartButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var (element, chartName) = GetCurrentChart();
                if (element == null)
                {
                    PublishToast("当前没有可导出的图表", ToastType.Error);
                    return;
                }

                if (DataContext is CalibrationAnalysisViewModel vm)
                {
                    vm.ClearHeatmapHover();
                }

                element.UpdateLayout();

                var dialog = new SaveFileDialog
                {
                    Filter = "PNG图片|*.png",
                    Title = "导出图表图片",
                    FileName = $"校正数据分析_{chartName}_{DateTime.Now:yyyyMMddHHmmss}.png"
                };

                if (dialog.ShowDialog() != true)
                    return;

                SaveElementAsPng(element, dialog.FileName);
                PublishToast("导出图片成功", ToastType.Info);
            }
            catch (Exception ex)
            {
                PublishToast($"导出图片失败: {ex.Message}", ToastType.Error);
            }
        }

        private (FrameworkElement? Element, string ChartName) GetCurrentChart()
        {
            return DirectionTabControl.SelectedIndex switch
            {
                0 => (XDirectionChartBorder, "X方向"),
                1 => (YDirectionChartBorder, "Y方向"),
                2 => (HeatmapChartBorder, "热力图"),
                _ => (null, string.Empty)
            };
        }

        private static void SaveElementAsPng(FrameworkElement element, string filePath)
        {
            int width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth));
            int height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight));

            var renderBitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            renderBitmap.Render(element);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(renderBitmap));

            using var stream = File.Create(filePath);
            encoder.Save(stream);
        }

        private static void PublishToast(string message, ToastType type)
        {
            EventBus.Instance.Publish(new ToastMessageEvent(message, type));
        }
    }
}
