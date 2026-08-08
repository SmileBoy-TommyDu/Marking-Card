using DrSoft.Drawing.Contracts;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.DXFHelper;
using DrSoft.Drawing.Controls.Mapping;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Model;
using SkiaSharp;
using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using System.Windows.Forms.Design;
using System.Windows.Media.Media3D;
using System.Xml.Linq;

namespace DrSoft.Drawing.Controls.Service
{
    public class CanvasService : ICanvasService
    {
        #region Private Fields
        private readonly DocumentContext _context = DocumentContext.Instance;
        CancellationTokenSource? _cts;
        private readonly DxfImporter _importer = new();
        private readonly MultiCanvas _canvas;
        private readonly CanvasStorageService _storageService = new();
        IEventBus? eventBus => EventBus.Instance;
        #endregion

        #region Constructor
        public CanvasService(MultiCanvas canvas)
        {
            _canvas = canvas;
            _importer.OnProgress += _importer_OnProgress;
            _importer.OnFailed += _importer_OnFailed;
            _importer.OnNewCompleted += _importer_OnNewCompleted;
        }
        #endregion

        #region Implementation
        public int AddCanvas(string title = "未命名")
        {
            return _canvas.CreateCanvas();
        }

        public void Close(int canvasId)
        {
            if (_canvas != null)
            {
                _canvas.CloseSelectCanvas(canvasId);
            }
        }

        public int? Open(CanvasSnapshotDto snapShotDto)
        {
            Trace.WriteLine($"给到画布，开始渲染,{DateTime.Now}");
            return _canvas.CreateCanvas(snapShotDto);
        }

        public CanvasStorageDocumentDto LoadFile(string filePath)
        {
            var document = _storageService.Load(filePath);
            if (document?.CanvasSnapshot != null)
            {
                document.CanvasSnapshot.Name = Path.GetFileName(filePath);
            }

            return document;
        }

        public void SaveFile(
            string filePath,
            IReadOnlyDictionary<int, byte[]>? layerPayloads = null,
            IReadOnlyDictionary<string, byte[]>? extensionPayloads = null)
        {
            if (_canvas.Context.ActiveCanvas is not DrawingCanvas canvas)
            {
                return;
            }

            RenameCanvas(Path.GetFileName(filePath));
            _storageService.Save(filePath, canvas, layerPayloads, extensionPayloads);
        }

        /// <summary>
        /// 异步打开画布
        /// </summary>
        public async Task<int?> OpenAsync(CanvasSnapshotDto snapShotDto)
        {
            Trace.WriteLine($"给到画布，开始渲染,{DateTime.Now}");
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                return _canvas.CreateCanvas(snapShotDto);
            }

            return await dispatcher.InvokeAsync(() => (int?)_canvas.CreateCanvas(snapShotDto));
        }

        public void RenameCanvas(string NewName)
        {

            try
            {
                if (_canvas.Context.ActiveCanvas != null)
                {
                    _canvas.Context.ActiveCanvas.Name = NewName;
                }
            }
            catch (Exception)
            {

            }
        }

        public void RenameCanvas(int canvasId, string newTitle)
        {
            try
            {
                if (_canvas.Context.ActiveCanvas != null)
                {
                    _canvas.Context.ActiveCanvas.Name = newTitle;
                }
            }
            catch (Exception)
            {

            }
        }

        public IEnumerable<CanvasSnapshotDto> Save(string filePath)
        {
            try
            {
                string name = Path.GetFileName(filePath);
                if (_canvas.Context.ActiveCanvas == null)
                {
                    return null;
                }
                if (name != null) RenameCanvas(name);
                CanvasSnapshotDto snapShot = GetActiveSnapshot();
                return [snapShot];

            }
            catch (Exception ex)
            {
                return null;
            }

        }

      

        public void SwitchCanvas()
        {
            throw new NotImplementedException();
        }

        public void SwitchCanvas(int canvasId)
        {
            //throw new NotImplementedException();
        }

        /// <summary>
        /// 获取当前活动画布快照（直接返回含 ILayerData 的 Dto，零拷贝）
        /// </summary>
        public CanvasSnapshotDto GetActiveSnapshot()
        {
            if (_canvas.Context.ActiveCanvas == null)
            {
                return null;
            }
            var snapShot = new CanvasSnapshotDto
            {
                Id = _canvas.Context.ActiveCanvas.Id,
                Name = _canvas.Context.ActiveCanvas.Name,
                Layers = ((DrawingCanvas)_canvas.Context.ActiveCanvas).LayerViewViewModel
                    ?.LayerViewModels
                    .Select(layer => (ILayerData)layer.Model)
                    .ToList() ?? new List<ILayerData>()
            };

            return snapShot;
        }

        /// <summary>
        /// 获取当前活动画布的只读数据视图（零拷贝，供打标卡直接读取，无需 DTO 转换）。
        /// </summary>
        public ICanvasData? GetActiveCanvasData()
        {
            return _canvas.Context.ActiveCanvas as ICanvasData;
        }

        /// <summary>
        /// 获取当前活动画布选中的图形
        /// </summary>
        /// <returns></returns>
        public SelectedSharpsDto GetSelectedSharps()
        {
            /*if (_canvas.Context.ActiveCanvas == null)
            {
                return null;
            }

            var selectedShapes = _canvas.Context.ActiveCanvas.SelectedShapes.Select(shape => shape.Clone()).ToList();
            return new SelectedSharpsDto
            {
                Id = _canvas.Context.ActiveCanvas.Id,
                Name = _canvas.Context.ActiveCanvas.Name,
                Shapes = DrawObjectMapper.MapShapes(selectedShapes)
            };*/
            throw new NotImplementedException();
        }

        /// <summary>
        /// 导入DXF文件
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public async Task<bool> ImportDxfAsync(string filePath)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            await _importer.ImportAsync(filePath, _cts.Token);
            return true;
        }

        private void _importer_OnProgress(double progress)
        {
            eventBus?.Publish(new DxfReportMsgEvent()
            {
                ProgressValue = progress,
                ShowTxt = $"Import Progress: {progress:P2}"
            });
        }
        private void _importer_OnFailed(Exception obj)
        {
            throw new NotImplementedException();
        }

        private async void _importer_OnNewCompleted(CanvasSnapshotDto dto, DXFHelper.Parser.ParseSummary summary)
        {
            string showtxt =
                   $"图元: {dto.Layers.First().Shapes.Count():N0}   " +
                   $"LINE:{summary.Lines}  ARC:{summary.Arcs}  CIRCLE:{summary.Circles}   " +
                   $"解析:{summary.ParseMs:F0}ms  总:{summary.TotalMs:F0}ms";
            Trace.WriteLine($"开始绘制,{showtxt},{DateTime.Now}");
            eventBus?.Publish(new DxfReportMsgEvent()
            {
                ProgressValue = 1.0,
                ShowTxt = showtxt
            });
            // 使用异步版本，映射在后台线程执行，避免阻塞 UI
            await OpenAsync(dto);
            Trace.WriteLine($"结束绘制:{DateTime.Now}");
        }
        /// <summary>
        /// 导出DXF文件
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public async Task<bool> ExportDxfAsync(string filePath)
        {
            var snapshot = GetActiveSnapshot();
            if (snapshot != null)
            {
                await Task.Run(() => DxfExporter.Export(filePath, snapshot));
                return true;
            }
            return false;
        }

        public void UpdateCanvasCenterPoint(double X, double Y)
        {

        }

        public void SetMachineBounds(float width, float height)
        {
            ((DrawingCanvas)_canvas.Context.ActiveCanvas).SetMachineBounds(width, height);
        }

        public void SetGridSize(float width, float height)
        {
            ((DrawingCanvas)_canvas.Context.ActiveCanvas).SetGridSize(width, height);
        }

        public void SetMicroMove(float MicroMoveX, float MicroMoveY)
        {
            ((DrawingCanvas)_canvas.Context.ActiveCanvas).SetMicroMove(MicroMoveX, MicroMoveY);
        }

        public CanvasParaModelDto GetCanvasPara()
        {
            CanvasParaModelDto canvasParaModel = new();
            return canvasParaModel;
            //  throw new NotImplementedException();
        }
        #endregion

        #region Events
        /*public event EventHandler<ActiveCanvasChangedEventArgs>? ActiveCanvasChanged;
        public event EventHandler<CanvasChangedArgs>? CanvasChanged;
        public event EventHandler<CanvasStatusArgs>? CanvasStatusChanged;
        public void NotifyCanvasStatusChanged(CanvasStatusArgs args)
        {
            CanvasStatusChanged?.Invoke(this, args);
        }

        public void NotifyCanvasChanged(CanvasChangedArgs args)
        {
            CanvasChanged?.Invoke(this, args);
        }

        public void NotifyActiveCanvasChanged(ActiveCanvasChangedEventArgs args)
        {
            ActiveCanvasChanged?.Invoke(this, args);
        }*/
        #endregion
    }
}
