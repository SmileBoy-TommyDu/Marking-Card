using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace DrSoft.MarkCard.UI.Converters
{
    public class DoubleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 源 -> 目标：直接返回数值
            return value?.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 目标 -> 源：处理字符串转换
            if (value is string str)
            {
                if (string.IsNullOrWhiteSpace(str)) return 0.0;

                // 尝试解析，允许小数点结尾的中间状态
                if (double.TryParse(str, NumberStyles.Any, culture, out double result))
                {
                    
                    return result;
                }
                else
                {
                    return Binding.TargetUpdatedEvent;
                }
            }
            // 如果解析失败（例如只输入了 "."），返回 Binding.DoNothing 保持界面不变，或者返回 0
            return Binding.DoNothing;
        }
    }
}
