using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;

namespace DrSoft.Drawing.Controls.Commands
{
    /// <summary>
    /// 重新填充命令：支持 Refill 操作的撤销/重做。
    /// 捕获每个 DrawingHatch 的旧填充参数、旧子图形和边界图形的旧填充参数。
    /// </summary>
    internal class CommandRefill : IDrawingCommand
    {
        private readonly List<HatchSnapshot> _snapshots;
        private readonly HatchParamDto _newParam;
        public string Description => "重新填充";

        public CommandRefill(IReadOnlyList<DrawingHatch> hatches, HatchParamDto newParam)
        {
            _newParam = newParam;
            _snapshots = new List<HatchSnapshot>(hatches.Count);
            foreach (var hatch in hatches)
            {
                var boundarySnapshots = hatch.Boundaries
                    .Select(b => (shape: b, oldParam: (b as IHatchable)?.HatchParamInfo))
                    .ToList();
                _snapshots.Add(new HatchSnapshot(
                    hatch,
                    hatch.HatchParamInfo,
                    hatch.Children.ToList(),
                    boundarySnapshots));
            }
        }

        public void Execute()
        {
            foreach (var snap in _snapshots)
            {
                snap.Hatch.RefillFromTargets(_newParam);
            }
            PostUpdate();
        }

        public bool Undo()
        {
            foreach (var snap in _snapshots)
            {
                // 恢复 hatch 自身的填充参数
                snap.Hatch.HatchParamInfo = snap.OldHatchParam;

                // 恢复边界图形的填充参数
                foreach (var (shape, oldParam) in snap.BoundarySnapshots)
                {
                    if (shape is IHatchable hatchable)
                        hatchable.HatchParamInfo = oldParam;
                }

                // 恢复旧子图形
                snap.Hatch.Children.Clear();
                foreach (var child in snap.OldChildren)
                {
                    snap.Hatch.Children.Add(child);
                }
            }
            PostUpdate();
            return true;
        }

        private void PostUpdate()
        {
            if (DocumentContext.Instance?.ActiveCanvas is DrawingCanvas canvas)
            {
                canvas.InvalidateVisibleCache();
                var affected = _snapshots.Select(s => (DrawObject)s.Hatch).ToList();
                canvas.InvalidateGeometryCaches(affected);
                canvas.SetSelectedShapes();
            }
            // Refill 不是变换操作，不需要 PublishTransformChange
        }

        private record HatchSnapshot(
            DrawingHatch Hatch,
            HatchParamDto? OldHatchParam,
            List<IShape> OldChildren,
            List<(IShape shape, HatchParamDto? oldParam)> BoundarySnapshots);
    }
}
