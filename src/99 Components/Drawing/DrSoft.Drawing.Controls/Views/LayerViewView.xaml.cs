using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Controls.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DrSoft.Drawing.Controls.Views
{
    /// <summary>
    /// LayerPanelView.xaml 的交互逻辑
    /// </summary>
    public partial class LayerViewView : System.Windows.Controls.UserControl
    {
        private LayerViewViewModel? _vm;

        public LayerViewView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            PreviewKeyDown += OnPreviewKeyDown;
        }

        public LayerViewView(LayerViewViewModel vm) : this()
        {
            DataContext = vm;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            _vm = e.NewValue as LayerViewViewModel;
        }

        /// <summary>
        /// 监听 Delete 键，根据选中节点类型执行对应层级的删除
        /// </summary>
        private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != Key.Delete) return;
            if (_vm == null) return;

            if (_vm.DeleteSelectedNodes())
                e.Handled = true;
        }

        /// <summary>
        /// 节点点击事件（支持 Ctrl / Shift 多选，锁定的图层/节点不可选中）
        /// </summary>
        private void UnifiedRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border { DataContext: INodeViewModel node }
                && DataContext is LayerViewViewModel panelVm)
            {
                // 记录拖拽起点
                _dragSource = node;
                _dragStartPoint = e.GetPosition(null);
                _isDragging = false;

                // 锁定的图层或锁定的子节点不允许选中
                if (node.IsLocked)
                {
                    e.Handled = true;
                    return;
                }

                bool ctrlPressed = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                bool shiftPressed = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
                bool altPressed = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;

                // alt+点击复制图层
                if (altPressed && node is LayerViewModel layerNode)
                {
                    panelVm.CopyLayer(layerNode);  // 复制点击的图层
                    e.Handled = true;
                    return;
                }

                panelVm.SelectNode(node, ctrlPressed, shiftPressed);

                e.Handled = true;
            }
        }
    }
}
