using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Controls.ViewModels;
using DrSoft.Drawing.Model;

namespace DrSoft.Drawing.Controls.Commands
{
    /// <summary>
    /// 解散单个容器（群组/组合）的命令。
    /// 保证成员还原到原父容器中的原位置，撤销时把原容器还原到原位置。
    /// </summary>
    internal class CommandContainerRelease : IDrawingCommand
    {
        private readonly LayerViewModel _layer;
        private readonly IShape _sourceShape;
        private readonly IReadOnlyList<IShape> _releasedChildren;
        private readonly IShape? _parentContainer;
        private readonly int _insertIndex;
        private readonly bool _suppressSelectionPublish;

        public string Description => "解散容器";

        public CommandContainerRelease(
            ContainerReleasePreparation preparation,
            bool suppressSelectionPublish = true)
        {
            _layer = preparation.TargetLayer as LayerViewModel
                     ?? throw new ArgumentException("TargetLayer 必须是 LayerViewModel", nameof(preparation));
            _sourceShape = preparation.SourceShape;
            _releasedChildren = preparation.ReleasedChildren.ToList();
            _parentContainer = preparation.ParentContainer;
            _insertIndex = preparation.InsertIndex;
            _suppressSelectionPublish = suppressSelectionPublish;
        }

        public void Execute()
        {
            // 移除原容器
            _layer.RemoveNodes(new[] { _sourceShape }, _parentContainer);

            // 在原位置插入成员
            _layer.InsertNodes(_releasedChildren, _parentContainer, _insertIndex);

            // 选中状态：清除已有选区，选中释放出的成员（与旧 CommandAdd 行为一致）
            ClearSelection();
            foreach (var child in _releasedChildren)
                child.IsSelected = true;

            RefreshSelection();
        }

        public bool Undo()
        {
            // 移除已释放的成员
            _layer.RemoveNodes(_releasedChildren, _parentContainer);

            // 在原位置还原原容器
            _layer.InsertNodes(new[] { _sourceShape }, _parentContainer, _insertIndex);

            // 选中状态：选中原容器。
            // 注意：这里不清除其他选区，以便多个容器批量解散后撤销时，
            // 所有被还原的容器都保持选中。
            _sourceShape.IsSelected = true;

            RefreshSelection();
            return true;
        }

        private void ClearSelection()
        {
            if (DocumentContext.Instance?.ActiveCanvas is not DrawingCanvas canvas)
                return;

            foreach (var existing in canvas.Selection)
                existing.IsSelected = false;
        }

        private void RefreshSelection()
        {
            if (DocumentContext.Instance?.ActiveCanvas is not DrawingCanvas canvas)
                return;

            if (_suppressSelectionPublish)
                canvas.RefreshSelectedShapesSilently();
            else
                canvas.SetSelectedShapes();
        }
    }
}
