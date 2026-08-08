using DrSoft.Drawing.Contracts;
using DrSoft.Drawing.Controls.Clipboard;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Mapping;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Model;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.Models
{
    public class MultiCanvas : IMultiCanvas
    {
        private readonly List<ICanvas> _canvasCollection = new();
                
        public DocumentContext Context { get; private set; } = DocumentContext.Instance;
        private int _documentCounter = 0;

        // 事件
        public event EventHandler<DrawingCanvas>? CanvasCreated;
        public event EventHandler<DrawingCanvas>? CanvasRemoved;
        public event EventHandler<DrawingCanvas>? ActiveCanvasChanged;
        public event EventHandler Redraw;
        public IReadOnlyList<ICanvas> CanvasCollection => _canvasCollection;
                
        public int CreateCanvas()
        {
            DrawingCanvas newCanvas = new() { IsActive = true };

            foreach (var c in _canvasCollection.OfType<DrawingCanvas>()) c.IsActive = false;

            _canvasCollection.Add(newCanvas);

            //TestShape(newCanvas);
            // 触发事件通知
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() => CanvasCreated?.Invoke(this, newCanvas));

            SwitchCanvas(newCanvas);
            
            return newCanvas.Id;
        }

        public int CreateCanvas(CanvasSnapshotDto snapShot)
        {
            // Layers 必须是 DrawingLayer（由调用方直接构建，零映射）
            var layers = new List<DrawingLayer>();
            foreach (var layerData in snapShot.Layers)
            {
                if (layerData is DrawingLayer dl)
                {
                    layers.Add(dl);
                }
                else
                {
                    // 非DrawingLayer不再支持，请调用方直接构建DrawingLayer
                    throw new InvalidOperationException($"CreateCanvas 要求 ILayerData 的实际类型为 DrawingLayer，收到: {layerData.GetType().Name}");
                }
            }

            DrawingCanvas newCanvas = new(layers) { IsActive = true };

            foreach (var c in _canvasCollection.OfType<DrawingCanvas>()) c.IsActive = false;

            newCanvas.Name = snapShot.Name;

            _canvasCollection.Add(newCanvas);
                        
            // 触发事件通知（使用 Dispatcher 确保在 UI 线程触发，避免跨线程修改 ObservableCollection）
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() => CanvasCreated?.Invoke(this, newCanvas));

            SwitchCanvas(newCanvas);
            return newCanvas.Id;
        }

        internal bool SwitchCanvas(DrawingCanvas canvas)
        {
            // 清空当前选中状态
            Context.ActiveCanvas?.ClearSelectedShapes();

            // 同步设置 ActiveCanvas，确保调用方（如 DrawingContext.Create）可立即访问
            Context.ActiveCanvas = canvas;
            Context.HasMousePosition = false; // 切换画布后重置，粘贴时才能使用原始坐标
            ((DrawingCanvas)Context.ActiveCanvas).SetMachineBounds(Context.DefaultMachineBounds.Width,Context.DefaultMachineBounds.Height);
            // 事件通知分发到 UI 线程，避免跨线程修改 ObservableCollection
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() => ActiveCanvasChanged?.Invoke(this, canvas));

            PublishCommandCapabilityChanged();
            return true;
        }

        private void PublishCommandCapabilityChanged()
        {
            var caps = new SelectionCapabilities
            {
                CanPaste = DrawingClipboard.Instance.HasContent
            };
            EventBus.Instance.Publish(new CommandCapabilityChangedEvent { Capabilities = caps });
        }

        public int CloseSelectCanvas(int canvasId)
        {
            if(_canvasCollection == null) return 0;
            var document = _canvasCollection.FirstOrDefault(d => d.Id == canvasId);
            if (document == null)
                return 0;

            bool isClosingActiveDocument = (Context.ActiveCanvas == document);

            _canvasCollection.Remove(document);
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() => CanvasRemoved?.Invoke(this, (DrawingCanvas)document));

            // 如果关闭的是当前激活文档，需要切换到其他文档
            if (isClosingActiveDocument)
            {
                var index = _canvasCollection.IndexOf(document);
                // 优先切换到后一个，否则前一个
                var nextDoc = index < _canvasCollection.Count - 1
                    ? _canvasCollection[index + 1]
                    : (index > 0 ? _canvasCollection[index - 1] : null);

                Context.ActiveCanvas = nextDoc;

                // 发出当前文档改变事件
                if (nextDoc is DrawingCanvas activeCanvas)
                {
                    activeCanvas.IsActive = true;
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(() => ActiveCanvasChanged?.Invoke(this, activeCanvas));
                }
                else
                {
                    // 如果没有下一个文档，触发文档变为无激活状态的事件
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(() => ActiveCanvasChanged?.Invoke(this, null));
                }

                //判断画布为空,失能绘图工具
                if(Context.ActiveCanvas == null)
                {

                }

            }

            PublishCommandCapabilityChanged();
            return canvasId;
        }

        public bool SwitchCanvas(ICanvas canvas)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// 复制指定画布，创建一个包含相同图形数据的新画布。
        /// </summary>
        /// <param name="canvasId">要复制的画布ID</param>
        /// <returns>新画布的ID，如果找不到画布则返回0</returns>
        public int DuplicateCanvas(int canvasId)
        {
            var sourceCanvas = _canvasCollection.FirstOrDefault(d => d.Id == canvasId) as DrawingCanvas;
            if (sourceCanvas == null)
                return 0;

            // 深拷贝所有图层和图形
            var clonedLayers = new List<DrawingLayer>();
            foreach (var sourceLayer in sourceCanvas.Layers)
            {
                var clonedLayer = new DrawingLayer
                {
                    Name = sourceLayer.Name,
                    Color = sourceLayer.Color,
                    IsVisible = sourceLayer.IsVisible,
                    IsLocked = sourceLayer.IsLocked,
                    SortId = sourceLayer.SortId
                };

                // 克隆该图层中的所有图形
                foreach (var shape in sourceLayer.AllShapesInternal)
                {
                    if (shape is DrawObject drawObj)
                    {
                        var clonedShape = (DrawObject)drawObj.Clone();
                        clonedLayer.AddShape(clonedShape);
                    }
                }

                clonedLayers.Add(clonedLayer);
            }

            // 创建新画布
            DrawingCanvas newCanvas = new(clonedLayers) { IsActive = true };
            newCanvas.Name = "画布" + newCanvas.Id;
            newCanvas.MachineBounds = sourceCanvas.MachineBounds;

            // 停用其他画布
            foreach (var c in _canvasCollection.OfType<DrawingCanvas>())
                c.IsActive = false;

            _canvasCollection.Add(newCanvas);

            // 触发事件通知
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() => CanvasCreated?.Invoke(this, newCanvas));

            SwitchCanvas(newCanvas);
            return newCanvas.Id;
        }
    }
}
