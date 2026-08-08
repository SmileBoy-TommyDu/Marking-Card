using System;
using System.Globalization;
using System.Windows.Data;

namespace DrSoft.MarkCard.UI.Converters
{
    public class TabIndexToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int tab = System.Convert.ToInt32(parameter);
            return (int)value == tab;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if ((bool)value) return System.Convert.ToInt32(parameter);
            return Binding.DoNothing;
        }
    }
}
