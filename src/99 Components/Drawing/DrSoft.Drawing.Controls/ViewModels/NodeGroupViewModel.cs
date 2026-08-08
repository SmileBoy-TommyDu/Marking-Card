using CommunityToolkit.Mvvm.ComponentModel;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Model;
using DrSoft.Drawing.Utility;

namespace DrSoft.Drawing.Controls.ViewModels
{
    // ─── 群组节点 ─────────────────────────────────────────────
    public partial class NodeGroupViewModel : ObservableObject, INodeViewModel
    {
        [ObservableProperty] private int _id;
        [ObservableProperty] private string _name;
        [ObservableProperty] private bool _isVisible = true;
        [ObservableProperty] private bool _isExpanded = false;
        [ObservableProperty] private bool _isSelected;
        [ObservableProperty] private bool _isLocked;
        [ObservableProperty] private ShapeType _shapeType;
        [ObservableProperty] private string _shapeTypeName = string.Empty;

        public NodeType NodeType => NodeType.Group;

        public IShape Model { get; }

        /// <summary>
        /// 虚拟化子节点集合：按需创建 ViewModel，避免一次性创建百万级节点
        /// </summary>
        public VirtualizingNodeCollection Children { get; }

        // INodeViewModel 显式实现（返回 IList）
        IList<INodeViewModel> INodeViewModel.Children => Children;

        public INodeViewModel? Parent { get; set; }

        public NodeGroupViewModel(IShape group)
        {
            Model = group;
            Id = group.UId;
            Name = group.Name;
            IsVisible = group.IsVisible;
            IsSelected = group.IsSelected;
            ShapeType = group.Type;
            ShapeTypeName = group.Type.GetDescription();

            // 创建虚拟化集合，子节点在滚动时按需创建
            Children = new VirtualizingNodeCollection(group, _ => this);
        }

        partial void OnIsSelectedChanged(bool value)
        {
            Model.IsSelected = value;
        }

        public string Icon => "▣";

        public void ClearSelection()
        {
            IsSelected = false;
            Children.ClearAllSelection();
        }

        public bool HasSelectedOrContainsSelected()
        {
            if (IsSelected) return true;
            // 仅检查已缓存的节点，不触发全量创建
            foreach (var kvp in Children.CachedItems)
            {
                if (kvp.Value.IsSelected) return true;
            }
            return false;
        }

        public IEnumerable<IShape> GetAllShapes()
        {
            return [Model];//Children.ModelChildren;
        }
    }
}
