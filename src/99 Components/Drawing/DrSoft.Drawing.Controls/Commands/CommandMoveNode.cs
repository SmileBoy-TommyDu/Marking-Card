using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Model;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.Commands
{
    /// <summary>
    /// 移动节点命令：支持组合图形子节点移动的撤销/重做。
    /// 捕获移动前后的世界坐标，Undo 恢复到旧位置，Redo 移动到新位置。
    /// </summary>
    internal class CommandMoveNode : IDrawingCommand
    {
        private readonly DrawCombination _combo;
        private readonly DrawObject _child;
        private readonly int _pointIndex;
        private readonly SKPoint _oldWorldPos;
        private readonly SKPoint _newWorldPos;
        public string Description => "移动节点";

        public CommandMoveNode(DrawCombination combo, DrawObject child, int pointIndex,
            SKPoint oldWorldPos, SKPoint newWorldPos)
        {
            _combo = combo;
            _child = child;
            _pointIndex = pointIndex;
            _oldWorldPos = oldWorldPos;
            _newWorldPos = newWorldPos;
        }

        public void Execute()
        {
            _combo.MoveChildPathNodeToWorldPosition(_child, _pointIndex, _newWorldPos);
            PostUpdate();
        }

        public bool Undo()
        {
            _combo.MoveChildPathNodeToWorldPosition(_child, _pointIndex, _oldWorldPos);
            PostUpdate();
            return true;
        }

        private void PostUpdate()
        {
            if (DocumentContext.Instance?.ActiveCanvas is DrawingCanvas canvas)
            {
                canvas.InvalidateVisibleCache();
                canvas.InvalidateGeometryCaches(new List<DrawObject> { _combo });
                canvas.SetSelectedShapes();
            }
            DocumentContext.Instance?.PublishTransformChange();
        }
    }
}
