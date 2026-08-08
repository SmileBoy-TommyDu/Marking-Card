using DrSoft.Docking.Enum;
using DrSoft.Docking.Interface;
using DrSoft.Docking.Model.Element;
using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace DrSoft.Docking.View
{
    public class LayoutDocumentGroupControl : BaseGroupControl
    {
        static LayoutDocumentGroupControl()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(LayoutDocumentGroupControl), new FrameworkPropertyMetadata(typeof(LayoutDocumentGroupControl)));
            FocusableProperty.OverrideMetadata(typeof(LayoutDocumentGroupControl), new FrameworkPropertyMetadata(false));
        }

        internal LayoutDocumentGroupControl(ILayoutGroup model, double desiredWidth = Constants.DockDefaultWidthLength, double desiredHeight = Constants.DockDefaultHeightLength) : base(model, desiredWidth, desiredHeight)
        {
            // 监听子元素 ShowTab 变化，动态控制 TabStrip 可见性
            model.PropertyChanged += OnModelPropertyChanged;
            foreach (var child in model.Children.OfType<DockElement>())
                child.PropertyChanged += OnChildPropertyChanged;
        }

        private FrameworkElement _tabStripBorder;
        private FrameworkElement _tabStripSeparator;
        private FrameworkElement _tabStripGrid;

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            _tabStripBorder = GetTemplateChild("TabStripBorder") as FrameworkElement;
            _tabStripSeparator = GetTemplateChild("TabStripSeparator") as FrameworkElement;
            _tabStripGrid = GetTemplateChild("TabStripGrid") as FrameworkElement;
            UpdateTabStripVisibility();

            // 确保模板应用后 SelectedIndex / SelectedContent 被正确初始化
            if (Items.Count > 0)
            {
                var idx = SelectedIndex;
                if (idx < 0) idx = 0;
                SelectedIndex = -1;
                SelectedIndex = idx;
            }
        }

        private void OnModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Children_CanSelect")
            {
                // 新子元素需要注册属性变更监听
                if (sender is ILayoutGroup group)
                {
                    foreach (var child in group.Children.OfType<DockElement>())
                        child.PropertyChanged -= OnChildPropertyChanged;
                    foreach (var child in group.Children.OfType<DockElement>())
                        child.PropertyChanged += OnChildPropertyChanged;
                }
                UpdateTabStripVisibility();
            }
        }

        private void OnChildPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "ShowTab")
                UpdateTabStripVisibility();
        }

        private void UpdateTabStripVisibility()
        {
            if (_tabStripGrid == null) return;
            var group = Model as ILayoutGroup;
            var anyShowTab = group?.Children
                .OfType<DockElement>()
                .Where(ele => ele.CanSelect)
                .Any(ele => ele.ShowTab) ?? true;

            var visibility = anyShowTab ? Visibility.Visible : Visibility.Collapsed;
            _tabStripGrid.Visibility = visibility;
            if (_tabStripSeparator != null)
                _tabStripSeparator.Visibility = visibility;
        }

        public override DragMode Mode
        {
            get
            {
                return DragMode.Document;
            }
        }

        public override void OnDrop(DragItem source)
        {
            if (DropMode == DropMode.Header
                || DropMode == DropMode.Center)
                base.OnDrop(source);
            else
            {
                IDockView child;
                if (source.RelativeObj is BaseFloatWindow)
                {
                    child = (source.RelativeObj as BaseFloatWindow).Child;
                    (source.RelativeObj as BaseFloatWindow).DetachChild(child);
                }
                else child = source.RelativeObj as IDockView;
                DockManager.ChangeDockMode(child, (Model as ILayoutGroup).Mode);

                if (_AssertSplitMode(DropMode))
                {
                    //must to changside
                    DockManager.ChangeSide(child, Model.Side);
                    if (DockViewParent == null)
                    {
                        var parent = Parent as BaseFloatWindow;
                        var seedWidth = ResolveAttachSeedWidth();
                        var seedHeight = ResolveAttachSeedHeight();
                        parent.DetachChild(this, false);
                        var panel = new LayoutGroupDocumentPanel()
                        {
                            DesiredWidth = seedWidth,
                            DesiredHeight = seedHeight,
                            Direction = (DropMode == DropMode.Left_WithSplit || DropMode == DropMode.Right_WithSplit) ? Direction.Horizontal : Direction.Vertical
                        };
                        panel._AttachChild(this, 0);
                        if (DropMode == DropMode.Left_WithSplit || DropMode == DropMode.Top_WithSplit)
                            panel.AttachChild(child, DropMode == DropMode.Left_WithSplit ? AttachMode.Left_WithSplit : AttachMode.Top_WithSplit, 0);
                        else panel.AttachChild(child, DropMode == DropMode.Right_WithSplit ? AttachMode.Right_WithSplit : AttachMode.Bottom_WithSplit, 1);
                        parent.AttachChild(panel, AttachMode.None, 0);
                    }
                    else
                    {
                        var parent = Parent as LayoutGroupDocumentPanel;
                        parent.Direction = (DropMode == DropMode.Left_WithSplit || DropMode == DropMode.Right_WithSplit) ? Direction.Horizontal : Direction.Vertical;
                        int index = parent.IndexOf(this);
                        switch (DropMode)
                        {
                            case DropMode.Left_WithSplit:
                                parent.AttachChild(child, AttachMode.Left_WithSplit, index);
                                break;
                            case DropMode.Top_WithSplit:
                                parent.AttachChild(child, AttachMode.Top_WithSplit, index);
                                break;
                            case DropMode.Right_WithSplit:
                                parent.AttachChild(child, AttachMode.Right_WithSplit, index + 1);
                                break;
                            case DropMode.Bottom_WithSplit:
                                parent.AttachChild(child, AttachMode.Bottom_WithSplit, index + 1);
                                break;
                        }
                    }
                }
                else
                {
                    DockManager.FormatChildSize(child as ILayoutSize, new Size(ActualWidth, ActualHeight));

                    var _parent = Parent as LayoutGroupDocumentPanel;
                    var child_size = child as ILayoutSize;
                    if (_parent.DockViewParent is LayoutRootPanel)
                    {
                        var rootPanel = _parent.DockViewParent as LayoutRootPanel;
                        rootPanel.DetachChild(_parent, false);
                        var pparent = new LayoutGroupPanel()
                        {
                            Direction = (DropMode == DropMode.Left || DropMode == DropMode.Right) ? Direction.Horizontal : Direction.Vertical
                        };
                        pparent._AttachChild(_parent, 0);
                        switch (DropMode)
                        {
                            case DropMode.Left:
                                DockManager.ChangeSide(child, DockSide.Left);
                                pparent.AttachChild(child, AttachMode.Left, 0);
                                break;
                            case DropMode.Top:
                                DockManager.ChangeSide(child, DockSide.Top);
                                pparent.AttachChild(child, AttachMode.Top, 0);
                                break;
                            case DropMode.Right:
                                DockManager.ChangeSide(child, DockSide.Right);
                                pparent.AttachChild(child, AttachMode.Right, 1);
                                break;
                            case DropMode.Bottom:
                                DockManager.ChangeSide(child, DockSide.Bottom);
                                pparent.AttachChild(child, AttachMode.Bottom, 1);
                                break;
                        }
                        rootPanel.AttachChild(pparent, AttachMode.None, 0);
                    }
                    else
                    {
                        var panel = _parent.DockViewParent as LayoutGroupPanel;
                        int index = panel.IndexOf(_parent);
                        switch (DropMode)
                        {
                            case DropMode.Left:
                                DockManager.ChangeSide(child, DockSide.Left);
                                if (panel.Direction == Direction.Horizontal)
                                    panel.AttachChild(child, AttachMode.Left, index);
                                else
                                {
                                    panel._DetachChild(_parent);
                                    var pparent = new LayoutGroupPanel()
                                    {
                                        Direction = Direction.Horizontal
                                    };
                                    pparent._AttachChild(_parent, 0);
                                    pparent._AttachChild(child, 0);
                                    panel._AttachChild(pparent, Math.Min(index, panel.Count));
                                }
                                break;
                            case DropMode.Top:
                                DockManager.ChangeSide(child, DockSide.Top);
                                if (panel.Direction == Direction.Vertical)
                                    panel.AttachChild(child, AttachMode.Top, index);
                                else
                                {
                                    panel._DetachChild(_parent);
                                    var pparent = new LayoutGroupPanel()
                                    {
                                        Direction = Direction.Vertical
                                    };
                                    pparent._AttachChild(_parent, 0);
                                    pparent._AttachChild(child, 0);
                                    panel._AttachChild(pparent, Math.Min(index, panel.Count));
                                }
                                break;
                            case DropMode.Right:
                                DockManager.ChangeSide(child, DockSide.Right);
                                if (panel.Direction == Direction.Horizontal)
                                    panel.AttachChild(child, AttachMode.Right, index + 1);
                                else
                                {
                                    panel._DetachChild(_parent);
                                    var pparent = new LayoutGroupPanel()
                                    {
                                        Direction = Direction.Horizontal
                                    };
                                    pparent._AttachChild(_parent, 0);
                                    pparent._AttachChild(child, 1);
                                    panel._AttachChild(pparent, Math.Min(index, panel.Count));
                                }
                                break;
                            case DropMode.Bottom:
                                DockManager.ChangeSide(child, DockSide.Bottom);
                                if (panel.Direction == Direction.Vertical)
                                    panel.AttachChild(child, AttachMode.Bottom, index + 1);
                                else
                                {
                                    panel._DetachChild(_parent);
                                    var pparent = new LayoutGroupPanel()
                                    {
                                        Direction = Direction.Vertical
                                    };
                                    pparent._AttachChild(_parent, 0);
                                    pparent._AttachChild(child, 1);
                                    panel._AttachChild(pparent, Math.Min(index, panel.Count));
                                }
                                break;
                        }
                    }
                }
            }

            if (source.RelativeObj is BaseFloatWindow)
                (source.RelativeObj as BaseFloatWindow).Close();
        }

        private bool _AssertSplitMode(DropMode mode)
        {
            return DropMode == DropMode.Left_WithSplit
                || DropMode == DropMode.Right_WithSplit
                || DropMode == DropMode.Top_WithSplit
                || DropMode == DropMode.Bottom_WithSplit;
        }
    }
}
