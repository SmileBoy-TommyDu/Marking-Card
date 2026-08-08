using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DrSoft.MarkCard.UI.Converters
{
    /// <summary>
    /// 将索引与选中索引比较，返回对应的 Brush
    /// </summary>
    public class IndexToSelectedBrushConverter : IMultiValueConverter
    {
        private static readonly SolidColorBrush SelectedBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A5EB4"));
        private static readonly SolidColorBrush DefaultBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DCDCDC"));

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 2 && values[0] is int index && values[1] is int selectedIndex)
            {
                return (index - 1) == selectedIndex ? SelectedBrush : DefaultBrush;
            }
            return DefaultBrush;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
