using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace DrSoft.MarkCard.CommonUI.UserControls
{
    /// <summary>
    /// 颜色选择控件：左侧基本/自定义颜色面板，右侧取色器（常驻展开）。
    /// 选中格子时显示虚线边框。
    /// </summary>
    public partial class ColorPickerControl : UserControl
    {
        #region 基本颜色表（8列×6行）

        private static readonly Color[] BasicColors =
        {
            // Row1
            Color.FromRgb(0xFF,0x80,0x80), Color.FromRgb(0xFF,0xFF,0x80), Color.FromRgb(0x80,0xFF,0x80),
            Color.FromRgb(0x00,0xFF,0x80), Color.FromRgb(0x80,0xFF,0xFF), Color.FromRgb(0x00,0x80,0xFF),
            Color.FromRgb(0xFF,0x80,0xC0), Color.FromRgb(0xFF,0x80,0xFF),
            // Row2
            Color.FromRgb(0xFF,0x00,0x00), Color.FromRgb(0xFF,0xFF,0x00), Color.FromRgb(0x80,0xFF,0x00),
            Color.FromRgb(0x00,0xFF,0x40), Color.FromRgb(0x00,0xFF,0xFF), Color.FromRgb(0x00,0x80,0xC0),
            Color.FromRgb(0x80,0x80,0xC0), Color.FromRgb(0xFF,0x00,0xFF),
            // Row3
            Color.FromRgb(0x80,0x40,0x40), Color.FromRgb(0xFF,0x80,0x40), Color.FromRgb(0x00,0xFF,0x00),
            Color.FromRgb(0x00,0x80,0x40), Color.FromRgb(0x00,0x80,0x80), Color.FromRgb(0x00,0x40,0x80),
            Color.FromRgb(0x80,0x00,0x80), Color.FromRgb(0x80,0x00,0x40),
            // Row4
            Color.FromRgb(0x80,0x00,0x00), Color.FromRgb(0xFF,0x80,0x00), Color.FromRgb(0x00,0x80,0x00),
            Color.FromRgb(0x00,0x80,0x80), Color.FromRgb(0x00,0x00,0xFF), Color.FromRgb(0x00,0x00,0xA0),
            Color.FromRgb(0x80,0x00,0xFF), Color.FromRgb(0xFF,0x00,0x80),
            // Row5
            Color.FromRgb(0x40,0x00,0x00), Color.FromRgb(0x80,0x40,0x00), Color.FromRgb(0x00,0x40,0x00),
            Color.FromRgb(0x00,0x40,0x40), Color.FromRgb(0x00,0x00,0x80), Color.FromRgb(0x00,0x00,0x40),
            Color.FromRgb(0x40,0x00,0x40), Color.FromRgb(0x40,0x00,0x20),
            // Row6
            Color.FromRgb(0xFF,0xFF,0xFF), Color.FromRgb(0xC0,0xC0,0xC0), Color.FromRgb(0x80,0x80,0x80),
            Color.FromRgb(0x40,0x40,0x40), Color.FromRgb(0x00,0x00,0x00), Color.FromRgb(0xFF,0xFF,0xC0),
            Color.FromRgb(0xFF,0xE0,0xC0), Color.FromRgb(0xFF,0xC0,0xC0),
        };

        #endregion

        #region 属性

        /// <summary>当前选中的颜色</summary>
        public Color SelectedColor { get; set; } = Colors.Black;

        /// <summary>是否点击了确定</summary>
        public bool IsConfirmed { get; private set; }

        /// <summary>颜色确认事件</summary>
        public event Action<Color>? ColorConfirmed;

        #endregion

        #region 自定义颜色槽

        private const int CustomSlotCount = 16;
        private readonly Color[] _customColors = Enumerable.Repeat(Colors.White, CustomSlotCount).ToArray();
        private int _nextCustomSlot = 0;
        private readonly List<Button> _customCellButtons = new();

        #endregion

        #region 内部 HSV 状态

        private bool _updatingFromCode;
        private double _currentHue = 0;        // 0-360
        private double _currentSaturation = 1; // 0-1 (取色板 X 轴)
        private double _currentValue = 1;       // 0-1 (取色板 Y 轴，1=亮)

        #endregion

        #region 运行时控件引用（嵌套命名元素通过 FindName 获取）

        private Ellipse? _crosshairCircle;
        private Rectangle? _hueArrowLine;

        #endregion

        #region 选中格子追踪

        private Button? _selectedBasicBtn;
        private Button? _selectedCustomBtn;

        #endregion

        public ColorPickerControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // 获取嵌套命名元素引用
            _crosshairCircle = (Ellipse?)FindName("CrosshairCircle");
            _hueArrowLine = (Rectangle?)FindName("HueArrowLine");

            BuildBasicColorGrid();
            BuildCustomColorGrid();
            // 记录旧颜色
            OldColorBorder.Background = new SolidColorBrush(SelectedColor);
            // 将初始颜色同步到取色器
            RgbToHsv(SelectedColor, out _currentHue, out _currentSaturation, out _currentValue);
            UpdateHueStopAndIndicator();
            UpdateCrosshair();
            UpdateInputsFromHsv();
            PreviewBorder.Background = new SolidColorBrush(SelectedColor);
        }

        #region 构建颜色格子

        private void BuildBasicColorGrid()
        {
            BasicColorGrid.Children.Clear();
            foreach (var c in BasicColors)
            {
                var btn = CreateColorCell(c, isCustom: false);
                BasicColorGrid.Children.Add(btn);
            }
        }

        private void BuildCustomColorGrid()
        {
            CustomColorGrid.Children.Clear();
            _customCellButtons.Clear();
            for (int i = 0; i < CustomSlotCount; i++)
            {
                int idx = i;
                var btn = CreateColorCell(_customColors[i], isCustom: true);
                btn.Click += (_, _) =>
                {
                    SelectColorCell(btn, isCustom: true);
                    var c = _customColors[idx];
                    SetSelectedColorFromCell(c);
                };
                _customCellButtons.Add(btn);
                CustomColorGrid.Children.Add(btn);
            }
        }

        private Button CreateColorCell(Color c, bool isCustom)
        {
            var btn = new Button
            {
                Style = (Style)FindResource("ColorCellStyle"),
                Background = new SolidColorBrush(c),
            };
            if (!isCustom)
            {
                btn.Click += (s, _) =>
                {
                    SelectColorCell((Button)s, isCustom: false);
                    SetSelectedColorFromCell(((Button)s).Background is SolidColorBrush scb ? scb.Color : Colors.Black);
                };
            }
            return btn;
        }

        /// <summary>清除旧选中标记，设置新选中格子的虚线边框</summary>
        private void SelectColorCell(Button btn, bool isCustom)
        {
            // 清除旧选中
            if (_selectedBasicBtn != null) _selectedBasicBtn.Tag = null;
            if (_selectedCustomBtn != null) _selectedCustomBtn.Tag = null;

            btn.Tag = "Selected";
            if (isCustom) _selectedCustomBtn = btn;
            else _selectedBasicBtn = btn;
        }

        /// <summary>点击色板格子后同步颜色到取色器（不触发取色器->格子的反向选中）</summary>
        private void SetSelectedColorFromCell(Color c)
        {
            SelectedColor = c;
            PreviewBorder.Background = new SolidColorBrush(c);
            RgbToHsv(c, out _currentHue, out _currentSaturation, out _currentValue);
            UpdateHueStopAndIndicator();
            UpdateCrosshair();
            _updatingFromCode = true;
            UpdateInputsFromHsv();
            _updatingFromCode = false;
        }

        #endregion

        #region 选中颜色更新（从取色器）

        private void SetSelectedColor(Color c)
        {
            SelectedColor = c;
            PreviewBorder.Background = new SolidColorBrush(c);
        }

        #endregion

        #region 取色板交互（Hue×Saturation）

        private bool _spectrumDragging;

        private void ColorSpectrum_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            _spectrumDragging = true;
            (sender as UIElement)?.CaptureMouse();
            PickFromSpectrum(e.GetPosition(ColorSpectrumBorder));
        }

        private void ColorSpectrum_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_spectrumDragging || e.LeftButton != MouseButtonState.Pressed) return;
            PickFromSpectrum(e.GetPosition(ColorSpectrumBorder));
        }

        private void ColorSpectrum_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _spectrumDragging = false;
            (sender as UIElement)?.ReleaseMouseCapture();
        }

        private void PickFromSpectrum(Point p)
        {
            double w = ColorSpectrumBorder.ActualWidth;
            double h = ColorSpectrumBorder.ActualHeight;
            if (w <= 0 || h <= 0) return;

            _currentSaturation = Math.Clamp(p.X / w, 0, 1);
            _currentValue = Math.Clamp(1 - p.Y / h, 0, 1);

            UpdateCrosshair(p.X, p.Y);

            var c = HsvToRgb(_currentHue, _currentSaturation, _currentValue);
            SetSelectedColor(c);
            _updatingFromCode = true;
            UpdateInputsFromHsv();
            _updatingFromCode = false;
        }

        private void UpdateCrosshair(double? x = null, double? y = null)
        {
            double w = ColorSpectrumBorder.ActualWidth;
            double h = ColorSpectrumBorder.ActualHeight;
            if (w <= 0 || h <= 0) return;

            double cx = x ?? _currentSaturation * w;
            double cy = y ?? (1 - _currentValue) * h;

            const double rad = 5;
            if (_crosshairCircle != null)
            {
                Canvas.SetLeft(_crosshairCircle, cx - rad);
                Canvas.SetTop(_crosshairCircle, cy - rad);
            }
        }

        #endregion

        #region 色相竖条交互

        private bool _hueDragging;

        private void HueBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            _hueDragging = true;
            (sender as UIElement)?.CaptureMouse();
            PickHue(e.GetPosition((UIElement)sender), (FrameworkElement)sender);
        }

        private void HueBar_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_hueDragging || e.LeftButton != MouseButtonState.Pressed) return;
            PickHue(e.GetPosition((UIElement)sender), (FrameworkElement)sender);
        }

        private void HueBar_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _hueDragging = false;
            (sender as UIElement)?.ReleaseMouseCapture();
        }

        private void PickHue(Point p, FrameworkElement hueBar)
        {
            double h = hueBar.ActualHeight;
            if (h <= 0) return;
            _currentHue = Math.Clamp(p.Y / h, 0, 1) * 360;

            UpdateHueStopAndIndicator();
            var c = HsvToRgb(_currentHue, _currentSaturation, _currentValue);
            SetSelectedColor(c);
            _updatingFromCode = true;
            UpdateInputsFromHsv();
            _updatingFromCode = false;
        }

        private void UpdateHueStopAndIndicator()
        {
            // 更新取色板纯色端点
            HueStop.Color = HsvToRgb(_currentHue, 1, 1);

            // 更新色相指示线位置
            if (_hueArrowLine != null)
            {
                double barH = HueIndicatorCanvas.ActualHeight > 0
                    ? HueIndicatorCanvas.ActualHeight
                    : 180;
                double y = (_currentHue / 360.0) * barH - 1;
                Canvas.SetTop(_hueArrowLine, Math.Max(0, y));
            }
        }

        #endregion

        #region HSL/RGB 文本输入

        private void HslRgb_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updatingFromCode) return;
            if (int.TryParse(HueTBox.Text, out int h)
             && int.TryParse(SatTBox.Text, out int s)
             && int.TryParse(LumTBox.Text, out int l))
            {
                h = Math.Clamp(h, 0, 360);
                s = Math.Clamp(s, 0, 100);
                l = Math.Clamp(l, 0, 100);
                var c = HslToRgb(h, s / 100.0, l / 100.0);
                RgbToHsv(c, out _currentHue, out _currentSaturation, out _currentValue);
                SetSelectedColor(c);
                UpdateHueStopAndIndicator();
                UpdateCrosshair();
                _updatingFromCode = true;
                RTextBox.Text = c.R.ToString();
                GTextBox.Text = c.G.ToString();
                BTextBox.Text = c.B.ToString();
                _updatingFromCode = false;
            }
        }

        private void RGB_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updatingFromCode) return;
            if (byte.TryParse(RTextBox.Text, out var r)
             && byte.TryParse(GTextBox.Text, out var g)
             && byte.TryParse(BTextBox.Text, out var b))
            {
                var c = Color.FromRgb(r, g, b);
                RgbToHsv(c, out _currentHue, out _currentSaturation, out _currentValue);
                SetSelectedColor(c);
                UpdateHueStopAndIndicator();
                UpdateCrosshair();
                _updatingFromCode = true;
                UpdateHslInputs(c);
                _updatingFromCode = false;
            }
        }

        private void UpdateInputsFromHsv()
        {
            var c = HsvToRgb(_currentHue, _currentSaturation, _currentValue);
            RgbToHsl(c, out double h, out double s, out double l);
            HueTBox.Text = ((int)Math.Round(h)).ToString();
            SatTBox.Text = ((int)Math.Round(s * 100)).ToString();
            LumTBox.Text = ((int)Math.Round(l * 100)).ToString();
            RTextBox.Text = c.R.ToString();
            GTextBox.Text = c.G.ToString();
            BTextBox.Text = c.B.ToString();
        }

        private void UpdateHslInputs(Color c)
        {
            RgbToHsl(c, out double h, out double s, out double l);
            HueTBox.Text = ((int)Math.Round(h)).ToString();
            SatTBox.Text = ((int)Math.Round(s * 100)).ToString();
            LumTBox.Text = ((int)Math.Round(l * 100)).ToString();
        }

        #endregion

        #region 添加到自定义颜色

        private void AddCustomColor_Click(object sender, RoutedEventArgs e)
        {
            int slot = _nextCustomSlot % CustomSlotCount;
            _customColors[slot] = SelectedColor;
            _customCellButtons[slot].Background = new SolidColorBrush(SelectedColor);
            _nextCustomSlot = (slot + 1) % CustomSlotCount;
        }

        #endregion

        #region 展开/折叠取色器

        private void DefineCustomBtn_Click(object sender, RoutedEventArgs e)
        {
            bool expand = PickerPanel.Visibility != Visibility.Visible;
            PickerPanel.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
            DefineCustomBtn.Content = expand ? "规定自定义颜色(D) <<" : "规定自定义颜色(D) >>";
        }

        #endregion

        #region 确定/取消

        private void ConfirmBtn_Click(object sender, RoutedEventArgs e)
        {
            IsConfirmed = true;
            ColorConfirmed?.Invoke(SelectedColor);
            CloseParentWindow();
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            IsConfirmed = false;
            CloseParentWindow();
        }

        private void CloseParentWindow()
        {
            var win = Window.GetWindow(this);
            win?.Close();
        }

        #endregion

        #region 颜色转换工具

        // HSV → RGB
        private static Color HsvToRgb(double h, double s, double v)
        {
            if (s == 0) { var gv = (byte)(v * 255); return Color.FromRgb(gv, gv, gv); }
            h /= 60;
            int i = (int)h;
            double f = h - i;
            double p = v * (1 - s);
            double q = v * (1 - s * f);
            double t = v * (1 - s * (1 - f));
            double r, g2, b;
            switch (i % 6)
            {
                case 0: r = v; g2 = t; b = p; break;
                case 1: r = q; g2 = v; b = p; break;
                case 2: r = p; g2 = v; b = t; break;
                case 3: r = p; g2 = q; b = v; break;
                case 4: r = t; g2 = p; b = v; break;
                default: r = v; g2 = p; b = q; break;
            }
            return Color.FromRgb((byte)(r * 255), (byte)(g2 * 255), (byte)(b * 255));
        }

        // RGB → HSV
        private static void RgbToHsv(Color c, out double h, out double s, out double v)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;
            v = max;
            s = max == 0 ? 0 : delta / max;
            if (delta == 0) { h = 0; return; }
            if (max == r) h = 60 * ((g - b) / delta % 6);
            else if (max == g) h = 60 * ((b - r) / delta + 2);
            else h = 60 * ((r - g) / delta + 4);
            if (h < 0) h += 360;
        }

        // RGB → HSL
        private static void RgbToHsl(Color c, out double h, out double s, out double l)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;
            l = (max + min) / 2;
            s = delta == 0 ? 0 : delta / (1 - Math.Abs(2 * l - 1));
            if (delta == 0) { h = 0; return; }
            if (max == r) h = 60 * ((g - b) / delta % 6);
            else if (max == g) h = 60 * ((b - r) / delta + 2);
            else h = 60 * ((r - g) / delta + 4);
            if (h < 0) h += 360;
        }

        // HSL → RGB
        private static Color HslToRgb(int hDeg, double s, double l)
        {
            double c2 = (1 - Math.Abs(2 * l - 1)) * s;
            double x = c2 * (1 - Math.Abs(hDeg / 60.0 % 2 - 1));
            double m = l - c2 / 2;
            double r, g, b;
            if (hDeg < 60) { r = c2; g = x; b = 0; }
            else if (hDeg < 120) { r = x; g = c2; b = 0; }
            else if (hDeg < 180) { r = 0; g = c2; b = x; }
            else if (hDeg < 240) { r = 0; g = x; b = c2; }
            else if (hDeg < 300) { r = x; g = 0; b = c2; }
            else { r = c2; g = 0; b = x; }
            return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
        }

        #endregion
    }
}
