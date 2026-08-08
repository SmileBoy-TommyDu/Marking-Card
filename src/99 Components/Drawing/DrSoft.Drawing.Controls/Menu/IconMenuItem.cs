using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace DrSoft.Drawing.Controls.Menu
{
    public class IconMenuItem : MenuItem
    {
        static IconMenuItem()
        {
            // 这一步非常重要：告诉 WPF 这个控件的默认样式在 Generic.xaml 中
            DefaultStyleKeyProperty.OverrideMetadata(typeof(IconMenuItem),
                new FrameworkPropertyMetadata(typeof(IconMenuItem)));
        }

        public static readonly DependencyProperty IconProperty =
            DependencyProperty.Register("Icon", typeof(object), typeof(IconMenuItem));

        public object Icon
        {
            get => GetValue(IconProperty);
            set => SetValue(IconProperty, value);
        }
    }
}
