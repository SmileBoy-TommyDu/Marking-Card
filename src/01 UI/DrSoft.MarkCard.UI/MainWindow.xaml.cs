using DrSoft.Docking.Enum;
using DrSoft.Docking.Interface;
using DrSoft.Docking.LayoutSetting;
using DrSoft.Drawing.Controls;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Utility;
using DrSoft.MarkCard.CommonUI.UserControls;
using DrSoft.MarkCard.Event;
using DrSoft.MarkCard.Service;
using DrSoft.MarkCard.UI.Models;
using DrSoft.MarkCard.UI.UserControls;
using DrSoft.MarkCard.UI.ViewModes;
using DrSoft.MarkCard.UI.Views;
using DrSoft.MarkCard.UI.Views.Config;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media; // for VisualTreeHelper
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;

namespace DrSoft.MarkCard.UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // Dock 源引用
        private IDockSource? _toolboxSource;
        private IDockSource? _layersSource;
        private IDockSource? _canvasSource;
        private IDockSource? _parametersTabSource;
        private IDockSource? _engravingToolSource;
        //private IDockSource? _laserToolbarSource;
        private IDockSource? _transformToolbarSource;
        private IDockSource? _sizeToolbarSource;


        // 明确使用 DrSoft.Drawing.Controls 中的 DrawingContext，避免与 System.Windows.Media.DrawingContext 冲突
        private readonly DrSoft.Drawing.Controls.DrawingContext _ctx;
        private readonly ToolbarViewModel _toolbarViewModel;
        private readonly ILogger<MainWindow> _logger;
        private readonly MarkParamService _paramService;
        private readonly CanvasViewModel _canvasViewModel;
        private readonly Queue<DateTime> _debugStatusClickTimestamps = new();
        private DebugLogWindow? _debugLogWindow;

        // 记录自定义最大化状态
        private bool _isCustomMaximized = false;
        // 记录窗口正常状态下的位置和大小（用于还原）
        private Rect _normalRect;

        public MainWindow(MainViewModel vm, ToolbarViewModel toolbarViewModel, CanvasViewModel canvasViewModel, MarkParamService paramService, ILogger<MainWindow> logger)
        {
            InitializeComponent();

            SystemParameters.StaticPropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SystemParameters.WorkArea))
                {
                    // Handle work area change
                    this.Dispatcher.Invoke(() =>
                    {
                        // Adjust window position if necessary
                        var workArea = SystemParameters.WorkArea;

                        this.Left = 0.0;
                        this.Top = 0.0;
                        this.Height = workArea.Height;
                        this.Width = workArea.Width;
                    });
                }
            };

            #region 设置窗口样式，去掉系统自带的最大化按钮,设置自己的最大化按钮的状态
            var workArea = SystemParameters.WorkArea;
            this.Left = 0.0;
            this.Top = 0.0;
            this.Height = workArea.Height;
            this.Width = workArea.Width;

            _isCustomMaximized = true;
            // 更新按钮图标（如果你的按钮名为 MaximizeButton）
            if (this.MaximizeButton != null)
            {
                this.MaximizeButton.NormalImage = _isCustomMaximized ? new BitmapImage(new Uri("/Resource/image/NormalButton.png", UriKind.Relative)) : new BitmapImage(new Uri("/Resource/image/MaximizeButton.png", UriKind.Relative));               
            }

            // 保存当前正常状态
            _normalRect = new Rect(this.Left, this.Top, this.Width - 300, this.Height - 280);
            #endregion

            _paramService = paramService;
            _logger = logger;
            _toolbarViewModel = toolbarViewModel;
            _canvasViewModel = canvasViewModel;

            _ctx = DrSoft.Drawing.Controls.DrawingContext.Create(App.Instance.Services);

            DataContext = vm;
            AllowDrop = true;
            Loaded += OnLoaded;
            Closing += OnClosing;

            this.Deactivated += OnApplicationDeactivated;

            this.PreviewKeyDown += (s, e) =>
            {
                //Debug.WriteLine($"Preview: Key={e.Key}, Modifiers={Keyboard.Modifiers}, Handled={e.Handled}");
            };
            this.PreviewKeyDown += OnGlobalPreviewKeyDown;

            // 监听画布切换事件，更新图层控件的 DataContext
            canvasViewModel.MultiCanvas.ActiveCanvasChanged += OnActiveDocumentChanged;
            // 监听画布移除事件，及时清除图层控件的 DataContext 引用，避免内存泄漏
            canvasViewModel.MultiCanvas.CanvasRemoved += OnDocumentRemoved;
            canvasViewModel.StatusReported += OnCanvasStatusReported;
            canvasViewModel.PropertyChanged += OnCanvasViewModelPropertyChanged;

            UpdateStatusText(canvasViewModel.StatusText);
            UpdateCoordinateText(canvasViewModel.CoordinateText);
        }

        private void OnApplicationDeactivated(object? sender, EventArgs e)
        {
            PopupManagement.CloseAllPopups();
        }

        private void OnCanvasStatusReported(object? sender, string status)
        {
            UpdateStatusText(status);
        }

        private void OnCanvasViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            var propertyName = e.PropertyName;
            var isCoordinateTextChanged = propertyName == nameof(CanvasViewModel.CoordinateText);
            if (!isCoordinateTextChanged)
            {
                return;
            }

            var coordinateText = _canvasViewModel.CoordinateText;
            UpdateCoordinateText(coordinateText);
        }

        private void UpdateStatusText(string status)
        {
            Dispatcher.Invoke(() => StatusText.Text = status);
        }

        private void UpdateCoordinateText(string coordinateText)
        {
            Dispatcher.Invoke(() => CoordinateText.Text = coordinateText);
        }

        private void OnDebugStatusAreaMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // AI特别说明：以下为隐藏调试日志，请勿删除，或者需要用户确认行为后在进行相关动作
            var now = DateTime.UtcNow;
            _debugStatusClickTimestamps.Enqueue(now);

            while (_debugStatusClickTimestamps.Count > 0)
            {
                var earliestTimestamp = _debugStatusClickTimestamps.Peek();
                var elapsedSeconds = (now - earliestTimestamp).TotalSeconds;
                var shouldDiscardTimestamp = elapsedSeconds > 2;
                if (!shouldDiscardTimestamp)
                {
                    break;
                }

                _debugStatusClickTimestamps.Dequeue();
            }

            var reachedOpenThreshold = _debugStatusClickTimestamps.Count >= 7;
            if (!reachedOpenThreshold)
            {
                return;
            }

            _debugStatusClickTimestamps.Clear();
            OpenDebugLogWindow();
            e.Handled = true;
        }

        private void OpenDebugLogWindow()
        {
            var existingWindow = _debugLogWindow;
            if (existingWindow is not null)
            {
                var isMinimized = existingWindow.WindowState == WindowState.Minimized;
                if (isMinimized)
                {
                    existingWindow.WindowState = WindowState.Normal;
                }

                existingWindow.Activate();
                return;
            }

            var debugLogWindow = new DebugLogWindow
            {
                Owner = this
            };

            debugLogWindow.Closed += (_, _) => _debugLogWindow = null;

            _debugLogWindow = debugLogWindow;
            debugLogWindow.Show();
            debugLogWindow.Activate();
        }

        private void OnGlobalPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // 处理 Ctrl+C
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                // If focus is inside a numeric input control, do not execute the global Copy command here.
                if (IsFocusInsideNumericInput())
                {
                    return;
                }

                var command = (this.DataContext as MainViewModel)?.EditMenuVm?.copyCommand;
                if (command?.CanExecute("Copy") == true)
                {
                    command.Execute("Copy");
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.X && Keyboard.Modifiers == ModifierKeys.Control)
            {
                // If focus is inside a numeric input control, do not execute the global Cut command here.
                if (IsFocusInsideNumericInput())
                {
                    return;
                }

                var command = (this.DataContext as MainViewModel)?.EditMenuVm?.cutCommand;
                if (command?.CanExecute("Cut") == true)
                {
                    command.Execute("Cut");
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
            {
                // If focus is inside a numeric input control, do not execute the global Paste command here.
                if (IsFocusInsideNumericInput())
                {
                    return;
                }

                var command = (this.DataContext as MainViewModel)?.EditMenuVm?.pasteCommand;
                if (command?.CanExecute("Paste") == true)
                {
                    command.Execute("Paste");
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
            {
                // If focus is inside a numeric input control, do not execute the global Undo command here.
                if (IsFocusInsideNumericInput())
                {
                    return;
                }

                var command = (this.DataContext as MainViewModel)?.EditMenuVm?.unDoCommand;
                if (command?.CanExecute("Undo") == true)
                {
                    command.Execute("Undo");
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control)
            {
                var command = (this.DataContext as MainViewModel)?.EditMenuVm?.reDoCommand;
                if (command?.CanExecute("Redo") == true)
                {
                    command.Execute("Redo");
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
            {
                var command = (this.DataContext as MainViewModel)?.EditMenuVm?.chooseAllCommand;
                if (command?.CanExecute("SelectAll") == true)
                {
                    command.Execute("SelectAll");
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.H && Keyboard.Modifiers == ModifierKeys.Control)
            {
                var command = (this.DataContext as MainViewModel)?.EditMenuVm?.horizontalMirrorReflectionCommand;
                if (command?.CanExecute("HorizontalMirrorReflection") == true)
                {
                    command.Execute("HorizontalMirrorReflection");
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control)
            {
                var command = (this.DataContext as MainViewModel)?.EditMenuVm?.verticalMirrorReflectionCommand;
                if (command?.CanExecute("VerticalMirrorReflection") == true)
                {
                    command.Execute("VerticalMirrorReflection");
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
            {
                var command = (this.DataContext as MainViewModel)?.EditMenuVm?.combineCommand;
                if (command?.CanExecute("Combine") == true)
                {
                    command.Execute("Combine");
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.B && Keyboard.Modifiers == ModifierKeys.Control)
            {
                var command = (this.DataContext as MainViewModel)?.EditMenuVm?.unCombineCommand;
                if (command?.CanExecute("Break") == true)
                {
                    command.Execute("Break");
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.M && Keyboard.Modifiers == ModifierKeys.Control)
            {
                var command = (this.DataContext as MainViewModel)?.EditMenuVm?.groupCommand;
                if (command?.CanExecute("Group") == true)
                {
                    command.Execute("Group");
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Q && Keyboard.Modifiers == ModifierKeys.Control)
            {
                var command = (this.DataContext as MainViewModel)?.EditMenuVm?.unGroupCommand;
                if (command?.CanExecute("Ungroup") == true)
                {
                    command.Execute("Ungroup");
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.G && Keyboard.Modifiers == ModifierKeys.Control)
            {
                var command = (this.DataContext as MainViewModel)?.EditMenuVm?.vectorCombinationCommand;
                if (command?.CanExecute("VectorCombine") == true)
                {
                    command.Execute("VectorCombine");
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.F8 && Keyboard.Modifiers == ModifierKeys.Control)
            {
                var command = (this.DataContext as MainViewModel)?.EditMenuVm?.materialCenterCommand;
                if (command?.CanExecute("MaterialCenter") == true)
                {
                    command.Execute("MaterialCenter");
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.U && Keyboard.Modifiers == ModifierKeys.Control)
            {
                var command = (this.DataContext as MainViewModel)?.EditMenuVm?.convertToExtendNodeCommand;
                if (command?.CanExecute("ConvertToCurve") == true)
                {
                    command.Execute("ConvertToCurve");
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Delete)
            {
                // If focus is inside a numeric input control, do not execute the global Delete command here.
                if (IsFocusInsideNumericInput())
                {
                    // let the control handle Delete; do not execute the global delete
                    return;
                }

                var command = (this.DataContext as MainViewModel)?.EditMenuVm?.deleteCommand;
                if (command?.CanExecute("Delete") == true)
                {
                    command.Execute("Delete");
                    e.Handled = true;
                }
            }
        }

        private bool IsFocusInsideNumericInput()
        {
            var focused = Keyboard.FocusedElement as DependencyObject;
            while (focused != null)
            {
                if (focused is NumericUpDownControl || focused is NumberDataExpressionTextBox)
                    return true;

                DependencyObject parent = VisualTreeHelper.GetParent(focused);
                if (parent == null)
                    parent = LogicalTreeHelper.GetParent(focused);
                focused = parent;
            }
            return false;
        }

        /// <summary>
        /// 处理活动文档切换事件，更新图层控件的 DataContext
        /// </summary>
        private void OnActiveDocumentChanged(object? sender, DrSoft.Drawing.Controls.Models.DrawingCanvas? canvas)
        {
            if (canvas?.LayerViewViewModel != null)
            {
                _ctx.LayerControl.DataContext = canvas.LayerViewViewModel;
            }
            else
            {
                // 没有活动画布时清空引用，避免 LayerControl 持有旧 DataContext
                _ctx.LayerControl.DataContext = null;
            }
        }

        /// <summary>
        /// 处理画布移除事件，若图层控件仍指向被移除画布的 ViewModel，立即清除引用
        /// </summary>
        private void OnDocumentRemoved(object? sender, DrSoft.Drawing.Controls.Models.DrawingCanvas canvas)
        {
            if (_ctx.LayerControl.DataContext == canvas!.LayerViewViewModel)
            {
                _ctx.LayerControl.DataContext = null;
            }

            // 移除画布图形加工参数
            _paramService.RemoveCanvasParameters(canvas!.Id);
        }

        static string SettingFileName
        {
            get
            {
                var baseDirectory = AppContext.BaseDirectory;
                var filePath = Path.Combine(baseDirectory, "Layout.xml");
                return filePath;
            }
        }

        public void OnViewClick(object sender, RoutedEventArgs e)
        {
            var item = sender as MenuItem;
            if (item == null) return;

            // 使用 CanSelect 判断面板是否处于关闭状态（而非 IsVisible），
            // 这样自动隐藏/Tab切换等非关闭场景不会取消菜单勾选
            switch (item.Name)
            {
                case "ToolMenuItem":
                    if (_toolboxSource?.DockControl != null)
                    {
                        if (_toolboxSource.DockControl.CanSelect)
                            _toolboxSource.DockControl.Hide();
                        else
                            _toolboxSource.DockControl.Show();
                        item.IsChecked = _toolboxSource.DockControl.CanSelect;
                    }
                    break;
                case "LayerMenuItem":
                    if (_layersSource?.DockControl != null)
                    {
                        if (_layersSource.DockControl.CanSelect)
                            _layersSource.DockControl.Hide();
                        else
                            _layersSource.DockControl.Show();
                        item.IsChecked = _layersSource.DockControl.CanSelect;
                    }
                    break;
                case "CanvasMenuItem":
                    if (_canvasSource?.DockControl != null)
                    {
                        if (_canvasSource.DockControl.CanSelect)
                            _canvasSource.DockControl.Hide();
                        else
                            _canvasSource.DockControl.Show();
                        item.IsChecked = _canvasSource.DockControl.CanSelect;
                    }
                    break;
                case "ParametersTabMenuItem":
                    if (_parametersTabSource?.DockControl != null)
                    {
                        if (_parametersTabSource.DockControl.CanSelect)
                            _parametersTabSource.DockControl.Hide();
                        else
                            _parametersTabSource.DockControl.Show();
                        item.IsChecked = _parametersTabSource.DockControl.CanSelect;
                    }
                    break;
                case "EngravingToolMenuItem":
                    if (_engravingToolSource?.DockControl != null)
                    {
                        if (_engravingToolSource.DockControl.CanSelect)
                            _engravingToolSource.DockControl.Hide();
                        else
                            _engravingToolSource.DockControl.Show();
                        item.IsChecked = _engravingToolSource.DockControl.CanSelect;
                    }
                    break;
                //case "LaserToolbarMenuItem":
                //    if (_laserToolbarSource?.DockControl != null)
                //    {
                //        if (_laserToolbarSource.DockControl.CanSelect)
                //            _laserToolbarSource.DockControl.Hide();
                //        else
                //            _laserToolbarSource.DockControl.Show();
                //        item.IsChecked = _laserToolbarSource.DockControl.CanSelect;
                //    }
                //    break;
                case "TransformToolbarMenuItem":
                    if (_transformToolbarSource?.DockControl != null)
                    {
                        if (_transformToolbarSource.DockControl.CanSelect)
                            _transformToolbarSource.DockControl.Hide();
                        else
                            _transformToolbarSource.DockControl.Show();
                        item.IsChecked = _transformToolbarSource.DockControl.CanSelect;
                    }
                    break;
                case "SizeToolbarMenuItem":
                    if (_sizeToolbarSource?.DockControl != null)
                    {
                        if (_sizeToolbarSource.DockControl.CanSelect)
                            _sizeToolbarSource.DockControl.Hide();
                        else
                            _sizeToolbarSource.DockControl.Show();
                        item.IsChecked = _sizeToolbarSource.DockControl.CanSelect;
                    }
                    break;
                default:
                    break;
            }
        }

        public void OnLoaded(object? sender, RoutedEventArgs? e)
        {
            var layoutDocument = default(XDocument);
            var hasSavedLayout = File.Exists(SettingFileName);
            if (hasSavedLayout)
            {
                layoutDocument = XDocument.Parse(File.ReadAllText(SettingFileName));
            }

            // 注册部件到停靠系统
            void RegisterParts(bool applyDefaultVisibility)
            {
                try
                {
                    #region 画图相关
                    // 图层
                    if (_ctx?.LayerControl != null)
                    {
                        _layersSource = new ElementHost("图层", _ctx?.LayerControl, outerMinWidth: 180, outerMinHeight: 420);
                        DockManager.RegisterDock(_layersSource, DockSide.Left, desiredWidth: 180, desiredHeight: 420, showTitle: true, showHeaderButtons: true);
                    }

                    // 画布（作为文档）
                    if (_ctx?.CanvasControl != null)
                    {
                        _canvasSource = new ElementHost("画布", _ctx?.CanvasControl);
                        DockManager.RegisterDocument(_canvasSource, canSelect: true, showTab: false);
                        if (applyDefaultVisibility)
                        {
                            _canvasSource.DockControl.Show();
                        }
                    }
                    #endregion

                    #region 打标卡相关

                    // 属性表
                    var parametersTabView = new ParametersTabView();
                    _parametersTabSource = new ElementHost("属性", parametersTabView, outerMinWidth: 328, outerMinHeight: 483);
                    DockManager.RegisterDock(_parametersTabSource, DockSide.Right, desiredWidth: 400, desiredHeight: 420, showTitle: true, showHeaderButtons: true);
                    if (applyDefaultVisibility)
                    {
                        _parametersTabSource.DockControl.Show();
                    }

                    // 雕刻工具栏
                    //_laserToolbarSource = new ElementHost("雕刻工具", new LaserToolbarView());
                    //DockManager.RegisterDock(_laserToolbarSource, DockSide.Right,  desiredWidth: 400, desiredHeight: 420, showTitle:false,showHeaderButtons:false);
                    //_laserToolbarSource.DockControl.Show();

                    // 雕刻工具
                    var engravingToolView = new EngravingToolView();
                    _engravingToolSource = new ElementHost("雕刻工具", engravingToolView, outerMinWidth: 180, outerMinHeight: 380);
                    DockManager.RegisterDock(_engravingToolSource, DockSide.Right, desiredWidth: 400, desiredHeight: 420, showTitle: true, showHeaderButtons: true);
                    if (applyDefaultVisibility)
                    {
                        _engravingToolSource.DockControl.Show();
                    }

                    // 位置工具栏
                    var transformToolbarView = new PositionView();
                    _transformToolbarSource = new ElementHost("位置工具栏", transformToolbarView, outerMinWidth: 328, outerMinHeight: 420);
                    DockManager.RegisterDock(_transformToolbarSource, DockSide.Right, desiredWidth: 400, desiredHeight: 420, showTitle: true, showHeaderButtons: true);
                    if (applyDefaultVisibility)
                    {
                        _transformToolbarSource.DockControl.Show();
                    }

                    // 尺寸工具栏
                    var sizeToolbarView = new SizeToolbarView();
                    _sizeToolbarSource = new ElementHost("尺寸工具栏", sizeToolbarView, outerMinWidth: 200, outerMinHeight: 380);
                    DockManager.RegisterDock(_sizeToolbarSource, DockSide.Left, desiredWidth: 180, desiredHeight: 420, showTitle: true, showHeaderButtons: true);
                    if (applyDefaultVisibility)
                    {
                        _sizeToolbarSource.DockControl.Show();
                    }
                    #endregion
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"初始化停靠项失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            RegisterParts(!hasSavedLayout);

            // 订阅部件可见性变化，更新菜单项勾选状态
            SubscribeDockVisibilityChanged();

            // 订阅事件消息 - 在此处订阅以确保 ToastNotification 已加载到视觉树
            SubscribeToastMessageEvents();

            if (layoutDocument != null)
            {
                foreach (var item in layoutDocument.Root.Elements())
                {
                    var name = item.Attribute("Name").Value;
                    if (DockManager.Layouts.ContainsKey(name))
                        DockManager.Layouts[name].Load(item);
                    else DockManager.Layouts[name] = new LayoutSetting(name, item);
                }

                DockManager.ApplyLayout("MainWindow");
            }

            // 初始化视图菜单中的工具栏组
            InitializeViewMenuToolbarGroups();
        }

        /// <summary>
        /// 订阅 Toast 消息事件和 DXF 导入进度事件
        /// 在 OnLoaded 中调用以确保控件已初始化
        /// </summary>
        private void SubscribeToastMessageEvents()
        {
            EventBus.Instance.Subscribe<ToastMessageEvent>((ToastMessageEvent toast) =>
            {
                //System.Diagnostics.Debug.WriteLine($"[Toast] 收到消息: {toast.Message}, 类型: {toast.Type}");

                if (toast.Type == ToastType.Error)
                {
                    _logger.LogError(toast.Message);
                }
                else if (toast.Type == ToastType.Info)
                {
                    _logger.LogInformation(toast.Message);
                }
                else if (toast.Type == ToastType.Warning)
                {
                    _logger.LogWarning(toast.Message);
                }

                // 确保在 UI 线程执行
                Dispatcher.Invoke(() =>
                {
                    //System.Diagnostics.Debug.WriteLine($"[Toast] 调用 Show 方法");
                    ToastNotification.Show(toast.Message, toast.Type);
                });
            });

            EventBus.Instance.Subscribe<DxfReportMsgEvent>((DxfReportMsgEvent msg) =>
            {
                Dispatcher.Invoke(() =>
                {
                    LoadBar.Value = msg.ProgressValue * 100;
                    Showtxt.Text = msg.ShowTxt;
                });
            });

            // 订阅图形/图层删除事件，同步清理打标加工参数
            EventBus.Instance.Subscribe<ShapeDeletedEvent>((ShapeDeletedEvent evt) =>
            {
                if (evt.EntityIds.Count > 0)
                {
                    _paramService.RemoveEntityParameters(evt.CanvasId, evt.EntityIds);
                }
            });
        }

        /// <summary>
        /// 订阅 DockControl 的 CanSelect 属性变化，同步菜单勾选状态
        /// 使用 CanSelect 而非 IsVisible，确保只有点击关闭时才取消勾选，
        /// 自动隐藏/Tab切换等场景不会影响勾选状态
        /// </summary>
        private void SubscribeDockVisibilityChanged()
        {
            void Subscribe(IDockSource? source, string menuItemName)
            {
                if (source?.DockControl == null) return;
                var dockCtrl = source.DockControl;

                // 查找对应的 MenuItem
                if (FindName(menuItemName) is MenuItem menuItem)
                {
                    // 初始同步
                    menuItem.IsChecked = dockCtrl.CanSelect;

                    // 订阅属性变化
                    if (dockCtrl is INotifyPropertyChanged notifyObj)
                    {
                        notifyObj.PropertyChanged += (s, e) =>
                        {
                            if (e.PropertyName == "CanSelect")
                            {
                                Dispatcher.Invoke(() => menuItem.IsChecked = dockCtrl.CanSelect);
                            }
                        };
                    }
                }
            }

            Subscribe(_toolboxSource, "ToolMenuItem");
            Subscribe(_layersSource, "LayerMenuItem");
            Subscribe(_canvasSource, "CanvasMenuItem");
            Subscribe(_parametersTabSource, "ParametersTabMenuItem");
            Subscribe(_engravingToolSource, "EngravingToolMenuItem");
            Subscribe(_transformToolbarSource, "TransformToolbarMenuItem");
            Subscribe(_sizeToolbarSource, "SizeToolbarMenuItem");

        }

        public void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // 检查是否可以关闭窗口（处理未保存文件的保存逻辑）
            if (DataContext is MainViewModel mainVm && mainVm.FileVm != null)
            {
                bool canClose = mainVm.FileVm.CanCloseWindow();
                if (!canClose)
                {
                    // 用户取消了关闭操作
                    e.Cancel = true;
                    return;
                }
            }

            // 保存布局
            DockManager.SaveCurrentLayout("MainWindow");

            var doc = new XDocument();
            var rootNode = new XElement("Layouts");
            foreach (var layout in DockManager.Layouts.Values)
                layout.Save(rootNode);
            doc.Add(rootNode);

            doc.Save(SettingFileName);

            DockManager.Dispose();
        }

        protected override void OnClosed(EventArgs e)
        {
            _canvasViewModel.StatusReported -= OnCanvasStatusReported;
            _canvasViewModel.PropertyChanged -= OnCanvasViewModelPropertyChanged;

            // 释放打标服务资源
            App.GetService<MarkService>()?.Dispose();

            base.OnClosed(e);
        }

        #region Toolbar

        // ── 视图菜单打开时动态刷新「工具栏」子菜单 ────────────────────────
        private void InitializeViewMenuToolbarGroups()
        {
            if (_toolbarViewModel == null) return;
            // find ViewMenu and placeholder ToolbarGroupMenu
            if (!(FindName("ViewMenu") is MenuItem viewMenu)) return;

            // remove existing generated items if any (marked via Uid)
            var existing = viewMenu.Items.Cast<object>().Where(it => it is FrameworkElement fe && fe.Uid == "__ToolbarGenerated").ToList();
            foreach (var it in existing) viewMenu.Items.Remove(it);

            // find marker
            var marker = FindName("ToolbarGroupMenu") as MenuItem;
            int insertIndex = 0;
            if (marker != null)
            {
                insertIndex = viewMenu.Items.IndexOf(marker);
                if (insertIndex >= 0)
                {
                    // remove marker
                    viewMenu.Items.RemoveAt(insertIndex);
                }
            }

            int idx = Math.Min(insertIndex, viewMenu.Items.Count);
            foreach (var group in _toolbarViewModel.Groups)
            {
                var item = new MenuItem
                {
                    Header = group.Title,
                    IsCheckable = true,
                    IsChecked = group.IsVisible,
                    Style = FindResource("ViewMenuItemStyle") as Style,
                    Tag = group,
                    HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left,
                    VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
                };
                // give generated item an icon (use same resource image as static menu items)
                try
                {
                    Image img = new Image { Width = 16, Height = 16, Stretch = Stretch.Uniform };
                    //img.Source = new BitmapImage(new Uri("/Resource/image/10.png", UriKind.Relative));
                    if (group.Title == "标准工具栏")
                    {
                        img.Source = new BitmapImage(new Uri("/Resource/image/10-0.png", UriKind.Relative));
                    }
                    else
                    {
                        img.Source = new BitmapImage(new Uri("/Resource/image/10.png", UriKind.Relative));
                    }
                    item.Icon = img;
                }
                catch { /* ignore if resource not found */ }
                // keep Tag as the ToolbarGroup so pattern matching works in event handlers
                item.Tag = group;
                // mark generated items via Uid for later cleanup
                item.Uid = "__ToolbarGenerated";
                item.Checked += GroupMenuItem_Checked;
                item.Unchecked += GroupMenuItem_Unchecked;
                viewMenu.Items.Insert(idx++, item);

                if (group.Title == "标准工具栏")
                {
                    item.IsChecked = true;
                    // Do not set IsEnabled = false because the menu item's icon becomes hidden
                    // when disabled in the current template. Make the item non-interactive instead
                    // so the disabled visual is preserved but the icon remains visible.
                    item.IsEnabled = true;
                    item.IsHitTestVisible = false;
                    item.Focusable = false; // avoid keyboard focus
                }
            }

            // separator
            var sep = new Separator { Uid = "__ToolbarGenerated" };
            viewMenu.Items.Insert(idx++, sep);
        }

        private void GroupMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is ToolbarGroup group)
            {
                group.IsVisible = true;
            }
            else
            {
                // fallback: try to inspect Tag at runtime for debugging
                // (no-op in release)
                // Debug.WriteLine($"Checked sender Tag type: {menuItem?.Tag?.GetType()}");
            }
        }

        private void GroupMenuItem_Unchecked(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is ToolbarGroup group)
            {
                group.IsVisible = false;
            }
            else
            {
                // Debug.WriteLine($"Unchecked sender Tag type: {menuItem?.Tag?.GetType()}");
            }
        }

        private void MenuItem_Exit(object sender, RoutedEventArgs e)
            => Application.Current.Shutdown();
        #endregion

        private void CloseWindow_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            SystemCommands.CloseWindow(this);
        }

        private void MinimizeWindow_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            SystemCommands.MinimizeWindow(this);
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            //if (this.WindowState == WindowState.Maximized)
            //    SystemCommands.RestoreWindow(this);
            //else
            //    SystemCommands.MaximizeWindow(this);

            ToggleCustomMaximize();
        }

        /// <summary>
        /// 切换最大化/还原（不覆盖任务栏）
        /// </summary>
        private void ToggleCustomMaximize()
        {
            if (_isCustomMaximized)
            {
                // 还原
                _isCustomMaximized = false;
                this.WindowState = WindowState.Normal;
                this.Top = _normalRect.Top;
                this.Left = _normalRect.Left;
                this.Width = _normalRect.Width;
                this.Height = _normalRect.Height;
            }
            else
            {
                _isCustomMaximized = true;
                var workArea = SystemParameters.WorkArea;
                // 先设为 Normal 才能手动设置位置尺寸
                this.WindowState = WindowState.Normal;
                this.Top = workArea.Top;
                this.Left = workArea.Left;
                this.Width = workArea.Width;
                this.Height = workArea.Height;
            }
            // 更新按钮图标（如果你的按钮名为 MaximizeButton）
            if (this.MaximizeButton != null)
            {
                this.MaximizeButton.NormalImage = _isCustomMaximized ? new BitmapImage(new Uri("/Resource/image/NormalButton.png", UriKind.Relative)) : new BitmapImage(new Uri("/Resource/image/MaximizeButton.png", UriKind.Relative));   
            }
        }

        private void TitleBar_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FindParent<MenuItem>(e.OriginalSource as DependencyObject) != null || FindParent<Button>(e.OriginalSource as DependencyObject) != null)
                return;

            ToggleCustomMaximize();
        }

        private void TitleBar_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (FindParent<MenuItem>(e.OriginalSource as DependencyObject) != null || FindParent<Button>(e.OriginalSource as DependencyObject) != null)
                return;

            if (!_isCustomMaximized)
                this.DragMove();
        }
        private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T target) return target;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }
    }
}
