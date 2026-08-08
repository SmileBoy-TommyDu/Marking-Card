using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DrSoft.MarkCard.CommonUI.UserControls
{
    public class NumberDataExpressionTextBox : TextBox
    {
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double?), typeof(NumberDataExpressionTextBox),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnValuePropertyChanged));

        public static readonly DependencyProperty ExpressionProperty =
            DependencyProperty.Register(nameof(Expression), typeof(string), typeof(NumberDataExpressionTextBox),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnExpressionPropertyChanged));

        // 新增：是否限制为非负数（true: 只能 >= 0）
        public static readonly DependencyProperty IsNonNegativeProperty =
            DependencyProperty.Register(nameof(IsNonNegative), typeof(bool), typeof(NumberDataExpressionTextBox),
                new FrameworkPropertyMetadata(false, OnIsNonNegativeChanged));

        // 最小值（默认无下界）
        public static readonly DependencyProperty MinValueProperty =
            DependencyProperty.Register(nameof(MinValue), typeof(double), typeof(NumberDataExpressionTextBox),
                new FrameworkPropertyMetadata(double.NegativeInfinity,
                    FrameworkPropertyMetadataOptions.AffectsMeasure,
                    OnRangeChanged));

        // 最大值（默认无上界）
        public static readonly DependencyProperty MaxValueProperty =
            DependencyProperty.Register(nameof(MaxValue), typeof(double), typeof(NumberDataExpressionTextBox),
                new FrameworkPropertyMetadata(double.PositiveInfinity,
                    FrameworkPropertyMetadataOptions.AffectsMeasure,
                    OnRangeChanged));

        // 小数位数（null表示自动）
        public static readonly DependencyProperty DecimalPlacesProperty =
            DependencyProperty.Register(nameof(DecimalPlaces), typeof(int?), typeof(NumberDataExpressionTextBox),
                new FrameworkPropertyMetadata(null, OnDecimalPlacesChanged));

        // 依赖属性：标题
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(
                nameof(Title),
                typeof(string),
                typeof(NumberDataExpressionTextBox),
                new PropertyMetadata("X"));

        // 依赖属性：单位
        public static readonly DependencyProperty UnitProperty =
            DependencyProperty.Register(
                nameof(Unit),
                typeof(string),
                typeof(NumberDataExpressionTextBox),
                new PropertyMetadata("(mm)"));


        // 是否显示范围校验 ToolTip 提示（默认不显示）
        public static readonly DependencyProperty ShowToolTipProperty =
            DependencyProperty.Register(nameof(ShowToolTip), typeof(bool), typeof(NumberDataExpressionTextBox),
                new PropertyMetadata(false));

        public static readonly DependencyProperty CommittedCommandProperty =
            DependencyProperty.Register(nameof(CommittedCommand), typeof(ICommand), typeof(NumberDataExpressionTextBox));

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }
        public string Unit
        {
            get => (string)GetValue(UnitProperty);
            set => SetValue(UnitProperty, value);
        }
        public int? DecimalPlaces
        {
            get => (int?)GetValue(DecimalPlacesProperty);
            set => SetValue(DecimalPlacesProperty, value);
        }

        public bool IsNonNegative
        {
            get => (bool)GetValue(IsNonNegativeProperty);
            set => SetValue(IsNonNegativeProperty, value);
        }

        /// <summary>
        /// 允许的最小值（含），默认 double.NegativeInfinity。输入/绑定值小于该值时自动钳位。
        /// </summary>
        public double MinValue
        {
            get => (double)GetValue(MinValueProperty);
            set => SetValue(MinValueProperty, value);
        }

        /// <summary>
        /// 允许的最大值（含），默认 double.PositiveInfinity。输入/绑定值大于该值时自动钳位。
        /// </summary>
        public double MaxValue
        {
            get => (double)GetValue(MaxValueProperty);
            set => SetValue(MaxValueProperty, value);
        }

        /// <summary>
        /// 是否在输入值超出范围时显示 ToolTip 提示，默认 false。
        /// </summary>
        public bool ShowToolTip
        {
            get => (bool)GetValue(ShowToolTipProperty);
            set => SetValue(ShowToolTipProperty, value);
        }

        public ICommand CommittedCommand
        {
            get => (ICommand)GetValue(CommittedCommandProperty);
            set => SetValue(CommittedCommandProperty, value);
        }
        private bool outOfRange = false; // 标记当前输入是否超出范围
        private string _lastExpression = null;
        private bool _isResult = false;
        private string _previousValidText = "";
        private double? _previousValidValue = null;
        private bool _skipNextLostFocus = false;
        private bool _isEditing = false;
        private bool _isReverting = false; // 防止恢复操作触发再次验证
        private ToolTip _activeToolTip = null;
        private System.Windows.Threading.DispatcherTimer _toolTipTimer = null;

        // New: parent can set this to tell the textbox not to treat empty text as 0 on LostFocus
        public bool IgnoreEmptyOnLostFocus { get; set; } = false;

        static NumberDataExpressionTextBox()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(NumberDataExpressionTextBox),
                new FrameworkPropertyMetadata(typeof(NumberDataExpressionTextBox)));
        }

        public NumberDataExpressionTextBox()
        {
            LostFocus += NumericExpressionTextBox_LostFocus;
            GotFocus += NumericExpressionTextBox_GotFocus;
            // select all on left-button double-click when already focused
            PreviewMouseLeftButtonDown += NumericExpressionTextBox_PreviewMouseLeftButtonDown;
            PreviewTextInput += OnPreviewTextInput;
            PreviewKeyDown += OnPreviewKeyDown;
            TextChanged += OnTextChanged;
            _previousValidText = Text;
        }

        private void NumericExpressionTextBox_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                if (e.ClickCount == 2 && IsKeyboardFocusWithin)
                {
                    // when already focused, a double-click should select all text
                    SelectAll();
                    e.Handled = true;
                }
            }
            catch { }
        }

        public double? Value
        {
            get => (double?)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public string Expression
        {
            get => (string)GetValue(ExpressionProperty);
            set => SetValue(ExpressionProperty, value);
        }

        private static void OnValuePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is NumberDataExpressionTextBox control)
            {
                // 非负数约束：强制将负值修正为 0
                if (control.IsNonNegative && e.NewValue is double nonNegVal && nonNegVal < 0)
                {
                    control.SetCurrentValue(ValueProperty, 0.0);
                    return;
                }

                // 范围约束：超出 [MinValue, MaxValue] 则恢复修改前的值
                if (e.NewValue is double newVal)
                {
                    if (newVal < control.MinValue || newVal > control.MaxValue)
                    {
                        // 恢复到上一次有效值
                        var revertValue = control._previousValidValue ?? 0.0;
                        control.SetCurrentValue(ValueProperty, revertValue);
                        return;
                    }
                }

                //if (control._isEditing)
                //    return;

                if (e.NewValue != null)
                {
                    double val = (double)e.NewValue;
                    string newText = control.FormatNumber(val);
                    if (control.Text != newText)
                    {
                        control.Text = newText;
                        control._previousValidText = newText;
                        control._previousValidValue = val;
                        control._isResult = true;
                        control._lastExpression = null;
                    }
                }
                else if (string.IsNullOrEmpty(control.Text))
                {
                    control.Text = "";
                }
            }
        }

        private static void OnExpressionPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is NumberDataExpressionTextBox control && e.NewValue is string newExpr)
            {
                if (!control._isResult && control._lastExpression != newExpr)
                {
                    control._lastExpression = newExpr;
                }
            }
        }

        private static void OnDecimalPlacesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is NumberDataExpressionTextBox control && control.Value.HasValue)
            {
                string newText = control.FormatNumber(control.Value.Value);
                if (control.Text != newText)
                    control.Text = newText;
            }
        }

        private static void OnIsNonNegativeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is NumberDataExpressionTextBox control && (bool)e.NewValue)
            {
                // 当启用非负限制时，若当前值为负数则立即修正为 0
                if (control.Value.HasValue && control.Value.Value < 0)
                {
                    control.SetCurrentValue(ValueProperty, 0.0);
                }
            }
        }

        /// <summary>
        /// MinValue / MaxValue 变化时，若当前 Value 越界则恢复到上次有效值。
        /// </summary>
        private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is NumberDataExpressionTextBox control && control.Value.HasValue)
            {
                double val = control.Value.Value;
                if (val < control.MinValue || val > control.MaxValue)
                {
                    var revertValue = control._previousValidValue ?? 0.0;
                    control.SetCurrentValue(ValueProperty, revertValue);
                }
            }
        }

        /// <summary>
        /// 将数值钳位到 [MinValue, MaxValue] 范围内。
        /// </summary>
        private double Clamp(double v)
        {
            if (v < MinValue) return MinValue;
            if (v > MaxValue) return MaxValue;
            return v;
        }

        private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var regex = new Regex(@"^[0-9+\-*/\(\)\.\s]");
            if (!regex.IsMatch(e.Text))
                e.Handled = true;
        }

        /// <summary>
        /// 文本变化时，对纯数字输入做即时范围验证。
        /// 如果输入的是完整数字且超出范围，立即恢复为修改前的值并提示。
        /// </summary>
        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            // If we're programmatically reverting or the user is actively editing, skip immediate validation
            if (_isReverting || _isEditing)
                return;

            string currentText = this.Text?.Trim();
            if (string.IsNullOrWhiteSpace(currentText))
                return;

            // 只对纯数字（非表达式）做即时验证
            string cleanText = Regex.Replace(currentText, @"\s+", "");
            if (!Regex.IsMatch(cleanText, @"^-?\d+(\.\d+)?$"))
                return;

            double numValue;
            if (!double.TryParse(cleanText, NumberStyles.Float, CultureInfo.InvariantCulture, out numValue))
                return;

            // 检查非负约束
            bool outOfRange = false;
            if (IsNonNegative && numValue < 0)
                outOfRange = true;

            // 检查范围约束
            if (!outOfRange && (numValue < MinValue || numValue > MaxValue))
                outOfRange = true;

            if (outOfRange)
            {
                _isReverting = true;
                this.Text = _previousValidText;
                this.CaretIndex = _previousValidText.Length;
                _isReverting = false;

                ShowTemporaryToolTip(BuildOutOfRangeToolTip());
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                e.Handled = false;
            }
            else if (e.Key == Key.Enter)
            {
                PerformCalculation();
                _skipNextLostFocus = true;
                MoveFocusToMainWindow();
                Keyboard.ClearFocus();
                e.Handled = true;
                _isEditing = false;
            }
        }
        private void MoveFocusToMainWindow()
        {
            Dispatcher.BeginInvoke((Action)(() =>
            {
                var mainWindow = Application.Current?.MainWindow as Window;
                Window target = mainWindow ?? Window.GetWindow(this);
                if (target != null)
                {
                    try { target.Activate(); } catch { }

                    if (target.Content is UIElement root)
                    {
                        bool originalFocusable = root.Focusable;
                        try
                        {
                            root.Focusable = true;
                            FocusManager.SetFocusedElement(FocusManager.GetFocusScope(target), root);
                            root.Focus();
                            Keyboard.Focus(root);
                        }
                        finally
                        {
                            root.Focusable = originalFocusable;
                        }
                    }
                    else
                    {
                        FocusManager.SetFocusedElement(FocusManager.GetFocusScope(target), target);
                        target.Focus();
                        Keyboard.Focus(target);
                    }
                }
            }), DispatcherPriority.ApplicationIdle);
        }
        private void NumericExpressionTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            _isEditing = true;
            SelectAll();
        }

        private void NumericExpressionTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            _isEditing = false;
            if (_skipNextLostFocus)
            {
                _skipNextLostFocus = false;
                return;
            }
            PerformCalculation();
            Keyboard.ClearFocus();
        }

        private void PerformCalculation()
        {
            string currentText = this.Text?.Trim();
            if (string.IsNullOrWhiteSpace(currentText))
            {
                // If parent requested to ignore empty on lost focus (e.g., cleared by Delete), 
                // restore text to previous valid value and return without committing
                if (IgnoreEmptyOnLostFocus)
                {
                    IgnoreEmptyOnLostFocus = false;
                    this.Text = _previousValidText;  // Restore text before returning
                    _isResult = true;
                    return;
                }

                currentText = "0";
            }

            double? result = EvaluateExpression(currentText);
            outOfRange = false; // Reset flag at start of calculation

            if (result.HasValue)
            {
                // Check raw out-of-range (without clamping). If out of allowed bounds, do NOT commit.
                // First check this control's own limits
                if (IsNonNegative && result.Value < 0)
                    outOfRange = true;

                if (result.Value < MinValue || result.Value > MaxValue)
                    outOfRange = true;

                // Also respect bounds declared on a parent NumericUpDownControl if present
                try
                {
                    var parent = System.Windows.Media.VisualTreeHelper.GetParent(this);
                    while (parent != null && !(parent is NumericUpDownControl))
                    {
                        parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
                    }
                    if (parent is NumericUpDownControl numUp)
                    {
                        if (result.Value < numUp.MinValue || result.Value > numUp.MaxValue)
                            outOfRange = true;
                    }
                }
                catch { }

                if (outOfRange)
                {
                    // restore previous valid text and show tooltip; do not update Value or execute command
                    this.Text = _previousValidText;
                    _isResult = true;

                    ShowTemporaryToolTip(BuildOutOfRangeToolTip());
                    return;
                }

                // within range: proceed and possibly clamp to boundaries (should be same as raw)
                string formattedResult = FormatNumber(result.Value);
                _lastExpression = currentText;
                this.Text = formattedResult;
                _isResult = true;
                _previousValidText = formattedResult;
                _previousValidValue = result.Value;

                Value = result.Value;
                Expression = _lastExpression;

                if (CommittedCommand != null && CommittedCommand.CanExecute(result.Value))
                    CommittedCommand.Execute(result.Value);
            }
            else
            {
                // 无效表达式或超出范围时，恢复上次有效值
                this.Text = _previousValidText;
                _isResult = true;

                if (_previousValidValue.HasValue)
                {
                    Value = _previousValidValue.Value;
                }

                // 提示用户
                if (outOfRange)
                {
                    ShowTemporaryToolTip(BuildOutOfRangeToolTip());
                }
                else
                {
                    ShowTemporaryToolTip("表达式无效");
                }
            }
        }

        /// <summary>
        /// 程序化弹出 ToolTip 提示，3秒后自动关闭。
        /// </summary>
        private void ShowTemporaryToolTip(string message)
        {
            if (!ShowToolTip)
                return;

            // 关闭已有的 ToolTip
            if (_activeToolTip != null)
            {
                _activeToolTip.IsOpen = false;
                _activeToolTip = null;
            }
            if (_toolTipTimer != null)
            {
                _toolTipTimer.Stop();
                _toolTipTimer = null;
            }

            var toolTip = new ToolTip
            {
                Content = message,
                Placement = PlacementMode.Bottom,
                PlacementTarget = this,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F56C6C")),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                IsOpen = true
            };
            this.ToolTip = toolTip;
            _activeToolTip = toolTip;

            _toolTipTimer = new System.Windows.Threading.DispatcherTimer();
            _toolTipTimer.Interval = TimeSpan.FromSeconds(3);
            _toolTipTimer.Tick += (s, args) =>
            {
                toolTip.IsOpen = false;
                this.ToolTip = null;
                _activeToolTip = null;
                _toolTipTimer.Stop();
                _toolTipTimer = null;
            };
            _toolTipTimer.Start();
        }

        private string BuildOutOfRangeToolTip()
        {
            bool hasMin = !double.IsNegativeInfinity(MinValue);
            bool hasMax = !double.IsPositiveInfinity(MaxValue);
            if (hasMin && hasMax)
                return $"输入值超出有效范围";
            if (hasMin)
                return $"输入值不能小于 {FormatNumber(MinValue)}";
            if (hasMax)
                return $"输入值不能大于 {FormatNumber(MaxValue)}";
            return "输入值超出有效范围";
        }

        private double? EvaluateExpression(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
                return null;

            try
            {
                string cleanExpr = Regex.Replace(expression, @"\s+", "");
                if (Regex.IsMatch(cleanExpr, @"^-?\d+(\.\d+)?$"))
                    return Convert.ToDouble(cleanExpr, CultureInfo.InvariantCulture);

                using (DataTable dt = new DataTable())
                {
                    string computeExpr = cleanExpr.Replace("×", "*").Replace("÷", "/");
                    var result = dt.Compute(computeExpr, "");
                    return Convert.ToDouble(result, CultureInfo.InvariantCulture);
                }
            }
            catch
            {
                return null;
            }
        }

        private string FormatNumber(double value)
        {
            if (DecimalPlaces.HasValue)
            {
                string format = $"F{DecimalPlaces.Value}";
                return value.ToString(format, CultureInfo.InvariantCulture);
            }
            else
            {
                if (Math.Abs(value - Math.Round(value)) < 1e-10)
                    return value.ToString("0", CultureInfo.InvariantCulture);
                return value.ToString("0.########", CultureInfo.InvariantCulture);
            }
        }
    }
}