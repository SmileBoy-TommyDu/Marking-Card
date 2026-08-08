using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using DrSoft.Drawing.Contracts;
using DrSoft.Drawing.Controls.Commands;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Controls.Menu;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Controls.Rendering;
using DrSoft.Drawing.Controls.Tools;
using DrSoft.Drawing.Controls.ViewModels;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Event.Tool;
using DrSoft.Drawing.Model;
using DrSoft.Drawing.Rendering;
using Microsoft.VisualBasic;
using SkiaSharp;
using SkiaSharp.Views.WPF;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using Application = System.Windows.Application;
using CommandManager = System.Windows.Input.CommandManager;
using Cursors = System.Windows.Input.Cursors;
using Point = System.Windows.Point;

namespace DrSoft.Drawing.Controls;

/// <summary>
/// 画布编辑器的 WPF 适配层 ViewModel。
/// 负责把宿主 UI、DocumentContext、工具系统和渲染管线桥接起来，但不直接持有图形交互细节。
/// </summary>
public partial class CanvasViewModel : ObservableObject, IDisposable
{
    private sealed class CanvasInteractionHost : ICanvasInteractionHost
    {
        private readonly CanvasViewModel _owner;
        private readonly IDialogService _dialogService;

        /// <summary>
        /// 面向交互内核的宿主适配器。
        /// 把状态栏、重绘、光标和对话框请求安全地投递到当前 WPF 宿主。
        /// </summary>
        public CanvasInteractionHost(CanvasViewModel owner, IDialogService dialogService)
        {
            _owner = owner;
            _dialogService = dialogService;
        }

        public void UpdateStatus(string status)
        {
            _owner.OnStatusReported(status);
        }

        public void Redraw()
        {
            _owner.Redraw();
        }

        public void SetCursor(System.Windows.Input.Cursor cursor)
        {
            if (_owner._currentSkiaCanvas == null)
                return;

            if (_owner._currentSkiaCanvas.Dispatcher.CheckAccess())
            {
                _owner._currentSkiaCanvas.Cursor = cursor;
            }
            else
            {
                _owner._currentSkiaCanvas.Dispatcher.Invoke(() => _owner._currentSkiaCanvas.Cursor = cursor);
            }
        }

        public MoveNodeDialogResult? ShowMoveNodeDialog(float currentX, float currentY)
        {
            return _dialogService.ShowDialogAsync<MoveNodePopupViewModel, MoveNodeDialogResult>(vm =>
            {
                vm.Title = "移动节点";
                vm.ConfirmText = "确认";
                vm.CancelText = "取消";
                vm.WindowHeight = 220;
                vm.SetInitialPosition(currentX, currentY);
            }).GetAwaiter().GetResult();
        }

        public ExtendNodeDialogResult? ShowExtendNodeDialog()
        {
            return _dialogService.ShowDialogAsync<ExtendNodePopupViewModel, ExtendNodeDialogResult>(vm =>
            {
                vm.Title = "输入坐标点";
                vm.ConfirmText = "确认";
                vm.CancelText = "取消";
                vm.WindowHeight = 250;
            }).GetAwaiter().GetResult();
        }

        public SeparateNodeDialogResult? ShowSeparateNodeDialog(float currentDistance)
        {
            return _dialogService.ShowDialogAsync<SeparateNodePopupViewModel, SeparateNodeDialogResult>(vm =>
            {
                vm.Title = "分离节点距离";
                vm.ConfirmText = "套用";
                vm.CancelText = "取消";
                vm.WindowHeight = 200;
                vm.SetInitialDistance(currentDistance);
            }).GetAwaiter().GetResult();
        }

        public bool IsShiftPressed()
        {
            return (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) == System.Windows.Input.ModifierKeys.Shift;
        }
    }

    internal DocumentContext Context { get; }

    // ── Public state ──────────────────────────────────────────────────────
    //public DrawingDocument Document       { get; }
    internal readonly RenderPipeline _renderPipeline;

    internal IEventBus? eventBus => EventBus.Instance;
    internal static bool UndoFlag = false, RedoFlag = false;

    private readonly ToolSelect _selectTool = new ToolSelect();
    private readonly ToolLine _lineTool = new ToolLine();
    private readonly ToolDot _dotTool = new ToolDot();
    private readonly ToolRectangle _rectangleTool = new ToolRectangle();
    private readonly ToolCircle _circleTool = new ToolCircle();
    private readonly ToolPolygon _polygonTool = new ToolPolygon();
    private readonly ToolArc _arcTool = new ToolArc();
    private readonly ToolBezier _bezierTool = new ToolBezier();
    private readonly ToolArbitraryCurve _arbitraryCurveTool = new ToolArbitraryCurve();
    private readonly ToolText _textTool = new ToolText();
    private readonly ToolZoom _zoomTool = new ToolZoom();

    private readonly ToolViewportMove _viewportMoveTool = new ToolViewportMove();

    // 存储画布控件引用，用于设置光标
    private SKElement? _currentSkiaCanvas;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectActive))]
    [NotifyPropertyChangedFor(nameof(IsNodeActive))]
    [NotifyPropertyChangedFor(nameof(IsDotActive))]
    [NotifyPropertyChangedFor(nameof(IsLineActive))]
    [NotifyPropertyChangedFor(nameof(IsRectangleActive))]
    [NotifyPropertyChangedFor(nameof(IsCircleActive))]
    [NotifyPropertyChangedFor(nameof(IsPolygonActive))]
    [NotifyPropertyChangedFor(nameof(IsArcActive))]
    [NotifyPropertyChangedFor(nameof(IsBezierActive))]
    [NotifyPropertyChangedFor(nameof(IsArbitraryCurveActive))]
    [NotifyPropertyChangedFor(nameof(IsTextActive))]
    [NotifyPropertyChangedFor(nameof(IsZoomInChecked))]
    [NotifyPropertyChangedFor(nameof(IsZoomOutChecked))]
    [NotifyPropertyChangedFor(nameof(IsZoomBackChecked))]
    [NotifyPropertyChangedFor(nameof(IsZoomToFullScreenChecked))]
    [NotifyPropertyChangedFor(nameof(IsZoomToFitChecked))]
    [NotifyPropertyChangedFor(nameof(IsZoomToSelectionChecked))]
    [NotifyPropertyChangedFor(nameof(IsMoveActive))]
    private string _activeToolName = "Select";

    public bool IsSelectActive => ActiveToolName == "Select";
    public bool IsNodeActive => ActiveToolName == "Node";

    public bool IsDotActive => ActiveToolName == "Dot";
    public bool IsLineActive => ActiveToolName == "Line";
    public bool IsRectangleActive => ActiveToolName == "Rectangle";
    public bool IsCircleActive => ActiveToolName == "Circle";
    public bool IsPolygonActive => ActiveToolName == "Polygon";
    public bool IsArcActive => ActiveToolName == "Arc";
    public bool IsBezierActive => ActiveToolName == "Bezier";
    public bool IsArbitraryCurveActive => ActiveToolName == "ArbitraryCurve";
    public bool IsTextActive => ActiveToolName == "Text";

    public bool IsZoomInChecked => ActiveToolName == "ZoomIn";
    public bool IsZoomOutChecked => ActiveToolName == "ZoomOut";
    public bool IsZoomBackChecked => false;
    public bool IsZoomToFullScreenChecked => ActiveToolName == "ZoomToFullScreen";
    public bool IsZoomToFitChecked => ActiveToolName == "ZoomToFit";
    public bool IsZoomToSelectionChecked => ActiveToolName == "ZoomToSelection";

    public bool IsMoveActive => ActiveToolName == "Move";

    [ObservableProperty]
    private ImageSource _zoomToolNormalIcon = new BitmapImage(new Uri("pack://application:,,,/DrSoft.Drawing.Controls;component/Resources/image/drawingToolIcon/ZoomInEnable.png"));
    [ObservableProperty]
    private ImageSource _zoomToolDisabledIcon = new BitmapImage(new Uri("pack://application:,,,/DrSoft.Drawing.Controls;component/Resources/image/drawingToolIcon/ZoomInDisable.png"));

    [ObservableProperty]
    private bool _isToolZoomPopupOpen;

    /// <summary>
    /// 绘图工具是否可用（当活动图层被锁定时不可用）
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectActive))]
    private bool _isDrawingToolEnabled = true;

    [ObservableProperty]
    private bool _isNodeEnabled;

    [ObservableProperty]
    private bool _isSelectEnabled;

    [ObservableProperty]
    private bool _isZoomBackEnabled = false;

    [ObservableProperty]
    private bool _isZoomToSelectionEnabled = false;

    [ObservableProperty]
    private bool _isMoveEnabled;


    // Bindable properties
    [ObservableProperty]
    private bool _canUndo;
    [ObservableProperty]
    private bool _canRedo;
    [ObservableProperty]
    private string _statusText = "就绪";
    [ObservableProperty]
    private string _coordinateText = "0,0";
    [ObservableProperty]
    private double _zoomPercent = 100;
    [ObservableProperty]
    private ObservableCollection<DrawingCanvas> _canvasList = new ObservableCollection<DrawingCanvas>();
    internal const int MaxCanvasNameLength = 15;
    private static readonly char[] InvalidCanvasNameChars = new char[] { ':', '\\', '/', '?', '*', '[', ']' };

    public bool GridVisible
    {
        get => _renderPipeline.Grid.IsVisible;
        set { _renderPipeline.Grid.IsVisible = value; OnPropertyChanged(); Redraw(); }
    }

    public float RulerWidth => _renderPipeline.Ruler.RulerWidth;

    public bool RulerVisible => _renderPipeline.Ruler.IsVisible;

    // Raised to ask the SKElement to repaint
    public event EventHandler? RedrawRequested;
    public event EventHandler? ActiveCanvasChanged;
    public event EventHandler<string>? StatusReported;

    private readonly MultiCanvas? _multiCanvas;
    private readonly RendererDispatcher _renderer;
    private readonly ICanvasInteractionHost _interactionHost;

    /// <summary>
    /// 多画布管理器
    /// </summary>
    public MultiCanvas MultiCanvas => _multiCanvas;

    private readonly IDialogService? _dialogService;

    //private FontSettings _fontSettings = new FontSettings();

    public ObservableCollection<MenuItemViewModel> MenuItems { get; set; }

    public Dictionary<string, CanvasRightClickCommand<bool, GraphicResult>> RightClickCommandDic { get; set; }

    // VM 发出"需要重绘"信号
    /// <summary>
    /// 初始化画布 ViewModel，并完成 DocumentContext 与宿主适配层的桥接。
    /// </summary>
    public CanvasViewModel(MultiCanvas multiCanvas, IDialogService dialogService, RendererDispatcher renderer)
    {
        _multiCanvas = multiCanvas;
        _renderer = renderer;
        Context = _multiCanvas.Context;
        Context.SelectTool = _selectTool;
        _interactionHost = new CanvasInteractionHost(this, dialogService);
        Context.InteractionHost = _interactionHost;
        Context.PublishCanvasChange += PublishCanvasChange;



        _dialogService = dialogService;

        Context.ActiveTool = _selectTool;

        // 订阅MultiCanvas事件
        _multiCanvas.CanvasCreated += OnCanvasCreated;
        _multiCanvas.CanvasRemoved += OnCanvasRemoved;
        _multiCanvas.ActiveCanvasChanged += OnActiveCanvasChanged;
        _multiCanvas.Redraw += (s, e) => Redraw();

        // 订阅画布变化事件：当选区不再是可编辑的单个 Combination 时，强制退出节点编辑模式
        EventBus.Instance.Subscribe<CanvasChangedEvent>(OnCanvasChangedForNodeTool);

        // 监听图层锁定状态变化，更新绘图工具可用性
        Context.ActiveCanvasChanged += OnActiveCanvasChangedForToolbar;

        // 订阅缩放相关事件（全局只注册一次）
        EventBus.Instance.Subscribe<ViewportChangedEvent>(OnViewportChanged);

        WeakReferenceMessenger.Default.Register<string>(this, (r, m) => RightMenuLockCommandItemStateChanged(m));

        InitializeCanvases();

        // 初始订阅图层事件
        SubscribeToLayerEvents();

        _renderPipeline = new RenderPipeline(_renderer);
        RightClickCommandDic = new Dictionary<string, CanvasRightClickCommand<bool, GraphicResult>>();
        MenuItems = new ObservableCollection<MenuItemViewModel>();

        BuildMenu();

        EventBus.Instance.Subscribe<CommandCapabilityChangedEvent>(data =>
        {
            SelectShapeToBuildMenu(data);
        });
    }

    internal void OnStatusReported(string status)
    {
        StatusText = status;
        StatusReported?.Invoke(this, status);
    }

    private void RightMenuLockCommandItemStateChanged(string m)
    {
        if (m == "解锁" || m == "锁定")
        {
            var editMenu = MenuItems.FirstOrDefault(m => m.Header == "编辑");
            if (editMenu != null)
            {
                var lockItem = editMenu.Children.FirstOrDefault(m => m.CommandParameter == "Lock");
                if (lockItem != null)
                {
                    var index = editMenu.Children.IndexOf(lockItem);
                    var lockMenuItem = new MenuItemViewModel
                    {
                        Header = m + "物件(L)",
                        CommandParameter = "Lock",
                        Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
                    };

                    editMenu.Children[index] = lockMenuItem;
                }
            }
        }
    }

    partial void OnActiveToolNameChanged(string value)
    {
        switch (value)
        {
            case "ZoomIn":
                IconMargin = new Thickness(0);
                ZoomToolNormalIcon = new BitmapImage(new Uri("pack://application:,,,/DrSoft.Drawing.Controls;component/Resources/image/drawingToolIcon/ZoomInEnable.png"));
                ZoomToolDisabledIcon = new BitmapImage(new Uri("pack://application:,,,/DrSoft.Drawing.Controls;component/Resources/image/drawingToolIcon/ZoomInDisable.png"));
                break;
            case "ZoomOut":
                IconMargin = new Thickness(0);
                ZoomToolNormalIcon = new BitmapImage(new Uri("pack://application:,,,/DrSoft.Drawing.Controls;component/Resources/image/drawingToolIcon/ZoomOutEnable.png"));
                ZoomToolDisabledIcon = new BitmapImage(new Uri("pack://application:,,,/DrSoft.Drawing.Controls;component/Resources/image/drawingToolIcon/ZoomOutDisable.png"));
                break;
            case "ZoomBack":
                IconMargin = new Thickness(2, 0, 0, 0); // 右移2像素
                ZoomToolNormalIcon = new BitmapImage(new Uri("pack://application:,,,/DrSoft.Drawing.Controls;component/Resources/image/drawingToolIcon/ZoomBackEnable.png"));
                ZoomToolDisabledIcon = new BitmapImage(new Uri("pack://application:,,,/DrSoft.Drawing.Controls;component/Resources/image/drawingToolIcon/ZoomBackDisable.png"));
                break;
            case "ZoomToFullScreen":
                IconMargin = new Thickness(0);
                ZoomToolNormalIcon = new BitmapImage(new Uri("pack://application:,,,/DrSoft.Drawing.Controls;component/Resources/image/drawingToolIcon/ZoomToFullScreenEnable.png"));
                ZoomToolDisabledIcon = new BitmapImage(new Uri("pack://application:,,,/DrSoft.Drawing.Controls;component/Resources/image/drawingToolIcon/ZoomToFullScreenDisable.png"));
                break;
            case "ZoomToFit":
                IconMargin = new Thickness(0);
                ZoomToolNormalIcon = new BitmapImage(new Uri("pack://application:,,,/DrSoft.Drawing.Controls;component/Resources/image/drawingToolIcon/ZoomToFitEnable.png"));
                ZoomToolDisabledIcon = new BitmapImage(new Uri("pack://application:,,,/DrSoft.Drawing.Controls;component/Resources/image/drawingToolIcon/ZoomToFitDisable.png"));
                break;
            case "ZoomToSelection":
                IconMargin = new Thickness(0);
                ZoomToolNormalIcon = new BitmapImage(new Uri("pack://application:,,,/DrSoft.Drawing.Controls;component/Resources/image/drawingToolIcon/ZoomToSelectionEnable.png"));
                ZoomToolDisabledIcon = new BitmapImage(new Uri("pack://application:,,,/DrSoft.Drawing.Controls;component/Resources/image/drawingToolIcon/ZoomToSelectionDisable.png"));
                break;
            default:
                break;
        }
    }

    private Thickness _iconMargin;
    public Thickness IconMargin
    {
        get => _iconMargin;
        set => SetProperty(ref _iconMargin, value);
    }

    private void InitializeCanvases()
    {
        NewCanvas();
    }

    [RelayCommand]
    public void NewCanvas(string? name = null)
    {
        _multiCanvas.CreateCanvas();
    }

    private string _toolTag;

    [RelayCommand]
    private void SelectTool(string toolTag)
    {
        // ZoomToSelection：立即执行缩放，不切换激活工具、不改变光标、不清除选中状态
        if (toolTag == "ZoomToSelection")
        {
            _zoomTool.ZoomName = toolTag;
            _zoomTool.OnMouseUp(new SKPoint(0, 0));
            IsToolZoomPopupOpen = false;
            return;
        }

        // 记录旧工具状态，用于判断是否从 Node 工具切换出去
        string oldToolTag = _toolTag;
        bool wasNodeActive = oldToolTag == "Node";
        bool isNodeActivating = toolTag == "Node";

        _toolTag = toolTag;
        Context.ActiveTool = toolTag switch
        {
            "Select" => _selectTool,
            "Line" => _lineTool,
            "Dot" => _dotTool,
            "Rectangle" => _rectangleTool,
            "Circle" => _circleTool,
            "Polygon" => _polygonTool,
            "Arc" => _arcTool,
            "Bezier" => _bezierTool,
            "ArbitraryCurve" => _arbitraryCurveTool,
            "Text" => _textTool,
            "ZoomIn" => _zoomTool,
            "ZoomOut" => _zoomTool,
            "ZoomBack" => _zoomTool,
            "ZoomToFullScreen" => _zoomTool,
            "ZoomToFit" => _zoomTool,
            "ZoomToSelection" => _zoomTool,
            "Move" => _viewportMoveTool,
            _ => _selectTool
        };

        if (toolTag.Contains("Zoom"))
        {
            IsToolZoomPopupOpen = false;
            _zoomTool.ZoomName = toolTag;
            if(toolTag.Equals("ZoomToFullScreen") || toolTag.Equals("ZoomToFit"))
            {
                _zoomTool.OnMouseUp(SKPoint.Empty);
                SelectTool("Select");
            }
        }

        UpdateToolbarButtonState(toolTag);

        // Node 工具不再出现在工具栏，节点编辑由 EditPathNodesToolViewModel 的 Edit 按钮驱动。
        // 保留此分支仅为兼容通过 RightClickCommand 等路径传入 "Node" tag 的场景。
        if (isNodeActivating)
        {
            if (Context.ActiveCanvas is DrawingCanvas activeCanvas)
                activeCanvas.EditNodes(true);
        }
        else if (wasNodeActive)
        {
            if (Context.ActiveCanvas is DrawingCanvas activeCanvas)
                activeCanvas.EditNodes(false);
        }

        // 设置光标
        if (toolTag == "Select")
        {
            Context.SetCursor(CanvasCursorFactory.GetCursor("pointer", Cursors.Arrow));
            Context.ReportStatus($"当前工具：{ActiveToolName}");
        }
        else
        {
            try
            {
                using (var stream = Application.GetResourceStream(new Uri($"pack://application:,,,/DrSoft.Drawing.Controls;component/Resources/{toolTag.ToLower()}.cur"))?.Stream)
                {
                    if (stream != null)
                    {

                        var customCursor = new System.Windows.Input.Cursor(stream);
                        Context.SetCursor(customCursor);
                        Context.ReportStatus($"当前工具：{ActiveToolName} 光标：自定义");

                    }
                    else
                    {
                        Context.SetCursor(Cursors.Cross);
                    }
                }
            }
            catch
            {
                Context.SetCursor(Cursors.Cross);
            }

            Context.ReportStatus($"当前工具：{ActiveToolName} 光标：十字标");
        }
    }

    private void OnViewportChanged(ViewportChangedEvent e)
    {
        ZoomPercent = e.ZoomPercent;
        IsZoomBackEnabled = e.CanZoomBack;
    }

    public bool BeginInlineTextInput(SKPoint worldPoint)
    {
        var hasCanvas = Context.ActiveCanvas != null;
        if (!hasCanvas)
        {
            Context.ReportStatus("错误：没有激活的画布");
            return false;
        }

        var result = _textTool.BeginInlineEdit(worldPoint);
        if (!result)
        {
            return false;
        }

        Context.RequestRedraw();
        Context.ReportStatus("输入文字，Ctrl+Enter 完成，Esc 取消");
        return true;
    }

    public bool UpdateInlineTextPreview(string text)
    {
        var hasCanvas = Context.ActiveCanvas != null;
        if (!hasCanvas)
        {
            Context.ReportStatus("错误：没有激活的画布");
            return false;
        }

        var result = _textTool.UpdateInlineTextPreview(text);
        if (!result)
        {
            return false;
        }

        Context.RequestRedraw();
        return true;
    }

    public bool CommitInlineText()
    {
        var result = _textTool.CommitInlineEdit();
        Context.RequestRedraw();

        if (result)
        {
            Context.ReportStatus("文字已添加");
        }

        return result;
    }

    public void CancelInlineText()
    {
        var isInlineInputActive = Context.IsDrawing && Context.ActiveTool == _textTool;
        if (!isInlineInputActive)
        {
            return;
        }

        _textTool.CancelInlineEdit();
        Context.RequestRedraw();
        Context.ReportStatus("已取消文字输入");
    }

    #region 右键Menu
    private void BuildMenu()
    {
        // 剪下
        var cutMenuItem = new MenuItemViewModel
        {
            Header = "剪下(_T)",
            InputGestureText = "Ctrl+X",
            CommandParameter = "Cut",
            Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
        };
        MenuItems.Add(cutMenuItem);
        RightClickCommandDic.Add(cutMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).Cut()) });

        // 复制
        var copyMenuItem = new MenuItemViewModel
        {
            Header = "复制(_C)",
            InputGestureText = "Ctrl+C",
            CommandParameter = "Copy",
            Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
        };
        MenuItems.Add(copyMenuItem);
        RightClickCommandDic.Add(copyMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).Copy()) });

        // 粘贴
        var pasteMenuItem = new MenuItemViewModel
        {
            Header = "粘贴(_P)",
            InputGestureText = "Ctrl+V",
            CommandParameter = "Paste",
            Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
        };
        MenuItems.Add(pasteMenuItem);
        RightClickCommandDic.Add(pasteMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).Paste()) });

        // 删除
        var deleteMenuItem = new MenuItemViewModel
        {
            Header = "删除(_D)",
            InputGestureText = "Del",
            CommandParameter = "Delete",
            Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
        };
        MenuItems.Add(deleteMenuItem);
        RightClickCommandDic.Add(deleteMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).Delete()) });

        // 分隔线
        MenuItems.Add(new MenuItemViewModel { IsSeparator = true });

        // 幅面居中
        var centerMenuItem = new MenuItemViewModel
        {
            Header = "幅面居中(_O)",
            CommandParameter = "Center",
            Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
        };
        MenuItems.Add(centerMenuItem);
        RightClickCommandDic.Add(centerMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).SetCenter(0, 0)) });


        // 镜像子菜单
        var mirrorMenu = new MenuItemViewModel { Header = "镜像" };

        var horizontalMirrorMenuItem = new MenuItemViewModel
        {
            Header = "水平镜像",
            CommandParameter = "HorizontalMirror",
            Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
        };
        mirrorMenu.Children.Add(horizontalMirrorMenuItem);
        RightClickCommandDic.Add(horizontalMirrorMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).HorizontalMirror()) });
        var verticalMirrorMenuItem = new MenuItemViewModel
        {
            Header = "垂直镜像",
            CommandParameter = "VerticalMirror",
            Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
        };
        mirrorMenu.Children.Add(verticalMirrorMenuItem);
        RightClickCommandDic.Add(verticalMirrorMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).VerticalMirror()) });

        MenuItems.Add(mirrorMenu);

        // 对齐子菜单
        var alignMenu = new MenuItemViewModel { Header = "对齐" };
        var leftAlignMenuItem = new MenuItemViewModel
        {
            Header = "左对齐",
            CommandParameter = "LeftAlign",
            Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
        };
        var rightAlignMenuItem = new MenuItemViewModel
        {
            Header = "右对齐",
            CommandParameter = "RightAlign",
            Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
        };
        var centerAlignMenuItem = new MenuItemViewModel
        {
            Header = "水平居中",
            CommandParameter = "CenterAlign",
            Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
        };
        alignMenu.Children.Add(leftAlignMenuItem);
        alignMenu.Children.Add(rightAlignMenuItem);
        alignMenu.Children.Add(centerAlignMenuItem);
        RightClickCommandDic.Add(leftAlignMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).Align(new AlignSettingsDto { AlignType = AlignTypeDto.Left, AlignStandard = AlignStandardDto.LastChooseOne })) });
        RightClickCommandDic.Add(rightAlignMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).Align(new AlignSettingsDto { AlignType = AlignTypeDto.Right, AlignStandard = AlignStandardDto.LastChooseOne })) });
        RightClickCommandDic.Add(centerAlignMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).Align(new AlignSettingsDto { AlignType = AlignTypeDto.Center, AlignStandard = AlignStandardDto.LastChooseOne })) });

        MenuItems.Add(alignMenu);

        // 分隔线
        MenuItems.Add(new MenuItemViewModel { IsSeparator = true });

        // 矩阵复制
        var matrixCopyMenuItem = new MenuItemViewModel
        {
            Header = "矩阵复制(_A)",
            CommandParameter = "MatrixCopy",
            Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
        };
        MenuItems.Add(matrixCopyMenuItem);
        RightClickCommandDic.Add(matrixCopyMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).MatrixCopy(2, 10, 2, 10)) });


        // 编辑子菜单
        var editMenu = new MenuItemViewModel { Header = "编辑" };
        var partitionMenuItem = new MenuItemViewModel
        {
            Header = "依分区打断物件",
            CommandParameter = "Partition",
            Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
        };
        var breakFillMenuItem = new MenuItemViewModel
        {
            Header = "打断填充物件(Y)",
            CommandParameter = "BreakFill",
            Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
        };
        var replaceMenuItem = new MenuItemViewModel
        {
            Header = "取代(R)",
            CommandParameter = "Replace",
            Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
        };
        var lockMenuItem = new MenuItemViewModel
        {
            Header = "锁定物件(L)",
            CommandParameter = "Lock",
            Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
        };
        editMenu.Children.Add(partitionMenuItem);
        editMenu.Children.Add(breakFillMenuItem);
        editMenu.Children.Add(replaceMenuItem);
        editMenu.Children.Add(lockMenuItem);
        RightClickCommandDic.Add(partitionMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).Partition(50, 50, 0.6, 0.6)) });
        RightClickCommandDic.Add(breakFillMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).BreakFill()) });
        RightClickCommandDic.Add(replaceMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).Replace()) });
        RightClickCommandDic.Add(lockMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).Lock()) });
        MenuItems.Add(editMenu);

        // 分隔线
        MenuItems.Add(new MenuItemViewModel { IsSeparator = true });

        // 群组
        var groupMenuItem = new MenuItemViewModel
        {
            Header = "群组(_M)",
            InputGestureText = "Ctrl+M",
            CommandParameter = "Group",
            Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
        };
        MenuItems.Add(groupMenuItem);
        RightClickCommandDic.Add(groupMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).Group()) });

        // 移动至新图层
        var moveToNewLayerMenuItem = new MenuItemViewModel
        {
            Header = "移动至新图层(_L)",
            CommandParameter = "MoveToNewLayer",
            Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
        }; MenuItems.Add(moveToNewLayerMenuItem);
        RightClickCommandDic.Add(moveToNewLayerMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).MoveToNewLayer()) });

    }

    private void SelectShapeToBuildMenu(CommandCapabilityChangedEvent data)
    {
        if (data == null) return;
        if (_isBuildingMenu) return;

        try
        {
            _isBuildingMenu = true;
            if (data.Capabilities.IsCircle)
            {
                AdjustMenu(new SelectedShape { SelectedShapeType = ShapeType.Circle, IsLocked = data.Capabilities.IsLocked });
            }
            else if (data.Capabilities.IsLine)
            {
                AdjustMenu(new SelectedShape { SelectedShapeType = ShapeType.Line, IsLocked = data.Capabilities.IsLocked });
            }
            else if (data.Capabilities.IsRectangle)
            {
                AdjustMenu(new SelectedShape { SelectedShapeType = ShapeType.Rectangle, IsLocked = data.Capabilities.IsLocked });
            }
            else if (data.Capabilities.IsPolygon)
            {
                AdjustMenu(new SelectedShape { SelectedShapeType = ShapeType.Polygon, IsLocked = data.Capabilities.IsLocked });
            }
            else if (data.Capabilities.IsArc)
            {
                AdjustMenu(new SelectedShape { SelectedShapeType = ShapeType.Arc, IsLocked = data.Capabilities.IsLocked });
            }
            else if (data.Capabilities.IsPoint)
            {
                AdjustMenu(new SelectedShape { SelectedShapeType = ShapeType.Point, IsLocked = data.Capabilities.IsLocked });
            }
            else
            {
                AdjustMenu(new SelectedShape { SelectedShapeType = ShapeType.Rectangle, IsLocked = data.Capabilities.IsLocked });
            }

            UpdateMenuCommandState(data);
        }
        finally
        {
            _isBuildingMenu = false;
        }
    }

    private void UpdateMenuCommandState(CommandCapabilityChangedEvent data)
    {
        if (RightClickCommandDic == null) return;

        if (RightClickCommandDic.ContainsKey("Cut"))
            RightClickCommandDic["Cut"].IsEnabled = data.Capabilities.CanCut;
        if (RightClickCommandDic.ContainsKey("Copy"))
            RightClickCommandDic["Copy"].IsEnabled = data.Capabilities.CanCopy;
        if (RightClickCommandDic.ContainsKey("Paste"))
            RightClickCommandDic["Paste"].IsEnabled = data.Capabilities.CanPaste;
        if (RightClickCommandDic.ContainsKey("Delete"))
            RightClickCommandDic["Delete"].IsEnabled = data.Capabilities.CanDelete;
        if (RightClickCommandDic.ContainsKey("Center"))
            RightClickCommandDic["Center"].IsEnabled = data.Capabilities.CanMaterialCenter;
        if (RightClickCommandDic.ContainsKey("HorizontalMirror"))
            RightClickCommandDic["HorizontalMirror"].IsEnabled = data.Capabilities.CanHorizontalMirrorReflection;
        if (RightClickCommandDic.ContainsKey("VerticalMirror"))
            RightClickCommandDic["VerticalMirror"].IsEnabled = data.Capabilities.CanVerticalMirrorReflection;
        if (RightClickCommandDic.ContainsKey("LeftAlign"))
            RightClickCommandDic["LeftAlign"].IsEnabled = data.Capabilities.CanAlign;
        if (RightClickCommandDic.ContainsKey("RightAlign"))
            RightClickCommandDic["RightAlign"].IsEnabled = data.Capabilities.CanAlign;
        if (RightClickCommandDic.ContainsKey("CenterAlign"))
            RightClickCommandDic["CenterAlign"].IsEnabled = data.Capabilities.CanAlign;
        if (RightClickCommandDic.ContainsKey("MatrixCopy"))
            RightClickCommandDic["MatrixCopy"].IsEnabled = data.Capabilities.CanAlign;
        if (RightClickCommandDic.ContainsKey("Partition"))
            RightClickCommandDic["Partition"].IsEnabled = data.Capabilities.CanPartition;

        if (RightClickCommandDic.ContainsKey("BreakFill"))
            RightClickCommandDic["BreakFill"].IsEnabled = data.Capabilities.CanBreakFill;
        if (RightClickCommandDic.ContainsKey("Replace"))
            RightClickCommandDic["Replace"].IsEnabled = data.Capabilities.CanReplace;
        if (RightClickCommandDic.ContainsKey("Lock"))
            RightClickCommandDic["Lock"].IsEnabled = data.Capabilities.CanLock;
        if (RightClickCommandDic.ContainsKey("Group"))
            RightClickCommandDic["Group"].IsEnabled = data.Capabilities.CanGroup;
        if (RightClickCommandDic.ContainsKey("MoveToNewLayer"))
            RightClickCommandDic["MoveToNewLayer"].IsEnabled = data.Capabilities.CanMoveToNewLayer;

        if (RightClickCommandDic.ContainsKey("ConvertToCurve"))
            RightClickCommandDic["ConvertToCurve"].IsEnabled = data.Capabilities.CanConvertToCurve;
        if (RightClickCommandDic.ContainsKey("ConvertToPoint"))
            RightClickCommandDic["ConvertToPoint"].IsEnabled = data.Capabilities.CanConvertToPointOrCircle;
        if (RightClickCommandDic.ContainsKey("ExtendHeadAndTail"))
            RightClickCommandDic["ExtendHeadAndTail"].IsEnabled = data.Capabilities.CanExtendHeadAndTail;
        if (RightClickCommandDic.ContainsKey("JumpPoint"))
            RightClickCommandDic["JumpPoint"].IsEnabled = data.Capabilities.CanJumpPoint;
        if (RightClickCommandDic.ContainsKey("SameRadius"))
            RightClickCommandDic["SameRadius"].IsEnabled = data.Capabilities.IsCircle;
        if (RightClickCommandDic.ContainsKey("SetCircleRadius"))
            RightClickCommandDic["SetCircleRadius"].IsEnabled = data.Capabilities.IsCircle;

        if (RightClickCommandDic.ContainsKey("EditNode"))
            RightClickCommandDic["EditNode"].IsEnabled = data.Capabilities.CanNodeEdit;
        if (RightClickCommandDic.ContainsKey("AddNode"))
            RightClickCommandDic["AddNode"].IsEnabled = data.Capabilities.CanAddNode;
        if (RightClickCommandDic.ContainsKey("DeleteNode"))
            RightClickCommandDic["DeleteNode"].IsEnabled = data.Capabilities.CanRemoveNode;
        if (RightClickCommandDic.ContainsKey("SplitNode"))
            RightClickCommandDic["SplitNode"].IsEnabled = data.Capabilities.CanNodeEdit;
        if (RightClickCommandDic.ContainsKey("ExtendNode"))
            RightClickCommandDic["ExtendNode"].IsEnabled = data.Capabilities.CanExtendNode;
        if (RightClickCommandDic.ContainsKey("ConnectNode"))
            RightClickCommandDic["ConnectNode"].IsEnabled = data.Capabilities.CanNodeEdit;
        if (RightClickCommandDic.ContainsKey("SelectNode"))
            RightClickCommandDic["SelectNode"].IsEnabled = data.Capabilities.CanNodeEdit;
        if (RightClickCommandDic.ContainsKey("SetNode"))
            RightClickCommandDic["SetNode"].IsEnabled = data.Capabilities.CanNodeEdit;

        if (RightClickCommandDic.ContainsKey("CurveToLine"))
            RightClickCommandDic["CurveToLine"].IsEnabled = data.Capabilities.IsCurve;
        if (RightClickCommandDic.ContainsKey("LineToCurve"))
            RightClickCommandDic["LineToCurve"].IsEnabled = data.Capabilities.IsLine;
        if (RightClickCommandDic.ContainsKey("ArcToCurve"))
            RightClickCommandDic["ArcToCurve"].IsEnabled = data.Capabilities.IsArc;

        if (RightClickCommandDic.ContainsKey("SharpCorner"))
            RightClickCommandDic["SharpCorner"].IsEnabled = true;
        if (RightClickCommandDic.ContainsKey("Smooth"))
            RightClickCommandDic["Smooth"].IsEnabled = true;
        if (RightClickCommandDic.ContainsKey("Symmetry"))
            RightClickCommandDic["Symmetry"].IsEnabled = true;


        foreach (var item in RightClickCommandDic)
        {
            //Debug.WriteLine($"UpdateMenuCommandState, Command:{item.Key}, Enable:{item.Value.IsEnabled}");
        }

        IsZoomToSelectionEnabled = data.Capabilities.TotalCount > 0;

    }

    private void AdjustMenu(SelectedShape selectedShape)
    {
        switch (selectedShape.SelectedShapeType)
        {
            case ShapeType.Point:
                if (MenuItems != null)
                {
                    var editMenu = MenuItems.FirstOrDefault(m => m.Header == "编辑");
                    if (editMenu != null)
                    {
                        int index = MenuItems.IndexOf(editMenu);

                        AdjustEditMenu(selectedShape, editMenu);
                    }
                    for (int i = MenuItems.Count - 1; i >= 14; i--)
                    {
                        MenuItems.RemoveAt(i);
                    }
                }
                break;
            case ShapeType.Bezier:
            case ShapeType.Line:
                if (MenuItems != null)
                {
                    var editMenu = MenuItems.FirstOrDefault(m => m.Header == "编辑");
                    if (editMenu != null)
                    {
                        int index = MenuItems.IndexOf(editMenu);

                        AdjustEditMenu(selectedShape, editMenu);
                    }

                    if (MenuItems.Count == 16)
                    {
                        var HasNodeMenu = MenuItems.Any(m => m.Header == "激活节点");
                        if (!HasNodeMenu)
                        {
                            MenuItems[15] = BuildActiveNodeMenu();
                        }
                    }
                    else
                    {
                        // 分隔线
                        MenuItems.Add(new MenuItemViewModel { IsSeparator = true });

                        var newActiveNodeMenu = BuildActiveNodeMenu();
                        MenuItems.Add(newActiveNodeMenu);
                    }
                }
                break;
            case ShapeType.PolyLine:
                break;
            case ShapeType.Rectangle:
                if (MenuItems != null)
                {
                    var editMenu = MenuItems.FirstOrDefault(m => m.Header == "编辑");
                    if (editMenu != null)
                    {
                        int index = MenuItems.IndexOf(editMenu);

                        AdjustEditMenu(selectedShape, editMenu);
                    }

                    for (int i = MenuItems.Count - 1; i >= 14; i--)
                    {
                        MenuItems.RemoveAt(i);
                    }

                }
                break;
            case ShapeType.Circle:
                if (MenuItems != null)
                {
                    var editMenu = MenuItems.FirstOrDefault(m => m.Header == "编辑");
                    if (editMenu != null)
                    {
                        int index = MenuItems.IndexOf(editMenu);

                        AdjustEditMenu(selectedShape, editMenu);
                    }

                    if (MenuItems.Count == 16)
                    {
                        var HascircleMenu = MenuItems.Any(m => m.Header == "圆形物件");
                        if (!HascircleMenu)
                        {
                            MenuItems[15] = BuildCircleMenu();
                        }
                    }
                    else
                    {
                        // 分隔线
                        MenuItems.Add(new MenuItemViewModel { IsSeparator = true });

                        var newCircleMenu = BuildCircleMenu();
                        MenuItems.Add(newCircleMenu);
                    }
                }
                break;
            case ShapeType.Polygon:
                if (MenuItems != null)
                {
                    var editMenu = MenuItems.FirstOrDefault(m => m.Header == "编辑");
                    if (editMenu != null)
                    {
                        int index = MenuItems.IndexOf(editMenu);

                        AdjustEditMenu(selectedShape, editMenu);
                    }
                    for (int i = MenuItems.Count - 1; i >= 14; i--)
                    {
                        MenuItems.RemoveAt(i);
                    }
                }
                break;
            case ShapeType.Arc:
                if (MenuItems != null)
                {
                    var editMenu = MenuItems.FirstOrDefault(m => m.Header == "编辑");
                    if (editMenu != null)
                    {
                        int index = MenuItems.IndexOf(editMenu);

                        AdjustEditMenu(selectedShape, editMenu);
                    }
                    for (int i = MenuItems.Count - 1; i >= 14; i--)
                    {
                        MenuItems.RemoveAt(i);
                    }
                }
                break;
            case ShapeType.Text:
                break;
            case ShapeType.Combination:
                break;
            case ShapeType.Group:
                break;
            case ShapeType.Hatch:
                break;
            case ShapeType.CubicPath:
                break;
            default:
                break;
        }
    }

    private void AdjustEditMenu(SelectedShape selectedShape, MenuItemViewModel editMenu)
    {
        if (editMenu == null) return;

        List<string> NeedToAddCommands = new List<string>();

        switch (selectedShape.SelectedShapeType)
        {
            case ShapeType.Point:
                NeedToAddCommands.Clear();
                NeedToAddCommands.Add("Partition");
                NeedToAddCommands.Add("BreakFill");
                NeedToAddCommands.Add("Replace");
                NeedToAddCommands.Add("Lock");

                BuildNewEditMenu(NeedToAddCommands, editMenu, selectedShape.IsLocked);
                break;
            case ShapeType.Bezier:
            case ShapeType.Line:
                NeedToAddCommands.Clear();
                NeedToAddCommands.Add("ConvertToPoint");
                NeedToAddCommands.Add("ConvertToImage");
                NeedToAddCommands.Add("ExtendHeadAndTail");
                NeedToAddCommands.Add("JumpPoint");
                NeedToAddCommands.Add("Partition");
                NeedToAddCommands.Add("BreakFill");
                NeedToAddCommands.Add("Replace");
                NeedToAddCommands.Add("Lock");

                BuildNewEditMenu(NeedToAddCommands, editMenu, selectedShape.IsLocked);
                break;
            case ShapeType.PolyLine:
                break;
            case ShapeType.Polygon:
            case ShapeType.Rectangle:
                NeedToAddCommands.Clear();
                NeedToAddCommands.Add("ConvertToCurve");
                NeedToAddCommands.Add("ConvertToPoint");
                NeedToAddCommands.Add("JumpPoint");
                NeedToAddCommands.Add("Partition");
                NeedToAddCommands.Add("BreakFill");
                NeedToAddCommands.Add("Replace");
                NeedToAddCommands.Add("Lock");

                BuildNewEditMenu(NeedToAddCommands, editMenu, selectedShape.IsLocked);
                break;
            case ShapeType.Arc:
            case ShapeType.Circle:
                NeedToAddCommands.Clear();
                NeedToAddCommands.Add("ConvertToCurve");
                NeedToAddCommands.Add("ConvertToPoint");
                NeedToAddCommands.Add("ExtendHeadAndTail");
                NeedToAddCommands.Add("JumpPoint");
                NeedToAddCommands.Add("Partition");
                NeedToAddCommands.Add("BreakFill");
                NeedToAddCommands.Add("Replace");
                NeedToAddCommands.Add("Lock");

                BuildNewEditMenu(NeedToAddCommands, editMenu, selectedShape.IsLocked);
                break;
            case ShapeType.Text:
                break;
            case ShapeType.Combination:
                break;
            case ShapeType.Group:
                break;
            case ShapeType.Hatch:
                break;
            case ShapeType.CubicPath:
                break;
            default:
                break;
        }
    }

    private void BuildNewEditMenu(List<string> NeedToAddCommands, MenuItemViewModel editMenu, bool IsLocked)
    {
        if (editMenu == null || NeedToAddCommands == null) return;

        if (NeedToAddCommands.Count == 0) return;

        try
        {
            if (NeedToAddCommands.Count > editMenu.Children.Count)
            {
                foreach (var cmdParameter in NeedToAddCommands)
                {
                    if (!editMenu.Children.Any(m => m.CommandParameter == cmdParameter))
                    {
                        switch (cmdParameter)
                        {
                            case "ConvertToCurve":
                                var convertToCurveMenuItem = new MenuItemViewModel
                                {
                                    Header = "转成曲线(Q)",
                                    CommandParameter = "ConvertToCurve",
                                    Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
                                };
                                editMenu.Children.Add(convertToCurveMenuItem);
                                if (!RightClickCommandDic.ContainsKey(convertToCurveMenuItem.CommandParameter))
                                    RightClickCommandDic.Add(convertToCurveMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).ConvertToCurve()) });
                                break;
                            case "ConvertToImage":
                                var convertToImageMenuItem = new MenuItemViewModel
                                {
                                    Header = "转影像(V)",
                                    CommandParameter = "ConvertToImage",
                                    Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
                                };
                                editMenu.Children.Add(convertToImageMenuItem);
                                if (!RightClickCommandDic.ContainsKey(convertToImageMenuItem.CommandParameter))
                                    RightClickCommandDic.Add(convertToImageMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).ConvertToImage()) });
                                break;
                            case "ConvertToPoint":
                                var convertToPointMenuItem = new MenuItemViewModel
                                {
                                    Header = "转成点",
                                    CommandParameter = "ConvertToPoint",
                                    Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
                                };
                                editMenu.Children.Add(convertToPointMenuItem);
                                if (!RightClickCommandDic.ContainsKey(convertToPointMenuItem.CommandParameter))
                                {
                                    var settings = new ConvertToDotSettingsDto();
                                    RightClickCommandDic.Add(convertToPointMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).ConvertToDot(settings)) });
                                }
                                break;
                            case "ExtendHeadAndTail":
                                var extendHeadAndTailMenuItem = new MenuItemViewModel
                                {
                                    Header = "头尾点延伸",
                                    CommandParameter = "ExtendHeadAndTail",
                                    Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
                                };
                                editMenu.Children.Add(extendHeadAndTailMenuItem);
                                if (!RightClickCommandDic.ContainsKey(extendHeadAndTailMenuItem.CommandParameter))
                                    RightClickCommandDic.Add(extendHeadAndTailMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).ExtendHeadAndTail()) });
                                break;
                            case "JumpPoint":
                                var jumpPointMenuItem = new MenuItemViewModel
                                {
                                    Header = "跳点(J)",
                                    CommandParameter = "JumpPoint",
                                    Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
                                };
                                editMenu.Children.Add(jumpPointMenuItem);
                                if (!RightClickCommandDic.ContainsKey(jumpPointMenuItem.CommandParameter))
                                {
                                    JumpSettingsDto jumpSettings = new JumpSettingsDto();
                                    RightClickCommandDic.Add(jumpPointMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).SetJumpPoint(jumpSettings)) });
                                }
                                break;
                            case "Partition":
                                var partitionMenuItem = new MenuItemViewModel
                                {
                                    Header = "依分区打断物件",
                                    CommandParameter = "Partition",
                                    Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
                                };
                                editMenu.Children.Add(partitionMenuItem);
                                if (!RightClickCommandDic.ContainsKey(partitionMenuItem.CommandParameter))
                                {
                                    RightClickCommandDic.Add(partitionMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).Partition(50, 50, 0.6, 0.6)) });
                                }
                                break;
                            case "BreakFill":
                                var breakFillMenuItem = new MenuItemViewModel
                                {
                                    Header = "打断填充物件(Y)",
                                    CommandParameter = "BreakFill",
                                    Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
                                };
                                editMenu.Children.Add(breakFillMenuItem);
                                if (!RightClickCommandDic.ContainsKey(breakFillMenuItem.CommandParameter))
                                {
                                    RightClickCommandDic.Add(breakFillMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).BreakFill()) });
                                }
                                break;
                            case "Replace":
                                var replaceMenuItem = new MenuItemViewModel
                                {
                                    Header = "取代(R)",
                                    CommandParameter = "Replace",
                                    Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
                                };
                                editMenu.Children.Add(replaceMenuItem);
                                if (!RightClickCommandDic.ContainsKey(replaceMenuItem.CommandParameter))
                                {
                                    RightClickCommandDic.Add(replaceMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).Replace()) });
                                }
                                break;
                            case "Lock":
                                var headerName = "锁定物件(L)";
                                if (!IsLocked)
                                {
                                    headerName = "锁定物件(L)";
                                }
                                else
                                {
                                    headerName = "解锁物件(L)";
                                }
                                var lockMenuItem = new MenuItemViewModel
                                {
                                    Header = headerName,
                                    CommandParameter = "Lock",
                                    Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
                                };
                                editMenu.Children.Add(lockMenuItem);
                                if (!RightClickCommandDic.ContainsKey(lockMenuItem.CommandParameter))
                                {
                                    RightClickCommandDic.Add(lockMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).Lock()) });
                                }
                                break;
                            default:
                                break;
                        }
                    }
                }
            }
            else if (NeedToAddCommands.Count == editMenu.Children.Count)
            {
                editMenu.Children.Clear();
                // 更换不一样的命令
                foreach (var addItem in NeedToAddCommands)
                {
                    switch (addItem)
                    {
                        case "ConvertToCurve":
                            var convertToCurveMenuItem = new MenuItemViewModel
                            {
                                Header = "转成曲线(Q)",
                                CommandParameter = "ConvertToCurve",
                                Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
                            };
                            editMenu.Children.Add(convertToCurveMenuItem);
                            if (!RightClickCommandDic.ContainsKey(convertToCurveMenuItem.CommandParameter))
                                RightClickCommandDic.Add(convertToCurveMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).ConvertToCurve()) });
                            break;
                        case "ConvertToImage":
                            var convertToImageMenuItem = new MenuItemViewModel
                            {
                                Header = "转影像(V)",
                                CommandParameter = "ConvertToImage",
                                Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
                            };
                            editMenu.Children.Add(convertToImageMenuItem);
                            if (!RightClickCommandDic.ContainsKey(convertToImageMenuItem.CommandParameter))
                                RightClickCommandDic.Add(convertToImageMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).ConvertToImage()) });
                            break;
                        case "ConvertToPoint":
                            var convertToPointMenuItem = new MenuItemViewModel
                            {
                                Header = "转成点",
                                CommandParameter = "ConvertToPoint",
                                Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
                            };
                            editMenu.Children.Add(convertToPointMenuItem);
                            if (!RightClickCommandDic.ContainsKey(convertToPointMenuItem.CommandParameter))
                            {
                                var settings = new ConvertToDotSettingsDto();
                                RightClickCommandDic.Add(convertToPointMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).ConvertToDot(settings)) });
                            }
                            break;
                        case "ExtendHeadAndTail":
                            var extendHeadAndTailMenuItem = new MenuItemViewModel
                            {
                                Header = "头尾点延伸",
                                CommandParameter = "ExtendHeadAndTail",
                                Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
                            };
                            editMenu.Children.Add(extendHeadAndTailMenuItem);
                            if (!RightClickCommandDic.ContainsKey(extendHeadAndTailMenuItem.CommandParameter))
                                RightClickCommandDic.Add(extendHeadAndTailMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).ExtendHeadAndTail()) });
                            break;
                        case "JumpPoint":
                            var jumpPointMenuItem = new MenuItemViewModel
                            {
                                Header = "跳点(J)",
                                CommandParameter = "JumpPoint",
                                Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
                            };
                            editMenu.Children.Add(jumpPointMenuItem);
                            if (!RightClickCommandDic.ContainsKey(jumpPointMenuItem.CommandParameter))
                            {
                                JumpSettingsDto jumpSettings = new JumpSettingsDto();
                                RightClickCommandDic.Add(jumpPointMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).SetJumpPoint(jumpSettings)) });
                            }
                            break;
                        case "Partition":
                            var partitionMenuItem = new MenuItemViewModel
                            {
                                Header = "依分区打断物件",
                                CommandParameter = "Partition",
                                Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
                            };
                            editMenu.Children.Add(partitionMenuItem);
                            if (!RightClickCommandDic.ContainsKey(partitionMenuItem.CommandParameter))
                            {
                                RightClickCommandDic.Add(partitionMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).Partition(50, 50, 0.6, 0.6)) });
                            }
                            break;
                        case "BreakFill":
                            var breakFillMenuItem = new MenuItemViewModel
                            {
                                Header = "打断填充物件(Y)",
                                CommandParameter = "BreakFill",
                                Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
                            };
                            editMenu.Children.Add(breakFillMenuItem);
                            if (!RightClickCommandDic.ContainsKey(breakFillMenuItem.CommandParameter))
                            {
                                RightClickCommandDic.Add(breakFillMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).BreakFill()) });
                            }
                            break;
                        case "Replace":
                            var replaceMenuItem = new MenuItemViewModel
                            {
                                Header = "取代(R)",
                                CommandParameter = "Replace",
                                Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
                            };
                            editMenu.Children.Add(replaceMenuItem);
                            if (!RightClickCommandDic.ContainsKey(replaceMenuItem.CommandParameter))
                            {
                                RightClickCommandDic.Add(replaceMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).Replace()) });
                            }
                            break;
                        case "Lock":
                            var headerName = "锁定物件(L)";
                            if (!IsLocked)
                            {
                                headerName = "锁定物件(L)";
                            }
                            else
                            {
                                headerName = "解锁物件(L)";
                            }
                            var lockMenuItem = new MenuItemViewModel
                            {
                                Header = headerName,
                                CommandParameter = "Lock",
                                Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
                            };
                            editMenu.Children.Add(lockMenuItem);
                            if (!RightClickCommandDic.ContainsKey(lockMenuItem.CommandParameter))
                            {
                                RightClickCommandDic.Add(lockMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).Lock()) });
                            }
                            break;
                        default:
                            break;
                    }
                }

                //// 更换不一样的命令
                //for (int i = editMenu.Children.Count - 1; i >= 0; i--)
                //{
                //    var child = editMenu.Children[i];
                //    if (child.CommandParameter != NeedToAddCommands[i])
                //    {
                //        switch (NeedToAddCommands[i])
                //        {
                //            case "ConvertToCurve":
                //                var convertToCurveMenuItem = new MenuItemViewModel
                //                {
                //                    Header = "转成曲线(Q)",
                //                    CommandParameter = "ConvertToCurve",
                //                    Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
                //                };
                //                editMenu.Children[i] = convertToCurveMenuItem;
                //                if (!RightClickCommandDic.ContainsKey(convertToCurveMenuItem.CommandParameter))
                //                    RightClickCommandDic.Add(convertToCurveMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).ConvertToCurve()) });
                //                break;
                //            case "ConvertToImage":
                //                var convertToImageMenuItem = new MenuItemViewModel
                //                {
                //                    Header = "转影像(V)",
                //                    CommandParameter = "ConvertToImage",
                //                    Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
                //                };
                //                editMenu.Children[i] = convertToImageMenuItem;
                //                if (!RightClickCommandDic.ContainsKey(convertToImageMenuItem.CommandParameter))
                //                    RightClickCommandDic.Add(convertToImageMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).ConvertToImage()) });
                //                break;
                //            case "ConvertToPoint":
                //                var convertToPointMenuItem = new MenuItemViewModel
                //                {
                //                    Header = "转成点",
                //                    CommandParameter = "ConvertToPoint",
                //                    Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
                //                };
                //                editMenu.Children[i] = convertToPointMenuItem;
                //                if (!RightClickCommandDic.ContainsKey(convertToPointMenuItem.CommandParameter))
                //                {
                //                    var settings = new ConvertToDotSettingsDto();
                //                    RightClickCommandDic.Add(convertToPointMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).ConvertToDot(settings)) });
                //                }
                //                break;
                //            case "ExtendHeadAndTail":
                //                var extendHeadAndTailMenuItem = new MenuItemViewModel
                //                {
                //                    Header = "头尾点延伸",
                //                    CommandParameter = "ExtendHeadAndTail",
                //                    Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
                //                };
                //                editMenu.Children[i] = extendHeadAndTailMenuItem;
                //                if (!RightClickCommandDic.ContainsKey(extendHeadAndTailMenuItem.CommandParameter))
                //                    RightClickCommandDic.Add(extendHeadAndTailMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).ExtendHeadAndTail()) });
                //                break;
                //            case "JumpPoint":
                //                var jumpPointMenuItem = new MenuItemViewModel
                //                {
                //                    Header = "跳点(J)",
                //                    CommandParameter = "JumpPoint",
                //                    Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
                //                };
                //                editMenu.Children[i] = jumpPointMenuItem;
                //                if (!RightClickCommandDic.ContainsKey(jumpPointMenuItem.CommandParameter))
                //                {
                //                    JumpSettingsDto jumpSettings = new JumpSettingsDto();
                //                    RightClickCommandDic.Add(jumpPointMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).SetJumpPoint(jumpSettings)) });
                //                }
                //                break;
                //            case "Partition":
                //                var partitionMenuItem = new MenuItemViewModel
                //                {
                //                    Header = "依分区打断物件",
                //                    CommandParameter = "Partition",
                //                    Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
                //                };
                //                editMenu.Children[i] = partitionMenuItem;
                //                if (!RightClickCommandDic.ContainsKey(partitionMenuItem.CommandParameter))
                //                {
                //                    RightClickCommandDic.Add(partitionMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).Partition(50, 50, 0.6, 0.6)) });
                //                }
                //                break;
                //            case "BreakFill":
                //                var breakFillMenuItem = new MenuItemViewModel
                //                {
                //                    Header = "打断填充物件(Y)",
                //                    CommandParameter = "BreakFill",
                //                    Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
                //                };
                //                editMenu.Children[i] = breakFillMenuItem;
                //                if (!RightClickCommandDic.ContainsKey(breakFillMenuItem.CommandParameter))
                //                {
                //                    RightClickCommandDic.Add(breakFillMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).BreakFill()) });
                //                }
                //                break;
                //            case "Replace":
                //                var replaceMenuItem = new MenuItemViewModel
                //                {
                //                    Header = "取代(R)",
                //                    CommandParameter = "Replace",
                //                    Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
                //                };
                //                editMenu.Children[i] = replaceMenuItem;
                //                if (!RightClickCommandDic.ContainsKey(replaceMenuItem.CommandParameter))
                //                {
                //                    RightClickCommandDic.Add(replaceMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).Replace()) });
                //                }
                //                break;
                //            case "Lock":
                //                var headerName = "锁定物件(L)";
                //                if (!IsLocked)
                //                {
                //                    headerName = "锁定物件(L)";
                //                }
                //                else
                //                {
                //                    headerName = "解锁物件(L)";
                //                }
                //                var lockMenuItem = new MenuItemViewModel
                //                {
                //                    Header = headerName,
                //                    CommandParameter = "Lock",
                //                    Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
                //                };
                //                editMenu.Children[i] = lockMenuItem;
                //                if (!RightClickCommandDic.ContainsKey(lockMenuItem.CommandParameter))
                //                {
                //                    RightClickCommandDic.Add(lockMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).Lock()) });
                //                }
                //                break;
                //            default:
                //                break;
                //        }
                //    }
                //}
            }
            else
            {
                // 需要移除多余的命令
                for (int i = editMenu.Children.Count - 1; i >= 0; i--)
                {
                    var child = editMenu.Children[i];
                    if (!NeedToAddCommands.Contains(child.CommandParameter))
                    {
                        editMenu.Children.RemoveAt(i);
                    }

                    if (child != null && child.CommandParameter == "Lock")
                    {
                        if (!IsLocked)
                        {
                            child.Header = "锁定物件(L)";
                        }
                        else
                        {
                            child.Header = "解锁物件(L)";
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"BuildNewEditMenu, Exception:{ex}，editMenu.Count:{editMenu.Children.Count},NeedToAddCommands.Count:{NeedToAddCommands.Count}");
        }
    }

    private MenuItemViewModel BuildCircleMenu()
    {
        var circleMenu = new MenuItemViewModel { Header = "圆形物件" };
        var sameRadiusMenuItem = new MenuItemViewModel
        {
            Header = "等半径(R)",
            CommandParameter = "SameRadius",
            Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
        };
        var setCircleRadiusMenuItem = new MenuItemViewModel
        {
            Header = "设置圆半径(E)",
            CommandParameter = "SetCircleRadius",
            Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
        };
        circleMenu.Children.Add(sameRadiusMenuItem);
        circleMenu.Children.Add(setCircleRadiusMenuItem);
        if (!RightClickCommandDic.ContainsKey(sameRadiusMenuItem.CommandParameter))
            RightClickCommandDic.Add(sameRadiusMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).SameRadius()) });
        if (!RightClickCommandDic.ContainsKey(setCircleRadiusMenuItem.CommandParameter))
            RightClickCommandDic.Add(setCircleRadiusMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).SetCircleRadius()) });
        return circleMenu;
    }

    private MenuItemViewModel BuildActiveNodeMenu()
    {
        var activeNodeMenu = new MenuItemViewModel { Header = "激活节点" };
        var editNodeMenuItem = new MenuItemViewModel
        {
            Header = "编辑节点",
            CommandParameter = "EditNode",
            Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
        };
        var seperator = new MenuItemViewModel { IsSeparator = true };
        var addNodeMenuItem = new MenuItemViewModel
        {
            Header = "新增节点(A)",
            CommandParameter = "AddNode",
            Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
        };
        var deleteNodeMenuItem = new MenuItemViewModel
        {
            Header = "删除节点(D)",
            CommandParameter = "DeleteNode",
            Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
        };
        var splitNodeMenuItem = new MenuItemViewModel
        {
            Header = "分离节点(B)",
            CommandParameter = "SplitNode",
            Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
        };
        var extendNodeMenuItem = new MenuItemViewModel
        {
            Header = "延伸节点(T)",
            CommandParameter = "ExtendNode",
            Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
        };
        var connectNodeMenuItem = new MenuItemViewModel
        {
            Header = "连接节点(C)",
            CommandParameter = "ConnectNode",
            Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
        };
        var selectNodeMenuItem = new MenuItemViewModel
        {
            Header = "框选节点(S)",
            CommandParameter = "SelectNode",
            Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
        };
        var setNodeMenuItem = new MenuItemViewModel
        {
            Header = "设定节点(S)",
            CommandParameter = "SetNode",
            Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
        };
        var seperator1 = new MenuItemViewModel { IsSeparator = true };
        var curveToLineMenuItem = new MenuItemViewModel
        {
            Header = "曲线转直线(L)",
            CommandParameter = "CurveToLine",
            Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
        };
        var lineToCurveMenuItem = new MenuItemViewModel
        {
            Header = "直线转曲线(C)",
            CommandParameter = "LineToCurve",
            Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
        };
        var arcToCurveMenuItem = new MenuItemViewModel
        {
            Header = "圆弧转曲线(K)",
            CommandParameter = "ArcToCurve",
            Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
        };
        var seperator2 = new MenuItemViewModel { IsSeparator = true };
        var sharpCornerMenuItem = new MenuItemViewModel
        {
            Header = "尖角(K)",
            CommandParameter = "SharpCorner",
            Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
        };
        var smoothMenuItem = new MenuItemViewModel
        {
            Header = "平滑(S)",
            CommandParameter = "Smooth",
            Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
        };
        var symmetryMenuItem = new MenuItemViewModel
        {
            Header = "对称(Y)",
            CommandParameter = "Symmetry",
            Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
        };

        activeNodeMenu.Children.Add(editNodeMenuItem);
        activeNodeMenu.Children.Add(seperator);
        activeNodeMenu.Children.Add(addNodeMenuItem);
        activeNodeMenu.Children.Add(deleteNodeMenuItem);
        activeNodeMenu.Children.Add(splitNodeMenuItem);
        activeNodeMenu.Children.Add(extendNodeMenuItem);
        activeNodeMenu.Children.Add(connectNodeMenuItem);
        activeNodeMenu.Children.Add(selectNodeMenuItem);
        activeNodeMenu.Children.Add(setNodeMenuItem);
        activeNodeMenu.Children.Add(seperator1);

        activeNodeMenu.Children.Add(curveToLineMenuItem);
        activeNodeMenu.Children.Add(lineToCurveMenuItem);
        activeNodeMenu.Children.Add(arcToCurveMenuItem);
        activeNodeMenu.Children.Add(seperator2);

        activeNodeMenu.Children.Add(sharpCornerMenuItem);
        activeNodeMenu.Children.Add(smoothMenuItem);
        activeNodeMenu.Children.Add(symmetryMenuItem);

        if (!RightClickCommandDic.ContainsKey(editNodeMenuItem.CommandParameter))
            RightClickCommandDic.Add(editNodeMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).EditNodes(true)) });
        if (!RightClickCommandDic.ContainsKey(addNodeMenuItem.CommandParameter))
            RightClickCommandDic.Add(addNodeMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).AddNodes(true)) });
        if (!RightClickCommandDic.ContainsKey(deleteNodeMenuItem.CommandParameter))
            RightClickCommandDic.Add(deleteNodeMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).DeleteNodes(true)) });
        if (!RightClickCommandDic.ContainsKey(splitNodeMenuItem.CommandParameter))
            RightClickCommandDic.Add(splitNodeMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).SeparateNodes(true)) });
        if (!RightClickCommandDic.ContainsKey(extendNodeMenuItem.CommandParameter))
            RightClickCommandDic.Add(extendNodeMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).ExtendNodes(true)) });
        if (!RightClickCommandDic.ContainsKey(connectNodeMenuItem.CommandParameter))
            RightClickCommandDic.Add(connectNodeMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).ConnectNodes(true)) });
        if (!RightClickCommandDic.ContainsKey(selectNodeMenuItem.CommandParameter))
            RightClickCommandDic.Add(selectNodeMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).SelectNodes(true)) });
        if (!RightClickCommandDic.ContainsKey(setNodeMenuItem.CommandParameter))
            RightClickCommandDic.Add(setNodeMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).SetNodes(true)) });

        if (!RightClickCommandDic.ContainsKey(curveToLineMenuItem.CommandParameter))
            RightClickCommandDic.Add(curveToLineMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).ConvertCurveToLine()) });
        if (!RightClickCommandDic.ContainsKey(lineToCurveMenuItem.CommandParameter))
            RightClickCommandDic.Add(lineToCurveMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).ConvertLineToCurve()) });
        if (!RightClickCommandDic.ContainsKey(arcToCurveMenuItem.CommandParameter))
            RightClickCommandDic.Add(arcToCurveMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).ConvertArcToCurve()) });

        if (!RightClickCommandDic.ContainsKey(sharpCornerMenuItem.CommandParameter))
            RightClickCommandDic.Add(sharpCornerMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).SetSharpCorner()) });
        if (!RightClickCommandDic.ContainsKey(smoothMenuItem.CommandParameter))
            RightClickCommandDic.Add(smoothMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).SetSmooth()) });
        if (!RightClickCommandDic.ContainsKey(symmetryMenuItem.CommandParameter))
            RightClickCommandDic.Add(symmetryMenuItem.CommandParameter, new CanvasRightClickCommand<bool, GraphicResult> { FuncResult = new Func<GraphicResult>(() => (Context.ActiveCanvas as DrawingCanvas).SetSymmetry()) });


        return activeNodeMenu;
    }

    public void MenuCommandExcute(string cmdName)
    {
        if (RightClickCommandDic.TryGetValue(cmdName, out var command))
        {
            if (command.FuncResultWithParamIn != null)
            {
                command.FuncResultWithParamIn.Invoke(true);
            }
            else if (command.FuncResult != null)
            {
                var result = command.FuncResult?.Invoke();
                if (cmdName == "Lock" && result.IsSuccess)
                {
                    var editMenu = MenuItems.FirstOrDefault(m => m.Header == "编辑");
                    if (editMenu != null)
                    {
                        var lockItem = editMenu.Children.FirstOrDefault(m => m.CommandParameter == "Lock");

                        if (lockItem != null)
                        {
                            var index = editMenu.Children.IndexOf(lockItem);
                            var lockMenuItem = new MenuItemViewModel
                            {
                                Header = lockItem.Header.Contains("锁定") ? "解锁物件(L)" : "锁定物件(L)",
                                CommandParameter = "Lock",
                                Command = new RelayCommand<string>(MenuCommandExcute, MenuCommandCanExcute)
                            };

                            WeakReferenceMessenger.Default.Send<string>(lockMenuItem.Header);

                            editMenu.Children[index] = lockMenuItem;
                        }
                    }
                }
            }
        }

        WeakReferenceMessenger.Default.Send(new CloseMenuMessage());
    }

    public bool MenuCommandCanExcute(string cmdName)
    {
        // 根据业务逻辑返回 true/false
        bool canExcute = false;
        if (cmdName != null && RightClickCommandDic.TryGetValue(cmdName, out var command))
        {
            canExcute = command.IsEnabled;
        }
        Debug.WriteLine($"MenuCommandCanExcute,cmdName:{cmdName}, canExcute:{canExcute}");
        return canExcute;
    }

    private void MenuClick(object sender, RoutedEventArgs e)
    {
        WeakReferenceMessenger.Default.Send(new CloseMenuMessage());
    }
    #endregion

    /// <summary>
    /// 切换到选择工具并更新UI状态
    /// </summary>
    public void SwitchToSelectTool(SKElement skiaCanvas)
    {
        Context.ActiveTool = _selectTool;
        UpdateToolbarButtonState("Select");

        // 设置选择工具的光标为 pointer
        if (skiaCanvas != null)
        {
            Context.SetCursor(CanvasCursorFactory.GetCursor("pointer", Cursors.Arrow));
            _currentSkiaCanvas = skiaCanvas; // 更新引用
        }

        Context.ReportStatus("切换到选择工具");
        Redraw();
    }

    /// <summary>
    /// 清空选中状态
    /// </summary>
    public void ClearSelection()
    {
        var selectedShapes = Context.ActiveCanvas?.Selection;
        if (selectedShapes.Count() > 0)
        {
            Context.ActiveCanvas.ClearSelectedShapes();
            Context.ReportStatus("已清空选中状态");
            Redraw();
        }

    }

    /// <summary>
    /// 更新工具栏按钮的选中状态
    /// </summary>
    /// <param name="toolTag">工具的Tag标识</param>
    private void UpdateToolbarButtonState(string toolTag)
    {
        ActiveToolName = toolTag;
        if (toolTag != "Select" && toolTag != "Node")
        {
            // 如果切换到非选择工具，清空选中状态
            ClearSelection();
        }
    }

    #region 鼠标和键盘输入
    public void HandleMouseDown(SKElement skiaCanvas, SKPoint p, MouseButtonEventArgs e)
    {
        if (Context.IsApplyingDeferredDragCommit) return;
        if (e.ChangedButton != MouseButton.Left) return;

        // 检查鼠标是否超出画布范围
        if (skiaCanvas != null && (p.X < 0 || p.Y < 0 || p.X > skiaCanvas.ActualWidth || p.Y > skiaCanvas.ActualHeight))
        {
            // 鼠标超出画布范围，不处理鼠标按下事件
            return;
        }

        var point = Context.ActiveCanvas?.Viewport.ScreenToWorld(p.X, p.Y) ?? new SKPoint(0, 0);
        //System.Diagnostics.Debug.WriteLine($"[MouseDown] ({point.X}, {point.Y})");
        Context.ActiveTool.OnMouseDown(point);

        // 优化：按当前工具状态决定是否需要重绘
        // 点击空白（无选中变化）不刷新；点击图形按脏区局部刷新
        if (Context.ActiveTool?.NeedRedrawOnDown ?? true)
        {
            Redraw();
        }
    }

    public void HandleMouseRightDown(SKElement skiaCanvas, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Right)
        {
            if (Context.ActiveTool.ToolType != ToolType.Select)
            {
                Context.ActiveTool.OnMouseRightDown();
            }
            else
            {
                // 右键时，如果处于节点编辑子模式（添加/删除/分离），重置为 None 以恢复光标
                if (Context.IsNodeEditing && Context.NodeEditSubMode != NodeEditSubMode.None)
                {
                    if (Context.ActiveTool is ToolSelect toolSelect)
                        toolSelect.SetNodeEditSubMode(NodeEditSubMode.None);
                }

                var pos = new Point(0, 0);

                WeakReferenceMessenger.Default.Send(new OpenMenuMessage(pos));

                //右键菜单
                Debug.WriteLine($"右键菜单打开");
            }
        }
    }

    public void HandleMouseMove(SKElement skiaCanvas, SKPoint p)
    {
        if (Context.IsApplyingDeferredDragCommit) return;
        _currentSkiaCanvas = skiaCanvas;
        bool isInCanvasBounds = skiaCanvas != null &&
                                p.X >= 0 && p.Y >= 0 &&
                                p.X <= skiaCanvas.ActualWidth &&
                                p.Y <= skiaCanvas.ActualHeight;

        // 鼠标已经在拖拽/框选中时，越界也要继续把事件交给工具层，
        // 否则鼠标离开画布后会卡在“正在拖拽”状态。
        bool hasActiveInteraction = Context.IsDrawing || Context.BoxSelect.IsActive;
        if (!isInCanvasBounds && !hasActiveInteraction)
        {
            Context.SetCursor(CanvasCursorFactory.GetCursor("pointer", Cursors.Arrow));
            return;
        }

        var point = Context.ActiveCanvas?.Viewport.ScreenToWorld(p.X, p.Y) ?? new SKPoint(0, 0);


        Context.ActiveTool.OnMouseMove(point);

        CoordinateText = $"{point.X:F1}, {point.Y:F1}";

        _renderPipeline.MousePoint = point;  // expose MousePoint setter
        if (!(Context.ActiveTool?.NeedRedrawOnMove ?? true))
        {
            return;
        }
        Redraw(); // 触发 Skia 的 OnPaint -> RenderPipeline.Render
    }

    public void HandleMouseUp(SKElement skiaCanvas, SKPoint p, MouseButtonEventArgs e)
    {
        if (Context.IsApplyingDeferredDragCommit) return;
        // 检查鼠标是否超出画布范围
        bool isOutOfBounds = skiaCanvas != null &&
                             (p.X < 0 || p.Y < 0 || p.X > skiaCanvas.ActualWidth || p.Y > skiaCanvas.ActualHeight);
        if (isOutOfBounds && !(Context.IsDrawing || Context.BoxSelect.IsActive))
        {
            // 鼠标超出画布范围，不处理鼠标释放事件
            return;
        }

        var point = Context.ActiveCanvas?.Viewport.ScreenToWorld(p.X, p.Y) ?? new SKPoint(0, 0);
        if (_panning && e.ChangedButton == MouseButton.Middle)
        {
            _panning = false;
            Context.SetCursor(Cursors.Cross);
            return;
        }

        Context.ActiveTool.OnMouseUp(point);
        if (e.ChangedButton == MouseButton.Left)
        {
            Context.ActiveTool.OnLeftMounseUp(p);
        }
        else if(e.ChangedButton == MouseButton.Right)
        {
            SwitchToSelectTool(skiaCanvas);
        }


        // 优化：按当前工具状态决定是否需要重绘
        // 拖拽完成/控制点结束已由 PublishTransformChange 触发重绘；仅单击无视觉变化时跳过
        if (Context.ActiveTool?.NeedRedrawOnUp ?? true)
        {
            Context.RequestRedraw();
        }
    }

    // ── 滚轮缩放节流状态 ───────────────────────────────────────
    private System.Windows.Threading.DispatcherTimer? _zoomRedrawTimer;
    private bool _zoomGestureSavedViewport;

    public void HandleMouseWheel(SKPoint p, int delta)
    {
        var point = Context.ActiveCanvas?.Viewport.ScreenToWorld(p);
        if (!point.HasValue) return;

        // 每次缩放手势（从静止到停止）仅保存一次视口历史，
        // 避免快速连续滚动时栈被 30+ 个几乎相同的条目填满。
        if (!_zoomGestureSavedViewport)
        {
            _zoomTool.SaveViewportState();
            _zoomGestureSavedViewport = true;
        }

        // 立即更新视口（O(1)，保证缩放手感跟手），但不触发重量级重绘。
        var screenPt = Context.ActiveCanvas?.Viewport.WorldToScreen(point.Value) ?? new SKPoint(0, 0);
        if (!Context.IsApplyingDeferredDragCommit)
        {
            Context.ActiveCanvas?.Viewport.ZoomAt(delta > 0 ? 1.25f : 1.0f / 1.25f, screenPt.X, screenPt.Y);
        }

        // 节流重绘：8ms 内合并多次滚轮事件为一次渲染，
        // 视口已即时更新，用户感知不到延迟，但避免了 30+ 次/秒的全量重绘。
        if (_zoomRedrawTimer == null)
        {
            _zoomRedrawTimer = new System.Windows.Threading.DispatcherTimer(
                TimeSpan.FromMilliseconds(8),
                System.Windows.Threading.DispatcherPriority.Render,
                OnZoomRedrawTimer,
                System.Windows.Threading.Dispatcher.CurrentDispatcher);
        }
        _zoomRedrawTimer.Stop();
        _zoomRedrawTimer.Start();
    }

    private void OnZoomRedrawTimer(object? sender, EventArgs e)
    {
        _zoomRedrawTimer!.Stop();

        // 标记缩放手势结束，下次滚轮时重新保存视口历史
        _zoomGestureSavedViewport = false;
        _zoomTool.CanZoomBack = true;

        // 执行重量级通知 + 重绘（EventBus、缓存失效、InvalidateVisual）
        _zoomTool.NotifyZoomChanged();
    }

    // Middle-button pan state
    private bool _panning;
    private float _panLastX, _panLastY;  // 世界坐标，用于工具操作
    private float _panLastScreenX, _panLastScreenY;  // 屏幕坐标，用于中键平移

    public void HandleMiddleDown(SKPoint p)
    {
        if (Context.IsApplyingDeferredDragCommit) return;
        _panning = true;
        _panLastScreenX = p.X;
        _panLastScreenY = p.Y;
    }

    public void HandleMiddleUp() => _panning = false;

    public void HandleMiddleMove(SKPoint p)
    {
        if (Context.IsApplyingDeferredDragCommit) return;
        var screenDX = (float)(p.X - _panLastScreenX);
        var screenDY = (float)(p.Y - _panLastScreenY);
        Context.ActiveCanvas?.Viewport.Pan(screenDX, screenDY);
        _panLastScreenX = p.X;
        _panLastScreenY = p.Y;

        // 更新坐标文本和状态文本
        var skPoint = Context.ActiveCanvas?.Viewport.ScreenToWorld(new SKPoint((float)p.X, (float)p.Y)) ?? new SKPoint(0, 0);
        CoordinateText = $"X: {skPoint.X:F1}, Y: {skPoint.Y:F1}";
        Redraw();
    }

    public void HandleKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (Context.IsApplyingDeferredDragCommit) return;
        if (Context.ActiveCanvas == null) return;

        if (e.Key == Key.Delete)
        {
            DeleteSelectedShapes();
            e.Handled = true;
            return;
        }

        var activeTool = Context.ActiveTool;
        var toolHandled = activeTool?.OnKeyDown(e.Key) ?? false;
        if (toolHandled)
        {
            e.Handled = true;
            Redraw();
            return;
        }

        if (!TryGetKeyboardMoveDelta(e.Key, Context.IsShiftPressed(), out float dx, out float dy))
            return;

        e.Handled = true;
        TryMoveSelectedShapesByKeyboard(dx, dy);
    }

    internal bool TryMoveSelectedShapesByKeyboard(float dx, float dy)
    {
        var activeCanvas = Context.ActiveCanvas as DrawingCanvas;
        if (activeCanvas == null)
        {
            return false;
        }

        var selectedShapes = activeCanvas.Selection
            .OfType<DrawObject>()
            .Where(shape => !shape.IsLocked)
            .ToList();
        if (selectedShapes.Count == 0)
        {
            return false;
        }

        Context.MarkSelectedDirty();

        activeCanvas.ExecuteTransformCommand(
            selectedShapes,
            "移动图形",
            () => selectedShapes.TranslateAllBy(dx, dy),
            includesChildren: false);

        Context.ReportStatus($"移动图形 {dx:F1}, {dy:F1}");
        return true;
    }

    internal bool TryGetKeyboardMoveDelta(Key key, bool isShiftPressed, out float dx, out float dy)
    {
        float multiplier = isShiftPressed ? 10f : 1f;
        float stepx = Context.KeysMoveSharpsStepX * multiplier;
        float stepy = Context.KeysMoveSharpsStepY * multiplier;

        dx = 0f;
        dy = 0f;

        switch (key)
        {
            case Key.Up:
                dy = stepy;
                return true;
            case Key.Down:
                dy = -stepy;
                return true;
            case Key.Left:
                dx = -stepx;
                return true;
            case Key.Right:
                dx = stepx;
                return true;
            default:
                return false;
        }
    }
    #endregion

    public void CanvasTab_Click(MouseButtonEventArgs e)
    {
        if (e.Source is Border border && border.DataContext is DrawingCanvas canvas)
        {
            SwitchToCanvas(canvas);
        }
        else if (e.Source is TextBlock tb)
        {
            var targetCanvas = CanvasList.FirstOrDefault(c => c.Id == (int)tb.Tag);
            SwitchToCanvas(targetCanvas);
        }
    }

    private void SwitchToCanvas(DrawingCanvas? canvas)
    {
        if (canvas == null || canvas == Context.ActiveCanvas) return;

        foreach (var c in CanvasList)
        {
            c.IsActive = false;
            OnPropertyChanged(nameof(c.IsActive));
        }
        canvas.IsActive = true;
        OnPropertyChanged(nameof(canvas.IsActive));

        Context.ActiveCanvas = canvas;

        // 切换到新画布的图层
        _multiCanvas.SwitchCanvas(canvas);

        Redraw();
        Context.ReportStatus($"切换到 {canvas.Name}");
    }

    [RelayCommand]
    private void SwitchToPreviousCanvas()
    {
        if (CanvasList.Count <= 1) return;
        var currentIndex = CanvasList.IndexOf(Context.ActiveCanvas as DrawingCanvas);
        if (currentIndex > 0)
        {
            var previousCanvas = CanvasList[currentIndex - 1];
            SwitchToCanvas(previousCanvas);
        }
    }

    [RelayCommand]
    private void SwitchToNextCanvas()
    {
        if (CanvasList.Count <= 1) return;
        var currentIndex = CanvasList.IndexOf(Context.ActiveCanvas as DrawingCanvas);
        if (currentIndex >= 0 && currentIndex < CanvasList.Count - 1)
        {
            var nextCanvas = CanvasList[currentIndex + 1];
            SwitchToCanvas(nextCanvas);
        }
    }

    public void CloseCanvas_Click(object sender, RoutedEventArgs e)
    {
        var btn = sender as System.Windows.Controls.Button;
        var canvas = btn?.Tag as DrawingCanvas;

        PublishCanvasChange(canvas, CanvasChangeType.BeforeRemove);
        _multiCanvas?.CloseSelectCanvas(canvas?.Id ?? 0);
    }

    [RelayCommand]
    public void DuplicateCanvas(int canvasId)
    {
        _multiCanvas?.DuplicateCanvas(canvasId);
        Context.ReportStatus("已复制画布");
    }

    // ── Document operations ───────────────────────────────────────────────
    internal bool TryRenameCanvas(DrawingCanvas canvas, string? proposedName, out string normalizedName)
    {
        normalizedName = NormalizeCanvasName(proposedName);
        if (canvas == null)
        {
            Context.ReportStatus("重命名失败：画布不存在");
            return false;
        }

        if (normalizedName.Length == 0)
        {
            Context.ReportStatus("画布名称不能为空");
            return false;
        }

        if (normalizedName.Length > MaxCanvasNameLength)
        {
            Context.ReportStatus($"画布名称不能超过 {MaxCanvasNameLength} 个字符");
            return false;
        }

        if (normalizedName.StartsWith('\'') || normalizedName.EndsWith('\''))
        {
            Context.ReportStatus("画布名称不能以单引号开头或结尾");
            return false;
        }

        if (ContainsInvalidCanvasNameChars(normalizedName))
        {
            Context.ReportStatus("画布名称不能包含 : \\ / ? * [ ]");
            return false;
        }

        var candidateName = normalizedName;
        bool duplicated = CanvasList.Any(existing =>
            !ReferenceEquals(existing, canvas) &&
            string.Equals(existing.Name, candidateName, StringComparison.OrdinalIgnoreCase));
        if (duplicated)
        {
            Context.ReportStatus($"画布名称“{normalizedName}”已存在");
            return false;
        }

        if (string.Equals(canvas.Name, normalizedName, StringComparison.Ordinal))
        {
            return true;
        }

        canvas.Name = normalizedName;
        PublishCanvasChange(canvas, CanvasChangeType.Renamed);
        Context.ReportStatus($"已将画布重命名为 {normalizedName}");
        return true;
    }

    internal static bool IsCanvasNameInputFragmentValid(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        return !ContainsInvalidCanvasNameChars(text);
    }

    private static string NormalizeCanvasName(string? name)
    {
        return (name ?? string.Empty).Trim();
    }

    private static bool ContainsInvalidCanvasNameChars(string text)
    {
        return text.IndexOfAny(InvalidCanvasNameChars) >= 0;
    }

    public bool Undo()
    {
        if (Context.ActiveCanvas == null) return false;
        Context.ActiveCanvas.CommandHistory.Undo();
        // 撤销后清理选中状态，移除不再存在的形状
        CleanupSelectedShapes();
        Redraw();
        return true;
    }

    public bool Redo()
    {
        if (Context.ActiveCanvas == null) return false;
        Context.ActiveCanvas.CommandHistory.Redo();
        // 重做后清理选中状态
        CleanupSelectedShapes();
        Redraw();
        return true;
    }

    /// <summary>
    /// 清理选中状态，移除不再存在于画布中的形状
    /// 这个方法主要用于处理特殊情况，常规的撤销/重做由各个命令自己处理
    /// </summary>
    private void CleanupSelectedShapes()
    {
        if (Context.ActiveCanvas == null) return;
        // 检查是否有需要清理的形状
        var selectedShaps = Context.ActiveCanvas.AllShapes.Where(it => it.IsVisible);

        // 只有在确实需要时才进行清理
        if (selectedShaps.Count() > 0)
        {
            var shapesToRemove = new List<IShape>();
            foreach (var selectedShape in selectedShaps)
            {
                selectedShape.IsSelected = false;
            }
        }
    }

    /// <summary>
    /// 删除所有选中的形状
    /// </summary>
    public void DeleteSelectedShapes()
    {
        if (Context.ActiveCanvas == null)
        {
            Context.ReportStatus("没有选中的形状");
            return;
        }

        try
        {
            // 获取要删除的形状
            var shapesToDelete = Context.ActiveCanvas.Selection;

            var canvas = (DrawingCanvas)Context.ActiveCanvas;
            var removeCommand = new CommandRemove(canvas.LayerViewModels, shapesToDelete);
            canvas.CommandHistory.Execute(removeCommand);

            Context.ActiveCanvas.ClearSelectedShapes();


            if (shapesToDelete.Count() > 0)
            {
                Context.ReportStatus($"删除了 {shapesToDelete.Count()} 个形状");
                Redraw();
            }
            else
            {
                Context.ReportStatus("没有找到要删除的形状");
            }
        }
        catch (Exception ex)
        {
            Context.ReportStatus($"删除失败: {ex.Message}");
        }
    }

    // ── Rendering ─────────────────────────────────────────────────────────
    public void Render(SKCanvas canvas, SKImageInfo info)
    {
        _renderPipeline.Render(canvas, info, Context);
    }

    public void Redraw() =>
        RedrawRequested?.Invoke(this, EventArgs.Empty);

    // ── MultiCanvas事件处理 ───────────────────────────────────────────────
    private LayerViewViewModel? _currentLayerVm;
    private LayerViewModel? _previousActiveLayer;
    private bool _isBuildingMenu;

    /// <summary>
    /// 订阅当前活动画布的图层事件
    /// </summary>
    private void SubscribeToLayerEvents()
    {
        // 取消旧订阅
        if (_currentLayerVm != null)
        {
            _currentLayerVm.OnLayerChanged -= OnLayersChangedForToolbar;
            _currentLayerVm.PropertyChanged -= OnLayerVmPropertyChanged;
        }
        if (_previousActiveLayer != null)
        {
            _previousActiveLayer.PropertyChanged -= OnActiveLayerLockedChanged;
        }

        // 获取当前画布的 LayerViewViewModel
        _currentLayerVm = Context.ActiveCanvas is DrawingCanvas dc
            ? dc.LayerViewViewModel : null;

        if (_currentLayerVm != null)
        {
            _currentLayerVm.OnLayerChanged += OnLayersChangedForToolbar;
            _currentLayerVm.PropertyChanged += OnLayerVmPropertyChanged;

            // 订阅 ActiveLayer 的 PropertyChanged（用于监听 IsLocked 变化）
            _previousActiveLayer = _currentLayerVm.ActiveLayer;
            if (_previousActiveLayer != null)
            {
                _previousActiveLayer.PropertyChanged += OnActiveLayerLockedChanged;
            }
        }
        else
        {
            _previousActiveLayer = null;
        }

        UpdateDrawingToolEnabled();
    }

    private void OnActiveCanvasChangedForToolbar(object? sender, EventArgs e)
    {
        SubscribeToLayerEvents();
    }

    private void OnLayersChangedForToolbar(object? sender, EventArgs e)
    {
        UpdateDrawingToolEnabled();
    }

    private void OnLayerVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LayerViewViewModel.ActiveLayer))
        {
            // 取消旧 ActiveLayer 的订阅
            if (_previousActiveLayer != null)
            {
                _previousActiveLayer.PropertyChanged -= OnActiveLayerLockedChanged;
            }

            // 订阅新 ActiveLayer 的 IsLocked 变化
            _previousActiveLayer = _currentLayerVm?.ActiveLayer;
            if (_previousActiveLayer != null)
            {
                _previousActiveLayer.PropertyChanged += OnActiveLayerLockedChanged;
            }

            UpdateDrawingToolEnabled();
        }
    }

    private void OnActiveLayerLockedChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LayerViewModel.IsLocked))
        {
            UpdateDrawingToolEnabled();
        }
    }

    /// <summary>
    /// 根据当前活动图层的锁定状态更新绘图工具可用性
    /// </summary>
    private void UpdateDrawingToolEnabled()
    {
        var activeLayer = _currentLayerVm?.ActiveLayer;

        IsDrawingToolEnabled = activeLayer == null || !activeLayer.IsLocked;

        // 如果绘图工具被禁用，自动切换到选择工具
        if (!IsDrawingToolEnabled && ActiveToolName != "Select")
        {
            SelectToolCommand.Execute("Select");
        }
    }

    /// <summary>
    /// 处理画布变化事件，当选中状态变为非 Combination 单选时，
    /// 若当前处于节点编辑模式则自动退出（调用 EditNodes(false)）。
    /// 节点编辑模式的进入/退出由 EditPathNodesToolViewModel 通过 IShapeService 驱动，
    /// CanvasViewModel 只负责"失去可编辑选区时强制退出"的保障逻辑。
    /// </summary>
    /// <summary>
    /// 重入保护标志位，防止 OnCanvasChangedForNodeTool 递归调用
    /// </summary>
    private bool _isInNodeToolCanvasChanged = false;

    private void OnCanvasChangedForNodeTool(CanvasChangedEvent data)
    {
        if (data.ChangeType != CanvasChangeType.SelectChanged) return;

        // 防止递归调用：如果已经在处理中，直接返回
        if (_isInNodeToolCanvasChanged) return;

        var selectedObjects = data.Data as Dictionary<ShapeType, SelectChangedInfo>;
        bool canEdit = selectedObjects != null &&
                       selectedObjects.Count == 1 &&
                       selectedObjects.FirstOrDefault().Key == ShapeType.Combination &&
                       selectedObjects.FirstOrDefault().Value.Count == 1;

        // 若选区不再满足可编辑条件，且当前处于节点编辑状态，则强制退出
        if (!canEdit && (Context.ActiveCanvas is DrawingCanvas canvas) && Context.IsNodeEditing)
        {
            _isInNodeToolCanvasChanged = true;
            try
            {
                canvas.EditNodes(false);
            }
            finally
            {
                _isInNodeToolCanvasChanged = false;
            }
        }
    }

    private void OnCanvasCreated(object? sender, DrawingCanvas canvas)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            CanvasList.Add(canvas);
            PublishCanvasChange(canvas, CanvasChangeType.Created);
        });
    }

    private void PublishCanvasChange(ICanvas? canvas, CanvasChangeType changeType, object? data = null)
    {
        eventBus?.Publish(new CanvasChangedEvent
        {
            CanvasId = canvas == null ? null : canvas.Id,
            CanvasName = canvas == null ? null : canvas.Name,
            Data = data,
            ChangeType = changeType
        });

        if (changeType == CanvasChangeType.Command)
        {
            Context.PublishSelectChanged();
        }

        if (changeType == CanvasChangeType.Switched)
        {
            Context.PublishCanvasChange(Context.ActiveCanvas, CanvasChangeType.Command, null);
        }
    }

    private void OnCanvasRemoved(object? sender, DrawingCanvas canvas)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            // 如果被移除的画布是当前活动画布，先清除 DocumentContext 中的引用
            if (Context.ActiveCanvas == canvas)
                Context.ActiveCanvas = null;

            // 如果当前订阅的图层事件属于被移除的画布，先取消订阅
            if (_currentLayerVm != null && canvas.LayerViewViewModel == _currentLayerVm)
            {
                _currentLayerVm.OnLayerChanged -= OnLayersChangedForToolbar;
                _currentLayerVm.PropertyChanged -= OnLayerVmPropertyChanged;
                _currentLayerVm = null;
            }
            if (_previousActiveLayer != null)
            {
                _previousActiveLayer.PropertyChanged -= OnActiveLayerLockedChanged;
                _previousActiveLayer = null;
            }

            CanvasList.Remove(canvas);
            PublishCanvasChange(canvas, CanvasChangeType.Removed);

            // 释放画布资源，断开所有引用链
            canvas.Dispose();

            // 强制 GC 回收被移除画布占用的内存
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        });
    }

    private void OnActiveCanvasChanged(object? sender, DrawingCanvas canvas)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (canvas == null)
            {
                Context.ActiveCanvas = null;
                CanvasList.Clear();
                Redraw();  // 触发重绘以显示灰色背景
                UpdateCanvasToolsStatus(false);
                PublishCanvasChange(null, CanvasChangeType.NoCanvas);
            }
            else
            {
                // 更新当前活动画布
                Context.ActiveCanvas = canvas;
                _zoomTool.ResetViewportState();
                _zoomTool.NotifyZoomChanged();
                UpdateCanvasToolsStatus(true);
                PublishCanvasChange(canvas, CanvasChangeType.Switched);
                PublishCanvasChange(canvas, CanvasChangeType.Command);
                Redraw();
            }
        });
    }

    private void UpdateCanvasToolsStatus(bool enable)
    {
        IsSelectEnabled = enable;
        IsDrawingToolEnabled = enable;
        IsMoveEnabled = enable;
    }

    public void Dispose()
    {
        //CommandManager.HistoryChanged -= OnHistoryChanged;

        // 取消订阅MultiCanvas事件
        if (_multiCanvas != null)
        {
            _multiCanvas.CanvasCreated -= OnCanvasCreated;
            _multiCanvas.CanvasRemoved -= OnCanvasRemoved;
            _multiCanvas.ActiveCanvasChanged -= OnActiveCanvasChanged;
        }

        // 取消订阅 EventBus
        EventBus.Instance.Unsubscribe<CanvasChangedEvent>(OnCanvasChangedForNodeTool);
        EventBus.Instance.Unsubscribe<ViewportChangedEvent>(OnViewportChanged);

        // 取消订阅 DocumentContext 事件
        Context.ActiveCanvasChanged -= OnActiveCanvasChangedForToolbar;

        // 取消订阅图层事件
        if (_currentLayerVm != null)
        {
            _currentLayerVm.OnLayerChanged -= OnLayersChangedForToolbar;
            _currentLayerVm.PropertyChanged -= OnLayerVmPropertyChanged;
        }
        if (_previousActiveLayer != null)
        {
            _previousActiveLayer.PropertyChanged -= OnActiveLayerLockedChanged;
        }
    }

    #region 机台范围设置命令

    /// <summary>
    /// 设置默认机台范围 (200x200，居中显示，正负100)
    /// </summary>
    public void SetDefaultMachineBounds(Rect2D? rect = null)
    {
        if (Context.ActiveCanvas is DrawingCanvas drawingCanvas)
        {
            drawingCanvas.MachineBounds = rect ?? new Rect2D(-100, -100, 200, 200);
            Redraw();
        }
    }
    #endregion
}
