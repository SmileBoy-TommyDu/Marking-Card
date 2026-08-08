using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrSoft.Drawing.Contracts;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Model;
using DrSoft.Drawing.Utility;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.Config;
using DrSoft.MarkCard.Model.DTO;
using DrSoft.MarkCard.Service;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace DrSoft.MarkCard.UI.ViewModes.Calibrate
{
    public partial class BarrelDistortionViewModel : ObservableObject
    {
        private readonly ILogger<BarrelDistortionViewModel> _logger;
        private readonly ScanHeadConfig _headConfig;
        private readonly CalibrationService _calibrationService;
        private readonly IDrawingService _drawingService;

        private int _canvasesId = -1;
        private CanvasSnapshotDto _canvasSnapshot;

        public BarrelDistortionViewModel(ScanHeadConfig headConfig)
        {
            _headConfig = headConfig;
            _calibrationService = App.GetService<CalibrationService>();
            _drawingService = App.GetService<IDrawingService>();
            _logger = App.GetService<ILogger<BarrelDistortionViewModel>>();
        }

        [ObservableProperty]
        private double _calibrationArea = 200;

        [ObservableProperty]
        private double _width3;

        [ObservableProperty]
        private double _width2;

        [ObservableProperty]
        private double _width1;

        [ObservableProperty]
        private double _height1;

        [ObservableProperty]
        private double _height2;

        [ObservableProperty]
        private double _height3;

        [ObservableProperty]
        private bool _allowModifyData;

        #region 图形创建

        private static DrawPolyLines MakeLine(string name, (double X, double Y) p1, (double X, double Y) p2)
        {
            return new DrawPolyLines(new List<SKPoint>() { new((float)p1.X, (float)p1.Y), new((float)p2.X, (float)p2.Y) });
        }

        /// <summary>
        /// 创建田字格校正图形：3条横线（下/中/上）+ 3条竖线（左/中/右）
        /// </summary>
        private List<DrawObject> CreateBarrelDistortionGraphics()
        {
            var graphics = new List<DrawObject>();

            if (CalibrationArea <= 0)
                return graphics;

            double halfArea = CalibrationArea / 2.0;

            // 3条横线（从下到上）：y = -halfArea, 0, +halfArea
            graphics.Add(MakeLine("横线_下", (-halfArea, -halfArea), (halfArea, -halfArea)));
            graphics.Add(MakeLine("横线_中", (-halfArea, 0), (halfArea, 0)));
            graphics.Add(MakeLine("横线_上", (-halfArea, halfArea), (halfArea, halfArea)));

            // 3条竖线（从左到右）：x = -halfArea, 0, +halfArea
            graphics.Add(MakeLine("竖线_左", (-halfArea, -halfArea), (-halfArea, halfArea)));
            graphics.Add(MakeLine("竖线_中", (0, -halfArea), (0, halfArea)));
            graphics.Add(MakeLine("竖线_右", (halfArea, -halfArea), (halfArea, halfArea)));

            return graphics;
        }

        [RelayCommand]
        private async Task CreateGraphics()
        {
            if (_canvasesId > -1)
            {
                _drawingService.CanvasService.Close(_canvasesId);
            }

            var calibrationLayer = new DrawingLayer
            {
                IsVisible = true,
                Name = "桶形校正图形",
            };
            calibrationLayer.AddShapes(CreateBarrelDistortionGraphics());

            _canvasSnapshot = new CanvasSnapshotDto
            {
                Id = 999,
                Name = "桶形校正图形",
                Layers = new List<ILayerData> { calibrationLayer }
            };

            var result = _drawingService.CanvasService.Open(_canvasSnapshot);

            if (result.HasValue)
            {
                _canvasesId = result.Value;
                await BindGlobalParamsToCalibrationGraphicsAsync(_canvasesId);
                EventBus.Instance.Publish(new ToastMessageEvent("桶形校正图形生成成功", ToastType.Info));
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

        private static IEnumerable<int> CollectAllIds(IShapeData shape)
        {
            yield return shape.UId;
            foreach (var child in shape.ChildShapes)
            {
                foreach (var id in CollectAllIds(child))
                    yield return id;
            }
        }

        #endregion

        #region 标刻图形

        /// <summary>
        /// 加载校正图形打标数据，覆盖校正工艺参数后下发到打标卡
        /// </summary>
        private async Task<bool> LoadCalibrateGraphicsAsync()
        {
            var calibrateParam = _calibrationService.GetCalibrationProcessParam();
            var markParamService = App.GetService<MarkParamService>();
            var markService = App.GetService<MarkService>();

            var param = calibrateParam.DeepCopy();

            param.JumpDelay = calibrateParam.JumpDelay * 1000;
            param.PolyDelay = calibrateParam.PolyDelay * 1000;
            param.MarkDelay = calibrateParam.MarkDelay * 1000;
            param.DotDuration = calibrateParam.DotDuration * 1000;
            param.LaserOffDelay = calibrateParam.LaserOffDelay * 1000;
            param.LaserOnDelay = calibrateParam.LaserOnDelay * 1000;

            MarkingJobDto markData = await markParamService.BuildMarkingJobAsync(RuntimeContext.ActiveCanvasId);
            if (markData != null)
            {
                for (int i = 0; i < markData.Shapes.Count; i++)
                {
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

                var err = markService.SetOffsetScale(_headConfig.CardNo, _headConfig.ScanHeadNo, 0, 0, 0, 1, 1);
                if (err != MarkErrorCode.None)
                {
                    EventBus.Instance.Publish(new ToastMessageEvent($"初始化偏移缩放失败: {err.GetDescription()}", ToastType.Error));
                    return false;
                }

                var errCode = markService.LoadMarkData(_headConfig.CardNo, markData);
                if (errCode == MarkErrorCode.None)
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
                var markService = App.GetService<MarkService>();
                markService.StartMarking(_headConfig.CardNo);
            }
        }

        #endregion

        #region 执行校正

        /// <summary>
        /// 执行桶形校正：将测量的田字格宽高数据下发到打标卡
        /// widthParam: [宽1(下), 宽2(中), 宽3(上)]
        /// heightParam: [高1(左), 高2(中), 高3(右)]
        /// </summary>
        [RelayCommand]
        private void ExecuteAndSave()
        {
            if (Width1 <= 0 || Width2 <= 0 || Width3 <= 0 ||
                Height1 <= 0 || Height2 <= 0 || Height3 <= 0)
            {
                EventBus.Instance.Publish(new ToastMessageEvent("请输入有效的校正数据（所有值需大于0）", ToastType.Error));
                return;
            }

            try
            {
                var markService = App.GetService<MarkService>();
                var widthParam = new double[] { Width1, Width2, Width3 };
                var heightParam = new double[] { Height1, Height2, Height3 };

                var errCode = markService.SetBarrelCorrection(_headConfig.CardNo, CalibrationArea, CalibrationArea, widthParam, heightParam);

                if (errCode == MarkErrorCode.None)
                {
                    _logger?.LogInformation("桶形校正设置成功: 卡{CardNo}", _headConfig.CardNo);
                    EventBus.Instance.Publish(new ToastMessageEvent("桶形校正设置成功", ToastType.Info));
                }
                else
                {
                    _logger?.LogError("桶形校正设置失败: {ErrorCode} {Message}", errCode, errCode.GetDescription());
                    EventBus.Instance.Publish(new ToastMessageEvent($"桶形校正设置失败: {errCode.GetDescription()}", ToastType.Error));
                }
            }
            catch (NotSupportedException ex)
            {
                _logger?.LogError(ex, "当前打标卡不支持桶形校正");
                EventBus.Instance.Publish(new ToastMessageEvent($"当前打标卡不支持桶形校正: {ex.Message}", ToastType.Error));
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "桶形校正异常");
                EventBus.Instance.Publish(new ToastMessageEvent($"桶形校正异常: {ex.Message}", ToastType.Error));
            }
        }

        #endregion

        [RelayCommand]
        private void DryRun()
        {
            // 空走逻辑
        }

        [RelayCommand]
        private void ToggleAllowModifyData()
        {
            AllowModifyData = !AllowModifyData;
        }

        [RelayCommand]
        private void Apply()
        {
            // 应用校正数据逻辑
        }

        [RelayCommand]
        private void Clear()
        {
            Width1 = 0;
            Width2 = 0;
            Width3 = 0;
            Height1 = 0;
            Height2 = 0;
            Height3 = 0;
        }

        [RelayCommand]
        private void StopProcess()
        {
            // 停止加工逻辑
        }
    }
}
