using System.Numerics;

namespace DrSoft.Drawing.Model
{
    public interface ICanvas
    {
        int Id { get; }
        string Name { get; set; }

        bool IsModified { get; set; }
        int SelectedShapeCount => Selection.Count;
        IViewport Viewport { get; }

        ISelectionSet Selection { get; }
        IReadOnlyDictionary<ShapeType, int> SelectedCountByType { get; }
        IEnumerable<IShape> AllShapes { get; }
        //int SelectedLayerId { get; set; }
        CommandHistory CommandHistory { get;}
        // 兼容历史调用链。重构中的交互/命令代码仍有部分通过 CommandManager 访问撤销栈。
        // 当前实现与 CommandHistory 指向同一实例，后续可在全量迁移完成后移除该别名。
        CommandHistory CommandManager => CommandHistory;
        Rect2D MachineBounds { get; set; }

        float InitZoomPercent { get; set; }
        void SetSelectedShapes();
        bool ClearSelectedShapes();
        void Clear();

        // 变换
        void Transform(Matrix3x2 matrix);
        Rect2D GetBounds();
    }
}
