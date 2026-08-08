using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Model;

namespace DrSoft.Drawing.Controls.Commands
{
    internal class CommandRemove : IDrawingCommand
    {
        private readonly Dictionary<ILayerViewModel, List<IShape>> _shapesByLayer;
        private readonly bool _suppressSelectionPublish;
        public string Description => $"删除 {_shapesByLayer.Values.Sum(s => s.Count)} 个图形";

        /// <summary>
        /// 单图层构造函数（向后兼容）
        /// </summary>
        public CommandRemove(ILayerViewModel layer, IEnumerable<IShape> shapes, bool suppressSelectionPublish = false)
        {
            _shapesByLayer = new Dictionary<ILayerViewModel, List<IShape>>
            {
                [layer] = shapes.ToList()
            };
            _suppressSelectionPublish = suppressSelectionPublish;
        }

        /// <summary>
        /// 多图层构造函数，自动按图层分组图形，
        /// 确保跨图层选择时只产生一个 Command，撤销一次即可还原。
        /// </summary>
        public CommandRemove(IEnumerable<ILayerViewModel> allLayers, IEnumerable<IShape> shapes, bool suppressSelectionPublish = false)
        {
            _shapesByLayer = BuildShapeByLayerMapping(allLayers, shapes);
            _suppressSelectionPublish = suppressSelectionPublish;
        }

        private static Dictionary<ILayerViewModel, List<IShape>> BuildShapeByLayerMapping(
            IEnumerable<ILayerViewModel> allLayers, IEnumerable<IShape> shapes)
        {
            var layerList = allLayers.ToList();
            var result = new Dictionary<ILayerViewModel, List<IShape>>();
            foreach (var shape in shapes)
            {
                var layer = layerList.FirstOrDefault(l => l.Contains(shape));
                if (layer != null)
                {
                    if (!result.TryGetValue(layer, out var list))
                    {
                        list = new List<IShape>();
                        result[layer] = list;
                    }
                    list.Add(shape);
                }
            }
            return result;
        }

        public void Execute()
        {
            foreach (var kvp in _shapesByLayer)
                kvp.Key.RemoveNodes(kvp.Value);

            if (DocumentContext.Instance?.ActiveCanvas is DrawingCanvas canvas)
            {
                if (_suppressSelectionPublish)
                    canvas.RefreshSelectedShapesSilently();
                else
                    canvas.SetSelectedShapes();
            }
        }

        public bool Undo()
        {
            foreach (var kvp in _shapesByLayer)
                kvp.Key.AddNodes(kvp.Value);

            // 恢复删除前的选中状态（RemoveShape 会清除 IsSelected）
            foreach (var shapes in _shapesByLayer.Values)
            {
                foreach (var shape in shapes)
                {
                    shape.IsSelected = true;
                }
            }

            if (DocumentContext.Instance?.ActiveCanvas is DrawingCanvas canvas)
            {
                if (_suppressSelectionPublish)
                    canvas.RefreshSelectedShapesSilently();
                else
                    canvas.SetSelectedShapes();
            }
            return true;
        }
    }
}
