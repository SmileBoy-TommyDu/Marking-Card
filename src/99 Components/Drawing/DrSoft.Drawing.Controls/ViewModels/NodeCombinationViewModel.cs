using CommunityToolkit.Mvvm.ComponentModel;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Model;
using DrSoft.Drawing.Utility;

namespace DrSoft.Drawing.Controls.ViewModels
{
    public partial class NodeCombinationViewModel : ObservableObject, INodeViewModel
    {
        [ObservableProperty] private int _id;
        [ObservableProperty] private string _name;
        [ObservableProperty] private bool _isVisible = true;
        [ObservableProperty] private bool _isExpanded = false;
        [ObservableProperty] private bool _isSelected;
        [ObservableProperty] private bool _isLocked;
        [ObservableProperty] private ShapeType _shapeType;
        [ObservableProperty] private string _shapeTypeName = string.Empty;

        public NodeType NodeType => NodeType.Combination;

        /// <summary>关联的填满模型</summary>
        public IShape Model { get; }

        /// <summary>
        /// 虚拟化子节点集合：按需创建 ViewModel，避免一次性创建百万级节点
        /// </summary>
        public VirtualizingNodeCollection Children { get; }

        IList<INodeViewModel> INodeViewModel.Children => Children;

        public INodeViewModel? Parent { get; set; }

        /// <summary>创建填满节点</summary>
        public NodeCombinationViewModel(IShape combination)
        {
            Model = combination;
            Id = combination.UId;
            Name = combination.Name;
            IsVisible = combination.IsVisible;
            IsSelected = combination.IsSelected;
            ShapeType = combination.Type;
            _shapeTypeName = combination.Type.GetDescription();
            if (combination is DrawCombination com && com.Kind == CombinationKind.Extended)
            {
                _shapeTypeName = "扩展";
            }
            Children = new VirtualizingNodeCollection(combination, _ => this);
        }

        partial void OnIsSelectedChanged(bool value)
        {
            Model.IsSelected = value;
        }

        public string Icon => "◰";

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
            return new IShape[] { Model };
        }
    }
}
