using System;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DrSoft.MarkCard.CommonUI.UserControls
{
    public partial class NumericUpDownControl : UserControl
    {
        private bool _isUpdatingValue;
        private bool _wasClearedByDelete = false;
        private double _lastValueBeforeDelete = 0.0;
        private double _previousValidValue = 0.0;
        private ToolTip _activeToolTip = null;
        private DispatcherTimer _toolTipTimer = null;

        // track value before user started editing so we can restore when out-of-range
        private double _valueBeforeEdit = 0.0;
        private bool _isEditing = false;

        public bool IsCommandTriggered { get; set; } = false;

        // 在 NumericUpDownControl 类的 public 成员区域添加：
        public event RoutedEventHandler? ValueTextBoxLostFocus;


        #region 依赖属性

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double), typeof(NumericUpDownControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is NumericUpDownControl control)
            {
                double newVal = (double)e.NewValue;
                // 范围约束：超出 [MinValue, MaxValue] 则恢复修改前的值
                if (newVal < control.MinValue || newVal > control.MaxValue)
                {
                    control.SetCurrentValue(ValueProperty, control._previousValidValue);
                    control.ShowTemporaryToolTip(control.BuildOutOfRangeToolTip());
                    return;
                }
                // 记录有效值
                control._previousValidValue = newVal;
                // 仅在非编辑状态时更新文本，避免干扰用户输入
                if (control.ValueTextBox != null && !control.ValueTextBox.IsFocused)
                {
                    control.ValueTextBox.Text = control.FormatNumber(newVal);
                }
            }
        }

        public double Step
        {
            get => (double)GetValue(StepProperty);
            set => SetValue(StepProperty, value);
        }
        public static readonly DependencyProperty StepProperty =
            DependencyProperty.Register(nameof(Step), typeof(double), typeof(NumericUpDownControl),
                new PropertyMetadata(0.001));

        public int DecimalPlaces
        {
            get => (int)GetValue(DecimalPlacesProperty);
            set => SetValue(DecimalPlacesProperty, value);
        }
        public static readonly DependencyProperty DecimalPlacesProperty =
            DependencyProperty.Register(nameof(DecimalPlaces), typeof(int), typeof(NumericUpDownControl),
                new PropertyMetadata(3, OnDecimalPlacesChanged));

        private static void OnDecimalPlacesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is NumericUpDownControl control)
            {
                // 调整 Step：若 DecimalPlaces == 0 则 step = 1；否则按常见规则 step = 1 / (10 ^ DecimalPlaces)
                try
                {
                    int dp = control.DecimalPlaces;
                    if (dp <= 0)
                    {
                        control.SetCurrentValue(StepProperty, 1.0);
                    }
                    else
                    {
                        control.SetCurrentValue(StepProperty, Math.Pow(10.0, -dp));
                    }
                }
                catch
                {
                    control.SetCurrentValue(StepProperty, 0.001);
                }

                control.UpdateTextFromValue(control.Value);
            }
        }

        public double MinValue
        {
            get => (double)GetValue(MinValueProperty);
            set => SetValue(MinValueProperty, value);
        }
        public static readonly DependencyProperty MinValueProperty =
            DependencyProperty.Register(nameof(MinValue), typeof(double), typeof(NumericUpDownControl),
                new PropertyMetadata(double.MinValue, OnRangeChanged));

        public double MaxValue
        {
            get => (double)GetValue(MaxValueProperty);
            set => SetValue(MaxValueProperty, value);
        }
        public static readonly DependencyProperty MaxValueProperty =
            DependencyProperty.Register(nameof(MaxValue), typeof(double), typeof(NumericUpDownControl),
                new PropertyMetadata(double.MaxValue, OnRangeChanged));

        private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is NumericUpDownControl control)
            {
                control.CoerceCurrentValueIntoRange();
                // if child exists, update its Min/Max as well
                if (control.ValueTextBox != null)
                {
                    control.ValueTextBox.MinValue = control.MinValue;
                    control.ValueTextBox.MaxValue = control.MaxValue;
                }
            }
        }
        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(NumericUpDownControl), new PropertyMetadata(string.Empty));
        // Unit dependency property
        public string Unit
        {
            get => (string)GetValue(UnitProperty);
            set => SetValue(UnitProperty, value);
        }
        public static readonly DependencyProperty UnitProperty =
            DependencyProperty.Register(nameof(Unit), typeof(string), typeof(NumericUpDownControl), new PropertyMetadata(string.Empty));

        // 是否显示范围校验 ToolTip 提示（默认不显示）
        public static readonly DependencyProperty ShowToolTipProperty =
            DependencyProperty.Register(nameof(ShowToolTip), typeof(bool), typeof(NumericUpDownControl),
                new PropertyMetadata(false));

        /// <summary>
        /// 是否在输入值超出范围时显示 ToolTip 提示，默认 false。
        /// </summary>
        public bool ShowToolTip
        {
            get => (bool)GetValue(ShowToolTipProperty);
            set => SetValue(ShowToolTipProperty, value);
        }

        // CommittedCommand dependency property
        public ICommand? CommittedCommand
        {
            get => (ICommand?)GetValue(CommittedCommandProperty);
            set => SetValue(CommittedCommandProperty, value);
        }
        public static readonly DependencyProperty CommittedCommandProperty =
            DependencyProperty.Register(nameof(CommittedCommand), typeof(ICommand), typeof(NumericUpDownControl), new PropertyMetadata(null, OnCommittedCommandChanged));

        private static void OnCommittedCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is NumericUpDownControl control)
            {
                control.UpdateWrappedCommittedCommand();
            }
        }

        // update child NumericExpressionTextBox's CommittedCommand to be a wrapper that enforces Min/Max
        private void UpdateWrappedCommittedCommand()
        {
            if (ValueTextBox == null)
                return;

            var original = (ICommand?)GetValue(CommittedCommandProperty);
            if (original == null)
            {
                ValueTextBox.CommittedCommand = null;
                return;
            }

            ValueTextBox.CommittedCommand = new CommittedCommandWrapper(original, this);
        }

        // simple ICommand wrapper that checks numeric param against Min/Max before forwarding
        private class CommittedCommandWrapper : ICommand
        {
            private readonly ICommand _inner;
            private readonly NumericUpDownControl _owner;
            public CommittedCommandWrapper(ICommand inner, NumericUpDownControl owner)
            {
                _inner = inner;
                _owner = owner;
            }

            public bool CanExecute(object parameter)
            {
                if (!_inner.CanExecute(parameter)) return false;
                if (parameter is double d)
                {
                    if (d < _owner.MinValue || d > _owner.MaxValue) return false;
                }
                return true;
            }

            public event EventHandler CanExecuteChanged
            {
                add { _inner.CanExecuteChanged += value; }
                remove { _inner.CanExecuteChanged -= value; }
            }

            public void Execute(object parameter)
            {
                if (parameter is double d)
                {
                    if (d < _owner.MinValue || d > _owner.MaxValue)
                        return; // don't forward
                }
                _inner.Execute(parameter);
            }
        }

        #endregion

        public NumericUpDownControl()
        {
            InitializeComponent();
            UpdateTextFromValue(Value);
            UpdateVisualState();

            // 监听 IsEnabled 变化，刷新禁用/启用的视觉外观
            this.IsEnabledChanged += (s, e) => UpdateVisualState();

            this.MouseEnter += (s, e) => OnMouseEntered();
            this.MouseLeave += (s, e) => OnMouseLeft();

            UpButton.GotFocus += (s, e) => UpdateVisualState();
            UpButton.LostFocus += (s, e) => UpdateVisualState();
            DownButton.GotFocus += (s, e) => UpdateVisualState();
            DownButton.LostFocus += (s, e) => UpdateVisualState();

            // handle keyboard arrow keys to trigger buttons
            this.PreviewKeyDown += NumericUpDownControl_PreviewKeyDown;

            // ensure we clear the 'cleared-by-delete' flag when user types new content
            if (ValueTextBox != null)
            {
                ValueTextBox.TextChanged += ValueTextBox_TextChanged;
                ValueTextBox.GotFocus += ValueTextBox_GotFocus;
                ValueTextBox.LostFocus += ValueTextBox_LostFocus;
                ValueTextBox.PreviewKeyDown += ValueTextBox_PreviewKeyDown;
            }

            // initialize edit tracking
            _valueBeforeEdit = Value;
            _isEditing = false;

            // ensure wrapped command set if CommittedCommand was assigned in XAML
            UpdateWrappedCommittedCommand();
        }

        private void OnMouseEntered()
        {
            // hide unit and show buttons
            ValueTextBox.Unit = string.Empty;
            UpButton.Visibility = Visibility.Visible;
            DownButton.Visibility = Visibility.Visible;
            UpdateVisualState();
        }

        private void OnMouseLeft()
        {
            // restore unit and hide buttons
            ValueTextBox.Unit = Unit;
            UpButton.Visibility = Visibility.Collapsed;
            DownButton.Visibility = Visibility.Collapsed;
            UpdateVisualState();
        }

        private void NumericUpDownControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!IsEnabled) return;
            if (e.Key == Key.Up)
            {
                // simulate up click
                UpButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, UpButton));
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                DownButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, DownButton));
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                // If focus is within this control (either on ValueTextBox or control), handle Delete/Backspace here
                if (this.IsKeyboardFocusWithin)
                {
                    // clear inner textbox and remember last value
                    if (ValueTextBox != null)
                    {
                        _wasClearedByDelete = true;
                        _lastValueBeforeDelete = Value;
                        // tell child textbox to ignore empty on lost focus so it won't commit 0
                        ValueTextBox.IgnoreEmptyOnLostFocus = true;
                        ValueTextBox.Text = string.Empty;
                    }
                    e.Handled = true; // prevent parent/window commands from executing
                }
            }
            else if (e.Key == Key.X && Keyboard.Modifiers == ModifierKeys.Control)
            {
                // Ctrl+X 剪切：将当前值复制到剪贴板并清空输入框
                if (this.IsKeyboardFocusWithin && ValueTextBox != null)
                {
                    _wasClearedByDelete = true;
                    _lastValueBeforeDelete = Value;
                    // 将当前显示文本复制到剪贴板
                    var textToCopy = string.IsNullOrWhiteSpace(ValueTextBox.Text)
                        ? FormatNumber(Value)
                        : ValueTextBox.Text;
                    Clipboard.SetText(textToCopy);
                    ValueTextBox.IgnoreEmptyOnLostFocus = true;
                    ValueTextBox.Text = string.Empty;
                    UpdateVisualState();
                    e.Handled = true; // 阻止全局 Cut 命令执行
                }
            }
            else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
            {
                // Ctrl+V 粘贴：从剪贴板读取数值并填入输入框
                if (this.IsKeyboardFocusWithin && ValueTextBox != null)
                {
                    var clipText = Clipboard.GetText();
                    if (!string.IsNullOrWhiteSpace(clipText))
                    {
                        if (double.TryParse(clipText, NumberStyles.Any, CultureInfo.InvariantCulture, out var pastedValue))
                        {
                            var clamped = Clamp(pastedValue);
                            ValueTextBox.IgnoreEmptyOnLostFocus = false;
                            ValueTextBox.Text = FormatNumber(clamped);
                            _wasClearedByDelete = false;
                            if (!AreClose(clamped, pastedValue))
                            {
                                ShowTemporaryToolTip(BuildOutOfRangeToolTip());
                            }
                            UpdateVisualState();
                        }
                    }
                    e.Handled = true; // 阻止全局 Paste 命令执行
                }
            }
            else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
            {
                // Ctrl+Z 撤销：恢复为编辑前的原始数值
                if (this.IsKeyboardFocusWithin && ValueTextBox != null)
                {
                    ValueTextBox.IgnoreEmptyOnLostFocus = false;
                    ValueTextBox.Text = FormatNumber(_valueBeforeEdit);
                    _isUpdatingValue = true;
                    SetCurrentValue(ValueProperty, _valueBeforeEdit);
                    _isUpdatingValue = false;
                    _wasClearedByDelete = false;
                    _isEditing = false;
                    UpdateVisualState();
                    e.Handled = true; // 阻止全局 Undo 命令执行
                }
            }
            else if (e.Key == Key.Enter)
            {
                // If user pressed Enter after using Delete to clear, restore previous value here before child handlers run

                if (ValueTextBox != null && string.IsNullOrWhiteSpace(ValueTextBox.Text))
                {
                    if (_wasClearedByDelete)
                    {
                        // restore previous text and clear ignore flag
                        ValueTextBox.IgnoreEmptyOnLostFocus = false;
                        ValueTextBox.Text = FormatNumber(_lastValueBeforeDelete);
                        _wasClearedByDelete = false;
                        UpdateVisualState();
                        e.Handled = true; // prevent child NumericExpressionTextBox from performing calculation/committing 0
                        return;
                    }
                }
            }
        }

        #region 按钮事件

        private void UpButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsEnabled) return;
            IsCommandTriggered = true;
            var newValue = Value + Step;
            SetClampedValue(newValue, true);
        }

        private void DownButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsEnabled) return;
            IsCommandTriggered = true;
            var newValue = Value - Step;
            SetClampedValue(newValue, true);
        }

        #endregion

        #region 文本框事件

        private void ValueTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // 如果曾经按过 Delete/Backspace 清空，失去焦点时恢复并清除标志
            if (_wasClearedByDelete && string.IsNullOrWhiteSpace(ValueTextBox?.Text))
            {
                _wasClearedByDelete = false;
                // ensure child won't treat empty as 0 when it processes LostFocus
                ValueTextBox.IgnoreEmptyOnLostFocus = false;
                ValueTextBox.Text = FormatNumber(_lastValueBeforeDelete);
                UpdateVisualState();
                _isEditing = false;
                return;
            }

            // NumericExpressionTextBox 已在内部完成表达式计算和范围验证，此处无需再检查范围
            // 只需同步 Value 属性即可
            IsCommandTriggered = true;
            if (ValueTextBox.Value.HasValue)
            {
                double val = ValueTextBox.Value.Value;
                // 只在值变化时更新，无需再做范围检查（已在 NumericExpressionTextBox 中做过）
                if (!AreClose(val, Value))
                {
                    _isUpdatingValue = true;
                    SetCurrentValue(ValueProperty, val);
                    _isUpdatingValue = false;
                }
            }
            else
            {
                // 表达式无效，恢复当前有效值的文本
                ValueTextBox.Text = FormatNumber(_previousValidValue);
                ShowTemporaryToolTip("表达式无效");
            }
            UpdateVisualState();

            // 触发公开事件，供外部订阅
            ValueTextBoxLostFocus?.Invoke(sender, e);
        }

        private void ValueTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            // store current Value before editing begins
            _valueBeforeEdit = Value;
            _isEditing = true;
            UpdateVisualState();
        }

        private void ValueTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // 识别 Delete/Backspace 快捷键：清空文本并阻止继续传播
            if (e.Key == Key.Delete)
            {
                if (ValueTextBox != null)
                {
                    _wasClearedByDelete = true;
                    _lastValueBeforeDelete = Value;
                    // tell child textbox to ignore empty on lost focus so it won't commit 0
                    ValueTextBox.IgnoreEmptyOnLostFocus = true;
                    ValueTextBox.Text = string.Empty;
                    UpdateVisualState();
                    e.Handled = true; // 阻止继续传递
                    return;
                }
            }
            if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
            {
                // Ctrl+Z 撤销：恢复为编辑前的原始数值
                if (ValueTextBox != null)
                {
                    ValueTextBox.IgnoreEmptyOnLostFocus = false;
                    ValueTextBox.Text = FormatNumber(_valueBeforeEdit);
                    _isUpdatingValue = true;
                    SetCurrentValue(ValueProperty, _valueBeforeEdit);
                    _isUpdatingValue = false;
                    _wasClearedByDelete = false;
                    _isEditing = false;
                    UpdateVisualState();
                    e.Handled = true;
                    return;
                }
            }
            if (e.Key == Key.Enter)
            {
                
                // 如果之前按过 Delete/Backspace 并且当前文本为空，恢复原值
                if (_wasClearedByDelete && string.IsNullOrWhiteSpace(ValueTextBox.Text))
                {
                    if (_wasClearedByDelete) ValueTextBox.Text = string.Empty;
                    ValueTextBox.Text = FormatNumber(_lastValueBeforeDelete);
                    _wasClearedByDelete = false;
                    _isEditing = false;
                    UpdateVisualState();
                    e.Handled = true;
                    return;
                }

                IsCommandTriggered = true;

                double? parsed = null;
                if (ValueTextBox.Value.HasValue)
                {
                    parsed = ValueTextBox.Value.Value;
                }
                else if (!string.IsNullOrWhiteSpace(ValueTextBox.Text))
                {
                    if (double.TryParse(ValueTextBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
                    {
                        parsed = v;
                    }
                }

                if (parsed.HasValue)
                {
                    // if out of allowed range, restore original value before edit
                    if (parsed.Value < MinValue || parsed.Value > MaxValue)
                    {
                        ValueTextBox.Text = FormatNumber(_valueBeforeEdit);
                        _wasClearedByDelete = false;
                        _isEditing = false;
                        UpdateVisualState();
                        e.Handled = true;
                        return;
                    }

                    var clamped = Clamp(parsed.Value);
                    if (!AreClose(clamped, parsed.Value))
                    {
                        // Clamping occurred, show warning and set clamped value
                        ValueTextBox.Text = FormatNumber(clamped);
                        SetCurrentValue(ValueProperty, clamped);
                        ShowTemporaryToolTip(BuildOutOfRangeToolTip());
                    }
                    else if (!AreClose(parsed.Value, Value))
                    {
                        // Valid value, update it
                        _isUpdatingValue = true;
                        SetCurrentValue(ValueProperty, parsed.Value);
                        _isUpdatingValue = false;
                    }
                    _wasClearedByDelete = false;
                }
                else
                {
                    // 表达式无效，恢复当前有效值的文本
                    ValueTextBox.Text = FormatNumber(_previousValidValue);
                    ShowTemporaryToolTip("表达式无效");
                }
                UpdateVisualState();

                e.Handled = true;
            }
        }

        // Clear the 'cleared by delete' flag when user types new content so new value can be committed
        private void ValueTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                if (_wasClearedByDelete)
                {
                    var txt = ValueTextBox?.Text;
                    if (!string.IsNullOrWhiteSpace(txt))
                    {
                        _wasClearedByDelete = false;
                        // user typed content again; allow normal lost-focus behavior
                        ValueTextBox.IgnoreEmptyOnLostFocus = false;
                    }
                }
            }
            catch { }
            UpdateVisualState();
        }

        #endregion

        #region 数值处理

        private void SetClampedValue(double value, bool normalizeText)
        {
            var clampedValue = Clamp(value);
            if (_isUpdatingValue) return;

            _isUpdatingValue = true;
            SetCurrentValue(ValueProperty, clampedValue);
            _isUpdatingValue = false;

            if (normalizeText && ValueTextBox != null)
            {
                ValueTextBox.Text = FormatNumber(clampedValue);
            }
        }

        private void CoerceCurrentValueIntoRange()
        {
            if (MinValue > MaxValue)
            {
                SetCurrentValue(MaxValueProperty, MinValue);
                return;
            }

            double val = Value;
            if (val < MinValue || val > MaxValue)
            {
                // 范围变更时，恢复到上次有效值
                SetCurrentValue(ValueProperty, _previousValidValue);
                UpdateTextFromValue(_previousValidValue);
            }
            else
            {
                UpdateTextFromValue(val);
            }
        }

        private void UpdateTextFromValue(double value)
        {
            if (ValueTextBox != null)
            {
                ValueTextBox.Text = FormatNumber(value);
            }
        }

        private string FormatNumber(double value)
        {
            var format = DecimalPlaces > 0 ? $"F{DecimalPlaces}" : "F0";
            return value.ToString(format, CultureInfo.InvariantCulture);
        }

        private double Clamp(double value)
        {
            var min = MinValue;
            var max = MaxValue;
            if (min > max) max = min;
            return Math.Clamp(value, min, max);
        }

        private static bool AreClose(double left, double right)
        {
            return Math.Abs(left - right) < 0.0000001;
        }

        #endregion

        #region ToolTip 提示

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

            _toolTipTimer = new DispatcherTimer();
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
            bool hasMin = MinValue != double.MinValue && !double.IsNegativeInfinity(MinValue);
            bool hasMax = MaxValue != double.MaxValue && !double.IsPositiveInfinity(MaxValue);
            if (hasMin && hasMax)
                return $"输入值超出有效范围";
            if (hasMin)
                return $"输入值不能小于 {FormatNumber(MinValue)}";
            if (hasMax)
                return $"输入值不能大于 {FormatNumber(MaxValue)}";
            return "输入值超出有效范围";
        }

        #endregion

        #region 视觉状态

        private void UpdateVisualState()
        {
            if (!IsEnabled)
            {
                RootBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E2E2"));
                RootBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D9D9D9"));
                ValueTextBox.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#999999"));
                IsCommandTriggered = false;
            }
            else if (ValueTextBox.IsFocused || UpButton.IsFocused || DownButton.IsFocused)
            {
                RootBorder.Background = Brushes.White;
                RootBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A5EB4"));
                ValueTextBox.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#333333"));
            }
            else if (ValueTextBox.IsMouseOver || UpButton.IsMouseOver || DownButton.IsMouseOver)
            {
                RootBorder.Background = Brushes.White;
                RootBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#858E90"));
                ValueTextBox.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#333333"));
            }
            else
            {
                RootBorder.Background = Brushes.White;
                RootBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D9D9D9"));
                ValueTextBox.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#333333"));
            }
        }

        #endregion
    }
}
