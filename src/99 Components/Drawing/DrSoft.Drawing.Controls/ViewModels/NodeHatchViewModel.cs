using CommunityToolkit.Mvvm.ComponentModel;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Model;
using DrSoft.Drawing.Utility;

namespace DrSoft.Drawing.Controls.ViewModels
{
    /// <summary>
    /// Hatch 填满节点
    /// 用于管理多个图形的填满操作
    /// </summary>
    public partial class NodeHatchViewModel : ObservableObject, INodeViewModel
    {
        [ObservableProperty] private int _id;
        [ObservableProperty] private string _name;
        [ObservableProperty] private bool _isVisible = true;
        [ObservableProperty] private bool _isExpanded = false;
        [ObservableProperty] private bool _isSelected;
        [ObservableProperty] private bool _isLocked;
        [ObservableProperty] private ShapeType _shapeType;
        [ObservableProperty] private string _shapeTypeName = string.Empty;

        public NodeType NodeType => NodeType.Hatch;

        /// <summary>关联的填满模型</summary>
        public IShape Model { get; }

        /// <summary>
        /// 虚拟化子节点集合：按需创建 ViewModel，避免一次性创建百万级节点
        /// </summary>
        public VirtualizingNodeCollection Children { get; }

        IList<INodeViewModel> INodeViewModel.Children => Children;

        public INodeViewModel? Parent { get; set; }

        /// <summary>创建填满节点</summary>
        public NodeHatchViewModel(IShape hatch)
        {
            Model = hatch;
            Id = hatch.UId;
            Name = hatch.Name;
            IsVisible = hatch.IsVisible;
            IsSelected = hatch.IsSelected;
            ShapeType = hatch.Type;
            ShapeTypeName = hatch.Type.GetDescription();

            Children = new VirtualizingNodeCollection(hatch, _ => this);
        }

        partial void OnIsSelectedChanged(bool value)
        {
            Model.IsSelected = value;
        }

        public string Icon => "▨";

        public void ClearSelection()
        {
            IsSelected = false;
            Children.ClearAllSelection();
        }

        public bool HasSelectedOrContainsSelected()
        {
            return IsSelected;
        }

        public IEnumerable<IShape> GetAllShapes()
        {
            return [Model];
        }
    }
}
