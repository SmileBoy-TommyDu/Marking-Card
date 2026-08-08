using DrSoft.Drawing.Controls.ViewModels;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Model;

namespace DrSoft.Drawing.Controls.Interface
{
    public interface ILayerViewModel
    {
        void AddNodes(IEnumerable<IShape> shapes);

        void RemoveNodes(IEnumerable<IShape> shapes);

        bool Contains(IShape shape);

        IEnumerable<INodeViewModel> GetSelectedNodes();
    }

    public interface INodeViewModel
    {
        int Id { get; }
        string Name { get; set; }
        NodeType NodeType { get; }
        bool IsVisible { get; set; }
        bool IsSelected { get; set; }
        bool IsExpanded { get; set; }
        bool IsLocked { get; set; }
        string ShapeTypeName { get; }

        IList<INodeViewModel> Children { get; }
        INodeViewModel? Parent { get; set; }

        void ClearSelection();
        bool HasSelectedOrContainsSelected();
        IEnumerable<IShape> GetAllShapes();
    }
}
