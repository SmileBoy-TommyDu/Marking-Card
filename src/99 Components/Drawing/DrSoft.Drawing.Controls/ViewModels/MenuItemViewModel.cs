using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace DrSoft.Drawing.Controls.ViewModels
{
    public class MenuItemViewModel
    {
        public string Header { get; set; }
        public string InputGestureText { get; set; }
        public ICommand Command { get; set; }
        public string CommandParameter { get; set; }
        public ObservableCollection<MenuItemViewModel> Children { get; set; }
        public bool IsSeparator { get; set; }

        public MenuItemViewModel()
        {
            Children = new ObservableCollection<MenuItemViewModel>();
        }
    }

    public class MenuItemTemplateSelector : DataTemplateSelector
    {
        public DataTemplate NormalTemplate { get; set; }
        public DataTemplate SeparatorTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (item is MenuItemViewModel vm && vm.IsSeparator)
                return SeparatorTemplate;
            return NormalTemplate;
        }
    }

    public class BoolToVisibilityCollapsedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isSeparator && isSeparator)
                return Visibility.Collapsed;
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
