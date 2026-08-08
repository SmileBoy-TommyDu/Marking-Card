using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DrSoft.MarkCard.CommonUI.UserControls
{
    public class NumericTextBox : TextBox
    {
        // 1. 定义 Value 依赖属性
        public double Value
        {
            get { return (double)GetValue(ValueProperty); }
            set { SetValue(ValueProperty, value); }
        }
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register("Value", typeof(double), typeof(NumericTextBox),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is NumericTextBox textBox && !textBox.Text.Equals(e.NewValue.ToString()))
            {
                textBox.Text = e.NewValue.ToString();
            }
        }

        // 2. 拦截键盘输入
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);

            // 允许控制键
            if (Keyboard.Modifiers == ModifierKeys.Control ||
                e.Key == Key.Back || e.Key == Key.Delete ||
                e.Key == Key.Tab || e.Key == Key.Enter ||
                e.Key == Key.Left || e.Key == Key.Right ||
                e.Key == Key.Home || e.Key == Key.End)
            {
                return;
            }

            // 允许数字键（主键盘和小键盘）
            bool isNumber = (e.Key >= Key.D0 && e.Key <= Key.D9) ||
                           (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9);

            // 允许小数点
            bool isDot = e.Key == Key.OemPeriod || e.Key == Key.Decimal;

            if (!isNumber && !isDot)
            {
                e.Handled = true;
            }
        }

        // 2.1 拦截文本输入
        protected override void OnPreviewTextInput(TextCompositionEventArgs e)
        {
            // 验证输入的文本是否为有效数字字符
            bool isValid = true;
            foreach (char c in e.Text)
            {
                if (!char.IsDigit(c) && c != '.' && c != '-')
                {
                    isValid = false;
                    break;
                }
            }

            e.Handled = !isValid;
            base.OnPreviewTextInput(e);
        }

        // 3. 文本改变时同步 Value
        protected override void OnTextChanged(TextChangedEventArgs e)
        {
            base.OnTextChanged(e);

            // 清理非数字字符
            string cleanedText = CleanNumericText(this.Text);
            if (cleanedText != this.Text)
            {
                this.Text = cleanedText;
                this.CaretIndex = this.Text.Length;
                return;
            }

            if (double.TryParse(cleanedText, out double result))
            {
                SetCurrentValue(ValueProperty, result);
            }
        }

        // 清理文本中的非数字字符
        private string CleanNumericText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var result = new StringBuilder();
            int dotCount = 0;
            bool hasNegative = false;

            foreach (char c in text)
            {
                if (char.IsDigit(c))
                {
                    result.Append(c);
                }
                else if (c == '.')
                {
                    dotCount++;
                    if (dotCount == 1)
                    {
                        result.Append(c);
                    }
                }
                else if (c == '-' && !hasNegative && result.Length == 0)
                {
                    hasNegative = true;
                    result.Append(c);
                }
            }

            return result.ToString();
        }
    }
}
