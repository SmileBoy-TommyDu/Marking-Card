using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Model;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.Commands
{
    /// <summary>
    /// 锁定/解锁命令：支持锁定状态切换的撤销/重做。
    /// 捕获操作前各图形的 IsLocked 和 Pen 状态，Undo/Redo 时恢复对应状态。
    /// </summary>
    internal class CommandLock : IDrawingCommand
    {
        private readonly (DrawObject Shape, bool WasLocked, SKPaint? OldPen)[] _snapshots;
        private readonly bool _targetLockState;
        public string Description { get; }

        public CommandLock(IEnumerable<DrawObject> shapes, bool targetLockState)
        {
            Description = targetLockState ? "锁定" : "解锁";
            _targetLockState = targetLockState;
            _snapshots = shapes.Select(s => (s, s.IsLocked, s.Pen)).ToArray();
        }

        public void Execute()
        {
            foreach (var (shape, _, _) in _snapshots)
            {
                shape.ApplyLockState(_targetLockState);
            }

            DocumentContext.Instance?.ActiveCanvas?.SetSelectedShapes();
            DocumentContext.Instance?.RequestRedraw();
        }

        public bool Undo()
        {
            // 恢复每个图形的原始锁定状态和画笔
            foreach (var (shape, wasLocked, oldPen) in _snapshots)
            {
                shape.IsLocked = wasLocked;
                shape.Pen = oldPen;
            }

            DocumentContext.Instance?.ActiveCanvas?.SetSelectedShapes();
            DocumentContext.Instance?.RequestRedraw();
            return true;
        }
    }
}
