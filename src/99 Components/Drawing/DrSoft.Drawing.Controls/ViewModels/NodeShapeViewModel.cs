using CommunityToolkit.Mvvm.ComponentModel;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Model;
using DrSoft.Drawing.Utility;
using System.Collections.ObjectModel;

namespace DrSoft.Drawing.Controls.ViewModels
{
    // ─── 树节点基类 ───────────────────────────────────────────
    public partial class NodeShapeViewModel : ObservableObject, INodeViewModel
    {
        [ObservableProperty] private int _id;
        [ObservableProperty] private string _name = string.Empty;
        [ObservableProperty] private bool _isVisible = true;
        [ObservableProperty] private bool _isSelected;
        [ObservableProperty] private bool _isLocked;
        [ObservableProperty] private ShapeType _shapeType;
        [ObservableProperty] private string _shapeTypeName = string.Empty;
        [ObservableProperty] private bool _isExpanded;

        public NodeType NodeType => NodeType.Shape;

        /// <summary>当前节点是否位于群组内部</summary>
        public bool IsInGroup => Parent is NodeGroupViewModel;

        // 直接使用图形接口定义
        public IShape Model { get; }

        public ObservableCollection<INodeViewModel> Children { get; } = new ObservableCollection<INodeViewModel>();

        // INodeViewModel 显式实现
        IList<INodeViewModel> INodeViewModel.Children => Children;
        public INodeViewModel? Parent { get; set; }

        public NodeShapeViewModel(IShape shape)
        {
            Model = shape;
            Id = shape.UId;
            Name = shape.Name;
            IsVisible = shape.IsVisible;
            IsSelected = shape.IsSelected;
            ShapeType = shape.Type;
            ShapeTypeName = shape.Type.GetDescription();
        }

        partial void OnIsSelectedChanged(bool value)
        {
            try { Model.IsSelected = value; } catch { }
        }

        public Uri IconPath => ShapeType switch
        {
            Drawing.Model.ShapeType.Point => new Uri("pack://application:,,,/DrSoft.Drawing.Controls;component/Resources/layer/point.png", UriKind.Absolute),
            Drawing.Model.ShapeType.Line => new Uri("pack://application:,,,/DrSoft.Drawing.Controls;component/Resources/layer/line.png", UriKind.Absolute),
            Drawing.Model.ShapeType.PolyLine => new Uri("pack://application:,,,/DrSoft.Drawing.Controls;component/Resources/layer/line.png", UriKind.Absolute),
            Drawing.Model.ShapeType.Rectangle => new Uri("pack://application:,,,/DrSoft.Drawing.Controls;component/Resources/layer/Rectangle.png", UriKind.Absolute),
            Drawing.Model.ShapeType.Circle => new Uri("pack://application:,,,/DrSoft.Drawing.Controls;component/Resources/layer/circle.png", UriKind.Absolute),
            Drawing.Model.ShapeType.Text => new Uri("pack://application:,,,/DrSoft.Drawing.Controls;component/Resources/layer/text.png", UriKind.Absolute),
            Drawing.Model.ShapeType.Arc => new Uri("pack://application:,,,/DrSoft.Drawing.Controls;component/Resources/layer/arc.png", UriKind.Absolute),
            Drawing.Model.ShapeType.Bezier => new Uri("pack://application:,,,/DrSoft.Drawing.Controls;component/Resources/layer/Bezier.png", UriKind.Absolute),
            Drawing.Model.ShapeType.Group => new Uri("pack://application:,,,/DrSoft.Drawing.Controls;component/Resources/layer/group-0.png", UriKind.Absolute),
            Drawing.Model.ShapeType.Polygon => new Uri("pack://application:,,,/DrSoft.Drawing.Controls;component/Resources/layer/Polygon.png", UriKind.Absolute),
            Drawing.Model.ShapeType.Combination => new Uri("pack://application:,,,/DrSoft.Drawing.Controls;component/Resources/layer/shape2.png", UriKind.Absolute),
            Drawing.Model.ShapeType.ArbitraryCurve => new Uri("pack://application:,,,/DrSoft.Drawing.Controls;component/Resources/layer/Bezier.png", UriKind.Absolute),
            _ => new Uri("pack://application:,,,/DrSoft.Drawing.Controls;component/Resources/layer/point.png", UriKind.Absolute)
        };

        public void ClearSelection()
        {
            IsSelected = false;
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
