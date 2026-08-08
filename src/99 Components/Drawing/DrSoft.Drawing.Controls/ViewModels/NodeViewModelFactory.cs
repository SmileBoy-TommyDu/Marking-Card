using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Model;

namespace DrSoft.Drawing.Controls.ViewModels
{
    /// <summary>
    /// 节点 ViewModel 工厂
    /// 根据 IShape 类型创建对应的 ViewModel，并递归构建子节点
    /// </summary>
    public static class NodeViewModelFactory
    {
        /// <summary>
        /// 将 IShape 转换为对应的 INodeViewModel，并递归构建子节点树
        /// </summary>
        /// <param name="shape">图形对象</param>
        /// <param name="parent">父节点</param>
        /// <param name="buildChildren">是否立即构建子节点（容器类型可延迟到后台线程构建）</param>
        public static INodeViewModel Create(IShape shape, INodeViewModel? parent = null, bool buildChildren = true)
        {
            INodeViewModel vm = shape switch
            {
                DrawingGroup => new NodeGroupViewModel(shape),
                DrawingHatch => new NodeHatchViewModel(shape),
                DrawCombination => new NodeCombinationViewModel(shape),
                _ => new NodeShapeViewModel(shape)
            };

            vm.Parent = parent;

            // 递归构建容器类型的子节点
            // VirtualizingNodeCollection 已在构造时加载模型子节点，跳过 eager 构建
            if (buildChildren && shape is IContainer container && vm.Children is not VirtualizingNodeCollection)
            {
                foreach (var child in container.Children)
                {
                    vm.Children.Add(Create(child, vm));
                }
            }

            return vm;
        }

        /// <summary>
        /// 在后台线程构建容器的子节点，完成后在 UI 线程添加到 Children 集合
        /// </summary>
        /// <param name="vm">需要加载子节点的容器 ViewModel</param>
        public static void LoadChildrenAsync(INodeViewModel vm)
        {
            if (vm.Children.Count > 0) return; // 已加载
            if (vm is not IContainer container) return;
            // VirtualizingNodeCollection 不需要异步加载（按需创建）
            if (vm.Children is VirtualizingNodeCollection) return;

            _ = Task.Run(() =>
            {
                // 后台线程：构建子节点 ViewModel 列表（不操作 ObservableCollection）
                var children = new List<INodeViewModel>();
                foreach (var child in container.Children)
                {
                    // 非容器类型的子节点可以同步构建；容器类型延迟构建
                    children.Add(Create(child, vm, buildChildren: false));
                }
                return children;
            }).ContinueWith(task =>
            {
                if (task.IsFaulted || task.IsCanceled) return;
                var children = task.Result;

                // UI 线程：批量添加到 ObservableCollection
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    foreach (var child in children)
                        vm.Children.Add(child);
                });
            });
        }
    }
}
