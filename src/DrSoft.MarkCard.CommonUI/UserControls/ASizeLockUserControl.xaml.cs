using CommunityToolkit.Mvvm.Input;
using DrSoft.MarkCard.CommonUI.UserControls;
using System;
using System.Collections.Generic;
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
    /// <summary>
    /// ASizeLockUserControl.xaml 的交互逻辑
    /// </summary>
    public partial class ASizeLockUserControl : UserControl
    {

        private double? _originalAspectRatio = null;
        private bool _isSyncing = false;         // 防止 W/H 依赖属性回调相互递归

        public ASizeLockUserControl()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                // 初始化时记录比例（如果已有值）
                if (WidthValue > 0 && HeightValue > 0)
                    _originalAspectRatio = WidthValue / HeightValue;

                // 在 Loaded 时尝试附加内部 TextBox 的 TextChanged（防止模板未应用时为 null）
                TryAttachInnerTextChangedHandlers();
            };

            Unloaded += (s, e) => TryDetachInnerTextChangedHandlers();

            // 注意：不要在构造函数里把 CommittedCommand 赋给 wText/hText，
            // 此时外部 Binding 尚未建立，读到的是 null；已改由 XAML 用
            // ElementName=root 直接绑定，宿主属性变化会自动同步到内部 TextBox。
        }

        private void TryAttachInnerTextChangedHandlers()
        {
            try
            {
                if (wText != null && wText.ValueTextBox != null)
                {
                    wText.ValueTextBox.Tag = wText.Tag;
                    wText.ValueTextBox.TextChanged -= Text_TextChanged;
                    wText.ValueTextBox.TextChanged += Text_TextChanged;
                }

                if (hText != null && hText.ValueTextBox != null)
                {
                    hText.ValueTextBox.Tag = hText.Tag;
                    hText.ValueTextBox.TextChanged -= Text_TextChanged;
                    hText.ValueTextBox.TextChanged += Text_TextChanged;
                }
            }
            catch { }
        }

        private void TryDetachInnerTextChangedHandlers()
        {
            try
            {
                if (wText != null && wText.ValueTextBox != null)
                    wText.ValueTextBox.TextChanged -= Text_TextChanged;
                if (hText != null && hText.ValueTextBox != null)
                    hText.ValueTextBox.TextChanged -= Text_TextChanged;
            }
            catch { }
        }

        // 依赖属性：标题
        private void Text_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsLocked) return;
            if (_isSyncing) return; // 防止重入

            var tb = (NumberDataExpressionTextBox)sender;

            // 确保已记录原始比例
            if (_originalAspectRatio == null && WidthValue > 0 && HeightValue > 0)
                _originalAspectRatio = WidthValue / HeightValue;

            double aspect = _originalAspectRatio ?? 1.0;
            if (aspect == 0) aspect = 1.0;

            try
            {
                _isSyncing = true;

                if (tb.Tag?.ToString() == "Width")
                {
                    double newWidthValue = 0;

                    if (double.TryParse(tb.Text, out newWidthValue))
                    {
                        // 新宽度确定后，更新高度
                        double newHeight = newWidthValue / aspect;
                        if (newHeight > 0 && Math.Abs(HeightValue - newHeight) > 1e-6)
                            HeightValue = newHeight;
                    }


                }
                else if (tb.Tag?.ToString() == "Height")
                {
                    double newHeightValue = 0;
                    if (double.TryParse(tb.Text, out newHeightValue))
                    {
                        double newWidth = newHeightValue * aspect;
                        if (newWidth > 0 && Math.Abs(WidthValue - newWidth) > 1e-6)
                            WidthValue = newWidth;
                    }
                }
            }
            finally
            {
                _isSyncing = false;
            }
        }

        // 依赖属性：标题
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(
                nameof(Title),
                typeof(string),
                typeof(ASizeLockUserControl),
                new PropertyMetadata("格点大小设置(mm)"));

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        // 依赖属性：单位
        public static readonly DependencyProperty UnitProperty =
            DependencyProperty.Register(
                nameof(Unit),
                typeof(string),
                typeof(ASizeLockUserControl),
                new PropertyMetadata("(mm)"));

        public string Unit
        {
            get => (string)GetValue(UnitProperty);
            set => SetValue(UnitProperty, value);
        }

        // 依赖属性：宽度标签
        public static readonly DependencyProperty WidthLabelProperty =
            DependencyProperty.Register(
                nameof(WidthLabel),
                typeof(string),
                typeof(ASizeLockUserControl),
                new PropertyMetadata("W"));

        public string WidthLabel
        {
            get => (string)GetValue(WidthLabelProperty);
            set => SetValue(WidthLabelProperty, value);
        }

        // 依赖属性：高度标签
        public static readonly DependencyProperty HeightLabelProperty =
            DependencyProperty.Register(
                nameof(HeightLabel),
                typeof(string),
                typeof(ASizeLockUserControl),
                new PropertyMetadata("H"));

        public string HeightLabel
        {
            get => (string)GetValue(HeightLabelProperty);
            set => SetValue(HeightLabelProperty, value);
        }

        // 依赖属性：宽度值（双向绑定，UpdateSourceTrigger=LostFocus）
        public static readonly DependencyProperty WidthValueProperty =
            DependencyProperty.Register(
                nameof(WidthValue),
                typeof(double),
                typeof(ASizeLockUserControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, null));



        public double WidthValue
        {
            get => (double)GetValue(WidthValueProperty);
            set => SetValue(WidthValueProperty, value);
        }

        // 依赖属性：高度值（双向绑定，UpdateSourceTrigger=LostFocus）
        public static readonly DependencyProperty HeightValueProperty =
            DependencyProperty.Register(
                nameof(HeightValue),
                typeof(double),
                typeof(ASizeLockUserControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, null));



        public double HeightValue
        {
            get => (double)GetValue(HeightValueProperty);
            set => SetValue(HeightValueProperty, value);
        }



        // 依赖属性：锁定状态（默认 False）
        public static readonly DependencyProperty IsLockedProperty =
            DependencyProperty.Register(
                nameof(IsLocked),
                typeof(bool),
                typeof(ASizeLockUserControl),
                new FrameworkPropertyMetadata(false, OnIsLockedChanged));

        public bool IsLocked
        {
            get => (bool)GetValue(IsLockedProperty);
            set => SetValue(IsLockedProperty, value);
        }
        // 允许上层 ViewModel 在特定选区约束下禁止用户切换锁定状态。
        public static readonly DependencyProperty IsLockToggleEnabledProperty =
            DependencyProperty.Register(
                nameof(IsLockToggleEnabled),
                typeof(bool),
                typeof(ASizeLockUserControl),
                new PropertyMetadata(true));

        public bool IsLockToggleEnabled
        {
            get => (bool)GetValue(IsLockToggleEnabledProperty);
            set => SetValue(IsLockToggleEnabledProperty, value);
        }

        // 只读属性：长宽比（避免除以0）
        public double AspectRatio
        {
            get
            {
                if (HeightValue == 0) return 1.0; // 避免除以0
                return WidthValue / HeightValue;
            }
        }

        // 依赖属性：宽度提交命令
        public static readonly DependencyProperty CommittedCommandProperty =
            DependencyProperty.Register(nameof(CommittedCommand), typeof(ICommand), typeof(ASizeLockUserControl));

        public ICommand CommittedCommand
        {
            get => (ICommand)GetValue(CommittedCommandProperty);
            set => SetValue(CommittedCommandProperty, value);
        }

        // 使用提交命令处理同步（与 SizeLockUserControl 保持一致）
        private void OnWidthCommitted(double value)
        {
            // 先记录原始比例（如尚未记录）
            if (_originalAspectRatio == null && WidthValue > 0 && HeightValue > 0)
                _originalAspectRatio = WidthValue / HeightValue;

            if (IsLocked)
            {
                double aspect = _originalAspectRatio ?? 1.0;
                if (aspect == 0) aspect = 1.0;

                double newHeight = value / aspect;
                if (newHeight > 0 && Math.Abs(HeightValue - newHeight) > 1e-6)
                    HeightValue = newHeight;
            }

            CommittedCommand?.Execute(new SizeLockEventArg() { WidthValue = value, HeightValue = HeightValue });
        }

        private void OnHeightCommitted(double value)
        {
            // 先记录原始比例（如尚未记录）
            if (_originalAspectRatio == null && WidthValue > 0 && HeightValue > 0)
                _originalAspectRatio = WidthValue / HeightValue;

            if (IsLocked)
            {
                double aspect = _originalAspectRatio ?? 1.0;
                if (aspect == 0) aspect = 1.0;

                double newWidth = value * aspect;
                if (newWidth > 0 && Math.Abs(WidthValue - newWidth) > 1e-6)
                    WidthValue = newWidth;
            }

            CommittedCommand?.Execute(new SizeLockEventArg() { WidthValue = WidthValue, HeightValue = value });
        }

        // 包装命令：在提交时进行同步并转发给外部 CommittedCommand
        public ICommand WidthCommittedCommand => new RelayCommand<double>(value => OnWidthCommitted(value));

        public ICommand HeightCommittedCommand => new RelayCommand<double>(value => OnHeightCommitted(value));


        private static void OnIsLockedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (ASizeLockUserControl)d;
            if (control.IsLocked && control.WidthValue > 0 && control.HeightValue > 0)
            {
                control._originalAspectRatio = control.WidthValue / control.HeightValue;
            }
            else if (!control.IsLocked)
            {
                // 可选：解锁时清空比例记录，下次锁定时重新记录
                control._originalAspectRatio = null;
            }
        }



        // 锁按钮点击事件（可选：如需额外逻辑）
        private void LockButton_Click(object sender, RoutedEventArgs e)
        {
            // 模板中的触发器已处理图标切换，此处可添加其他逻辑（如日志、通知）
        }

    }
}
