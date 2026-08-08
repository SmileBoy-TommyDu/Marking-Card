using DrSoft.MarkCard.CommonUI.UserControls;
using DrSoft.MarkCard.UI.UserControls;
using DrSoft.MarkCard.UI.ViewModes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DrSoft.MarkCard.UI.Views
{
    /// <summary>
    /// PositionView.xaml 的交互逻辑
    /// </summary>
    public partial class PositionView : UserControl
    {
        public PositionView()
        {
            InitializeComponent();
            DataContext = App.GetRequiredService<PositionViewModel>();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
           
        }


        /// <summary>
        /// 递归查找指定类型的视觉子元素
        /// </summary>
        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                    yield return typedChild;
                foreach (var descendant in FindVisualChildren<T>(child))
                    yield return descendant;
            }
        }

        /// <summary>
        /// 点击空白处时，清除所有 NumericExpressionTextBox 的焦点
        /// </summary>
        private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
   
            if (!IsChildOfNumericExpressionTextBox(e.OriginalSource as DependencyObject))
            {
                Keyboard.ClearFocus();
            }
        }

        /// <summary>
        /// 判断元素是否在 NumericExpressionTextBox 的视觉树内
        /// </summary>
        private static bool IsChildOfNumericExpressionTextBox(DependencyObject element)
        {
            while (element != null)
            {
                if (element is NumberDataExpressionTextBox)
                    return true;
                element = VisualTreeHelper.GetParent(element);
            }
            return false;
        }
    }
}
