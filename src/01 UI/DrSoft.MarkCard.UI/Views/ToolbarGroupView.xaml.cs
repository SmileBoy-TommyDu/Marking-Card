using DrSoft.MarkCard.UI.Models;
using DrSoft.MarkCard.UI.ViewModes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DrSoft.MarkCard.UI.Views
{
    /// <summary>
    /// ToolbarGroupView.xaml 的交互逻辑
    /// </summary>
    public partial class ToolbarGroupView : UserControl
    {
        public const string DragFormat = "ToolbarGroup";

        private Point _dragStartPoint;
        private bool _isDragStarted;

        public ToolbarGroupView()
        {
            InitializeComponent();
            AllowDrop = true;
            DragOver += OnDragOver;
            Drop += OnDrop;
            DragLeave += OnDragLeave;
        }

        // ── IsLast：隐藏/显示右侧分隔竖线 ────────────────────────────────
        public static readonly DependencyProperty IsLastProperty =
            DependencyProperty.Register(nameof(IsLast), typeof(bool),
                typeof(ToolbarGroupView),
                new PropertyMetadata(false, OnIsLastChanged));

        public bool IsLast
        {
            get => (bool)GetValue(IsLastProperty);
            set => SetValue(IsLastProperty, value);
        }

        private static void OnIsLastChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ToolbarGroupView ctrl)
                ctrl.Separator.Visibility = (bool)e.NewValue
                    ? Visibility.Collapsed
                    : Visibility.Visible;
        }

        // ── 拖拽手柄：悬停高亮点阵 ───────────────────────────────────────
        private void DragHandle_MouseEnter(object sender, MouseEventArgs e)
            => SetDotColor(Color.FromArgb(220, 100, 100, 180));

        private void DragHandle_MouseLeave(object sender, MouseEventArgs e)
        {
            SetDotColor(Color.FromArgb(0xFF, 0x33, 0x33, 0x4A));
            ResetDropHighlight();
            _isDragStarted = false;
        }

        private void SetDotColor(Color color)
        {
            var brush = new SolidColorBrush(color);
            foreach (var child in LogicalTreeHelper.GetChildren(DragDots))
                if (child is Ellipse e) e.Fill = brush;
        }

        // ── 拖拽开始 ──────────────────────────────────────────────────────
        private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(this);
            _isDragStarted = true;
            MouseMove += OnMouseMoveWhileDragging;
            MouseLeftButtonUp += OnMouseLeftButtonUp;
        }

        private void OnMouseMoveWhileDragging(object sender, MouseEventArgs e)
        {
            if (!_isDragStarted || e.LeftButton != MouseButtonState.Pressed) return;
            var delta = e.GetPosition(this) - _dragStartPoint;
            if (Math.Abs(delta.X) < 4 && Math.Abs(delta.Y) < 4) return;

            MouseMove -= OnMouseMoveWhileDragging;
            MouseLeftButtonUp -= OnMouseLeftButtonUp;
            _isDragStarted = false;

            if (DataContext is ToolbarGroup group)
                DragDrop.DoDragDrop(this, new DataObject(DragFormat, group), DragDropEffects.Move);
        }

        private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            MouseMove -= OnMouseMoveWhileDragging;
            MouseLeftButtonUp -= OnMouseLeftButtonUp;
            _isDragStarted = false;
        }

        // ── 放置目标：高亮 + 执行重排 ────────────────────────────────────
        private void OnDragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DragFormat)) { e.Effects = DragDropEffects.None; return; }
            e.Effects = DragDropEffects.Move;
            e.Handled = true;

            GroupBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(200, 74, 95, 160));
            GroupBorder.Background = new SolidColorBrush(Color.FromArgb(30, 74, 95, 160));
        }

        private void OnDragLeave(object sender, DragEventArgs e) => ResetDropHighlight();

        private void OnDrop(object sender, DragEventArgs e)
        {
            ResetDropHighlight();
            if (!e.Data.GetDataPresent(DragFormat)) return;
            if (e.Data.GetData(DragFormat) is not ToolbarGroup dragged) return;
            if (DataContext is not ToolbarGroup target || dragged == target) return;

            var vm = FindToolbarViewModel();
            if (vm == null) return;

            // 落在左半 → 插到 target 前；落在右半 → 插到 target 后
            if (e.GetPosition(this).X < ActualWidth / 2)
                vm.ReorderGroup(dragged, target);
            else
                vm.ReorderGroupAfter(dragged, target);

            e.Handled = true;
        }

        private void ResetDropHighlight()
        {
            GroupBorder.BorderBrush = Brushes.Transparent;
            GroupBorder.Background = Brushes.Transparent;
        }

        // ── 向上查找 ToolbarViewModel ────────────────────────────────────
        private ToolbarViewModel? FindToolbarViewModel()
        {
            DependencyObject? cur = this;
            while (cur != null)
            {
                if (cur is FrameworkElement fe && fe.DataContext is ToolbarViewModel vm)
                    return vm;
                cur = VisualTreeHelper.GetParent(cur);
            }
            return null;
        }
    }
}
