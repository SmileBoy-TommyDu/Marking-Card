using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DrSoft.MarkCard.UI.Converters
{
    /// <summary>
    /// 将索引与选中索引比较，返回对应的文字颜色 Brush
    /// </summary>
    public class IndexToSelectedForegroundConverter : IMultiValueConverter
    {
        private static readonly SolidColorBrush SelectedForeground = new SolidColorBrush(Colors.White);
        private static readonly SolidColorBrush DefaultForeground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#333333"));

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 2 && values[0] is int index && values[1] is int selectedIndex)
            {
                return (index - 1) == selectedIndex ? SelectedForeground : DefaultForeground;
            }
            return DefaultForeground;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
