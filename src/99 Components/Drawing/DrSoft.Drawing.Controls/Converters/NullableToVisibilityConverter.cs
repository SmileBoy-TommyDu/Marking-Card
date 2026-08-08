using System;
using System.Windows;
using System.Windows.Data;

namespace DrSoft.Drawing.Controls.Converters
{
    /// <summary>
    /// 将非null值转换为Visible，null值转换为Collapsed
    /// </summary>
    public class NullableToVisibilityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            return value != null ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
