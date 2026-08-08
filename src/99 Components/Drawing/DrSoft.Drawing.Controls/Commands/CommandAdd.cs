using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Model;
using System.Linq;

namespace DrSoft.Drawing.Controls.Commands
{
    internal class CommandAdd : IDrawingCommand
    {
        private readonly ILayerViewModel _layer;
        private readonly IEnumerable<IShape> _shapes;
        private readonly bool _suppressSelectionPublish;
        public string Description => $"���� {_shapes.GetType().Name.Replace("Shape", "")}";

        public CommandAdd(ILayerViewModel layer, IEnumerable<IShape> shapes, bool suppressSelectionPublish = false)
        {
            _layer = layer;
            _shapes = shapes;
            _suppressSelectionPublish = suppressSelectionPublish;
        }

        public void Execute()
        {
            if (_layer == null) return;
            _layer.AddNodes(_shapes);

            if (DocumentContext.Instance?.ActiveCanvas is not DrawingCanvas canvas)
                return;

            foreach (var existing in canvas.Selection)
            {
                existing.IsSelected = false;
            }

            foreach (var shape in _shapes)
            {
                shape.IsSelected = true;
            }

            DocumentContext.Instance.SelectState = SelectState.FirstSelected;
            if (_suppressSelectionPublish)
                canvas.RefreshSelectedShapesSilently(publishSelectionChanged: true, publishCanvasSelectionChange: false);
            else
                canvas.SetSelectedShapes();
        }

        public bool Undo()
        {
            _layer.RemoveNodes(_shapes);

            // ��ѡ���б����Ƴ�����������״
            foreach (var shape in _shapes)
            {
                shape.IsSelected = false;
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
