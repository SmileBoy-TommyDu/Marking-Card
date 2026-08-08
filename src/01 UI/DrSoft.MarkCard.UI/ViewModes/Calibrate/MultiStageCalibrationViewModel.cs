using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrSoft.Drawing.Contracts;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Model;
using DrSoft.Drawing.Utility;
using DrSoft.MarkCard.Model.Config;
using DrSoft.MarkCard.Model.DTO;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Service;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;

namespace DrSoft.MarkCard.UI.ViewModes.Calibrate
{
    public partial class MultiStageCalibrationViewModel : ObservableObject
    {
        private static readonly PenDto DefaultPen = new PenDto 
        { 
            Color = DrawingColorDto.Black, 
            Width = 0.2f, 
            Style = PenStyleDto.Solid 
        };

        private readonly ILogger<MultiStageCalibrationViewModel> _logger;

        private readonly ScanHeadConfig _scanHeadConfig;

        private readonly CalibrationService _calibrateService;
        private readonly IDrawingService _drawingService;

        public MultiStageCalibrationViewModel(ScanHeadConfig scanHeadConfig)
        {
            _scanHeadConfig = scanHeadConfig;
            _calibrateService = App.GetService<CalibrationService>();
            _calibrationFilePath = Path.GetFileName( scanHeadConfig.HeadFilePath);
            _logger = App.GetService<ILogger<MultiStageCalibrationViewModel>>();

            _drawingService = App.GetService<IDrawingService>();

            EventBus.Instance.Subscribe<BaseEvent<ScanHeadConfig>>(OnScanHeadConfigChanged);
        }

        private void OnScanHeadConfigChanged(BaseEvent<ScanHeadConfig> eventArgs)
        {
            if (eventArgs!=null&&eventArgs.Data != null)
            {
                if (eventArgs.EventName == "scanHeadConfigUpdated")
                {
                    var newConfig = eventArgs.Data;
                    if (newConfig.CardNo == _scanHeadConfig.CardNo && newConfig.ScanHeadNo == _scanHeadConfig.ScanHeadNo)
                    {
                        _scanHeadConfig.HeadFilePath = newConfig.HeadFilePath;
                        CalibrationFilePath = Path.GetFileName(newConfig.HeadFilePath);
                        _logger.LogInformation("接收到新的扫描头配置，更新校正档路径: {FilePath}", newConfig.HeadFilePath);
                    }
                }
            }
            
        }

        [ObservableProperty]
        private double _calibrationArea = 100;

        [ObservableProperty]
        private string _secondaryDataPath = string.Empty;

        private string _secondaryDataContent = string.Empty;
        public string SecondaryDataContent
        {
            get => _secondaryDataContent;
            set => SetProperty(ref _secondaryDataContent, value);
        }

        /// <summary>
        /// 可选的校正阶数列表
        /// </summary>
        public List<uint> AvailableStages { get; } = new() { 3, 5, 7, 9, 11, 17, 23, 33, 65 };

        /// <summary>
        /// 校正阶数
        /// </summary>
        [ObservableProperty]
        private uint _calibrationStage=11;

        [ObservableProperty]
        private string _calibrationFilePath = string.Empty;

        [ObservableProperty]
        private double _graphicsLength = 2;

        [ObservableProperty]
        private bool _isCrossSelected;

        [ObservableProperty]
        private bool _isGridSelected;

        [ObservableProperty]
        private bool _isCircleSelected = true;

        [ObservableProperty]
        private bool _addXYDirection = true;

        [ObservableProperty]
        private double _signDimension = 2;

        [ObservableProperty]
        private ObservableCollection<CalibrationResult> _calibrationDataList = new ObservableCollection<CalibrationResult>();

        private List<(double X, double Y)> _sourcePoints = new();
        private List<(double X, double Y)> _targetPoints = new();

        private void NotifyPointCountMismatch()
        {
            if (_sourcePoints.Count == 0 || _targetPoints.Count == 0 || _sourcePoints.Count == _targetPoints.Count)
                return;

            if (_targetPoints.Count < _sourcePoints.Count)
            {
                EventBus.Instance.Publish(new ToastMessageEvent($"二次元数据点数不足：缺少 {_sourcePoints.Count - _targetPoints.Count} 个点（源点 {_sourcePoints.Count}，目标点 {_targetPoints.Count}）", ToastType.Error));
            }
            else
            {
                EventBus.Instance.Publish(new ToastMessageEvent($"二次元数据点数超出：多出 {_targetPoints.Count - _sourcePoints.Count} 个点（源点 {_sourcePoints.Count}，目标点 {_targetPoints.Count}）", ToastType.Error));
            }
        }

        [RelayCommand]
        private void BrowseSecondaryData()
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "数据文件|*.csv;*.txt;*.xlsx;*.xls",
                Title = "选择二次元数据文件"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                SecondaryDataPath = openFileDialog.SafeFileName;
                var ext = Path.GetExtension(openFileDialog.FileName).ToLowerInvariant();

                try
                {
                    if (ext is ".xlsx" or ".xls")
                    {
                        _targetPoints = ParseExcelTargetPoints(openFileDialog.FileName, ext);
                    }
                    else
                    {
                        SecondaryDataContent = File.ReadAllText(openFileDialog.FileName);
                        _targetPoints = ParseTargetPoints(SecondaryDataContent);
                    }
                    RefreshCalibrationDataList();
                    NotifyPointCountMismatch();
                }
                catch (Exception ex)
                {
                    SecondaryDataContent = string.Empty;
                    _targetPoints.Clear();
                    RefreshCalibrationDataList();
                    EventBus.Instance.Publish(new ToastMessageEvent($"读取二次元数据失败: {ex.Message}", ToastType.Error));
                }
            }
        }

        private List<(double X, double Y)> BuildSourcePoints()
        {
            var points = new List<(double X, double Y)>();

            if (CalibrationStage < 2 || CalibrationArea <= 0)
                return points;

            double gridSize = CalibrationArea / (CalibrationStage - 1);
            double halfArea = CalibrationArea / 2.0;

            for (int j = 0; j < CalibrationStage; j++)
            {
                double y = -halfArea + j * gridSize;
                for (int i = 0; i < CalibrationStage; i++)
                {
                    double x = -halfArea + i * gridSize;
                    points.Add((x, y));
                }
            }

            return points;
        }

        private static List<(double X, double Y)> ParseTargetPoints(string rawText)
        {
            var points = new List<(double X, double Y)>();

            if (string.IsNullOrWhiteSpace(rawText))
                return points;

            using var reader = new StringReader(rawText);
            string? line;
            bool inDataBlock = false;

            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (line.StartsWith(":BEGIN", StringComparison.OrdinalIgnoreCase))
                {
                    inDataBlock = true;
                    continue;
                }

                if (line.StartsWith(":END", StringComparison.OrdinalIgnoreCase))
                    break;

                if (!inDataBlock && line.StartsWith(":"))
                    continue;

                var parts = line.Split(new[] { ',', '\t', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    continue;

                if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                    && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                {
                    points.Add((x, y));
                }
            }

            return points;
        }

        /// <summary>
        /// 解析 Excel 文件（xlsx/xls）中的二次元数据点，读取第一个工作表的前两列作为 X/Y 坐标
        /// </summary>
        private static List<(double X, double Y)> ParseExcelTargetPoints(string filePath, string extension)
        {
            var points = new List<(double X, double Y)>();

            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            IWorkbook workbook = extension == ".xls"
                ? new HSSFWorkbook(fileStream)
                : new XSSFWorkbook(fileStream);

            var sheet = workbook.GetSheetAt(0);
            if (sheet == null)
                return points;

            for (int rowIdx = 0; rowIdx <= sheet.LastRowNum; rowIdx++)
            {
                var row = sheet.GetRow(rowIdx);
                if (row == null || row.Cells.Count < 2)
                    continue;

                var xCell = row.GetCell(0);
                var yCell = row.GetCell(1);
                if (xCell == null || yCell == null)
                    continue;

                if (TryGetCellDouble(xCell, out var x) && TryGetCellDouble(yCell, out var y))
                {
                    points.Add((x, y));
                }
            }

            return points;
        }

        /// <summary>
        /// 尝试从单元格中获取 double 值，支持数值型和字符串型单元格
        /// </summary>
        private static bool TryGetCellDouble(ICell cell, out double value)
        {
            value = 0;
            switch (cell.CellType)
            {
                case CellType.Numeric:
                    value = cell.NumericCellValue;
                    return true;
                case CellType.String:
                    return double.TryParse(cell.StringCellValue?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
                case CellType.Formula:
                    try
                    {
                        value = cell.NumericCellValue;
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                default:
                    return false;
            }
        }

        private void RefreshCalibrationDataList()
        {
            CalibrationDataList.Clear();

            int count = Math.Max(_sourcePoints.Count, _targetPoints.Count);
            for (int i = 0; i < count; i++)
            {
                var source = i < _sourcePoints.Count ? _sourcePoints[i] : ((double X, double Y)?)null;
                var target = i < _targetPoints.Count ? _targetPoints[i] : ((double X, double Y)?)null;

                CalibrationDataList.Add(new CalibrationResult
                {
                    Index = i + 1,
                    SourceX = source?.X,
                    SourceY = source?.Y,
                    TargetX = target?.X,
                    TargetY = target?.Y
                });
            }
        }

        private static DrawPolyLines MakeLine(string name, (double X, double Y) p1, (double X, double Y) p2)
        {
            return new DrawPolyLines(new List<SKPoint>() { new((float)p1.X, (float)p1.Y), new((float)p2.X, (float)p2.Y) });
            
        }

        private static DrawCircle MakeCircle(string name, (double X, double Y) center, double radius)
        {
            float cx = (float)center.X, cy = (float)center.Y, r = (float)radius;
            var circle = new DrawCircle
            {
                Name = name,
                RadiusX = r,
                RadiusY = r,
                Points = new List<SKPoint>(2) { new(cx, cy), new(cx + r, cy) },
            };

            circle.SetRotationCenter(new SKPoint(cx, cy));
            return circle;
        }

        private List<DrawObject> CreateCalibrationGraphics()
        {
            var graphics = new List<DrawObject>();

            if (CalibrationStage < 2 || CalibrationArea <= 0)
                return graphics;

            double gridSize = CalibrationArea / (_calibrationStage - 1);
            double halfArea = CalibrationArea / 2.0;

            // 十字图形
            if (IsCrossSelected)
            {
                double crossSize = GraphicsLength / 2.0;
                for (int i = 0; i < _calibrationStage; i++)
                {
                    for (int j = 0; j < _calibrationStage; j++)
                    {
                        double x = -halfArea + i * gridSize;
                        double y = -halfArea + j * gridSize;
                        graphics.Add(MakeLine($"十字_水平_{i}_{j}", (x - crossSize, y), (x + crossSize, y)));
                        graphics.Add(MakeLine($"十字_垂直_{i}_{j}", (x, y - crossSize), (x, y + crossSize)));
                    }
                }
            }

            // 网格图形
            if (IsGridSelected)
            {
                for (int i = 0; i < _calibrationStage; i++)
                {
                    double y = -halfArea + i * gridSize;
                    graphics.Add(MakeLine($"网格_水平_{i}", (-halfArea, y), (halfArea, y)));
                }
                for (int j = 0; j < _calibrationStage; j++)
                {
                    double x = -halfArea + j * gridSize;
                    graphics.Add(MakeLine($"网格_垂直_{j}", (x, -halfArea), (x, halfArea)));
                }
            }

            // 圆形图形
            if (IsCircleSelected)
            {
                double radius = GraphicsLength / 2.0;
                for (int i = 0; i < _calibrationStage; i++)
                {
                    for (int j = 0; j < _calibrationStage; j++)
                    {
                        double x = -halfArea + i * gridSize;
                        double y = -halfArea + j * gridSize;
                        graphics.Add(MakeCircle($"圆_{i}_{j}", (x, y), radius));
                    }
                }
            }

            // XY 方向标识
            if (AddXYDirection)
            {
                double arrowSize = SignDimension;
                double axisLength = halfArea;
                double halfSize = arrowSize / 2.0;
                double offset = GraphicsLength > 1 ? GraphicsLength * 2 : 2;
                double xCenter = axisLength;
                double yCenter = axisLength;

                graphics.Add(MakeLine("X标识_斜线1", (xCenter - halfSize + offset, -halfSize), (xCenter + halfSize + offset, halfSize)));
                graphics.Add(MakeLine("X标识_斜线2", (xCenter - halfSize + offset, halfSize), (xCenter + halfSize + offset, -halfSize)));
                graphics.Add(MakeLine("Y标识_左上斜线", (-halfSize, yCenter + halfSize + offset), (0, yCenter + offset)));
                graphics.Add(MakeLine("Y标识_右上斜线", (halfSize, yCenter + halfSize + offset), (0, yCenter + offset)));
                graphics.Add(MakeLine("Y标识_垂直线", (0, yCenter + offset), (0, yCenter - halfSize + offset)));
            }

            return graphics;
        }

        private int canvasesId = -1;

        private CanvasSnapshotDto canvasSnapshot = null;

        [RelayCommand]
        private async Task CreateGraphics()
        {
            if (canvasesId > -1)
            {
                _drawingService.CanvasService.Close(canvasesId);
            }

            _sourcePoints = new List<(double X, double Y)>();
            _targetPoints = new List<(double X, double Y)>();
            SecondaryDataPath = "";
            _sourcePoints = BuildSourcePoints();
            RefreshCalibrationDataList();
            NotifyPointCountMismatch();

            // 生成图形逻辑（直接创建 DrawingLayer，零映射）
            var calibrationLayer = new DrawingLayer
            {
                IsVisible = true,
                Name = "图层1",
            };
            calibrationLayer.AddShapes(CreateCalibrationGraphics());

            canvasSnapshot = new CanvasSnapshotDto
            {
                Id = 999,
                Name = "校正图形",
                Layers = new List<ILayerData> { calibrationLayer }
            };

            var result = _drawingService.CanvasService.Open(canvasSnapshot);

            if(result.HasValue)
            {
                canvasesId = result.Value;

                // 校正图形创建后，将全局加工参数绑定到所有校正图形
                await BindGlobalParamsToCalibrationGraphicsAsync(canvasesId);
            }
            else
            {
                EventBus.Instance.Publish(new ToastMessageEvent("校正图形创建失败", ToastType.Error));
            }
        }

        /// <summary>
        /// 将全局加工参数绑定到校正画布中的所有图形
        /// </summary>
        private async Task BindGlobalParamsToCalibrationGraphicsAsync(int canvasId)
        {
            var markParamService = App.GetService<MarkParamService>();
            var canvasData = _drawingService.CanvasService.GetActiveCanvasData();
            if (canvasData == null) return;

            var shapeIds = canvasData.Layers
                .SelectMany(l => l.Shapes)
                .SelectMany(s => CollectAllIds(s))
                .Distinct()
                .ToList();

            if (shapeIds.Count > 0)
            {
                await markParamService.BindGlobalParametersToEntitiesAsync(canvasId, shapeIds);
            }
        }

        /// <summary>
        /// 递归收集图形及其子图形的所有 UId
        /// </summary>
        private static IEnumerable<int> CollectAllIds(IShapeData shape)
        {
            yield return shape.UId;
            foreach (var child in shape.ChildShapes)
            {
                foreach (var id in CollectAllIds(child))
                    yield return id;
            }
        }

        private async Task<bool> LoadCalibrateGraphicsAsync()
        {
            // 加载校正图形逻辑
            
            var calibrateParam = _calibrateService.GetCalibrationProcessParam();
            var _markParamService = App.GetService<MarkParamService>();
            var _markService = App.GetService<MarkService>();

            var param = calibrateParam.DeepCopy();

            param.JumpDelay = calibrateParam.JumpDelay*1000 ;
            param.PolyDelay = calibrateParam.PolyDelay*1000 ;
            param.MarkDelay = calibrateParam.MarkDelay * 1000 ;
            param.DotDuration = calibrateParam.DotDuration * 1000;
            param.LaserOffDelay = calibrateParam.LaserOffDelay * 1000;
            param.LaserOnDelay = calibrateParam.LaserOnDelay * 1000;

            MarkingJobDto markData = await _markParamService.BuildMarkingJobAsync(RuntimeContext.ActiveCanvasId);
            if (markData != null)
            {
                for (int i = 0; i < markData.Shapes.Count; i++)
                {
                    {
                        

                        //获取打标图形数据的UId将校准参数覆盖
                        var uid = markData.Shapes[i].UId;
                        if (markData.ParameterMap.ContainsKey(uid))
                        {
                            markData.ParameterMap[uid] = param;
                        }
                        else
                        {
                            markData.ParameterMap.Add(uid, param);
                        }
                    }                    
                }

                var err = _markService.SetOffsetScale(_scanHeadConfig.CardNo, _scanHeadConfig.ScanHeadNo, 0, 0, 0, 1, 1);
                if (err != Model.MarkErrorCode.None)
                {
                    EventBus.Instance.Publish(new ToastMessageEvent($"初始化偏移缩放失败: {err.GetDescription()}", ToastType.Error));
                    return false;
                }
                var errCode = _markService.LoadMarkData(_scanHeadConfig.CardNo, markData);
                if (errCode == Model.MarkErrorCode.None)
                {
                    EventBus.Instance.Publish(new ToastMessageEvent("下发打标数据成功", ToastType.Info));
                }
                else
                {
                    EventBus.Instance.Publish(new ToastMessageEvent($"下发打标数据失败: {errCode.GetDescription()}", ToastType.Error));
                }

                return true;

            }
            else
            {
                EventBus.Instance.Publish(new ToastMessageEvent("获取打标数据失败", ToastType.Error));
                return false;
            }
        }

        [RelayCommand]
        private async Task MarkGraphics()
        {
            if (await LoadCalibrateGraphicsAsync())
            {
                var _markService = App.GetService<MarkService>();
                _markService.StartMarking(_scanHeadConfig.CardNo);
            }
        }

        [RelayCommand]
        private void ExecuteAndSave()
        {
            //获取程序运行目录下的校正目录（calibrationFile），如果目录不存在，创建
            var calibrationDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "calibrationFile");
            if (!Directory.Exists(calibrationDir))
            {
                Directory.CreateDirectory(calibrationDir);
            }
            var newCalibrationFilePath = Path.Combine(calibrationDir, $"{DateTime.Now:yyyyMMdd_HHmmss}.ct5");

            if (string.IsNullOrEmpty(_scanHeadConfig.HeadFilePath))
            {
                EventBus.Instance.Publish(new ToastMessageEvent("没有设置校正档，请先设置校正档", ToastType.Error));
                return;
            }

            if (_sourcePoints == null || _sourcePoints.Count < 3)
            {
                EventBus.Instance.Publish(new ToastMessageEvent("请先生成校正图形",ToastType.Error));
                return;
            }

            if(_targetPoints==null|| _targetPoints.Count < 3)
            {
                EventBus.Instance.Publish(new ToastMessageEvent("未导入二次元数据", ToastType.Error));
                return;
            }

            if(_sourcePoints.Count!= _targetPoints.Count)
            {
                EventBus.Instance.Publish(new ToastMessageEvent($"校正源点和目标点数量不匹配（源点 {_sourcePoints.Count}，目标点 {_targetPoints.Count}）", ToastType.Error));
                return;
            }

            var errCode =  _calibrateService.CreateCalibrationFile(
                _scanHeadConfig.HeadFilePath,
                newCalibrationFilePath,
                _sourcePoints.Select(g=>g.X).ToArray(),
                _sourcePoints.Select(g=>g.Y).ToArray(),
                _targetPoints.Select(g=>g.X).ToArray(),
                _targetPoints.Select(g=>g.Y).ToArray());
           
            if(errCode== Model.MarkErrorCode.None)
            {
                _logger.LogInformation("校正文件创建成功: {FilePath}", newCalibrationFilePath);
                errCode = _calibrateService.LoadCalibrationFile(_scanHeadConfig.CardNo, _scanHeadConfig.ScanHeadNo==1? newCalibrationFilePath:null, _scanHeadConfig.ScanHeadNo==2?newCalibrationFilePath:null);

                if (errCode == Model.MarkErrorCode.None)
                {
                    _scanHeadConfig.HeadFilePath = newCalibrationFilePath;
                    _logger.LogInformation("校正文件加载卡{CardNo}头{ScanHeadNo}成功: {FilePath}", _scanHeadConfig.CardNo, _scanHeadConfig.ScanHeadNo, newCalibrationFilePath);
                    EventBus.Instance.Publish(new BaseEvent<ScanHeadConfig> { EventName = "scanHeadConfigUpdated", Data = _scanHeadConfig });
                    EventBus.Instance.Publish(new ToastMessageEvent($"校正文件创建成功: {newCalibrationFilePath}", ToastType.Info));

                    //CalibrationFilePath = Path.GetFileName(newCalibrationFilePath);
                }
                else
                {
                    _logger.LogError("校正文件创建成功，但加载失败: {ErrorCode} {Message}", errCode, errCode.GetDescription());
                    EventBus.Instance.Publish(new ToastMessageEvent($"校正文件加载失败: {errCode.GetDescription()}", ToastType.Error));
                }
            }
            else
            {
                _logger.LogError("校正文件创建失败: {ErrorCode} {Message}", errCode, errCode.GetDescription());
                EventBus.Instance.Publish(new ToastMessageEvent($"校正文件创建失败: {errCode.GetDescription()}", ToastType.Error));
                return;
            }

            
        }

        public class CalibrationResult
        {
            public int Index { get; set; }
            public double? SourceX { get; set; }
            public double? SourceY { get; set; }
            public double? TargetX { get; set; }
            public double? TargetY { get; set; }
        }

        public List<(double X, double Y)> GetSourcePointsForAnalysis()
        {
            return BuildSourcePoints();
        }

        public List<(double X, double Y)> GetTargetPointsForAnalysis()
        {
            return _targetPoints.ToList();
        }
    }
}
