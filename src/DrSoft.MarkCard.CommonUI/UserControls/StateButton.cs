using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace DrSoft.MarkCard.CommonUI.UserControls
{
    /// <summary>
    /// 支持 Enable/Disable 状态显示不同图标的自定义按钮控件
    /// </summary>
    [TemplatePart(Name = ElementRoot, Type = typeof(Border))]
    [TemplatePart(Name = ElementInRootX, Type = typeof(Border))]
    [TemplatePart(Name = ElementInRootY, Type = typeof(Border))]
    [TemplatePart(Name = ElementNormalIcon, Type = typeof(Image))]
    [TemplatePart(Name = ElementDisabledIcon, Type = typeof(Image))]
    public class StateButton : Button
    {
        private const string ElementRoot = "PART_Root";
        private const string ElementInRootX = "InnerShadowBorderX";
        private const string ElementInRootY = "InnerShadowBorderY";
        private const string ElementNormalIcon = "PART_NormalIcon";
        private const string ElementDisabledIcon = "PART_DisabledIcon";

        private Border _rootBorder;
        private Border _inRootBorderX;
        private Border _inRootBorderY;
        private Image _normalIcon;
        private Image _disabledIcon;

        #region 依赖属性定义

        /// <summary>
        /// 正常状态图标
        /// </summary>
        public static readonly DependencyProperty NormalIconProperty =
            DependencyProperty.Register(
                nameof(NormalIcon),
                typeof(ImageSource),
                typeof(StateButton),
                new PropertyMetadata(null, OnIconPropertyChanged));

        /// <summary>
        /// 禁用状态图标
        /// </summary>
        public static readonly DependencyProperty DisabledIconProperty =
            DependencyProperty.Register(
                nameof(DisabledIcon),
                typeof(ImageSource),
                typeof(StateButton),
                new PropertyMetadata(null, OnIconPropertyChanged));

        /// <summary>
        /// 悬停状态图标（可选）
        /// </summary>
        public static readonly DependencyProperty HoverIconProperty =
            DependencyProperty.Register(
                nameof(HoverIcon),
                typeof(ImageSource),
                typeof(StateButton),
                new PropertyMetadata(null, OnIconPropertyChanged));

        /// <summary>
        /// 按下状态图标（可选）
        /// </summary>
        public static readonly DependencyProperty PressedIconProperty =
            DependencyProperty.Register(
                nameof(PressedIcon),
                typeof(ImageSource),
                typeof(StateButton),
                new PropertyMetadata(null, OnIconPropertyChanged));

        /// <summary>
        /// 图标宽度
        /// </summary>
        public static readonly DependencyProperty IconWidthProperty =
            DependencyProperty.Register(
                nameof(IconWidth),
                typeof(double),
                typeof(StateButton),
                new PropertyMetadata(16.0));

        /// <summary>
        /// 图标高度
        /// </summary>
        public static readonly DependencyProperty IconHeightProperty =
            DependencyProperty.Register(
                nameof(IconHeight),
                typeof(double),
                typeof(StateButton),
                new PropertyMetadata(16.0));

        /// <summary>
        /// 按钮圆角半径
        /// </summary>
        public static readonly DependencyProperty CornerRadiusProperty =
            DependencyProperty.Register(
                nameof(CornerRadius),
                typeof(CornerRadius),
                typeof(StateButton),
                new PropertyMetadata(new CornerRadius(2)));

        /// <summary>
        /// 悬停时背景色
        /// </summary>
        public static readonly DependencyProperty HoverBackgroundProperty =
            DependencyProperty.Register(
                nameof(HoverBackground),
                typeof(Brush),
                typeof(StateButton),
                new PropertyMetadata(null));

        /// <summary>
        /// 按下时背景色
        /// </summary>
        public static readonly DependencyProperty PressedBackgroundProperty =
            DependencyProperty.Register(
                nameof(PressedBackground),
                typeof(Brush),
                typeof(StateButton),
                new PropertyMetadata(null));

        /// <summary>
        /// 是否选中（持久状态）
        /// </summary>
        public static readonly DependencyProperty IsCheckedProperty =
            DependencyProperty.Register(
                nameof(IsChecked),
                typeof(bool),
                typeof(StateButton),
                new PropertyMetadata(false, OnIconPropertyChanged));

        public bool IsChecked
        {
            get => (bool)GetValue(IsCheckedProperty);
            set => SetValue(IsCheckedProperty, value);
        }

        #endregion

        #region .NET 属性包装

        public ImageSource NormalIcon
        {
            get => (ImageSource)GetValue(NormalIconProperty);
            set => SetValue(NormalIconProperty, value);
        }

        public ImageSource DisabledIcon
        {
            get => (ImageSource)GetValue(DisabledIconProperty);
            set => SetValue(DisabledIconProperty, value);
        }

        public ImageSource HoverIcon
        {
            get => (ImageSource)GetValue(HoverIconProperty);
            set => SetValue(HoverIconProperty, value);
        }

        public ImageSource PressedIcon
        {
            get => (ImageSource)GetValue(PressedIconProperty);
            set => SetValue(PressedIconProperty, value);
        }

        public double IconWidth
        {
            get => (double)GetValue(IconWidthProperty);
            set => SetValue(IconWidthProperty, value);
        }

        public double IconHeight
        {
            get => (double)GetValue(IconHeightProperty);
            set => SetValue(IconHeightProperty, value);
        }

        public CornerRadius CornerRadius
        {
            get => (CornerRadius)GetValue(CornerRadiusProperty);
            set => SetValue(CornerRadiusProperty, value);
        }

        public Brush HoverBackground
        {
            get => (Brush)GetValue(HoverBackgroundProperty);
            set => SetValue(HoverBackgroundProperty, value);
        }

        public Brush PressedBackground
        {
            get => (Brush)GetValue(PressedBackgroundProperty);
            set => SetValue(PressedBackgroundProperty, value);
        }

        #endregion

        #region 构造函数

        static StateButton()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(StateButton),
                new FrameworkPropertyMetadata(typeof(StateButton)));
        }

        public StateButton()
        {
            Width = 32;
            Height = 32;
            IconWidth = 32;
            IconHeight = 32;
            // 设置默认的背景色
            SetValue(HoverBackgroundProperty, new SolidColorBrush(Color.FromRgb(232, 240, 254)));
            SetValue(PressedBackgroundProperty, new SolidColorBrush(Color.FromRgb(208, 220, 245)));
        }

        #endregion

        #region 模板应用和状态更新

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            // 获取模板中的控件引用
            _rootBorder = GetTemplateChild(ElementRoot) as Border;
            _inRootBorderX = GetTemplateChild(ElementInRootX) as Border;
            _inRootBorderY = GetTemplateChild(ElementInRootY) as Border;
            _normalIcon = GetTemplateChild(ElementNormalIcon) as Image;
            _disabledIcon = GetTemplateChild(ElementDisabledIcon) as Image;

            _rootBorder.Background = Brushes.Transparent;
            _inRootBorderX.BorderBrush = Brushes.Transparent;

            // 更新视觉状态
            UpdateVisualState();
        }

        /// <summary>
        /// 属性变化回调
        /// </summary>
        private static void OnIconPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var button = d as StateButton;
            button?.UpdateVisualState();
        }

        /// <summary>
        /// 当 IsEnabled 属性变化时更新状态
        /// </summary>
        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);

            if (e.Property == IsEnabledProperty)
            {
                UpdateVisualState();
            }
        }

        /// <summary>
        /// 鼠标进入时更新
        /// </summary>
        protected override void OnMouseEnter(System.Windows.Input.MouseEventArgs e)
        {
            base.OnMouseEnter(e);
            UpdateVisualState();
        }

        /// <summary>
        /// 鼠标离开时更新
        /// </summary>
        protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            UpdateVisualState();
        }

        /// <summary>
        /// 鼠标按下时更新
        /// </summary>
        protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            UpdateVisualState();
        }

        /// <summary>
        /// 鼠标释放时更新
        /// </summary>
        protected override void OnMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            UpdateVisualState();
        }

        /// <summary>
        /// 更新视觉状态（图标和背景）
        /// </summary>
        private void UpdateVisualState()
        {
            if (_normalIcon == null || _disabledIcon == null) return;

            // 根据 IsEnabled 状态显示不同的图标
            if (!IsEnabled)
            {
                // 禁用状态
                _normalIcon.Visibility = Visibility.Collapsed;
                _disabledIcon.Visibility = Visibility.Visible;

                if (_rootBorder != null)
                {
                    _rootBorder.Background = Brushes.Transparent;
                }
                return;
            }

            // 启用状态：根据鼠标状态显示不同图标
            bool isMouseOver = IsMouseOver;
            bool isPressed = IsPressed;

            if (isPressed && PressedIcon != null)
            {
                // 按下状态显示按下图标
                _normalIcon.Source = PressedIcon;
                _disabledIcon.Visibility = Visibility.Collapsed;
                _normalIcon.Visibility = Visibility.Visible;
            }
            else if (isMouseOver && HoverIcon != null)
            {
                // 悬停状态显示悬停图标
                _normalIcon.Source = HoverIcon;
                _disabledIcon.Visibility = Visibility.Collapsed;
                _normalIcon.Visibility = Visibility.Visible;
            }
            else
            {
                // 正常状态显示正常图标
                _normalIcon.Source = NormalIcon;
                _disabledIcon.Visibility = Visibility.Collapsed;
                _normalIcon.Visibility = Visibility.Visible;
            }

            // 更新背景色
            //if (_rootBorder != null)
            //{
            //    if (isPressed && PressedBackground != null)
            //    {
            //        _rootBorder.Background = PressedBackground;
            //    }
            //    else if (isMouseOver && HoverBackground != null)
            //    {
            //        _rootBorder.Background = HoverBackground;
            //    }
            //    else
            //    {
            //        _rootBorder.Background = Brushes.Transparent;
            //    }
            //}

            ////是否选中
            //if (_inRootBorderX != null && _inRootBorderY != null)
            //{
            //    if (isMouseOver)
            //    {
            //        _inRootBorderX.BorderBrush = Brushes.Red;
            //        _inRootBorderY.BorderBrush = Brushes.Red;
            //    }
            //    else if (IsChecked)
            //    {
            //        _inRootBorderX.BorderBrush = new SolidColorBrush(Color.FromRgb(95, 151, 207));
            //        _inRootBorderY.BorderBrush = new SolidColorBrush(Color.FromRgb(95, 151, 207));
            //    }
            //    else
            //    {
            //        _inRootBorderX.BorderBrush = Brushes.Transparent;
            //        _inRootBorderY.BorderBrush = Brushes.Transparent;
            //    }
            //}
        }

        #endregion
    }
}
