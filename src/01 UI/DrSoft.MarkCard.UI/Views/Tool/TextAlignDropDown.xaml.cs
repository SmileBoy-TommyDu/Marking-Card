using DrSoft.Drawing.Event;
using DrSoft.MarkCard.Event.Tool;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DrSoft.MarkCard.UI.Views.Tool
{
    /// <summary>
    /// TextAlignDropDown.xaml 的交互逻辑
    /// </summary>
    public partial class TextAlignDropDown : UserControl
    {
        private const string LeftIconPath = "/Resource/image/Fonts/align-left.png";
        private const string CenterIconPath = "/Resource/image/Fonts/align-center.png";
        private const string RightIconPath = "/Resource/image/Fonts/align-right.png";

        private readonly Dictionary<int, string> _iconPaths = new()
        {
            [0] = LeftIconPath,
            [1] = CenterIconPath,
            [2] = RightIconPath,
        };

        private readonly Dictionary<int, string> _tooltips = new()
        {
            [0] = "左对齐",
            [1] = "居中对齐",
            [2] = "右对齐",
        };

        public bool CommandTriggered { get; set; } = false;

        private int _textAlignment;
        public int TextAlignment
        {
            get => _textAlignment;
            set
            {
                var normalized = NormalizeAlignment(value);
                if (_textAlignment == normalized)
                {
                    UpdateVisualState();
                    return;
                }

                _textAlignment = normalized;
                UpdateVisualState();
            }
        }

        public TextAlignDropDown()
        {
            InitializeComponent();

            LeftAlignOption.Content = CreateIcon(LeftIconPath);
            CenterAlignOption.Content = CreateIcon(CenterIconPath);
            RightAlignOption.Content = CreateIcon(RightIconPath);
            TextAlignment = 0;

            DependencyPropertyDescriptor.FromProperty(Button.IsEnabledProperty, typeof(Button))
                .AddValueChanged(DropDownButton, OnDropDownButtonIsEnabledChanged);
        }

        private void OnDropDownButtonIsEnabledChanged(object? sender, EventArgs e)
        {
            UpdateVisualState();
        }

        /// <summary>
        /// 点击按钮：显示Popup浮层
        /// </summary>
        private void DropDownButton_Click(object sender, RoutedEventArgs e)
        {
            if (!DropDownButton.IsEnabled)
            {
                return;
            }

            AlignmentPopup.IsOpen = !AlignmentPopup.IsOpen;
            CommandTriggered = true;
        }

        private void AlignmentOption_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn)
            {
                return;
            }

            if (btn.Tag is not string tag || !int.TryParse(tag, out var alignment))
            {
                return;
            }

            TextAlignment = alignment;
            AlignmentPopup.IsOpen = false;

            EventBus.Instance.Publish(new ToolButtonClickedEvent
            {
                ToolTip = "对齐"
            });
        }

        private void UpdateVisualState()
        {
            DropDownButton.Content = CreateIcon(_iconPaths[TextAlignment], DropDownButton.IsEnabled ? 1.0 : 0.5);
            DropDownButton.ToolTip = _tooltips[TextAlignment];

            SetOptionBackground(LeftAlignOption, TextAlignment == 0);
            SetOptionBackground(CenterAlignOption, TextAlignment == 1);
            SetOptionBackground(RightAlignOption, TextAlignment == 2);
        }

        private static void SetOptionBackground(Button button, bool selected)
        {
            button.Background = selected
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DDEEFF"))
                : Brushes.Transparent;
        }

        private static Image CreateIcon(string path, double opacity = 1.0)
        {
            return new Image
            {
                Source = new BitmapImage(new Uri(path, UriKind.Relative)),
                Width = 32,
                Height = 32,
                Stretch = Stretch.Uniform,
                Opacity = opacity,
            };
        }

        private static int NormalizeAlignment(int value)
        {
            return value switch
            {
                1 => 1,
                2 => 2,
                _ => 0,
            };
        }
    }
}
