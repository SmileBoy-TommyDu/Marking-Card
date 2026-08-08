using System;
using System.Globalization;
using System.Windows.Data;

namespace DrSoft.MarkCard.UI.Converters
{
    public class NumericGreaterOrEqualConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return false;

            double val;
            try
            {
                val = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return false;
            }

            double threshold = 0;
            if (parameter != null)
            {
                double.TryParse(parameter.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out threshold);
            }

            return val >= threshold;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
