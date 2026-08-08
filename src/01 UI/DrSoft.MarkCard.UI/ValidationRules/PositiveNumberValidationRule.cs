using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace DrSoft.MarkCard.UI.ValidationRules
{
    public class PositiveNumberValidationRule : ValidationRule
    {
        public double MinValue { get; set; } = 0;
        public double MaxValue { get; set; } = double.MaxValue;

        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            if (value == null || string.IsNullOrWhiteSpace(value.ToString()))
                return new ValidationResult(false, "值不能为空");

            if (double.TryParse(value.ToString(), out double result))
            {
                if (result < MinValue)
                    return new ValidationResult(false, $"值不能小于 {MinValue}");
                if (result > MaxValue)
                    return new ValidationResult(false, $"值不能大于 {MaxValue}");
                return ValidationResult.ValidResult;
            }

            return new ValidationResult(false, "请输入有效的数字");
        }
    }
}
