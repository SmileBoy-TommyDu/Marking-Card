using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DrSoft.MarkCard.UI.Converters
{
    public class EnumToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null) return Visibility.Collapsed;

            // 如果绑定的枚举值字符串等于参数中的字符串，则显示
            bool isVisible = value.ToString().Equals(parameter.ToString(), StringComparison.OrdinalIgnoreCase);
            return isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
