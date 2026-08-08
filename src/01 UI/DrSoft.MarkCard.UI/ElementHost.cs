using DrSoft.Docking.Interface;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DrSoft.MarkCard.UI
{
    // 包装任意 FrameworkElement 为 IDockSource
    public class ElementHost : ContentControl, IDockSource
    {
        public ElementHost(
            string header,
            FrameworkElement? element,
            double outerMinWidth = 0,
            double outerMinHeight = 0)
        {
            Header = header;
            OuterMinWidth = outerMinWidth;
            OuterMinHeight = outerMinHeight;
            if (element != null)
            {
                DetachFromParent(element);
                Content = element;
            }
        }

        public IDockControl DockControl { get; set; }
        public string Header { get; }
        public ImageSource Icon => null;
        public double OuterMinWidth { get; }
        public double OuterMinHeight { get; }

        private static void DetachFromParent(FrameworkElement element)
        {
            if (element == null) return;

            try
            {
                var logicalParent = LogicalTreeHelper.GetParent(element);
                if (logicalParent is Panel lp)
                {
                    if (lp.Children.Contains(element)) lp.Children.Remove(element);
                    return;
                }
                if (logicalParent is ContentControl lcc)
                {
                    if (lcc.Content == element) lcc.Content = null;
                    return;
                }
                if (logicalParent is Decorator ld)
                {
                    if (ld.Child == element) ld.Child = null;
                    return;
                }
                if (logicalParent is ItemsControl lic)
                {
                    if (lic.Items.Contains(element)) lic.Items.Remove(element);
                    return;
                }
                if (logicalParent is Border lbd)
                {
                    if (lbd.Child == element) lbd.Child = null;
                    return;
                }

                var visualParent = VisualTreeHelper.GetParent(element);
                if (visualParent is Panel vp)
                {
                    if (vp.Children.Contains(element)) vp.Children.Remove(element);
                }
                else if (visualParent is ContentControl vcc)
                {
                    if (vcc.Content == element) vcc.Content = null;
                }
                else if (visualParent is Decorator vdec)
                {
                    if (vdec.Child == element) vdec.Child = null;
                }
                else if (visualParent is ItemsControl vic)
                {
                    if (vic.Items.Contains(element)) vic.Items.Remove(element);
                }
                else if (visualParent is Border vbd)
                {
                    if (vbd.Child == element) vbd.Child = null;
                }
            }
            catch
            {
                // 断开父失败则忽略（调用方会收到更明确的异常）
            }
        }
    }
}
