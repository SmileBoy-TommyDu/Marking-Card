using DrSoft.MarkCard.UI.Models;
using DrSoft.MarkCard.UI.ViewModes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace DrSoft.MarkCard.UI.Views
{
    /// <summary>
    /// ToolbarView.xaml 的交互逻辑
    /// </summary>
    public partial class ToolbarView : UserControl
    {
        public ToolbarView()
        {
            InitializeComponent();
            DataContextChanged += (_, _) => SubscribeViewModel();
            Loaded += (_, _) => { SubscribeViewModel(); SizeChanged += (_, _) => UpdateSeparators(); };
        }

        private ToolbarViewModel? Vm => DataContext as ToolbarViewModel;

        // ── 订阅 VisibleGroups 变化 ───────────────────────────────────────
        private void SubscribeViewModel()
        {
            if (Vm == null) return;
            Vm.VisibleGroups.CollectionChanged += (_, _) =>
                Dispatcher.InvokeAsync(UpdateSeparators);
        }

        // ── 每个 GroupControl 加载/布局后重新计算分隔线 ───────────────────
        private void GroupControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is ToolbarGroupView ctrl)
                ctrl.SizeChanged += (_, _) => Dispatcher.InvokeAsync(UpdateSeparators);

            Dispatcher.InvokeAsync(UpdateSeparators);
        }

        // ── 核心：判断哪些组是"该行最右侧"→ 隐藏其分隔线 ─────────────────
        // WrapPanel 换行后，每行最后一个组的右边缘 ≈ 容器可用宽度
        // 用 TransformToAncestor 把每个组的右边缘换算到 GroupsPanel 坐标系
        private void UpdateSeparators()
        {
            var controls = GetGroupControls().ToList();
            if (controls.Count == 0) return;

            // 容器可用宽度
            double panelWidth = GroupsPanel.ActualWidth;
            if (panelWidth <= 0) return;

            foreach (var ctrl in controls)
            {
                try
                {
                    // 把控件右边缘换算到 GroupsPanel 坐标
                    var transform = ctrl.TransformToAncestor(GroupsPanel);
                    var rightEdge = transform.Transform(new Point(ctrl.ActualWidth, 0)).X;

                    // 允许 6px 误差（Margin、像素对齐等）
                    bool isRowEnd = panelWidth - rightEdge < 6.0;
                    ctrl.IsLast = isRowEnd;
                }
                catch
                {
                    // TransformToAncestor 在布局未完成时可能抛异常，忽略
                }
            }
        }

        // ── 遍历可视树获取所有 GroupControl ──────────────────────────────
        private IEnumerable<ToolbarGroupView> GetGroupControls()
        {
            for (int i = 0; i < GroupsPanel.Items.Count; i++)
            {
                var container = GroupsPanel.ItemContainerGenerator
                                    .ContainerFromIndex(i) as FrameworkElement;
                if (container == null) continue;
                var ctrl = FindChild<ToolbarGroupView>(container);
                if (ctrl != null) yield return ctrl;
            }
        }

        // ── ⋮ 按钮：动态生成含 CheckBox 的菜单 ───────────────────────────
        private void MoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (Vm == null) return;

            var menu = new ContextMenu { Style = BuildMenuStyle() };

            menu.Items.Add(new MenuItem
            {
                Header = "工具栏分组",
                IsEnabled = false,
                Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x5A)),
            });
            menu.Items.Add(new Separator());

            foreach (var group in Vm.Groups)
            {
                var item = new MenuItem
                {
                    Header = group.Title,
                    IsCheckable = true,
                    IsChecked = group.IsVisible,
                    Tag = group,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xD4)),
                };
                item.Checked += (_, _) => { if (item.Tag is ToolbarGroup g) g.IsVisible = true; };
                item.Unchecked += (_, _) => { if (item.Tag is ToolbarGroup g) g.IsVisible = false; };
                menu.Items.Add(item);
            }

            menu.Items.Add(new Separator());
            var showAll = new MenuItem
            {
                Header = "全部显示",
                Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xD4)),
            };
            showAll.Click += (_, _) => { foreach (var g in Vm.Groups) g.IsVisible = true; };
            menu.Items.Add(showAll);

            menu.PlacementTarget = MoreButton;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private static Style BuildMenuStyle()
        {
            var style = new Style(typeof(ContextMenu));
            style.Setters.Add(new Setter(BackgroundProperty,
                new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x1E))));
            style.Setters.Add(new Setter(Control.BorderBrushProperty,
                new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x3C))));
            return style;
        }

        private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var found = FindChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }
    }
}
