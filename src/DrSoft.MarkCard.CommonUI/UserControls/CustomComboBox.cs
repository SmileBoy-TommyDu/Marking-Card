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
    public class CustomComboBox : ComboBox
    {
        static CustomComboBox()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(CustomComboBox),
                new FrameworkPropertyMetadata(typeof(CustomComboBox)));
        }

        protected override void OnMouseEnter(MouseEventArgs e)
        {
            base.OnMouseEnter(e);
            if (IsEnabled)
            {
                VisualStateManager.GoToState(this, "MouseOver", true);
            }
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            if (IsEnabled)
            {
                VisualStateManager.GoToState(this, "Normal", true);
            }
        }

        protected override void OnDropDownOpened(EventArgs e)
        {
            base.OnDropDownOpened(e);
            if (IsEnabled)
            {
                VisualStateManager.GoToState(this, "Pressed", true);
            }
        }

        protected override void OnDropDownClosed(EventArgs e)
        {
            base.OnDropDownClosed(e);
            if (IsEnabled)
            {
                if (IsMouseOver)
                    VisualStateManager.GoToState(this, "MouseOver", true);
                else
                    VisualStateManager.GoToState(this, "Normal", true);
            }
        }
    }
}
