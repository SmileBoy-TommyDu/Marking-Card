using System.Windows;
using System.Windows.Media;

namespace DrSoft.MarkCard.CommonUI.UserControls
{
    /// <summary>
    /// 颜色选择对话框：将 <see cref="ColorPickerControl"/> 包装为独立弹窗。
    /// <example>
    /// <code>
    /// var dlg = new ColorPickerDialog(currentColor);
    /// if (dlg.ShowDialog() == true)
    ///     myColor = dlg.SelectedColor;
    /// </code>
    /// </example>
    /// </summary>
    public class ColorPickerDialog : Window
    {
        private readonly ColorPickerControl _picker;

        public Color SelectedColor => _picker.SelectedColor;

        public ColorPickerDialog(Color? initialColor = null)
        {
            Title = "颜色";
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            _picker = new ColorPickerControl();
            if (initialColor.HasValue)
                _picker.SelectedColor = initialColor.Value;

            _picker.ColorConfirmed += _ => DialogResult = true;
            Content = _picker;
        }

        public void SetInitialColor(string color)
        {
            try
            {
                var col = (Color)ColorConverter.ConvertFromString(color);
                _picker.SelectedColor = col;
            }
            catch
            {
                // ignore parse errors
            }
        }
    }
}
