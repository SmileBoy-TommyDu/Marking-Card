using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Controls.ViewModels;
using DrSoft.Drawing.Model;

namespace DrSoft.Drawing.Controls.Commands
{
    /// <summary>
    /// 在已有父容器内创建子容器（群组/组合）的命令。
    /// 将选中的图形从父容器中移除，把新容器插入到父容器的原位置。
    /// 撤销时反向操作，完整还原原始状态。
    /// </summary>
    internal class CommandGroupInContainer : IDrawingCommand
    {
        private readonly LayerViewModel _layer;
        private readonly IShape _parentGroup;
        private readonly IReadOnlyList<IShape> _originalShapes;
        private readonly IShape _newGroup;
        private readonly int _insertIndex;
        private readonly string _description;

        public string Description => _description;

        /// <param name="layer">目标图层</param>
        /// <param name="parentGroup">父容器（必须是 IContainer，如 DrawingGroup）</param>
        /// <param name="originalShapes">需要被群组/组合的图形（按在父容器中的原始顺序）</param>
        /// <param name="newGroup">新创建的容器（群组或组合）</param>
        /// <param name="insertIndex">新容器在父容器中的插入位置（取 originalShapes 中的最小索引）</param>
        /// <param name="description">命令描述</param>
        public CommandGroupInContainer(
            LayerViewModel layer,
            IShape parentGroup,
            IReadOnlyList<IShape> originalShapes,
            IShape newGroup,
            int insertIndex,
            string description = "群组")
        {
            _layer = layer ?? throw new ArgumentNullException(nameof(layer));
            _parentGroup = parentGroup ?? throw new ArgumentNullException(nameof(parentGroup));
            _originalShapes = originalShapes ?? throw new ArgumentNullException(nameof(originalShapes));
            _newGroup = newGroup ?? throw new ArgumentNullException(nameof(newGroup));
            _insertIndex = insertIndex;
            _description = description;
        }

        public void Execute()
        {
            var parentNode = _layer.FindNode(_parentGroup);
            var vc = parentNode?.Children as VirtualizingNodeCollection;

            if (vc != null)
            {
                // 通过 VirtualizingNodeCollection 统一操作模型层和视图层
                // 按降序移除原图形，避免索引偏移
                var indices = new List<int>();
                foreach (var shape in _originalShapes)
                {
                    int idx = vc.IndexOfModelId(shape.UId);
                    if (idx >= 0)
                        indices.Add(idx);
                }
                indices.Sort();
                for (int i = indices.Count - 1; i >= 0; i--)
                    vc.RemoveAt(indices[i]);

                // 在最小索引位置插入新群组（同步模型层 + 视图层）
                var groupNode = NodeViewModelFactory.Create(_newGroup, parentNode!, buildChildren: false);
                int insertAt = Math.Min(_insertIndex, vc.Count);
                vc.Insert(insertAt, groupNode);
            }
            else
            {
                // 退化为直接模型操作
                var parentChildren = ((IContainer)_parentGroup).Children;

                var indices = new List<(int index, IShape shape)>();
                foreach (var shape in _originalShapes)
                {
                    int idx = parentChildren.IndexOf(shape);
                    if (idx >= 0)
                        indices.Add((idx, shape));
                }
                foreach (var (index, shape) in indices.OrderByDescending(x => x.index))
                    parentChildren.RemoveAt(index);

                parentChildren.Insert(_insertIndex, _newGroup);
            }

            // 更新选中状态
            UpdateSelection(new[] { _newGroup });
        }

        public bool Undo()
        {
            var parentNode = _layer.FindNode(_parentGroup);
            var vc = parentNode?.Children as VirtualizingNodeCollection;

            if (vc != null)
            {
                // 通过 VirtualizingNodeCollection 统一操作模型层和视图层
                // 移除新群组
                int groupIdx = vc.IndexOfModelId(_newGroup.UId);
                if (groupIdx >= 0)
                    vc.RemoveAt(groupIdx);

                // 按原始顺序重新插入原图形
                for (int i = 0; i < _originalShapes.Count; i++)
                {
                    var node = NodeViewModelFactory.Create(_originalShapes[i], parentNode!, buildChildren: false);
                    int insertAt = Math.Min(_insertIndex + i, vc.Count);
                    vc.Insert(insertAt, node);
                }
            }
            else
            {
                // 退化为直接模型操作
                var parentChildren = ((IContainer)_parentGroup).Children;

                parentChildren.Remove(_newGroup);

                for (int i = 0; i < _originalShapes.Count; i++)
                {
                    int idx = Math.Min(_insertIndex + i, parentChildren.Count);
                    parentChildren.Insert(idx, _originalShapes[i]);
                }
            }

            // 恢复原始图形的选中状态
            UpdateSelection(_originalShapes);
            return true;
        }

        private static void UpdateSelection(IEnumerable<IShape> shapesToSelect)
        {
            if (DocumentContext.Instance?.ActiveCanvas is not DrawingCanvas canvas)
                return;

            // 清除所有已有选中
            foreach (var existing in canvas.Selection)
                existing.IsSelected = false;

            // 选中目标图形
            foreach (var shape in shapesToSelect)
                shape.IsSelected = true;

            canvas.SetSelectedShapes();
        }
    }
}
