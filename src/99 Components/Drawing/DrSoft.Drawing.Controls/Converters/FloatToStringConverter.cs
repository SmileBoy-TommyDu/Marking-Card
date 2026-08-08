using System;
using System.Globalization;
using System.Windows.Data;

namespace DrSoft.Drawing.Controls.Converters
{
    public class FloatToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 从float转换为字符串
            if (value is float floatValue)
            {
                return floatValue.ToString("F2", culture);
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 从字符串转换为float
            if (value is string stringValue)
            {
                if (float.TryParse(stringValue, NumberStyles.Float, culture, out float result))
                {
                    return result;
                }
            }
            return 0f;
        }
    }
}