using DrSoft.Drawing.Utility;
using System.Globalization;
using System.Windows.Data;

namespace DrSoft.MarkCard.UI.Converters
{
    public class EnumDescriptionConverter : IValueConverter
    {
        // 将枚举值转换为描述字符串 (用于显示)
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Enum enumValue)
            {
                return enumValue.GetDescription();
            }
            return value;
        }

        // 将描述字符串转换回枚举值 (通常不需要实现，因为 SelectedItem 绑定的是枚举本身)
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
