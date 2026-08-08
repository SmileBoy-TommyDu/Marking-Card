using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrSoft.Drawing.Event;
using Microsoft.Win32;

namespace DrSoft.MarkCard.UI.ViewModes.Calibrate
{
    public partial class CalibrationAnalysisViewModel : ObservableObject
    {
        private const double AxisXLeft = 50;
        private const double PlotXLeft = 70;
        private const double PlotXRightPadding = 20;
        private const double AxisYTop = 20;
        private const double AxisYBottomTick = 270;
        private const double AxisYBottomLine = 280;

        private List<(double X, double Y)> _targetPoints = new();

        [ObservableProperty]
        private string _errorCalibrationFilePath = string.Empty;

        [ObservableProperty]
        private double _totalArea = 100;

        [ObservableProperty]
        private double _calibrationStage = 11;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ErrorUnit))]
        [NotifyPropertyChangedFor(nameof(YAxisTitle))]
        private double _magnification = 1000;

        [ObservableProperty]
        private int _selectedDirectionIndex;

        [ObservableProperty]
        private double _chartViewportWidth = 760;

        /// <summary>
        /// 校正阶数可选项
        /// </summary>
        public List<double> AvailableStages { get; } = new() { 3, 5, 7, 9, 11, 17, 23, 33, 65 };

        /// <summary>
        /// 放大倍数可选项
        /// </summary>
        public List<double> AvailableMagnifications { get; } = new() { 1, 1000 };

        /// <summary>
        /// 误差单位：放大倍数 >= 1000 时显示 μm，否则为 mm
        /// </summary>
        public string ErrorUnit => Magnification >= 1000 ? "μm" : "mm";

        /// <summary>
        /// X/Y 方向图的 Y 轴标题（包含单位）
        /// </summary>
        public string YAxisTitle => $"误差({ErrorUnit})";

        /// <summary>
        /// X/Y 方向图的 X 轴标题
        /// </summary>
        public string XAxisTitle => "点位";

        /// <summary>
        /// 热力图 X 轴标题（坐标，mm）
        /// </summary>
        public string HeatmapXAxisTitle => "X(mm)";

        /// <summary>
        /// 热力图 Y 轴标题（坐标，mm）
        /// </summary>
        public string HeatmapYAxisTitle => "Y(mm)";

        public double AxisXRight => Math.Max(AxisXLeft + 10, ChartViewportWidth - 20);

        private double PlotXRange => Math.Max(120, AxisXRight - PlotXLeft - PlotXRightPadding);

        public double PlotXRangeValue => PlotXRange;

        partial void OnChartViewportWidthChanged(double value)
        {
            OnPropertyChanged(nameof(AxisXRight));
            OnPropertyChanged(nameof(PlotXRangeValue));

            if (value <= 0)
                return;

            if (XDirectionDataPoints.Count > 0)
                UpdateChartFromData(XDirectionDataPoints, XDirectionPlotPoints, XDirectionXAxisTicks, XDirectionYAxisTicks);

            if (YDirectionDataPoints.Count > 0)
                UpdateChartFromData(YDirectionDataPoints, YDirectionPlotPoints, YDirectionXAxisTicks, YDirectionYAxisTicks);

            if (_heatmapGrid != null)
                RenderHeatmapFromGrid();
        }

        // 原始数据点（用于刻度计算）
        [ObservableProperty]
        private ObservableCollection<DataPoint> _xDirectionDataPoints = new();

        [ObservableProperty]
        private ObservableCollection<DataPoint> _yDirectionDataPoints = new();

        // 映射到 Canvas 的绘图点
        [ObservableProperty]
        private ObservableCollection<DataPoint> _xDirectionPlotPoints = new();

        [ObservableProperty]
        private ObservableCollection<DataPoint> _yDirectionPlotPoints = new();

        // 各图独立刻度
        [ObservableProperty]
        private ObservableCollection<AxisTick> _xDirectionXAxisTicks = new();

        [ObservableProperty]
        private ObservableCollection<AxisTick> _xDirectionYAxisTicks = new();

        [ObservableProperty]
        private ObservableCollection<AxisTick> _yDirectionXAxisTicks = new();

        [ObservableProperty]
        private ObservableCollection<AxisTick> _yDirectionYAxisTicks = new();

        [ObservableProperty]
        private ImageSource? _heatmapImageSource;

        [ObservableProperty]
        private ObservableCollection<AxisTick> _heatmapXAxisTicks = new();

        [ObservableProperty]
        private ObservableCollection<AxisTick> _heatmapYAxisTicks = new();

        [ObservableProperty]
        private string _heatmapMaxLabel = string.Empty;

        [ObservableProperty]
        private string _heatmapHoverLabel = string.Empty;

        [ObservableProperty]
        private double _heatmapHoverCanvasX;

        [ObservableProperty]
        private double _heatmapHoverCanvasY;

        [ObservableProperty]
        private Visibility _heatmapHoverVisibility = Visibility.Collapsed;

        [RelayCommand]
        private void Import()
        {
            try
            {
                var openFileDialog = new OpenFileDialog
                {
                    Filter = "数据文件|*.csv;*.txt",
                    Title = "选择误差校正文件"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    ErrorCalibrationFilePath = openFileDialog.SafeFileName;
                    var raw = File.ReadAllText(openFileDialog.FileName);
                    _targetPoints = ParseTargetPoints(raw);
                    Calculate();
                }
            }
            catch (Exception ex)
            {
                EventBus.Instance.Publish(new ToastMessageEvent($"导入文件失败: {ex.Message}", ToastType.Error));
            }
        }

        [RelayCommand]
        private void Calculate()
        {
            XDirectionDataPoints.Clear();
            YDirectionDataPoints.Clear();

            var sourcePoints = BuildSourcePoints();
            if (sourcePoints.Count == 0 || _targetPoints.Count == 0 || TotalArea <= 0)
            {
                ClearCharts();
                return;
            }

            if (sourcePoints.Count != _targetPoints.Count)
            {
                EventBus.Instance.Publish(new ToastMessageEvent("导入二次元数据阶数与设置阶数不匹配",ToastType.Error));
                return;
            }

            int count = Math.Min(sourcePoints.Count, _targetPoints.Count);
            double scale = Magnification <= 0 ? 1 : Magnification;

            for (int i = 0; i < count; i++)
            {
                var source = sourcePoints[i];
                var target = _targetPoints[i];

                double dx = target.X - source.X;
                double dy = target.Y - source.Y;

                XDirectionDataPoints.Add(new DataPoint
                {
                    X = i + 1,
                    Y = dx * scale,
                    ToolTip = $"点位: {i + 1}\nX误差: {(dx * scale):F4} {ErrorUnit}"
                });
                YDirectionDataPoints.Add(new DataPoint
                {
                    X = i + 1,
                    Y = dy * scale,
                    ToolTip = $"点位: {i + 1}\nY误差: {(dy * scale):F4} {ErrorUnit}"
                });
            }

            UpdateChartFromData(XDirectionDataPoints, XDirectionPlotPoints, XDirectionXAxisTicks, XDirectionYAxisTicks);
            UpdateChartFromData(YDirectionDataPoints, YDirectionPlotPoints, YDirectionXAxisTicks, YDirectionYAxisTicks);
            UpdateHeatmap(sourcePoints, count, scale);
        }

     

        private void ClearCharts()
        {
            XDirectionPlotPoints.Clear();
            YDirectionPlotPoints.Clear();
            XDirectionXAxisTicks.Clear();
            XDirectionYAxisTicks.Clear();
            YDirectionXAxisTicks.Clear();
            YDirectionYAxisTicks.Clear();
            HeatmapImageSource = null;
            HeatmapXAxisTicks.Clear();
            HeatmapYAxisTicks.Clear();
            HeatmapHoverLabel = string.Empty;
            HeatmapHoverVisibility = Visibility.Collapsed;
            _heatmapGrid = null;
        }

        private void UpdateChartFromData(
            ObservableCollection<DataPoint> sourceData,
            ObservableCollection<DataPoint> plotData,
            ObservableCollection<AxisTick> xTicks,
            ObservableCollection<AxisTick> yTicks)
        {
            plotData.Clear();
            xTicks.Clear();
            yTicks.Clear();

            if (sourceData.Count == 0)
                return;

            double minX = sourceData.Min(p => p.X);
            double maxX = sourceData.Max(p => p.X);
            double minY = sourceData.Min(p => p.Y);
            double maxY = sourceData.Max(p => p.Y);

            if (Math.Abs(maxX - minX) < 1e-12)
            {
                minX -= 1;
                maxX += 1;
            }

            if (Math.Abs(maxY - minY) < 1e-12)
            {
                minY -= 1;
                maxY += 1;
            }

            // X 轴刻度：标签水平居中对齐刻度线
            // 估算单个字符宽度（FontSize=10 约为 6px）
            const double charWidth = 6.0;
            const double xLabelPadding = 4.0;  // 每侧额外留白
            int xTickCount = Math.Clamp(sourceData.Count / 20, 4, 10);
            for (int i = 0; i <= xTickCount; i++)
            {
                double ratio = (double)i / xTickCount;
                double xValue = minX + (maxX - minX) * ratio;
                double xCanvas = PlotXLeft + PlotXRange * ratio;

                string label = xValue.ToString("0.####");
                double labelW = label.Length * charWidth + xLabelPadding * 2;

                xTicks.Add(new AxisTick
                {
                    Label = label,
                    TickX1 = xCanvas,
                    TickY1 = 275,
                    TickX2 = xCanvas,
                    TickY2 = AxisYBottomLine,
                    LabelWidth = labelW,
                    // Margin.Left = 刻度线位置 - 标签宽度/2，使标签中心对准刻度线
                    LabelMargin = new Thickness(xCanvas - labelW / 2, 285, 0, 0)
                });
            }

            // Y 轴刻度：标签右对齐，贴在 Y 轴左侧
            const double yAxisLabelWidth = 42;  // Y 轴标签固定宽度
            int yTickCount = 4;
            for (int i = 0; i <= yTickCount; i++)
            {
                double ratio = (double)i / yTickCount;
                double yCanvas = AxisYTop + (AxisYBottomTick - AxisYTop) * ratio;
                double yValue = maxY - (maxY - minY) * ratio;

                yTicks.Add(new AxisTick
                {
                    Label = yValue.ToString("0.####"),
                    TickX1 = AxisXLeft - 5,
                    TickY1 = yCanvas,
                    TickX2 = AxisXLeft,
                    TickY2 = yCanvas,
                    LabelWidth = yAxisLabelWidth,
                    // Margin.Left = Y 轴位置 - 标签宽度 - 间距，标签右对齐
                    LabelMargin = new Thickness(AxisXLeft - yAxisLabelWidth - 6, yCanvas - 5, 0, 0)
                });
            }

            foreach (var p in sourceData)
            {
                double x = PlotXLeft + (p.X - minX) / (maxX - minX) * PlotXRange;
                double y = AxisYBottomTick - (p.Y - minY) / (maxY - minY) * (AxisYBottomTick - AxisYTop);
                plotData.Add(new DataPoint { X = x, Y = y, ToolTip = p.ToolTip });
            }
        }

        private double[,]? _heatmapGrid;
        private int _heatmapStage;
        private double _heatmapMinSourceX, _heatmapMaxSourceX, _heatmapMinSourceY, _heatmapMaxSourceY;

        private void UpdateHeatmap(List<(double X, double Y)> sourcePoints, int count, double scale)
        {
            int stage = (int)Math.Round(CalibrationStage);
            if (stage < 2 || count == 0)
            {
                HeatmapImageSource = null;
                _heatmapGrid = null;
                return;
            }

            // Build error magnitude grid (stage x stage)
            var errorGrid = new double[stage, stage];
            for (int idx = 0; idx < count; idx++)
            {
                int col = idx % stage;
                int row = idx / stage;
                if (row >= stage || col >= stage) continue;

                var s = sourcePoints[idx];
                var t = _targetPoints[idx];
                double dx = (t.X - s.X) * scale;
                double dy = (t.Y - s.Y) * scale;
                //综合误差大小（欧几里得距离）
                errorGrid[row, col] = Math.Sqrt(dx * dx + dy * dy);
            }

            _heatmapGrid = errorGrid;
            _heatmapStage = stage;
            _heatmapMinSourceX = sourcePoints.Min(p => p.X);
            _heatmapMaxSourceX = sourcePoints.Max(p => p.X);
            _heatmapMinSourceY = sourcePoints.Min(p => p.Y);
            _heatmapMaxSourceY = sourcePoints.Max(p => p.Y);

            RenderHeatmapFromGrid();
        }

        private void RenderHeatmapFromGrid()
        {
            if (_heatmapGrid == null) return;

            var errorGrid = _heatmapGrid;
            int stage = _heatmapStage;

            double maxError = 0;
            for (int r = 0; r < stage; r++)
                for (int c = 0; c < stage; c++)
                    if (errorGrid[r, c] > maxError) maxError = errorGrid[r, c];

            if (maxError < 1e-12) maxError = 1;
            HeatmapMaxLabel = $"最大综合误差: {maxError:F2} {ErrorUnit}";

            // Render bitmap with bilinear interpolation
            int imgW = Math.Max(200, (int)(PlotXRange));
            int imgH = 250;
            var bitmap = new WriteableBitmap(imgW, imgH, 96, 96, PixelFormats.Bgra32, null);
            var pixels = new byte[imgW * imgH * 4];

            for (int py = 0; py < imgH; py++)
            {
                double fy = (double)py / (imgH - 1) * (stage - 1); // row in grid (top=row0)
                int r0 = Math.Clamp((int)fy, 0, stage - 2);
                int r1 = r0 + 1;
                double ry = fy - r0;

                for (int px = 0; px < imgW; px++)
                {
                    double fx = (double)px / (imgW - 1) * (stage - 1);
                    int c0 = Math.Clamp((int)fx, 0, stage - 2);
                    int c1 = c0 + 1;
                    double rx = fx - c0;

                    double val = errorGrid[r0, c0] * (1 - rx) * (1 - ry)
                               + errorGrid[r0, c1] * rx * (1 - ry)
                               + errorGrid[r1, c0] * (1 - rx) * ry
                               + errorGrid[r1, c1] * rx * ry;

                    double t = Math.Clamp(val / maxError, 0, 1);
                    var (cr, cg, cb) = HeatColor(t);

                    int idx = (py * imgW + px) * 4;
                    pixels[idx + 0] = cb; // B
                    pixels[idx + 1] = cg; // G
                    pixels[idx + 2] = cr; // R
                    pixels[idx + 3] = 255; // A
                }
            }

            bitmap.WritePixels(new System.Windows.Int32Rect(0, 0, imgW, imgH), pixels, imgW * 4, 0);
            bitmap.Freeze();
            HeatmapImageSource = bitmap;

            // Update heatmap axis ticks
            UpdateHeatmapTicks(imgW, imgH);
        }

        private void UpdateHeatmapTicks(int imgW, int imgH)
        {
            HeatmapXAxisTicks.Clear();
            HeatmapYAxisTicks.Clear();

            double plotLeft = PlotXLeft;
            double plotRight = PlotXLeft + PlotXRange;
            double plotTop = AxisYTop;
            double plotBottom = AxisYBottomTick;

            // 热力图 X 轴刻度：标签居中对齐
            const double heatCharWidth = 6.0;
            const double heatLabelPadding = 4.0;
            const double heatYLabelWidth = 42;
            int xTickCount = Math.Clamp(_heatmapStage / 2, 4, 10);
            for (int i = 0; i <= xTickCount; i++)
            {
                double ratio = (double)i / xTickCount;
                double val = _heatmapMinSourceX + (_heatmapMaxSourceX - _heatmapMinSourceX) * ratio;
                double xCanvas = plotLeft + PlotXRange * ratio;

                string label = val.ToString("F1");
                double labelW = label.Length * heatCharWidth + heatLabelPadding * 2;

                HeatmapXAxisTicks.Add(new AxisTick
                {
                    Label = label,
                    TickX1 = xCanvas, TickY1 = 275,
                    TickX2 = xCanvas, TickY2 = AxisYBottomLine,
                    LabelWidth = labelW,
                    LabelMargin = new Thickness(xCanvas - labelW / 2, 285, 0, 0)
                });
            }

            // 热力图 Y 轴刻度：标签右对齐
            int yTickCount = Math.Clamp(_heatmapStage / 2, 4, 10);
            for (int i = 0; i <= yTickCount; i++)
            {
                double ratio = (double)i / yTickCount;
                double yCanvas = plotTop + (plotBottom - plotTop) * ratio;
                double val = _heatmapMaxSourceY - (_heatmapMaxSourceY - _heatmapMinSourceY) * ratio;

                HeatmapYAxisTicks.Add(new AxisTick
                {
                    Label = val.ToString("F1"),
                    TickX1 = AxisXLeft - 5, TickY1 = yCanvas,
                    TickX2 = AxisXLeft, TickY2 = yCanvas,
                    LabelWidth = heatYLabelWidth,
                    LabelMargin = new Thickness(AxisXLeft - heatYLabelWidth - 6, yCanvas - 5, 0, 0)
                });
            }
        }

        public void UpdateHeatmapHover(double mouseX, double mouseY, double imgWidth, double imgHeight)
        {
            if (_heatmapGrid == null || _heatmapStage < 2 || imgWidth <= 0 || imgHeight <= 0)
            {
                HeatmapHoverLabel = string.Empty;
                HeatmapHoverVisibility = Visibility.Collapsed;
                return;
            }

            double nx = Math.Clamp(mouseX / imgWidth, 0, 1);
            double ny = Math.Clamp(mouseY / imgHeight, 0, 1);

            double sourceX = _heatmapMinSourceX + (_heatmapMaxSourceX - _heatmapMinSourceX) * nx;
            double sourceY = _heatmapMaxSourceY - (_heatmapMaxSourceY - _heatmapMinSourceY) * ny;
            double err = SampleHeatmapBilinear(nx, ny);

            HeatmapHoverLabel = $"X: {sourceX:F3} mm\nY: {sourceY:F3} mm\n综合误差: {err:F4} {ErrorUnit}";

            const double panelWidth = 130;
            const double panelHeight = 60;
            double left = PlotXLeft + mouseX + 10;
            double top = AxisYTop + mouseY + 10;
            double maxLeft = PlotXLeft + PlotXRange - panelWidth;
            double maxTop = AxisYTop + 250 - panelHeight;
            HeatmapHoverCanvasX = Math.Clamp(left, PlotXLeft, Math.Max(PlotXLeft, maxLeft));
            HeatmapHoverCanvasY = Math.Clamp(top, AxisYTop, Math.Max(AxisYTop, maxTop));
            HeatmapHoverVisibility = Visibility.Visible;
        }

        public void ClearHeatmapHover()
        {
            HeatmapHoverLabel = string.Empty;
            HeatmapHoverVisibility = Visibility.Collapsed;
        }

        private double SampleHeatmapBilinear(double nx, double ny)
        {
            if (_heatmapGrid == null || _heatmapStage < 2)
                return 0;

            int stage = _heatmapStage;
            double fx = nx * (stage - 1);
            double fy = ny * (stage - 1);

            int c0 = Math.Clamp((int)fx, 0, stage - 2);
            int c1 = c0 + 1;
            int r0 = Math.Clamp((int)fy, 0, stage - 2);
            int r1 = r0 + 1;

            double rx = fx - c0;
            double ry = fy - r0;

            return _heatmapGrid[r0, c0] * (1 - rx) * (1 - ry)
                 + _heatmapGrid[r0, c1] * rx * (1 - ry)
                 + _heatmapGrid[r1, c0] * (1 - rx) * ry
                 + _heatmapGrid[r1, c1] * rx * ry;
        }

        private static (byte R, byte G, byte B) HeatColor(double t)
        {
            // 0=blue, 0.25=cyan, 0.5=yellow, 0.75=orange, 1=red
            double r, g, b;
            if (t < 0.25)
            {
                double s = t / 0.25;
                r = 0; g = s; b = 1;
            }
            else if (t < 0.5)
            {
                double s = (t - 0.25) / 0.25;
                r = s; g = 1; b = 1 - s;
            }
            else if (t < 0.75)
            {
                double s = (t - 0.5) / 0.25;
                r = 1; g = 1 - s; b = 0;
            }
            else
            {
                double s = (t - 0.75) / 0.25;
                r = 1; g = 0; b = 0;
                // darken slightly at very top
                r = 1 - s * 0.2;
            }
            return ((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }

        private List<(double X, double Y)> BuildSourcePoints()
        {
            var points = new List<(double X, double Y)>();
            int stage = (int)Math.Round(CalibrationStage);

            if (stage < 2 || TotalArea <= 0)
                return points;

            double gridSize = TotalArea / (stage - 1);
            double halfArea = TotalArea / 2.0;

            for (int j = 0; j < stage; j++)
            {
                double y = -halfArea + j * gridSize;
                for (int i = 0; i < stage; i++)
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
    }

    public partial class DataPoint : ObservableObject
    {
        [ObservableProperty]
        private double _x;

        [ObservableProperty]
        private double _y;

        [ObservableProperty]
        private string _toolTip = string.Empty;
    }

    public partial class AxisTick : ObservableObject
    {
        [ObservableProperty]
        private string _label = string.Empty;

        [ObservableProperty]
        private double _tickX1;

        [ObservableProperty]
        private double _tickY1;

        [ObservableProperty]
        private double _tickX2;

        [ObservableProperty]
        private double _tickY2;

        /// <summary>
        /// 标签文本容器的宽度（固定宽度，TextBlock 填满此宽度后用 TextAlignment 对齐）
        /// </summary>
        [ObservableProperty]
        private double _labelWidth;

        /// <summary>
        /// 标签容器的定位边距（Left, Top, Right, Bottom）
        /// </summary>
        [ObservableProperty]
        private Thickness _labelMargin;
    }
}
