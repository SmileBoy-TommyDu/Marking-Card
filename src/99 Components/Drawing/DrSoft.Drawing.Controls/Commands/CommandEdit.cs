using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Model;
using System.Collections.Generic;
using System.Linq;

namespace DrSoft.Drawing.Controls.Commands
{
    /// <summary>
    /// 图形属性调整命令：支持 AdjustRect / AdjustCircle / AdjustArc / AdjustPolygon 等操作的撤销/重做。
    /// 在执行操作前捕获 before 快照，操作完成后调用 CaptureAfterState() 捕获 after 快照。
    /// </summary>
    internal class CommandEdit : IDeferredCommand
    {
        private readonly List<ShapeEditRecord> _snapshots;
        private Action? _restoreBeforeSelection;
        private Action? _restoreAfterSelection;
        public string Description { get; }

        public CommandEdit(IEnumerable<DrawObject> shapes, string description = "调整属性")
        {
            Description = description;
            _snapshots = shapes.Select(s => new ShapeEditRecord(s, s.CaptureSnapshot())).ToList();
        }

        /// <summary>
        /// 在属性修改完成后调用，捕获 after 快照以支持 Redo。
        /// </summary>
        public void CaptureAfterState()
        {
            foreach (var s in _snapshots)
                s.After = s.Shape.CaptureSnapshot();
        }

        public void Execute()
        {
            foreach (var s in _snapshots)
            {
                s.After?.Restore();
            }
            _restoreAfterSelection?.Invoke();
            RefreshUI();
        }

        public bool Undo()
        {
            foreach (var s in _snapshots)
                s.Before.Restore();

            _restoreBeforeSelection?.Invoke();
            RefreshUI();
            return true;
        }

        public void SetSelectionRestoreActions(
            Action? restoreBeforeSelection,
            Action? restoreAfterSelection)
        {
            _restoreBeforeSelection = restoreBeforeSelection;
            _restoreAfterSelection = restoreAfterSelection;
        }

        private void RefreshUI()
        {
            if (DocumentContext.Instance?.ActiveCanvas is DrawingCanvas canvas)
            {
                var shapes = _snapshots.Select(s => s.Shape).OfType<IShape>().ToList();
                canvas.InvalidateVisibleCache();
                canvas.InvalidateGeometryCaches(shapes);
                canvas.SetSelectedShapes();
                canvas.RegenerateHatchForShapes(shapes);
            }
            DocumentContext.Instance?.RequestRedraw();
        }

        private record ShapeEditRecord(DrawObject Shape, IShapeMemento Before)
        {
            public IShapeMemento? After { get; set; }
        }
    }
}
