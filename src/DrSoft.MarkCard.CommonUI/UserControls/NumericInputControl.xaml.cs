using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace DrSoft.MarkCard.CommonUI.UserControls
{
    public partial class NumericInputControl : UserControl
    {
        private bool _isUpdatingText;
        private bool _isUpdatingValue;

        #region 依赖属性

        /// <summary>
        /// 数值属性
        /// </summary>
        public double Value
        {
            get { return (double)GetValue(ValueProperty); }
            set
            {
                SetValue(ValueProperty, value);
            }
        }
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register("Value", typeof(double), typeof(NumericInputControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is NumericInputControl control)
            {
                var clampedValue = control.Clamp((double)e.NewValue);
                if (!AreClose(clampedValue, (double)e.NewValue))
                {
                    control.SetCurrentValue(ValueProperty, clampedValue);
                    return;
                }

                control.UpdateTextFromValue(clampedValue);
            }
        }

        public double MinValue
        {
            get { return (double)GetValue(MinValueProperty); }
            set { SetValue(MinValueProperty, value); }
        }

        public static readonly DependencyProperty MinValueProperty =
            DependencyProperty.Register(nameof(MinValue), typeof(double), typeof(NumericInputControl),
                new PropertyMetadata(double.MinValue, OnRangeChanged));

        public double MaxValue
        {
            get { return (double)GetValue(MaxValueProperty); }
            set { SetValue(MaxValueProperty, value); }
        }

        public static readonly DependencyProperty MaxValueProperty =
            DependencyProperty.Register(nameof(MaxValue), typeof(double), typeof(NumericInputControl),
                new PropertyMetadata(double.MaxValue, OnRangeChanged));

        private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is NumericInputControl control)
            {
                control.CoerceCurrentValueIntoRange();
            }
        }

        /// <summary>
        /// 单位属性
        /// </summary>
        public string Unit
        {
            get { return (string)GetValue(UnitProperty); }
            set { SetValue(UnitProperty, value); }
        }
        public static readonly DependencyProperty UnitProperty =
            DependencyProperty.Register("Unit", typeof(string), typeof(NumericInputControl),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.None, OnUnitChanged));

        private static void OnUnitChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is NumericInputControl control)
            {
                control.unitText.Text = (string)e.NewValue;
            }
        }

        /// <summary>
        /// 输入框宽度属性
        /// </summary>
        public double InputWidth
        {
            get { return (double)GetValue(InputWidthProperty); }
            set { SetValue(InputWidthProperty, value); }
        }

        public static readonly DependencyProperty InputWidthProperty =
            DependencyProperty.Register("InputWidth", typeof(double), typeof(NumericInputControl),
                new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.None, OnInputWidthChanged));

        private static void OnInputWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is NumericInputControl control)
            {
                control.numericText.Width = (double)e.NewValue;
            }
        }

        #endregion

        public NumericInputControl()
        {
            InitializeComponent();

            numericText.Text = Value.ToString(CultureInfo.InvariantCulture);

            numericText.TextChanged += NumericText_TextChanged;
            numericText.PreviewKeyDown += NumericText_PreviewKeyDown;
            numericText.PreviewTextInput += NumericText_PreviewTextInput;
            numericText.LostFocus += NumericText_LostFocus;

        }

        private void NumericText_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
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
        }

        private void NumericText_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control ||
                e.Key == Key.Back || e.Key == Key.Delete ||
                e.Key == Key.Tab || e.Key == Key.Enter ||
                e.Key == Key.Left || e.Key == Key.Right ||
                e.Key == Key.Home || e.Key == Key.End)
            {
                return;
            }

            bool isNumber = (e.Key >= Key.D0 && e.Key <= Key.D9) ||
                            (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9);

            bool isDot = e.Key == Key.OemPeriod || e.Key == Key.Decimal;
            bool isMinus = (e.Key == Key.OemMinus || e.Key == Key.Subtract) && MinValue < 0;

            if (!isNumber && !isDot && !isMinus)
            {
                e.Handled = true;
            }
        }

        private void NumericText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingText)
            {
                return;
            }

            TextBox textBox = (TextBox)sender;

            string cleanedText = CleanNumericText(textBox.Text);
            if (cleanedText != textBox.Text)
            {
                _isUpdatingText = true;
                textBox.Text = cleanedText;
                textBox.CaretIndex = textBox.Text.Length;
                _isUpdatingText = false;
                return;
            }

            if (double.TryParse(cleanedText, out double result))
            {
                SetClampedValue(result, false);
            }
        }

        private void NumericText_LostFocus(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(numericText.Text, out double result))
            {
                SetClampedValue(result, true);
            }
            else
            {
                UpdateTextFromValue(Value);
            }
        }

        private void SetClampedValue(double value, bool normalizeText)
        {
            var clampedValue = Clamp(value);
            if (_isUpdatingValue)
            {
                return;
            }

            _isUpdatingValue = true;
            SetCurrentValue(ValueProperty, clampedValue);
            _isUpdatingValue = false;

            if (normalizeText)
            {
                UpdateTextFromValue(clampedValue);
            }
        }

        private void CoerceCurrentValueIntoRange()
        {
            if (MinValue > MaxValue)
            {
                SetCurrentValue(MaxValueProperty, MinValue);
                return;
            }

            var clampedValue = Clamp(Value);
            if (!AreClose(clampedValue, Value))
            {
                SetCurrentValue(ValueProperty, clampedValue);
            }

            UpdateTextFromValue(clampedValue);
        }

        private void UpdateTextFromValue(double value)
        {
            var text = value.ToString(CultureInfo.InvariantCulture);
            if (numericText.Text == text)
            {
                return;
            }

            _isUpdatingText = true;
            numericText.Text = text;
            numericText.CaretIndex = numericText.Text.Length;
            _isUpdatingText = false;
        }

        private double Clamp(double value)
        {
            var min = MinValue;
            var max = MaxValue;
            if (min > max)
            {
                max = min;
            }

            return Math.Clamp(value, min, max);
        }

        private static bool AreClose(double left, double right)
        {
            return Math.Abs(left - right) < 0.0000001;
        }

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
                else if (c == '-' && !hasNegative && result.Length == 0 && MinValue < 0)
                {
                    hasNegative = true;
                    result.Append(c);
                }
            }

            return result.ToString();
        }
    }
}
