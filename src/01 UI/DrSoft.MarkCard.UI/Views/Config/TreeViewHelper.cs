using System.Windows;
using System.Windows.Controls;

namespace DrSoft.MarkCard.UI.Views.Config
{
    /// <summary>
    /// TreeView 附加行为：当子节点被选中时，自动高亮其父节点
    /// </summary>
    public static class TreeViewHelper
    {
        /// <summary>
        /// 附加到 TreeView 上启用父节点高亮
        /// </summary>
        public static readonly DependencyProperty EnableParentHighlightProperty =
            DependencyProperty.RegisterAttached(
                "EnableParentHighlight",
                typeof(bool),
                typeof(TreeViewHelper),
                new PropertyMetadata(false, OnEnableParentHighlightChanged));

        public static bool GetEnableParentHighlight(DependencyObject obj) =>
            (bool)obj.GetValue(EnableParentHighlightProperty);

        public static void SetEnableParentHighlight(DependencyObject obj, bool value) =>
            obj.SetValue(EnableParentHighlightProperty, value);

        /// <summary>
        /// 附加到 TreeViewItem 上，表示其子节点中有被选中的项
        /// </summary>
        public static readonly DependencyProperty HasSelectedChildProperty =
            DependencyProperty.RegisterAttached(
                "HasSelectedChild",
                typeof(bool),
                typeof(TreeViewHelper),
                new PropertyMetadata(false));

        public static bool GetHasSelectedChild(DependencyObject obj) =>
            (bool)obj.GetValue(HasSelectedChildProperty);

        public static void SetHasSelectedChild(DependencyObject obj, bool value) =>
            obj.SetValue(HasSelectedChildProperty, value);

        /// <summary>
        /// 等效选中状态：IsSelected=true 或 HasSelectedChild=true 时为 true
        /// 供 ExpandPath 图标绑定使用
        /// </summary>
        public static readonly DependencyProperty IsEffectivelySelectedProperty =
            DependencyProperty.RegisterAttached(
                "IsEffectivelySelected",
                typeof(bool),
                typeof(TreeViewHelper),
                new PropertyMetadata(false));

        public static bool GetIsEffectivelySelected(DependencyObject obj) =>
            (bool)obj.GetValue(IsEffectivelySelectedProperty);

        public static void SetIsEffectivelySelected(DependencyObject obj, bool value) =>
            obj.SetValue(IsEffectivelySelectedProperty, value);

        private static void OnEnableParentHighlightChanged(
            DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TreeView treeView)
            {
                if ((bool)e.NewValue)
                    treeView.SelectedItemChanged += OnSelectedItemChanged;
                else
                    treeView.SelectedItemChanged -= OnSelectedItemChanged;
            }
        }

        private static void OnSelectedItemChanged(
            object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (sender is not TreeView treeView) return;

            // 清除所有标记
            ClearAll(treeView);

            // 标记当前选中项
            if (e.NewValue is TreeViewItem selectedItem)
            {
                SetIsEffectivelySelected(selectedItem, true);

                // 从当前选中项向上遍历，标记所有祖先
                var parent = ItemsControl.ItemsControlFromItemContainer(selectedItem) as TreeViewItem;
                while (parent != null)
                {
                    SetHasSelectedChild(parent, true);
                    SetIsEffectivelySelected(parent, true);
                    parent = ItemsControl.ItemsControlFromItemContainer(parent) as TreeViewItem;
                }
            }
        }

        private static void ClearAll(ItemsControl itemsControl)
        {
            foreach (var item in itemsControl.Items)
            {
                if (item is TreeViewItem tvi)
                {
                    SetHasSelectedChild(tvi, false);
                    SetIsEffectivelySelected(tvi, false);
                    ClearAll(tvi);
                }
            }
        }
    }
}
