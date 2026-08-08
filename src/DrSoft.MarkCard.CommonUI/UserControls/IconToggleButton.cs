using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;

namespace DrSoft.MarkCard.CommonUI.UserControls
{
    public class IconToggleButton : ToggleButton
    {
        public static readonly DependencyProperty IconProperty =
          DependencyProperty.Register(
              nameof(Icon),
              typeof(string),
              typeof(IconToggleButton),
              new PropertyMetadata(null));

        public string Icon
        {
            get => (string)GetValue(IconProperty);
            set => SetValue(IconProperty, value);
        }

        public static readonly DependencyProperty IconActiveProperty =
          DependencyProperty.Register(
              nameof(IconActive),
              typeof(string),
              typeof(IconToggleButton),
              new PropertyMetadata(null));

        public string IconActive
        {
            get => (string)GetValue(IconActiveProperty);
            set => SetValue(IconActiveProperty, value);
        }


    }
}
