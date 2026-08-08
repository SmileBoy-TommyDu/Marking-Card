using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DrSoft.MarkCard.UI.Converters
{
    /// <summary>
    /// 十六进制颜色字符串 (#RGB/#RRGGBB/#AARRGGBB) 与 Brush 双向转换
    /// </summary>
    public class HexColorToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string hex && !string.IsNullOrWhiteSpace(hex))
            {
                try
                {
                    var color = (Color)ColorConverter.ConvertFromString(hex);
                    return new SolidColorBrush(color);
                }
                catch
                {
                    return Brushes.Transparent;
                }
            }
            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SolidColorBrush brush)
            {
                var c = brush.Color;
                return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            }
            return "#000000";
        }
    }
}
