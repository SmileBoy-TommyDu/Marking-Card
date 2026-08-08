using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Controls.Tools;
using DrSoft.Drawing.Controls.ViewModels;
using DrSoft.Drawing.Controls.Views;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Model;
using DrSoft.Drawing.Utility;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Point = System.Windows.Point;

namespace DrSoft.Drawing.Controls;
/// <summary>
/// WPF 画布控件壳层。
/// 只负责订阅/解绑控件生命周期、转发输入，以及把重绘请求桥接到 Skia 视图。
/// </summary>
public partial class DrawingCanvasControl : System.Windows.Controls.UserControl
{
    internal CanvasViewModel? ViewModel { get; private set; }
    private readonly SpacePanGestureState _spacePanGesture = new();
    private SKPoint _cachedMouseDownPoint = new SKPoint();
    private SKPoint _cachedMouseMovePoint = new SKPoint();
    private SKPoint _cachedMouseUpPoint = new SKPoint();
    private SKPoint _cachedMouseWheelPoint = new SKPoint();
    private SKPoint _cachedMouseRightDownPoint = new SKPoint();
    private bool _isRulerOverlayVisible;
    private bool _runtimeSubscriptionsAttached;
    private bool _isCompletingInlineTextInput;
    private bool _isInlineTextInputActive;
    private bool _isCompletingCanvasRename;
    private DrawingCanvas? _renamingCanvas;
    private TextBlock? _renamingCanvasNameTextBlock;
    private System.Windows.Controls.TextBox? _renamingCanvasNameEditor;

    internal DrawingCanvasControl(CanvasViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;// new DrawingCanvasViewModel();
        DataContext = ViewModel;

        MouseLeftButtonUp += DrawingCanvasControl_MouseLeftButtonUp;
        Loaded += OnControlLoaded;
        Unloaded += OnControlUnloaded;

        // 在 View 的构造函数中注册消息
        WeakReferenceMessenger.Default.Register<OpenMenuMessage>(this, (r, msg) =>
        {
            // 打开 Popup
            if (ContextMenuPopup.IsOpen)
            {
                ContextMenuPopup.IsOpen = false;
            }
            ContextMenuPopup.IsOpen = true;
        });

        WeakReferenceMessenger.Default.Register<CloseMenuMessage>(this, (r, msg) =>
        {
            // 关闭 Popup
            ContextMenuPopup.IsOpen = false;
        });

        ToolZoom.IsDragDown = true;
    }

    private void OnControlLoaded(object sender, RoutedEventArgs e)
    {
        AttachRuntimeSubscriptions();
        RulerOverlayCanvas.IgnorePixelScaling = SkiaCanvas.IgnorePixelScaling;
        SkiaCanvas.Focus();
        // 启动时设置默认光标为 pointer
        SkiaCanvas.Cursor = CanvasCursorFactory.GetCursor("pointer", System.Windows.Input.Cursors.Arrow);
        // 延迟一帧确保 SkiaCanvas 已完全初始化
        Dispatcher.BeginInvoke(() => ViewModel?.Redraw(), System.Windows.Threading.DispatcherPriority.Render);
        PopupManagement.Register(ContextMenuPopup);
        UpdateAddButtonPosition();
    }

    private void OnControlUnloaded(object sender, RoutedEventArgs e)
    {
        PopupManagement.Unregister(ContextMenuPopup);
        DetachRuntimeSubscriptions();
    }

    /// <summary>
    /// 只在控件真正 Loaded 后附加运行时订阅，避免构造期过早绑定和重复订阅。
    /// </summary>
    private void AttachRuntimeSubscriptions()
    {
        if (_runtimeSubscriptionsAttached)
        {
            return;
        }

        if (ViewModel != null)
        {
            ViewModel.RedrawRequested += OnRedrawRequested;
            ViewModel.CanvasList.CollectionChanged += OnCanvasListCollectionChanged;
            ViewModel.Context.ActiveCanvasChanged += OnActiveCanvasChanged;
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        CanvasTabs.ItemContainerGenerator.StatusChanged += OnCanvasTabsGeneratorStatusChanged;
        _runtimeSubscriptionsAttached = true;
    }

    /// <summary>
    /// 成对释放运行时订阅，修复匿名 lambda 无法解绑导致的生命周期泄漏问题。
    /// </summary>
    private void DetachRuntimeSubscriptions()
    {
        if (!_runtimeSubscriptionsAttached)
        {
            return;
        }

        if (ViewModel != null)
        {
            ViewModel.RedrawRequested -= OnRedrawRequested;
            ViewModel.CanvasList.CollectionChanged -= OnCanvasListCollectionChanged;
            ViewModel.Context.ActiveCanvasChanged -= OnActiveCanvasChanged;
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        CanvasTabs.ItemContainerGenerator.StatusChanged -= OnCanvasTabsGeneratorStatusChanged;
        _runtimeSubscriptionsAttached = false;
    }

    private void OnRedrawRequested(object? sender, EventArgs e)
    {
        SkiaCanvas.InvalidateVisual();
        RulerOverlayCanvas.InvalidateVisual();
    }

    private void OnCanvasListCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateAddButtonPosition();
    }

    private void OnCanvasTabsGeneratorStatusChanged(object? sender, EventArgs e)
    {
        UpdateAddButtonPosition();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var propertyName = e.PropertyName;
        var isToolChanged = propertyName == nameof(CanvasViewModel.ActiveToolName);
        if (!isToolChanged)
        {
            return;
        }

        var isTextToolActive = ViewModel?.IsTextActive == true;
        if (isTextToolActive)
        {
            return;
        }

        CommitInlineTextInput();
    }

    private void DrawingCanvasControl_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // 关闭 Popup
        ContextMenuPopup.IsOpen = false;
    }

    private void ContextMenuPopup_Opened(object sender, EventArgs e)
    {
        if (this.DataContext is CanvasViewModel vm && vm.MenuItems != null)
        {
            //// 强制 WPF 全局重新评估所有命令的 CanExecute
            //CommandManager.InvalidateRequerySuggested();

            foreach (var item in vm.MenuItems)
            {
                // 递归刷新所有子菜单项的命令
                RefreshCommand(item);
            }
        }
    }

    private void ContextSubMenuPopup_Opened(object sender, EventArgs e)
    {
        if (this.DataContext is CanvasViewModel vm && vm.MenuItems != null)
        {
            //// 强制 WPF 全局重新评估所有命令的 CanExecute
            //CommandManager.InvalidateRequerySuggested();

            foreach (var item in vm.MenuItems)
            {
                // 递归刷新所有子菜单项的命令
                if (item.Children != null && item.Children.Count > 0)
                {
                    foreach (var child in item.Children)
                    {
                        RefreshCommand(child);
                    }
                }
            }
        }
    }

    private void RefreshCommand(MenuItemViewModel item)
    {
        if (item.Command is RelayCommand<string> relay)
        {
            relay.NotifyCanExecuteChanged();
        }
        foreach (var child in item.Children)
            RefreshCommand(child);
    }

    private void OnPaint(object sender, SKPaintSurfaceEventArgs e)
    {
        ViewModel?.Render(e.Surface.Canvas, e.Info);
    }

    private void OnRulerOverlayPaint(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        if (!_isRulerOverlayVisible || ViewModel?.RulerVisible != true)
        {
            return;
        }

        float rulerWidth = ViewModel.RulerWidth;
        using var paint = new SKPaint
        {
            Color = SKColors.Red,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            IsAntialias = true
        };

        if (_cachedMouseMovePoint.Y >= 0 && _cachedMouseMovePoint.Y <= e.Info.Height)
        {
            canvas.DrawLine(0, _cachedMouseMovePoint.Y, rulerWidth + 1, _cachedMouseMovePoint.Y, paint);
        }

        if (_cachedMouseMovePoint.X >= 0 && _cachedMouseMovePoint.X <= e.Info.Width)
        {
            canvas.DrawLine(_cachedMouseMovePoint.X, 0, _cachedMouseMovePoint.X, rulerWidth + 1, paint);
        }
    }

    private System.Windows.Point GetPhysicalPosition(System.Windows.Input.MouseEventArgs e)
    {
        var p = e.GetPosition(SkiaCanvas);
        if (SkiaCanvas.IgnorePixelScaling)
        {
            var source = PresentationSource.FromVisual(SkiaCanvas);
            if (source?.CompositionTarget != null)
            {
                p = new System.Windows.Point(p.X * source.CompositionTarget.TransformToDevice.M11,
                              p.Y * source.CompositionTarget.TransformToDevice.M22);
            }
        }
        return p;
    }

    private void ToolZoom_Click(object sender, RoutedEventArgs e)
    {
        if (!ToolZoom.IsEnabled) return;
        ToolZoomPopup.IsOpen = !ToolZoomPopup.IsOpen;
        FocusCanvasKeyboardTarget();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel?.Context.ActiveCanvas == null) return;
        var logicalPoint = e.GetPosition(SkiaCanvas);
        SkiaCanvas.Focus();
        var p = GetPhysicalPosition(e);
        _cachedMouseDownPoint.X = (float)p.X;
        _cachedMouseDownPoint.Y = (float)p.Y;

        var isLeftButton = e.ChangedButton == MouseButton.Left;
        var isTextToolActive = ViewModel.IsTextActive;
        if (isLeftButton && isTextToolActive)
        {
            var worldPoint = ViewModel.Context.ActiveCanvas.Viewport.ScreenToWorld(
                _cachedMouseDownPoint.X,
                _cachedMouseDownPoint.Y);

            CommitInlineTextInput();
            BeginInlineTextInput(worldPoint, logicalPoint);

            e.Handled = true;
            return;
        }

        SkiaCanvas.CaptureMouse();

        // 如果是中键按下，专门处理
        if (e.ChangedButton == MouseButton.Middle)
        {
            ViewModel?.HandleMiddleDown(_cachedMouseDownPoint);
            System.Windows.Input.Mouse.OverrideCursor = CanvasCursorFactory.GetMoveCursor(isActive: true);
        }
        else if (_spacePanGesture.TryStartPan(e.ChangedButton))
        {
            ViewModel?.HandleMiddleDown(_cachedMouseDownPoint);
            ApplySpacePanCursor();
            e.Handled = true;
        }
        else
        {
            ViewModel?.HandleMouseDown(SkiaCanvas, _cachedMouseDownPoint, e);
        }
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (ViewModel?.Context.ActiveCanvas == null) return;
        var logicalPoint = e.GetPosition(SkiaCanvas);
        var p = GetPhysicalPosition(e);
        bool isMiddlePressed = e.MiddleButton == System.Windows.Input.MouseButtonState.Pressed;
        _isRulerOverlayVisible = logicalPoint.X >= 0 &&
                                 logicalPoint.Y >= 0 &&
                                 logicalPoint.X <= SkiaCanvas.ActualWidth &&
                                 logicalPoint.Y <= SkiaCanvas.ActualHeight;

        _cachedMouseMovePoint.X = (float)p.X;
        _cachedMouseMovePoint.Y = (float)p.Y;
        RulerOverlayCanvas.InvalidateVisual();
        // 如果中键按下，直接处理平移
        if (isMiddlePressed || _spacePanGesture.IsPanningWithSpace)
        {
            ViewModel?.HandleMiddleMove(_cachedMouseMovePoint);
        }
        else
        {
            ViewModel?.HandleMouseMove(SkiaCanvas, _cachedMouseMovePoint);
        }
    }

    private void OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isRulerOverlayVisible)
        {
            return;
        }

        _isRulerOverlayVisible = false;
        RulerOverlayCanvas.InvalidateVisual();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel?.Context.ActiveCanvas == null) return;
        SkiaCanvas.ReleaseMouseCapture();
        var p = GetPhysicalPosition(e);
        _cachedMouseUpPoint.X = (float)p.X;
        _cachedMouseUpPoint.Y = (float)p.Y;

        // 如果是中键释放，专门处理
        if (e.ChangedButton == MouseButton.Middle)
        {
            ViewModel?.HandleMiddleUp();
            System.Windows.Input.Mouse.OverrideCursor = null;
        }
        else if (_spacePanGesture.TryEndPan(e.ChangedButton))
        {
            ViewModel?.HandleMiddleUp();
            ApplySpacePanCursor();
            e.Handled = true;
        }
        else
        {
            ViewModel?.HandleMouseUp(SkiaCanvas, _cachedMouseUpPoint, e);
        }
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (ViewModel?.Context.ActiveCanvas == null) return;
        var p = GetPhysicalPosition(e);
        _cachedMouseWheelPoint.X = (float)p.X;
        _cachedMouseWheelPoint.Y = (float)p.Y;

        ViewModel?.HandleMouseWheel(_cachedMouseWheelPoint, e.Delta);
    }

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (ViewModel?.Context.ActiveCanvas == null) return;
        bool isShiftKey = e.Key == Key.LeftShift || e.Key == Key.RightShift;
        if (!isShiftKey || !e.IsRepeat)
        {
            RefreshDrawingPreviewForShiftToggle(e.Key);
        }

        HandleCanvasKeyDown(e);
    }

    private void OnKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (ViewModel?.Context.ActiveCanvas == null) return;
        RefreshDrawingPreviewForShiftToggle(e.Key);
        if (_spacePanGesture.HandleKeyUp(e.Key))
        {
            ApplySpacePanCursor();
            e.Handled = true;
        }
    }

    private void HandleCanvasKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (_spacePanGesture.HandleKeyDown(e.Key))
        {
            ApplySpacePanCursor();
            e.Handled = true;
            return;
        }

        ViewModel?.HandleKeyDown(e);
    }

    /// <summary>
    /// shift键绘制的时候，刷新绘制中的图形
    /// </summary>
    /// <param name="key"></param>
    private void RefreshDrawingPreviewForShiftToggle(Key key)
    {
        bool isShiftKey = key == Key.LeftShift || key == Key.RightShift;
        if (!isShiftKey)
        {
            return;
        }

        var viewModel = ViewModel;
        if (viewModel?.Context.ActiveCanvas == null)
        {
            return;
        }

        bool isDrawing = viewModel.Context.IsDrawing;
        bool hasCurrentShape = viewModel.Context.CurrentShape != null;
        if (!isDrawing || !hasCurrentShape)
        {
            return;
        }

        var physicalPoint = Mouse.GetPosition(SkiaCanvas);
        if (SkiaCanvas.IgnorePixelScaling)
        {
            var source = PresentationSource.FromVisual(SkiaCanvas);
            if (source?.CompositionTarget != null)
            {
                physicalPoint = new Point(
                    physicalPoint.X * source.CompositionTarget.TransformToDevice.M11,
                    physicalPoint.Y * source.CompositionTarget.TransformToDevice.M22);
            }
        }

        _cachedMouseMovePoint.X = (float)physicalPoint.X;
        _cachedMouseMovePoint.Y = (float)physicalPoint.Y;

        viewModel.HandleMouseMove(SkiaCanvas, _cachedMouseMovePoint);
    }

    /// <summary>
    /// 根据空格临时抓手的当前阶段，应用对应的全局覆盖光标。
    /// 这里只做 View 层映射：状态机只产出 Ready/Active/None，具体 Cursor 资源仍由画布视图决定。
    /// </summary>
    private void ApplySpacePanCursor()
    {
        System.Windows.Input.Mouse.OverrideCursor = _spacePanGesture.CursorMode switch
        {
            SpacePanCursorMode.Ready => CanvasCursorFactory.GetMoveCursor(),
            SpacePanCursorMode.Active => CanvasCursorFactory.GetMoveCursor(isActive: true),
            _ => null
        };
    }

    private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel?.Context.ActiveCanvas == null) return;
        CommitInlineTextInput();
        ViewModel?.HandleMouseRightDown(SkiaCanvas, e);
    }

    private void BeginInlineTextInput(SKPoint worldPoint, Point logicalPoint)
    {
        var viewModel = ViewModel;
        if (viewModel == null)
        {
            return;
        }

        var beginResult = viewModel.BeginInlineTextInput(worldPoint);
        if (!beginResult)
        {
            return;
        }

        _isInlineTextInputActive = true;
        Canvas.SetLeft(InlineTextInputSink, logicalPoint.X);
        Canvas.SetTop(InlineTextInputSink, logicalPoint.Y);

        _isCompletingInlineTextInput = true;
        InlineTextInputSink.Text = string.Empty;
        _isCompletingInlineTextInput = false;

        Dispatcher.BeginInvoke(() =>
        {
            InlineTextInputSink.Focus();
            InlineTextInputSink.CaretIndex = InlineTextInputSink.Text.Length;
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void InlineTextInputSink_TextChanged(object sender, TextChangedEventArgs e)
    {
        var canUpdatePreview = _isInlineTextInputActive && !_isCompletingInlineTextInput;
        if (!canUpdatePreview)
        {
            return;
        }

        ViewModel?.UpdateInlineTextPreview(InlineTextInputSink.Text);
    }

    private void InlineTextInputSink_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var isEscape = e.Key == Key.Escape;
        if (isEscape)
        {
            CancelInlineTextInput();
            e.Handled = true;
            return;
        }

        var isCtrlEnter = e.Key == Key.Enter &&
                          (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        if (!isCtrlEnter)
        {
            return;
        }

        CommitInlineTextInput();
        e.Handled = true;
    }

    private void InlineTextInputSink_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        CommitInlineTextInput();
    }

    private void CommitInlineTextInput()
    {
        var canCommit = _isInlineTextInputActive && !_isCompletingInlineTextInput;
        if (!canCommit)
        {
            return;
        }

        _isCompletingInlineTextInput = true;

        try
        {
            ViewModel?.CommitInlineText();
            HideInlineTextInput();
        }
        finally
        {
            _isCompletingInlineTextInput = false;
        }
    }

    private void CancelInlineTextInput()
    {
        var canCancel = _isInlineTextInputActive && !_isCompletingInlineTextInput;
        if (!canCancel)
        {
            return;
        }

        _isCompletingInlineTextInput = true;

        try
        {
            ViewModel?.CancelInlineText();
            HideInlineTextInput();
        }
        finally
        {
            _isCompletingInlineTextInput = false;
        }
    }

    private void HideInlineTextInput()
    {
        _isInlineTextInputActive = false;
        InlineTextInputSink.Text = string.Empty;
        Canvas.SetLeft(InlineTextInputSink, -1000);
        Canvas.SetTop(InlineTextInputSink, -1000);
        FocusCanvasKeyboardTarget();
    }

    /// <summary>
    /// 把键盘焦点显式切回画布。
    /// 左侧工具栏按钮点击后，WPF 可能把焦点留在按钮本身，导致空格键先被按钮消费，
    /// 画布收不到 KeyDown/KeyUp，临时抓手也就不会生效。
    /// 这里在工具栏交互后主动归还焦点，保证空格等画布快捷键继续由 SkiaCanvas 处理。
    /// </summary>
    private void FocusCanvasKeyboardTarget()
    {
        if (!SkiaCanvas.IsKeyboardFocusWithin)
        {
            Keyboard.Focus(SkiaCanvas);
        }
    }

    private void CanvasTab_Click(object sender, MouseButtonEventArgs e)
    {
        CommitCanvasRename();
        CommitInlineTextInput();
        ViewModel?.CanvasTab_Click(e);
    }

    private void CloseCanvas_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        CommitCanvasRename();
        CommitInlineTextInput();
        ViewModel?.CloseCanvas_Click(sender, e);
    }

    private void CanvasName_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Alt + 左键复制画布
        if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
        {
            if (sender is TextBlock textBlock && textBlock.Tag is int canvasId)
            {
                ViewModel?.DuplicateCanvasCommand.Execute(canvasId);
                e.Handled = true;
            }
        }
    }

    private void CanvasName_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return;
        }

        if (sender is not TextBlock textBlock || textBlock.DataContext is not DrawingCanvas canvas)
        {
            return;
        }

        BeginCanvasRename(canvas, textBlock);
        e.Handled = true;
    }

    private void CanvasNameEditor_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitCanvasRename();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            CancelCanvasRename();
            e.Handled = true;
        }
    }

    private void CanvasNameEditor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        CommitCanvasRename();
    }

    private void CanvasNameEditor_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox textBox)
        {
            return;
        }

        if (!CanvasViewModel.IsCanvasNameInputFragmentValid(e.Text)
            || WouldExceedCanvasNameLength(textBox, e.Text))
        {
            e.Handled = true;
        }
    }

    private void CanvasNameEditor_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox textBox)
        {
            return;
        }

        var pastedText = e.DataObject.GetDataPresent(System.Windows.DataFormats.UnicodeText)
            ? e.DataObject.GetData(System.Windows.DataFormats.UnicodeText) as string
            : e.DataObject.GetDataPresent(System.Windows.DataFormats.Text)
                ? e.DataObject.GetData(System.Windows.DataFormats.Text) as string
                : null;

        if (string.IsNullOrEmpty(pastedText)
            || !CanvasViewModel.IsCanvasNameInputFragmentValid(pastedText)
            || WouldExceedCanvasNameLength(textBox, pastedText))
        {
            e.CancelCommand();
        }
    }

    private void OnActiveCanvasChanged(object? sender, System.EventArgs e)
    {
        CommitCanvasRename();
        CancelInlineTextInput();
        Dispatcher.BeginInvoke(() => ScrollActiveTabIntoView(), System.Windows.Threading.DispatcherPriority.Render);
    }

    private void ScrollCanvasLeft_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        ViewModel?.SwitchToPreviousCanvasCommand.Execute(null);
    }

    private void ScrollCanvasRight_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        ViewModel?.SwitchToNextCanvasCommand.Execute(null);
    }

    private void ScrollActiveTabIntoView()
    {
        if (ViewModel?.Context.ActiveCanvas is not DrawingCanvas activeCanvas) return;

        var container = CanvasTabs.ItemContainerGenerator.ContainerFromItem(activeCanvas) as FrameworkElement;
        if (container != null)
        {
            container.BringIntoView();
        }
    }

    private void CanvasTabsScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateAddButtonPosition();
    }

    private void UpdateAddButtonPosition()
    {
        Dispatcher.BeginInvoke(() =>
        {
            double contentWidth = CanvasTabsScrollViewer.ExtentWidth;
            double viewportWidth = CanvasTabsScrollViewer.ViewportWidth;

            // 内容（含+按钮占位）能完全放下时，显示ScrollViewer内部的+按钮；否则显示固定位置的+按钮
            if (contentWidth <= viewportWidth + 1)
            {
                AddButtonInScroll.Visibility = Visibility.Visible;
                AddButtonFixed.Visibility = Visibility.Collapsed;
            }
            else
            {
                AddButtonInScroll.Visibility = Visibility.Collapsed;
                AddButtonFixed.Visibility = Visibility.Visible;
            }
        }, System.Windows.Threading.DispatcherPriority.Render);
    }

    private void BeginCanvasRename(DrawingCanvas canvas, TextBlock textBlock)
    {
        CommitCanvasRename();

        if (!TryGetCanvasNameElements(textBlock, out var displayText, out var editor))
        {
            return;
        }

        _renamingCanvas = canvas;
        _renamingCanvasNameTextBlock = displayText;
        _renamingCanvasNameEditor = editor;

        _isCompletingCanvasRename = true;
        editor.Text = canvas.Name ?? string.Empty;
        displayText.Visibility = Visibility.Collapsed;
        editor.Visibility = Visibility.Visible;
        _isCompletingCanvasRename = false;

        Dispatcher.BeginInvoke(() =>
        {
            editor.Focus();
            editor.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void CommitCanvasRename()
    {
        if (_renamingCanvas == null || _renamingCanvasNameEditor == null || _isCompletingCanvasRename)
        {
            return;
        }

        _isCompletingCanvasRename = true;

        try
        {
            if (ViewModel?.TryRenameCanvas(_renamingCanvas, _renamingCanvasNameEditor.Text, out var normalizedName) == true)
            {
                _renamingCanvasNameEditor.Text = normalizedName;
                EndCanvasRename();
            }
            else
            {
                Dispatcher.BeginInvoke(() =>
                {
                    _renamingCanvasNameEditor?.Focus();
                    _renamingCanvasNameEditor?.SelectAll();
                }, System.Windows.Threading.DispatcherPriority.Input);
            }
        }
        finally
        {
            _isCompletingCanvasRename = false;
        }
    }

    private void CancelCanvasRename()
    {
        if (_renamingCanvas == null || _renamingCanvasNameEditor == null || _isCompletingCanvasRename)
        {
            return;
        }

        _isCompletingCanvasRename = true;

        try
        {
            _renamingCanvasNameEditor.Text = _renamingCanvas.Name ?? string.Empty;
            EndCanvasRename();
        }
        finally
        {
            _isCompletingCanvasRename = false;
        }
    }

    private void EndCanvasRename()
    {
        if (_renamingCanvasNameTextBlock != null)
        {
            _renamingCanvasNameTextBlock.Visibility = Visibility.Visible;
        }

        if (_renamingCanvasNameEditor != null)
        {
            _renamingCanvasNameEditor.Visibility = Visibility.Collapsed;
        }

        _renamingCanvas = null;
        _renamingCanvasNameTextBlock = null;
        _renamingCanvasNameEditor = null;
    }

    private static bool TryGetCanvasNameElements(
        FrameworkElement source,
        out TextBlock? displayText,
        out System.Windows.Controls.TextBox? editor)
    {
        displayText = null;
        editor = null;

        if (source.Parent is not Grid grid)
        {
            return false;
        }

        foreach (var child in grid.Children)
        {
            if (child is TextBlock textBlock)
            {
                displayText = textBlock;
            }
            else if (child is System.Windows.Controls.TextBox textBox)
            {
                editor = textBox;
            }
        }

        return displayText != null && editor != null;
    }

    private static bool WouldExceedCanvasNameLength(System.Windows.Controls.TextBox textBox, string incomingText)
    {
        var currentText = textBox.Text ?? string.Empty;
        var nextLength = currentText.Length - textBox.SelectionLength + incomingText.Length;
        return nextLength > CanvasViewModel.MaxCanvasNameLength;
    }


}
