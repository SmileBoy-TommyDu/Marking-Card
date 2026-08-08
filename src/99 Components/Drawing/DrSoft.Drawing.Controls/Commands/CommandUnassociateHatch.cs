using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Model;

namespace DrSoft.Drawing.Controls.Commands
{
    /// <summary>
    /// 解除填充物与边界图形关联的命令
    /// </summary>
    internal class CommandUnassociateHatch : IDrawingCommand
    {
        public string Description => "解除填充关联";

        // 只保存 TargetShapes 的快照，key=hatch 对象，value=操作前的关联列表
        private readonly List<(DrawingHatch Hatch, List<IShape> SavedTargets)> _snapshots;

        /// <summary>
        /// 解除填充物与边界图形关联的命令
        /// </summary>
        /// <param name="hatches"></param>
        public CommandUnassociateHatch(IEnumerable<DrawingHatch> hatches)
        {
            // 构造时立即执行 Clear 并保存 before 快照（只存 TargetShapes，不存几何数据）
            _snapshots = hatches.Select(h =>
            {
                var saved = h.Boundaries.ToList();   // 只拷贝列表引用，不走 Memento
                h.Boundaries.Clear();
                return (h, saved);
            }).ToList();
        }

        /// <summary>Redo：再次清空 TargetShapes。</summary>
        public void Execute()
        {
            foreach (var (hatch, _) in _snapshots)
                hatch.Boundaries.Clear();
        }

        /// <summary>Undo：恢复 TargetShapes 关联列表。</summary>
        public bool Undo()
        {
            foreach (var (hatch, saved) in _snapshots)
            {
                hatch.Boundaries.Clear();
                hatch.Boundaries.AddRange(saved);
            }
            return true;
        }
    }
}
