using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DrSoft.MarkCard.UI.Converters
{
    public class BoolToBrushConverter : IValueConverter
    {
        // 当布尔值为 true 时使用的颜色（默认黑色）
        public Brush TrueBrush { get; set; } = Brushes.Black;

        // 当布尔值为 false 时使用的颜色（默认灰色）
        public Brush FalseBrush { get; set; } = Brushes.Gray;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
            {
                return b ? TrueBrush : FalseBrush;
            }
            return FalseBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
