using DrSoft.Docking.Commands;
using DrSoft.Docking.Enum;
using DrSoft.Docking.Interface;
using DrSoft.Docking.Model.Element;
using DrSoft.Docking.Model.Layout;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Xml.Linq;
using System.Runtime.InteropServices;
using DrSoft.Docking.Shell.Standard;
using System.IO;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;

namespace DrSoft.Docking.View
{
    public abstract class BaseFloatWindow : Window, ILayoutViewParent
    {
        private static Dictionary<string, Size> _designSizeCache = new Dictionary<string, Size>();

        private static bool TryGetDesignSizeFromXaml(Type type, out double width, out double height)
        {
            width = 0; height = 0;
            if (type == null) return false;
            var key = type.FullName;
            if (string.IsNullOrEmpty(key)) return false;
            if (_designSizeCache.TryGetValue(key, out var s))
            {
                width = s.Width; height = s.Height; return width > 0 || height > 0;
            }

            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var files = Directory.GetFiles(baseDir, "*.xaml", SearchOption.AllDirectories);
                var classMarker1 = $"x:Class=\"{type.FullName}\"";
                var classMarker2 = $"x:Class='{type.FullName}'";
                var regexW = new Regex("d:DesignWidth\\s*=\\s*\"([0-9]+(?:\\.[0-9]+)?)\"", RegexOptions.IgnoreCase);
                var regexH = new Regex("d:DesignHeight\\s*=\\s*\"([0-9]+(?:\\.[0-9]+)?)\"", RegexOptions.IgnoreCase);

                foreach (var file in files)
                {
                    string text = File.ReadAllText(file);
                    if (text.Contains(classMarker1) || text.Contains(classMarker2))
                    {
                        var mW = regexW.Match(text);
                        var mH = regexH.Match(text);
                        if (mW.Success) double.TryParse(mW.Groups[1].Value, out width);
                        if (mH.Success) double.TryParse(mH.Groups[1].Value, out height);
                        _designSizeCache[key] = new Size(width, height);
                        return width > 0 || height > 0;
                    }
                }
            }
            catch { }

            _designSizeCache[key] = new Size(0, 0);
            return false;
        }

        protected BaseFloatWindow(DockManager dockManager, bool needReCreate = false)
        {
            _dockManager = dockManager;
            MinWidth = 150;
            MinHeight = 60;
            _widthEceeed = 0;
            _heightEceeed = 0;
            NeedReCreate = needReCreate;
            //AllowsTransparency = true;
            //WindowStyle = WindowStyle.None;
            ShowActivated = true;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (DockManager.DragManager.DragItem != null && (DockManager.DragManager._dragWnd == null || DockManager.DragManager._dragWnd == this))
            {
                IntPtr windowHandle = new WindowInteropHelper(this).Handle;
                var mousePosition = this.PointToScreenDPI(Mouse.GetPosition(this));
                IntPtr lParam = new IntPtr(((int)mousePosition.X & (int)0xFFFF) | (((int)mousePosition.Y) << 16));

                Win32Helper.SendMessage(windowHandle, Win32Helper.WM_NCLBUTTONDOWN, new IntPtr(Win32Helper.HT_CAPTION), lParam);
            }
        }

        internal IntPtr Handle { get { return _hwndSrc.Handle; } }
        protected HwndSource _hwndSrc;
        protected HwndSourceHook _hwndSrcHook;

        protected virtual void OnLoaded(object sender, RoutedEventArgs e)
        {
            this.Loaded -= new RoutedEventHandler(OnLoaded);

            _hwndSrc = PresentationSource.FromDependencyObject(this) as HwndSource;
            _hwndSrcHook = new HwndSourceHook(FilterMessage);
            _hwndSrc.AddHook(_hwndSrcHook);
        }

        protected virtual IntPtr FilterMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            handled = false;
            switch (msg)
            {
                case Win32Helper.WM_ENTERSIZEMOVE:
                    if (!DockManager.DragManager.IsDragging)
                    {
                        _isDragging = true;
                        if (this is AnchorGroupWindow)
                            DockManager.DragManager.IntoDragAction(new DragItem(this, DockMode.Float, DragMode.Anchor, new Point(), Rect.Empty, new Size(ActualWidth, ActualHeight)), true);
                        else DockManager.DragManager.IntoDragAction(new DragItem(this, DockMode.Float, DragMode.Document, new Point(), Rect.Empty, new Size(ActualWidth, ActualHeight)), true);
                    }
                    break;
                case Win32Helper.WM_MOVING:
                    if (DockManager.DragManager.IsDragging)
                        DockManager.DragManager.OnMouseMove();
                    else
                    {
                        _isDragging = true;
                        if (this is AnchorGroupWindow)
                            DockManager.DragManager.IntoDragAction(new DragItem(this, DockMode.Float, DragMode.Anchor, new Point(), Rect.Empty, new Size(ActualWidth, ActualHeight)), true);
                        else DockManager.DragManager.IntoDragAction(new DragItem(this, DockMode.Float, DragMode.Document, new Point(), Rect.Empty, new Size(ActualWidth, ActualHeight)), true);
                    }
                    break;
                case Win32Helper.WM_SIZING:
                    // Enforce MinWidth/MinHeight while user is resizing the window.
                    try
                    {
                        var rect = Marshal.PtrToStructure<RECT>(lParam);
                        int width = rect.Right - rect.Left;
                        int height = rect.Bottom - rect.Top;
                        int minW = Math.Max(1, (int)Math.Round(this.MinWidth));
                        int minH = Math.Max(1, (int)Math.Round(this.MinHeight));
                        int edge = wParam.ToInt32();

                        if (width < minW)
                        {
                            switch (edge)
                            {
                                case Win32Helper.WMSZ_LEFT:
                                case Win32Helper.WMSZ_TOPLEFT:
                                case Win32Helper.WMSZ_BOTTOMLEFT:
                                    rect.Left = rect.Right - minW;
                                    break;
                                default:
                                    rect.Right = rect.Left + minW;
                                    break;
                            }
                        }

                        if (height < minH)
                        {
                            switch (edge)
                            {
                                case Win32Helper.WMSZ_TOP:
                                case Win32Helper.WMSZ_TOPLEFT:
                                case Win32Helper.WMSZ_TOPRIGHT:
                                    rect.Top = rect.Bottom - minH;
                                    break;
                                default:
                                    rect.Bottom = rect.Top + minH;
                                    break;
                            }
                        }

                        Marshal.StructureToPtr(rect, lParam, false);
                    }
                    catch { }
                    break;
                case Win32Helper.WM_EXITSIZEMOVE:
                    if (DockManager.DragManager.IsDragging)
                    {
                        DockManager.DragManager.DoDragDrop();
                        _isDragging = false;
                    }
                    _UpdateLocation(Child);
                    break;
                default:
                    break;
            }
            return IntPtr.Zero;
        }

        protected virtual void OnUnloaded(object sender, RoutedEventArgs e)
        {
            this.Unloaded -= new RoutedEventHandler(OnUnloaded);

            if (_hwndSrc != null)
            {
                _hwndSrc.RemoveHook(_hwndSrcHook);
                _hwndSrc.Dispose();
                _hwndSrc = null;
            }
        }

        private void _UpdateLocation(object obj)
        {
            if (obj != null)
            {
                if (obj is LayoutGroupPanel)
                    foreach (var child in (obj as LayoutGroupPanel).Children)
                        _UpdateLocation(child as IDockView);

                if (obj is BaseGroupControl)
                {
                    var size = obj as ILayoutSize;
                    size.FloatLeft = Left;
                    size.FloatTop = Top;
                }

                if (obj is BaseLayoutGroup)
                {
                    foreach (DockElement item in (obj as BaseLayoutGroup).Children)
                    {
                        item.FloatLeft = Left;
                        item.FloatTop = Top;
                    }
                }
            }
        }

        #region Command
        protected override void OnInitialized(EventArgs e)
        {
            CommandBindings.Add(new CommandBinding(SystemCommands.ShowSystemMenuCommand, OnShowSystemMenuExecute));
            CommandBindings.Add(new CommandBinding(GlobalCommands.CloseCommand, OnCloseExecute, OnCloseCanExecute));
            CommandBindings.Add(new CommandBinding(GlobalCommands.RestoreCommand, OnRestoreExecute, OnRestoreCanExecute));
            CommandBindings.Add(new CommandBinding(GlobalCommands.MaximizeCommand, OnMaximizeExecute, OnMaximizeCanExecute));
            base.OnInitialized(e);
        }

        private void OnShowSystemMenuExecute(object sender, ExecutedRoutedEventArgs e)
        {
            SystemCommands.ShowSystemMenu(this, new Point(WindowState == WindowState.Maximized ? 0 : Left, WindowState == WindowState.Maximized ? SystemParameters.CaptionHeight : SystemParameters.CaptionHeight + Top));
        }

        protected virtual void OnMaximizeCanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = true;
        }

        protected void OnMaximizeExecute(object sender, ExecutedRoutedEventArgs e)
        {
            SystemCommands.MaximizeWindow(this);
        }

        protected virtual void OnRestoreCanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = true;
        }

        protected void OnRestoreExecute(object sender, ExecutedRoutedEventArgs e)
        {
            SystemCommands.RestoreWindow(this);
        }

        protected void OnCloseCanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = true;
        }

        protected void OnCloseExecute(object sender, ExecutedRoutedEventArgs e)
        {
            SystemCommands.CloseWindow(this);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            if (_dockManager == null) return;
            var child = Child;
            DetachChild(Child);
            if (child is IDisposable)
                (child as IDisposable).Dispose();
        }
        #endregion

        protected double _widthEceeed;
        internal double WidthEceeed
        {
            get { return _widthEceeed; }
        }

        protected double _heightEceeed;
        internal double HeightEceeed
        {
            get { return _heightEceeed; }
        }

        internal virtual ILayoutViewWithSize Child
        {
            get
            {
                return Content == null ? null : Content as ILayoutViewWithSize;
            }
        }

        protected bool _needReCreate;
        internal bool NeedReCreate
        {
            get { return _needReCreate; }
            set { _needReCreate = value; }
        }

        protected bool _isDragging = false;
        public bool IsDragging
        {
            get { return _isDragging; }
        }

        protected DockManager _dockManager;

        public virtual DockManager DockManager
        {
            get
            {
                return _dockManager;
            }
            internal set
            {
                _dockManager = value;
            }
        }

        public virtual void Recreate() { }

        public void HitTest(Point p)
        {
            var p1 = (Content as FrameworkElement).PointToScreenDPIWithoutFlowDirection(new Point());
            VisualTreeHelper.HitTest(Content as FrameworkElement, _HitFilter, _HitRessult, new PointHitTestParameters(new Point(p.X - p1.X, p.Y - p1.Y)));
        }

        private HitTestResultBehavior _HitRessult(HitTestResult result)
        {
            DockManager.DragManager.DragTarget = null;
            return HitTestResultBehavior.Stop;
        }

        private HitTestFilterBehavior _HitFilter(DependencyObject potentialHitTestTarget)
        {
            if (potentialHitTestTarget is BaseGroupControl)
            {
                //设置DragTarget，以实时显示TargetWnd
                DockManager.DragManager.DragTarget = potentialHitTestTarget as IDragTarget;
                return HitTestFilterBehavior.Stop;
            }
            return HitTestFilterBehavior.Continue;
        }

        public virtual void DetachChild(IDockView child, bool force = true)
        {
            if (child == Content)
            {
                DockManager.RemoveFloatWindow(this);
                var isApplyingLayout = DockManager != null && DockManager.IsApplyingLayout;
                if (!isApplyingLayout)
                {
                    SaveSize();
                }
                if (child is BaseGroupControl)
                    (child as BaseGroupControl).IsDraggingFromDock = false;
                Content = null;
                if (force)
                    _dockManager = null;
            }
        }

        public virtual void AttachChild(IDockView child, AttachMode mode, int index)
        {
            if (Content != child)
            {
                Content = child;
                DockManager.AddFloatWindow(this);
                // Set minimum size based on child's desired size (content-based minimum)
                double minW = Constants.MinDockWidth, minH = Constants.MinDockHeight;
                if (child is ILayoutSize layoutSize)
                {
                    if (layoutSize.DesiredWidth > 0) minW = Math.Max(minW, layoutSize.DesiredWidth);
                    if (layoutSize.DesiredHeight > 0) minH = Math.Max(minH, layoutSize.DesiredHeight);
                }

                // If child has visual content with explicit min/width/height or design-time values, prefer those
                if (child is BaseGroupControl group)
                {
                    // inspect each dock element's content
                    try
                    {
                        var model = group.Model as DrSoft.Docking.Model.Layout.LayoutGroup;
                        foreach (var item in model.Children)
                        {
                            if (item is DrSoft.Docking.Model.Element.DockElement de && de.Content is FrameworkElement fe)
                            {
                                if (!double.IsNaN(fe.Width) && fe.Width > 0) minW = Math.Max(minW, fe.Width);
                                if (!double.IsNaN(fe.Height) && fe.Height > 0) minH = Math.Max(minH, fe.Height);
                                minW = Math.Max(minW, fe.MinWidth);
                                minH = Math.Max(minH, fe.MinHeight);

                                // try design-time attributes from XAML file
                                if (TryGetDesignSizeFromXaml(fe.GetType(), out double dw, out double dh))
                                {
                                    if (dw > 0) minW = Math.Max(minW, dw);
                                    if (dh > 0) minH = Math.Max(minH, dh);
                                }
                            }
                        }
                    }
                    catch { }
                }

                var childElement = child as FrameworkElement;
                if (childElement != null)
                {
                    minW = Math.Max(minW, childElement.MinWidth);
                    minH = Math.Max(minH, childElement.MinHeight);
                }

                var dockSource = default(IDockSource);
                if (child is BaseGroupControl groupControl)
                {
                    var model = groupControl.Model as LayoutGroup;
                    dockSource = model?.Children
                        .OfType<DockElement>()
                        .Select(item => item.Content as IDockSource)
                        .FirstOrDefault(item => item != null);
                }

                if (dockSource != null)
                {
                    if (dockSource.OuterMinWidth > 0)
                    {
                        minW = Math.Max(minW, dockSource.OuterMinWidth);
                    }

                    if (dockSource.OuterMinHeight > 0)
                    {
                        minH = Math.Max(minH, dockSource.OuterMinHeight);
                    }
                }

                // account for window chrome/extra paddings tracked by _widthEceeed/_heightEceeed
                MinWidth = minW + _widthEceeed;
                MinHeight = minH + _heightEceeed;
                Height = (child as ILayoutSize).DesiredHeight + _heightEceeed;
                Width = (child as ILayoutSize).DesiredWidth + _widthEceeed;
            }
        }

        public int IndexOf(IDockView child)
        {
            if (child == Child)
                return 0;
            else return -1;
        }

        public void SaveSize()
        {
            //保存Size信息
            if (Content is ILayoutSize)
            {
                var _child = Content as ILayoutSize;
                _child.DesiredWidth = Math.Max(ActualWidth - _widthEceeed, Constants.MinDockWidth);
                _child.DesiredHeight = Math.Max(ActualHeight - _heightEceeed, Constants.MinDockHeight);
            }
        }

        public XElement GenerateLayout()
        {
            var ele = new XElement("FloatWindow");
            if (Child is BaseGroupControl)
                ele.Add((Child as BaseGroupControl).GenerateLayout());
            else if (Child is LayoutGroupPanel)
                ele.Add((Child as LayoutGroupPanel).GenerateLayout());
            return ele;
        }
    }
}
