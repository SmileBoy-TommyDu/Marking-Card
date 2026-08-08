using DrSoft.Drawing.Controls;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Controls.Service;
using DrSoft.Drawing.Controls.ViewModels;
using DrSoft.Drawing.Contracts;
using DrSoft.Drawing.DTO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SkiaSharp;
using System;
using System.Windows;
using System.Windows.Controls;

namespace DrawingControlsDemo
{
    public partial class MainWindow : Window
    {
        private CanvasViewModel? _viewModel;
        private DrawingCanvasControl? _drawingCanvas;
        private ICanvasService? _canvasService;
        private IShapeService? _shapeService;

        // 文字样式设置
        private string _currentFontFamily = "微软雅黑";
        private float _currentFontSize = 12f;
        private bool _isBold = false;
        private bool _isItalic = false;
        private bool _isUnderline = false;
        private float _lineHeight = 1.2f;
        private float _charSpacing = 0f;
        private SKTextAlign _horizontalAlign = SKTextAlign.Left;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeDrawingCanvas();
        }

        private void InitializeDrawingCanvas()
        {
            var app = Application.Current as App;
            if (app?.Services == null) return;

            _viewModel = app.Services.GetService<CanvasViewModel>();
            _canvasService = app.Services.GetService<ICanvasService>();
            _shapeService = app.Services.GetService<IShapeService>();

            if (_viewModel != null)
            {
                var drawingContext = DrawingContext.Create(_viewModel);
                _drawingCanvas = drawingContext.CanvasControl;
                CanvasContainer.Children.Add(_drawingCanvas);
                _drawingCanvas.HorizontalAlignment = HorizontalAlignment.Stretch;
                _drawingCanvas.VerticalAlignment = VerticalAlignment.Stretch;
            }

            UpdateStatus();
        }

        #region 绘图工具

        private void SelectTool_Click(object sender, RoutedEventArgs e)
        {
            _viewModel?.SelectToolCommand.Execute("Select");
            UpdateStatus("已切换到选择工具");
        }

        private void DrawingTool_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null || _viewModel == null) return;

            _viewModel.SelectToolCommand.Execute(button.Tag.ToString());
            UpdateStatus($"已切换到{button.Content}工具");
        }

        #endregion

        #region 操作

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            _shapeService?.Delete();
            UpdateStatus("已删除选中图形");
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            _shapeService?.Copy();
            UpdateStatus("已复制选中图形");
        }

        private void Paste_Click(object sender, RoutedEventArgs e)
        {
            _shapeService?.Paste();
            UpdateStatus("已粘贴图形");
        }

        private void MirrorHorizontal_Click(object sender, RoutedEventArgs e)
        {
            _shapeService?.HorizontalMirror();
            UpdateStatus("已水平镜像选中图形");
        }

        private void MirrorVertical_Click(object sender, RoutedEventArgs e)
        {
            _shapeService?.VerticalMirror();
            UpdateStatus("已垂直镜像选中图形");
        }

        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            _viewModel?.Undo();
            UpdateStatus("已撤销操作");
        }

        private void Redo_Click(object sender, RoutedEventArgs e)
        {
            _viewModel?.Redo();
            UpdateStatus("已重做操作");
        }

        #endregion

        #region 视图操作

        private void Pan_Click(object sender, RoutedEventArgs e)
        {
            // 切换到平移模式
            _viewModel?.SelectToolCommand.Execute("Select");
            UpdateStatus("平移模式: 按住鼠标中键拖动画布移动");
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            // 通过 Viewport 放大
            var viewport = GetActiveViewport();
            if (viewport != null)
            {
                viewport.ZoomAt(1.2f, viewport.OffsetX, viewport.OffsetY);
                _viewModel?.Redraw();
                UpdateZoomText(viewport.Scale);
                UpdateStatus("已放大视图");
            }
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            // 通过 Viewport 缩小
            var viewport = GetActiveViewport();
            if (viewport != null)
            {
                viewport.ZoomAt(0.8f, viewport.OffsetX, viewport.OffsetY);
                _viewModel?.Redraw();
                UpdateZoomText(viewport.Scale);
                UpdateStatus("已缩小视图");
            }
        }

        private void ResetView_Click(object sender, RoutedEventArgs e)
        {
            var viewport = GetActiveViewport();
            if (viewport != null)
            {
                viewport.Reset();
                _viewModel?.Redraw();
                UpdateZoomText(viewport.Scale);
                UpdateStatus("已重置视图");
            }
        }

        private void ZoomToFit_Click(object sender, RoutedEventArgs e)
        {
            var viewport = GetActiveViewport();
            if (viewport != null)
            {
                viewport.Reset();
                // 使用默认缩放比例
                viewport.ZoomAt(1.0f, viewport.OffsetX, viewport.OffsetY);
                _viewModel?.Redraw();
                UpdateZoomText(viewport.Scale);
                UpdateStatus("已适应窗口");
            }
        }

        private Viewport? GetActiveViewport()
        {
            if (DocumentContext.Instance.ActiveCanvas is DrawingCanvas canvas)
            {
                return canvas.Viewport;
            }
            return null;
        }

        private void UpdateZoomText(float scale)
        {
            ZoomText.Text = $"{scale * 100:F0}%";
        }

        #endregion

        #region 文字样式

        private void FontFamily_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FontFamilyCombo.SelectedItem is ComboBoxItem item)
            {
                _currentFontFamily = item.Content?.ToString() ?? "微软雅黑";
            }
        }

        private void FontSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FontSizeCombo.SelectedItem is ComboBoxItem item)
            {
                if (float.TryParse(item.Content?.ToString(), out float size))
                {
                    _currentFontSize = size;
                }
            }
        }

        private void Bold_Click(object sender, RoutedEventArgs e)
        {
            _isBold = BoldCheckBox.IsChecked == true;
        }

        private void Italic_Click(object sender, RoutedEventArgs e)
        {
            _isItalic = ItalicCheckBox.IsChecked == true;
        }

        private void Underline_Click(object sender, RoutedEventArgs e)
        {
            _isUnderline = UnderlineCheckBox.IsChecked == true;
        }

        private void LineHeight_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LineHeightCombo.SelectedItem is ComboBoxItem item)
            {
                if (float.TryParse(item.Content?.ToString(), out float height))
                {
                    _lineHeight = height;
                }
            }
        }

        private void CharSpacing_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CharSpacingCombo.SelectedItem is ComboBoxItem item)
            {
                if (float.TryParse(item.Content?.ToString(), out float spacing))
                {
                    _charSpacing = spacing;
                }
            }
        }

        private void AlignLeft_Click(object sender, RoutedEventArgs e)
        {
            _horizontalAlign = SKTextAlign.Left;
            UpdateStatus("文字对齐: 左对齐");
        }

        private void AlignCenter_Click(object sender, RoutedEventArgs e)
        {
            _horizontalAlign = SKTextAlign.Center;
            UpdateStatus("文字对齐: 居中");
        }

        private void AlignRight_Click(object sender, RoutedEventArgs e)
        {
            _horizontalAlign = SKTextAlign.Right;
            UpdateStatus("文字对齐: 右对齐");
        }

        private void UpdateTextFont_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var fontSettings = new FontSettingsDto
                {
                    FontFamily = _currentFontFamily,
                    FontSize = _currentFontSize,
                    IsBold = _isBold,
                    IsItalic = _isItalic,
                    IsUnderline = _isUnderline,
                    LineHeight = _lineHeight,
                    CharacterSpacing = _charSpacing,
                    HorizontalAlign = (int)_horizontalAlign
                };

                _shapeService?.SetTextFont(fontSettings);
                UpdateStatus($"已更新文字样式: {_currentFontFamily}, {_currentFontSize}pt, " +
                           $"{( _isBold ? "粗体" : "")}{( _isItalic ? "斜体" : "")}{( _isUnderline ? "下划线" : "")}");
            }
            catch (Exception ex)
            {
                UpdateStatus($"更新文字样式失败: {ex.Message}");
            }
        }

        #endregion

        #region 文件操作

        private void NewCanvas_Click(object sender, RoutedEventArgs e)
        {
            _viewModel?.NewCanvasCommand.Execute(null);
            UpdateStatus("已新建画布");
        }

        private async void ImportDxf_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "DXF文件|*.dxf|所有文件|*.*",
                Title = "导入DXF文件"
            };

            if (openFileDialog.ShowDialog() == true && _canvasService != null)
            {
                try
                {
                    await _canvasService.ImportDxfAsync(openFileDialog.FileName);
                    UpdateStatus($"已导入DXF: {System.IO.Path.GetFileName(openFileDialog.FileName)}");
                }
                catch (Exception ex)
                {
                    UpdateStatus($"导入失败: {ex.Message}");
                    MessageBox.Show($"导入失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void ExportDxf_Click(object sender, RoutedEventArgs e)
        {
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "DXF文件|*.dxf",
                Title = "导出DXF文件",
                FileName = "drawing.dxf"
            };

            if (saveFileDialog.ShowDialog() == true && _canvasService != null)
            {
                try
                {
                    await _canvasService.ExportDxfAsync(saveFileDialog.FileName);
                    UpdateStatus($"已导出DXF: {System.IO.Path.GetFileName(saveFileDialog.FileName)}");
                }
                catch (Exception ex)
                {
                    UpdateStatus($"导出失败: {ex.Message}");
                    MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion

        private void UpdateStatus(string message = null)
        {
            StatusText.Text = message ?? "就绪";
        }
    }
}