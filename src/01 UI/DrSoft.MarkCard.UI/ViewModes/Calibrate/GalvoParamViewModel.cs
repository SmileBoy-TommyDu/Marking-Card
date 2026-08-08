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
using SkiaSharp;

namespace DrSoft.MarkCard.UI.ViewModes.Calibrate
{
    public partial class GalvoParamViewModel : ObservableObject
    {
        private ScanHeadConfig _headConfig;

        private readonly MarkService _markService;
        private readonly CalibrationService _calibrationService;
        private readonly IDrawingService _drawingService;
        private int _canvasesId = -1;

        public GalvoParamViewModel(ScanHeadConfig headConfig)
        {
            _headConfig = headConfig;
            _markService = App.GetRequiredService<MarkService>();
            _calibrationService = App.GetRequiredService<CalibrationService>();
            _drawingService = App.GetRequiredService<IDrawingService>();
            _xAxisReverse = _headConfig.MirrorX;
            _yAxisReverse = _headConfig.MirrorY;
            _xYAxisReverse = _headConfig.ReverseXY;
        }

        [ObservableProperty]
        private bool _xAxisReverse;

        partial void OnXAxisReverseChanged(bool value)
        {
            _headConfig.MirrorX = value;
        }



        [ObservableProperty]
        private bool _yAxisReverse;
        partial void OnYAxisReverseChanged(bool value)
        {
            _headConfig.MirrorY = value;
        }


        [ObservableProperty]
        private bool _xYAxisReverse;
        partial void OnXYAxisReverseChanged(bool value)
        {
                _headConfig.ReverseXY = value;
        }

        [ObservableProperty]
        private double _lineLength;

        [ObservableProperty]
        private double _measuredLength;

        [ObservableProperty]
        private double _scaleFactor;

        [RelayCommand]
        private void DryRun()
        {
            // 空走逻辑
        }

        [RelayCommand]
        private void CreateGraphics()
        {
            // 关闭旧画布
            if (_canvasesId > -1)
            {
                _drawingService.CanvasService.Close(_canvasesId);
            }

            double halfSize = 50;   // 幅面 100mm / 2
            double arrowSize = 8;   // 箭头大小（mm）
            double letterSize = 4;  // 标签字符大小

            var graphics = new List<DrawObject>();

            // 辅助方法：创建折线 DrawObject
            DrawPolyLines MakeLine(string name, params (double X, double Y)[] pts)
            {
                var skPts = pts.Select(p => new SKPoint((float)p.X, (float)p.Y)).ToList();
                return new DrawPolyLines(skPts) { Name = name };
            }

            // ── X 轴主线：(-50, 0) → (50-arrowSize, 0) ──
            graphics.Add(MakeLine("X轴主线", (-halfSize, 0), (halfSize, 0)));

            // ── X 轴箭头：三角形 ──
            double arrowTipX = halfSize;
            double arrowBaseX = halfSize - arrowSize;
            double arrowHalfH = 5;
            graphics.Add(MakeLine("X轴箭头",
               (arrowBaseX, arrowHalfH),(arrowTipX, 0), (arrowBaseX, -arrowHalfH)));

            // ── Y 轴主线：(0, -50) → (0, 50-arrowSize) ──
            graphics.Add(MakeLine("Y轴主线", (0, -halfSize), (0, halfSize)));

            // ── Y 轴箭头：三角形 ──
            double arrowTipY = halfSize;
            double arrowBaseY = halfSize - arrowSize;
            double arrowHalfW = 5;
            graphics.Add(MakeLine("Y轴箭头", (arrowHalfW, arrowBaseY),(0, arrowTipY), (-arrowHalfW, arrowBaseY)));

            // ── X+ 标签：放在 X 轴箭头下方，用线条模拟 "X+" ──
            double xPos = halfSize-9;
            double yPos = -10;
            graphics.Add(MakeLine("X+标记",
                // X 字母：交叉线
                (xPos - letterSize / 2, yPos - letterSize / 2),
                (xPos + letterSize / 2, yPos + letterSize / 2)));
            graphics.Add(MakeLine("X+标记2",
                (xPos + letterSize / 2, yPos - letterSize / 2),
                (xPos - letterSize / 2, yPos + letterSize / 2)));
            graphics.Add(MakeLine("X+号",
                // + 号
                (xPos + letterSize, yPos),
                (xPos + letterSize * 2, yPos)));
            graphics.Add(MakeLine("X+号2",
                (xPos + letterSize * 1.5, yPos - letterSize / 2),
                (xPos + letterSize * 1.5, yPos + letterSize / 2)));

            // ── Y+ 标签：放在 Y 轴箭头右方，用线条模拟 "Y+" ──
            double yLabelX = 10;
            double yLabelY = halfSize-3;
            graphics.Add(MakeLine("Y+标记",
                // Y 字母：V 形开口朝上 + 竖线向下
                (yLabelX - letterSize / 2, yLabelY + letterSize / 2),
                (yLabelX, yLabelY),
                (yLabelX + letterSize / 2, yLabelY + letterSize / 2)));
            graphics.Add(MakeLine("Y+标记2",
                (yLabelX, yLabelY),
                (yLabelX, yLabelY - letterSize / 2)));
            graphics.Add(MakeLine("Y+号",
                // + 号
                (yLabelX + letterSize, yLabelY),
                (yLabelX + letterSize * 2, yLabelY)));
            graphics.Add(MakeLine("Y+号2",
                (yLabelX + letterSize * 1.5, yLabelY - letterSize / 2),
                (yLabelX + letterSize * 1.5, yLabelY + letterSize / 2)));

            // 创建图层并打开画布
            var layer = new DrawingLayer { IsVisible = true, Name = "振镜坐标系" };
            layer.AddShapes(graphics);

            var snapshot = new CanvasSnapshotDto
            {
                Id = 998,
                Name = "振镜坐标系",
                Layers = new List<ILayerData> { layer }
            };

            var result = _drawingService.CanvasService.Open(snapshot);
            if (result.HasValue)
            {
                _canvasesId = result.Value;
            }
            else
            {
                EventBus.Instance.Publish(new ToastMessageEvent("坐标系图形创建失败", ToastType.Error));
            }
        }

        private async Task<bool> LoadCalibrateGraphicsAsync()
        {
            // 加载校正图形逻辑

            var calibrateParam = _calibrationService.GetCalibrationProcessParam();
            var _markParamService = App.GetService<MarkParamService>();
            var _markService = App.GetService<MarkService>();

            var param = calibrateParam.DeepCopy();

            param.JumpDelay = calibrateParam.JumpDelay * 1000;
            param.PolyDelay = calibrateParam.PolyDelay * 1000;
            param.MarkDelay = calibrateParam.MarkDelay * 1000;
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
                var err = _markService.SetOffsetScale(_headConfig.CardNo, _headConfig.ScanHeadNo, 0, 0, 0, 1, 1);
                if (err != Model.MarkErrorCode.None)
                {
                    EventBus.Instance.Publish(new ToastMessageEvent($"初始化偏移缩放失败: {err.GetDescription()}", ToastType.Error));
                    return false;
                }
                var errCode = _markService.LoadMarkData(_headConfig.CardNo, markData);
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
                _markService.StartMarking(_headConfig.CardNo);
            }
        }

        [RelayCommand]
        private void DryRunScale()
        {
            // 比例因子空走逻辑
        }

        [RelayCommand]
        private void MarkGraphicsScale()
        {
            // 比例因子标刻图形逻辑
        }

        [RelayCommand]
        private void ApplyScale()
        {
            if (LineLength > 0 && MeasuredLength > 0)
            {
                // 新比例因子 = 旧比例因子 * 理论长度 / 测量长度
                ScaleFactor = ScaleFactor * LineLength / MeasuredLength;
            }
        }

        [RelayCommand]
        private void ClearScale()
        {
            LineLength = 0;
            MeasuredLength = 0;
            ScaleFactor = 0;
        }

        public Action? CloseAction { get; set; }

        [RelayCommand]
        private void Cancel()
        {
            CloseAction?.Invoke();
        }

        [RelayCommand]
        private void Save()
        {
            var config = App.GetService<DrSoft.MarkCard.Model.Config.Config>();
            config?.SaveToFile();

            _markService.SetTransformMatrix(_headConfig.CardNo, _headConfig.CardNo, 1, 0, 0, 1);
            
        }
    }
}
