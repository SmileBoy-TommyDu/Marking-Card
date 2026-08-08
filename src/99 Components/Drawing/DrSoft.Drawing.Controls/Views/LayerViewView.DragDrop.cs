using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Controls.ViewModels;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WpfColor = System.Windows.Media.Color;
using WpfDataObject = System.Windows.DataObject;
using WpfDragDrop = System.Windows.DragDrop;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfTreeView = System.Windows.Controls.TreeView;

namespace DrSoft.Drawing.Controls.Views
{
    public partial class LayerViewView
    {
        private bool _isDragging;
        private WpfPoint _dragStartPoint;
        private INodeViewModel? _dragSource;
        private FrameworkElement? _currentIndicatorHost;
        private TreeViewItem? _currentIndicatorTargetItem;
        private TreeViewItem? _currentIndicatorPreviousItem;
        private TreeViewItem? _currentIndicatorNextItem;
        private DropPosition? _currentIndicatorPosition;
        private int? _currentLayerDropSlotIndex;
        private int? _currentNodeDropSlotIndex;
        private int? _currentNodeDropContainerId;
        private INodeViewModel? _currentNodeDropContainer;
        private readonly Dictionary<int, (double Top, double Height)> _layerDragBaseGeometries = [];

        private DropIndicatorAdorner? _currentAdorner;
        private AdornerLayer? _adornerLayer;

        private const double DragThreshold = 5.0;
        private const double MinimumDropEdgeBandHeight = 5.0;
        private const double LayerPreviewGapSize = 12.0;
        private const double DropAnimationDurationMs = 140.0;
        private const double LayerDropSlotHysteresisPx = 4.0;
        private const double NodeDropCenterHysteresisPx = 4.0;

        private void TreeView_PreviewMouseMove(object sender, WpfMouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragSource == null) return;

            WpfPoint currentPosition = e.GetPosition(null);
            Vector diff = _dragStartPoint - currentPosition;

            if (!_isDragging &&
                (Math.Abs(diff.X) > DragThreshold || Math.Abs(diff.Y) > DragThreshold))
            {
                _isDragging = true;

                var data = new WpfDataObject(typeof(INodeViewModel), _dragSource);
                WpfDragDrop.DoDragDrop((DependencyObject)sender, data, WpfDragDropEffects.Move);

                ClearDropFeedback();
                _isDragging = false;
                _dragSource = null;
            }
        }

        private void TreeView_DragOver(object sender, WpfDragEventArgs e)
        {
            if (_vm == null || !e.Data.GetDataPresent(typeof(INodeViewModel)))
            {
                e.Effects = WpfDragDropEffects.None;
                e.Handled = true;
                return;
            }

            var source = (INodeViewModel?)e.Data.GetData(typeof(INodeViewModel));
            if (source == null)
            {
                e.Effects = WpfDragDropEffects.None;
                e.Handled = true;
                return;
            }

            if (source is LayerViewModel && sender is WpfTreeView treeView &&
                TryGetLayerReorderDropInfo(treeView, e, out var layerDropInfo))
            {
                e.Effects = WpfDragDropEffects.Move;
                ShowLayerDropIndicator(treeView, layerDropInfo);
                e.Handled = true;
                return;
            }

            if (source is not LayerViewModel &&
                sender is WpfTreeView nodeTreeView &&
                TryGetNodeReorderDropInfo(nodeTreeView, source, e, out var nodeDropInfo))
            {
                e.Effects = WpfDragDropEffects.Move;
                ShowNodeDropIndicator(nodeTreeView, nodeDropInfo);
            }
            else
            {
                e.Effects = WpfDragDropEffects.None;
                ClearDropFeedback();
            }

            e.Handled = true;
        }

        private void TreeView_DragLeave(object sender, WpfDragEventArgs e)
        {
            if (sender is WpfTreeView treeView)
            {
                var pos = e.GetPosition(treeView);
                if (pos.X >= 0 && pos.X <= treeView.ActualWidth &&
                    pos.Y >= 0 && pos.Y <= treeView.ActualHeight)
                {
                    return;
                }
            }

            ClearDropFeedback();
        }

        private void TreeView_Drop(object sender, WpfDragEventArgs e)
        {
            if (_vm == null || !e.Data.GetDataPresent(typeof(INodeViewModel))) return;

            var source = (INodeViewModel?)e.Data.GetData(typeof(INodeViewModel));
            if (source == null) return;

            if (source is LayerViewModel sourceLayer && sender is WpfTreeView treeView &&
                TryGetLayerReorderDropInfo(treeView, e, out var layerDropInfo))
            {
                var beforePositions = CaptureVisibleLayerItemPositions(treeView);
                ClearDropFeedback();
                bool moved = _vm.ReorderLayerToSlot(sourceLayer, layerDropInfo.SlotIndex);
                if (moved)
                {
                    treeView.UpdateLayout();
                    AnimateLayerDrop(treeView, beforePositions);
                }

                e.Handled = true;
                return;
            }

            if (source is LayerViewModel ||
                sender is not WpfTreeView nodeTreeView ||
                !TryGetNodeReorderDropInfo(nodeTreeView, source, e, out var nodeDropInfo))
            {
                return;
            }

            var beforeNodePositions = CaptureVisibleNodeItemPositions(nodeTreeView, nodeDropInfo.Container);
            ClearDropFeedback();
            bool nodeMoved = _vm.ReorderNodeToSlot(source, nodeDropInfo.Container, nodeDropInfo.SlotIndex);
            if (nodeMoved)
            {
                nodeTreeView.UpdateLayout();
                AnimateNodeDrop(nodeTreeView, nodeDropInfo.Container, beforeNodePositions);
            }

            e.Handled = true;
        }

        private static (TreeViewItem? Item, INodeViewModel? Node, DropPosition Position) GetDropTarget(object sender, WpfDragEventArgs e)
        {
            var hit = VisualTreeHelper.HitTest((Visual)sender, e.GetPosition((IInputElement)sender));
            if (hit == null) return (null, null, DropPosition.After);

            var depObj = hit.VisualHit;
            TreeViewItem? targetItem = null;
            while (depObj != null)
            {
                if (depObj is TreeViewItem tvi)
                {
                    targetItem = tvi;
                    break;
                }

                depObj = VisualTreeHelper.GetParent(depObj);
            }

            if (targetItem?.DataContext is not INodeViewModel targetNode)
                return (null, null, DropPosition.After);

            var headerElement = GetItemHeaderElement(targetItem);
            double headerHeight = headerElement?.ActualHeight ?? 0;
            if (headerHeight <= 0)
            {
                headerHeight = targetItem.ActualHeight;
            }

            if (headerHeight <= 0)
                return (targetItem, targetNode, DropPosition.After);

            double relativeY = e.GetPosition(headerElement ?? targetItem).Y;
            double clampedY = Math.Clamp(relativeY, 0, headerHeight);
            double edgeBandHeight = Math.Min(Math.Max(MinimumDropEdgeBandHeight, headerHeight * 0.25), headerHeight / 2.0);

            DropPosition position;
            if (clampedY <= edgeBandHeight)
                position = DropPosition.Before;
            else if (clampedY >= headerHeight - edgeBandHeight)
                position = DropPosition.After;
            else
                position = DropPosition.Inside;

            return (targetItem, targetNode, position);
        }

        internal static FrameworkElement? GetItemHeaderElement(TreeViewItem targetItem)
        {
            return targetItem.Template?.FindName("PART_Header", targetItem) as FrameworkElement;
        }

        private static bool IsNodeDescendantOf(INodeViewModel ancestor, INodeViewModel node)
        {
            var current = node.Parent;
            while (current != null)
            {
                if (ReferenceEquals(current, ancestor))
                    return true;

                current = current.Parent;
            }

            return false;
        }

        private static LayerViewModel? FindOwningLayer(INodeViewModel node)
        {
            INodeViewModel? current = node;
            while (current != null)
            {
                if (current is LayerViewModel layer)
                    return layer;

                current = current.Parent;
            }

            return null;
        }

        private static TreeViewItem? FindTreeViewItem(WpfTreeView treeView, INodeViewModel node)
        {
            for (int i = 0; i < treeView.Items.Count; i++)
            {
                if (treeView.ItemContainerGenerator.ContainerFromIndex(i) is not TreeViewItem item)
                    continue;

                var found = FindTreeViewItemRecursive(item, node);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static TreeViewItem? FindTreeViewItemRecursive(TreeViewItem item, INodeViewModel node)
        {
            if (ReferenceEquals(item.DataContext, node))
                return item;

            for (int i = 0; i < item.Items.Count; i++)
            {
                if (item.ItemContainerGenerator.ContainerFromIndex(i) is not TreeViewItem childItem)
                    continue;

                var found = FindTreeViewItemRecursive(childItem, node);
                if (found != null)
                    return found;
            }

            return null;
        }

        private bool TryGetNodeReorderDropInfo(
            WpfTreeView treeView,
            INodeViewModel source,
            WpfDragEventArgs e,
            out NodeReorderDropInfo dropInfo)
        {
            dropInfo = default!;
            if (_vm == null || source is LayerViewModel || source.Parent == null)
                return false;

            if (!TryResolveNodeDropContainer(treeView, source, e, out var container, out var containerItem, out var useContainerEdges))
                return false;

            var visibleItems = GetVisibleNodeItems(treeView, container);
            if (visibleItems.Count == 0 || useContainerEdges)
            {
                int edgeSlotIndex = GetContainerEdgeSlotIndex(containerItem, container, e);
                _currentNodeDropContainerId = container.Id;
                _currentNodeDropSlotIndex = edgeSlotIndex;
                _currentNodeDropContainer = container;

                DropPosition edgePosition = edgeSlotIndex <= 0
                    ? DropPosition.Before
                    : DropPosition.After;

                dropInfo = new NodeReorderDropInfo(
                    container,
                    edgeSlotIndex,
                    container,
                    edgePosition,
                    containerItem,
                    null,
                    null);
                return true;
            }

            double y = e.GetPosition(treeView).Y;
            int slotIndex = TryGetStableNodeDropSlotIndex(visibleItems, container, y, out var stableSlotIndex)
                ? stableSlotIndex
                : GetNodeDropSlotIndex(visibleItems, y);

            _currentNodeDropContainerId = container.Id;
            _currentNodeDropSlotIndex = slotIndex;
            _currentNodeDropContainer = container;

            VisibleNodeItem? previousItem = visibleItems.LastOrDefault(item => item.NodeIndex < slotIndex);
            VisibleNodeItem? nextItem = visibleItems.FirstOrDefault(item => item.NodeIndex >= slotIndex);

            if (nextItem != null)
            {
                dropInfo = new NodeReorderDropInfo(
                    container,
                    slotIndex,
                    nextItem.Node,
                    DropPosition.Before,
                    nextItem.Item,
                    previousItem?.Item,
                    nextItem.Item);
                return true;
            }

            var lastVisibleItem = visibleItems[^1];
            dropInfo = new NodeReorderDropInfo(
                container,
                slotIndex,
                lastVisibleItem.Node,
                DropPosition.After,
                lastVisibleItem.Item,
                lastVisibleItem.Item,
                null);
            return true;
        }

        private bool TryResolveNodeDropContainer(
            WpfTreeView treeView,
            INodeViewModel source,
            WpfDragEventArgs e,
            out INodeViewModel container,
            out TreeViewItem containerItem,
            out bool useContainerEdges)
        {
            container = null!;
            containerItem = null!;
            useContainerEdges = false;

            var sourceParent = source.Parent;
            if (sourceParent == null)
                return false;

            var (targetItem, targetNode, _) = GetDropTarget(treeView, e);
            if (targetItem == null || targetNode == null)
            {
                return TryResolveNodeDropContainerFromCurrentState(
                    treeView,
                    source,
                    sourceParent,
                    out container,
                    out containerItem,
                    out useContainerEdges);
            }

            if (sourceParent is NodeGroupViewModel sourceGroup)
            {
                if (ReferenceEquals(targetNode, sourceGroup) || IsNodeDescendantOf(sourceGroup, targetNode))
                {
                    var groupItem = FindTreeViewItem(treeView, sourceGroup);
                    if (groupItem == null)
                        return false;

                    container = sourceGroup;
                    containerItem = groupItem;
                    useContainerEdges = ReferenceEquals(targetNode, sourceGroup);
                    return true;
                }

                return false;
            }

            if (targetNode is LayerViewModel targetLayer)
            {
                container = targetLayer;
                containerItem = targetItem;
                useContainerEdges = true;
                return true;
            }

            if (ReferenceEquals(targetNode.Parent, sourceParent))
            {
                var sameParentItem = FindTreeViewItem(treeView, sourceParent);
                if (sameParentItem == null)
                    return false;

                container = sourceParent;
                containerItem = sameParentItem;
                return true;
            }

            var owningLayer = FindOwningLayer(targetNode);
            if (owningLayer == null)
                return false;

            var owningLayerItem = FindTreeViewItem(treeView, owningLayer);
            if (owningLayerItem == null)
                return false;

            container = owningLayer;
            containerItem = owningLayerItem;
            return true;
        }

        private bool TryResolveNodeDropContainerFromCurrentState(
            WpfTreeView treeView,
            INodeViewModel source,
            INodeViewModel sourceParent,
            out INodeViewModel container,
            out TreeViewItem containerItem,
            out bool useContainerEdges)
        {
            container = null!;
            containerItem = null!;
            useContainerEdges = false;

            if (_currentNodeDropContainer != null)
            {
                var currentContainerItem = FindTreeViewItem(treeView, _currentNodeDropContainer);
                if (currentContainerItem != null)
                {
                    container = _currentNodeDropContainer;
                    containerItem = currentContainerItem;
                    return true;
                }
            }

            if (sourceParent is NodeGroupViewModel sourceGroup)
            {
                var groupItem = FindTreeViewItem(treeView, sourceGroup);
                if (groupItem == null)
                    return false;

                container = sourceGroup;
                containerItem = groupItem;
                return true;
            }

            var sourceLayer = FindOwningLayer(source);
            if (sourceLayer == null)
                return false;

            var sourceLayerItem = FindTreeViewItem(treeView, sourceLayer);
            if (sourceLayerItem == null)
                return false;

            container = sourceLayer;
            containerItem = sourceLayerItem;
            return true;
        }

        private static int GetContainerEdgeSlotIndex(
            TreeViewItem containerItem,
            INodeViewModel container,
            WpfDragEventArgs e)
        {
            var headerElement = GetItemHeaderElement(containerItem);
            double headerHeight = headerElement?.ActualHeight ?? containerItem.ActualHeight;
            if (headerHeight <= 0)
                return container.Children.Count;

            double relativeY = e.GetPosition(headerElement ?? containerItem).Y;
            double clampedY = Math.Clamp(relativeY, 0, headerHeight);
            return clampedY < headerHeight / 2.0
                ? 0
                : container.Children.Count;
        }

        private bool TryGetStableNodeDropSlotIndex(
            IReadOnlyList<VisibleNodeItem> visibleItems,
            INodeViewModel container,
            double y,
            out int slotIndex)
        {
            slotIndex = -1;
            if (_currentNodeDropContainerId != container.Id ||
                _currentNodeDropSlotIndex is not int currentSlotIndex)
            {
                return false;
            }

            var previousItem = visibleItems.LastOrDefault(item => item.NodeIndex < currentSlotIndex);
            var nextItem = visibleItems.FirstOrDefault(item => item.NodeIndex >= currentSlotIndex);

            double lowerBoundary = previousItem?.CenterY ?? double.NegativeInfinity;
            double upperBoundary = nextItem?.CenterY ?? double.PositiveInfinity;

            if (y < lowerBoundary - NodeDropCenterHysteresisPx ||
                y > upperBoundary + NodeDropCenterHysteresisPx)
            {
                return false;
            }

            slotIndex = currentSlotIndex;
            return true;
        }

        private static int GetNodeDropSlotIndex(
            IReadOnlyList<VisibleNodeItem> visibleItems,
            double y)
        {
            foreach (var item in visibleItems)
            {
                if (y < item.CenterY)
                    return item.NodeIndex;
            }

            return visibleItems[^1].NodeIndex + 1;
        }

        private List<VisibleNodeItem> GetVisibleNodeItems(
            WpfTreeView treeView,
            INodeViewModel container)
        {
            var result = new List<VisibleNodeItem>();
            var containerItem = FindTreeViewItem(treeView, container);
            if (containerItem == null)
                return result;

            for (int i = 0; i < container.Children.Count; i++)
            {
                if (containerItem.ItemContainerGenerator.ContainerFromIndex(i) is not TreeViewItem item)
                    continue;

                var header = GetItemHeaderElement(item);
                if (header == null || header.ActualHeight <= 0 || header.ActualWidth <= 0)
                    continue;

                if (item.DataContext is not INodeViewModel node)
                    continue;

                var topLeft = header.TransformToAncestor(treeView).Transform(new WpfPoint(0, 0));
                double top = topLeft.Y;
                double height = header.ActualHeight;
                double bottom = top + height;
                if (bottom < 0 || top > treeView.ActualHeight)
                    continue;

                result.Add(new VisibleNodeItem(i, item, node, header, top, height));
            }

            return result;
        }

        private bool TryGetLayerReorderDropInfo(
            WpfTreeView treeView,
            WpfDragEventArgs e,
            out LayerReorderDropInfo dropInfo)
        {
            dropInfo = default!;
            var visibleItems = GetVisibleLayerItems(treeView);
            if (visibleItems.Count == 0)
                return false;

            double y = e.GetPosition(treeView).Y;
            int slotIndex = TryGetStableLayerDropSlotIndex(visibleItems, y, out var stableSlotIndex)
                ? stableSlotIndex
                : GetLayerDropSlotIndex(visibleItems, y);

            _currentLayerDropSlotIndex = slotIndex;

            VisibleLayerItem? previousItem = visibleItems.LastOrDefault(item => item.LayerIndex < slotIndex);
            VisibleLayerItem? nextItem = visibleItems.FirstOrDefault(item => item.LayerIndex >= slotIndex);

            if (nextItem != null)
            {
                dropInfo = new LayerReorderDropInfo(
                    slotIndex,
                    nextItem.Node,
                    DropPosition.Before,
                    nextItem.Item,
                    previousItem?.Item,
                    nextItem.Item);
                return true;
            }

            var lastVisibleItem = visibleItems[^1];
            dropInfo = new LayerReorderDropInfo(
                slotIndex,
                lastVisibleItem.Node,
                DropPosition.After,
                lastVisibleItem.Item,
                lastVisibleItem.Item,
                null);
            return true;
        }

        private bool TryGetStableLayerDropSlotIndex(
            IReadOnlyList<VisibleLayerItem> visibleItems,
            double y,
            out int slotIndex)
        {
            slotIndex = -1;
            if (_currentLayerDropSlotIndex is not int currentSlotIndex)
                return false;

            var previousItem = visibleItems.LastOrDefault(item => item.LayerIndex < currentSlotIndex);
            var nextItem = visibleItems.FirstOrDefault(item => item.LayerIndex >= currentSlotIndex);

            double lowerBoundary = previousItem?.CenterY ?? double.NegativeInfinity;
            double upperBoundary = nextItem?.CenterY ?? double.PositiveInfinity;

            if (y < lowerBoundary - LayerDropSlotHysteresisPx ||
                y > upperBoundary + LayerDropSlotHysteresisPx)
            {
                return false;
            }

            slotIndex = currentSlotIndex;
            return true;
        }

        private static int GetLayerDropSlotIndex(
            IReadOnlyList<VisibleLayerItem> visibleItems,
            double y)
        {
            foreach (var item in visibleItems)
            {
                if (y < item.CenterY)
                    return item.LayerIndex;
            }

            return visibleItems[^1].LayerIndex + 1;
        }

        private List<VisibleLayerItem> GetVisibleLayerItems(WpfTreeView treeView)
        {
            var result = new List<VisibleLayerItem>();
            if (_vm == null)
                return result;

            if (_layerDragBaseGeometries.Count == 0)
            {
                CaptureLayerDragBaseGeometries(treeView);
            }

            for (int i = 0; i < _vm.LayerViewModels.Count; i++)
            {
                if (treeView.ItemContainerGenerator.ContainerFromIndex(i) is not TreeViewItem item)
                    continue;

                var header = GetItemHeaderElement(item);
                if (header == null || header.ActualHeight <= 0 || header.ActualWidth <= 0)
                    continue;

                if (item.DataContext is not LayerViewModel layerNode)
                    continue;

                if (!_layerDragBaseGeometries.TryGetValue(layerNode.Id, out var baseGeometry))
                {
                    var topLeft = header.TransformToAncestor(treeView).Transform(new WpfPoint(0, 0));
                    baseGeometry = (topLeft.Y, header.ActualHeight);
                    _layerDragBaseGeometries[layerNode.Id] = baseGeometry;
                }

                double top = baseGeometry.Top;
                double height = baseGeometry.Height;
                double bottom = top + height;
                if (bottom < 0 || top > treeView.ActualHeight)
                    continue;

                result.Add(new VisibleLayerItem(i, item, layerNode, header, top, height));
            }

            return result;
        }

        private void CaptureLayerDragBaseGeometries(WpfTreeView treeView)
        {
            if (_vm == null)
                return;

            for (int i = 0; i < _vm.LayerViewModels.Count; i++)
            {
                if (treeView.ItemContainerGenerator.ContainerFromIndex(i) is not TreeViewItem item)
                    continue;

                if (item.DataContext is not LayerViewModel layerNode)
                    continue;

                var header = GetItemHeaderElement(item);
                if (header == null || header.ActualHeight <= 0 || header.ActualWidth <= 0)
                    continue;

                var topLeft = header.TransformToAncestor(treeView).Transform(new WpfPoint(0, 0));
                _layerDragBaseGeometries[layerNode.Id] = (topLeft.Y, header.ActualHeight);
            }
        }

        private static TranslateTransform GetOrCreateTranslateTransform(TreeViewItem item)
        {
            if (item.RenderTransform is TranslateTransform existingTranslate)
                return existingTranslate;

            if (item.RenderTransform is TransformGroup existingGroup)
            {
                var groupTranslate = existingGroup.Children.OfType<TranslateTransform>().FirstOrDefault();
                if (groupTranslate != null)
                    return groupTranslate;

                var newTranslate = new TranslateTransform();
                existingGroup.Children.Add(newTranslate);
                return newTranslate;
            }

            if (item.RenderTransform is Transform existingTransform &&
                existingTransform != Transform.Identity)
            {
                var group = new TransformGroup();
                group.Children.Add(existingTransform);
                var translate = new TranslateTransform();
                group.Children.Add(translate);
                item.RenderTransform = group;
                return translate;
            }

            var transform = new TranslateTransform();
            item.RenderTransform = transform;
            return transform;
        }

        private Dictionary<int, double> CaptureVisibleLayerItemPositions(WpfTreeView treeView)
        {
            var positions = new Dictionary<int, double>();
            foreach (var item in GetVisibleLayerItems(treeView))
            {
                double top = item.Item.TransformToAncestor(treeView).Transform(new WpfPoint(0, 0)).Y;
                positions[item.Node.Id] = top;
            }

            return positions;
        }

        private Dictionary<int, double> CaptureVisibleNodeItemPositions(
            WpfTreeView treeView,
            INodeViewModel container)
        {
            var positions = new Dictionary<int, double>();
            foreach (var item in GetVisibleNodeItems(treeView, container))
            {
                double top = item.Item.TransformToAncestor(treeView).Transform(new WpfPoint(0, 0)).Y;
                positions[item.Node.Id] = top;
            }

            return positions;
        }

        private void AnimateLayerDrop(WpfTreeView treeView, IReadOnlyDictionary<int, double> beforePositions)
        {
            foreach (var item in GetVisibleLayerItems(treeView))
            {
                if (!beforePositions.TryGetValue(item.Node.Id, out double beforeTop))
                    continue;

                double afterTop = item.Item.TransformToAncestor(treeView).Transform(new WpfPoint(0, 0)).Y;
                double delta = beforeTop - afterTop;
                if (Math.Abs(delta) < 0.5)
                    continue;

                var transform = GetOrCreateTranslateTransform(item.Item);
                transform.BeginAnimation(TranslateTransform.YProperty, null);
                transform.Y = delta;
                transform.BeginAnimation(
                    TranslateTransform.YProperty,
                    new DoubleAnimation(0, TimeSpan.FromMilliseconds(DropAnimationDurationMs))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    },
                    HandoffBehavior.SnapshotAndReplace);
            }
        }

        private void AnimateNodeDrop(
            WpfTreeView treeView,
            INodeViewModel container,
            IReadOnlyDictionary<int, double> beforePositions)
        {
            foreach (var item in GetVisibleNodeItems(treeView, container))
            {
                if (!beforePositions.TryGetValue(item.Node.Id, out double beforeTop))
                    continue;

                double afterTop = item.Item.TransformToAncestor(treeView).Transform(new WpfPoint(0, 0)).Y;
                double delta = beforeTop - afterTop;
                if (Math.Abs(delta) < 0.5)
                    continue;

                var transform = GetOrCreateTranslateTransform(item.Item);
                transform.BeginAnimation(TranslateTransform.YProperty, null);
                transform.Y = delta;
                transform.BeginAnimation(
                    TranslateTransform.YProperty,
                    new DoubleAnimation(0, TimeSpan.FromMilliseconds(DropAnimationDurationMs))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    },
                    HandoffBehavior.SnapshotAndReplace);
            }
        }

        private void ShowLayerDropIndicator(WpfTreeView treeView, LayerReorderDropInfo dropInfo)
        {
            ShowDropIndicator(
                treeView,
                dropInfo.AnchorItem,
                dropInfo.Position,
                dropInfo.PreviousItem,
                dropInfo.NextItem);
        }

        private void ShowNodeDropIndicator(WpfTreeView treeView, NodeReorderDropInfo dropInfo)
        {
            ShowDropIndicator(
                treeView,
                dropInfo.AnchorItem,
                dropInfo.Position,
                dropInfo.PreviousItem,
                dropInfo.NextItem);
        }

        private void ShowDropIndicator(
            FrameworkElement adornedElement,
            TreeViewItem targetItem,
            DropPosition position,
            TreeViewItem? previousItem,
            TreeViewItem? nextItem)
        {
            if (ReferenceEquals(_currentIndicatorHost, adornedElement) &&
                ReferenceEquals(_currentIndicatorTargetItem, targetItem) &&
                ReferenceEquals(_currentIndicatorPreviousItem, previousItem) &&
                ReferenceEquals(_currentIndicatorNextItem, nextItem) &&
                _currentIndicatorPosition == position)
            {
                return;
            }

            RemoveDropIndicator();

            var layer = AdornerLayer.GetAdornerLayer(adornedElement);
            if (layer == null) return;

            _adornerLayer = layer;
            _currentAdorner = new DropIndicatorAdorner(
                adornedElement,
                targetItem,
                position,
                previousItem,
                nextItem,
                LayerPreviewGapSize);
            _adornerLayer.Add(_currentAdorner);
            _currentIndicatorHost = adornedElement;
            _currentIndicatorTargetItem = targetItem;
            _currentIndicatorPreviousItem = previousItem;
            _currentIndicatorNextItem = nextItem;
            _currentIndicatorPosition = position;
        }

        private void RemoveDropIndicator()
        {
            if (_currentAdorner != null && _adornerLayer != null)
            {
                _adornerLayer.Remove(_currentAdorner);
                _currentAdorner = null;
            }

            _adornerLayer = null;
            _currentIndicatorHost = null;
            _currentIndicatorTargetItem = null;
            _currentIndicatorPreviousItem = null;
            _currentIndicatorNextItem = null;
            _currentIndicatorPosition = null;
        }

        private void ClearDropFeedback()
        {
            RemoveDropIndicator();
            _currentLayerDropSlotIndex = null;
            _currentNodeDropSlotIndex = null;
            _currentNodeDropContainerId = null;
            _currentNodeDropContainer = null;
            _layerDragBaseGeometries.Clear();
        }

        private sealed record VisibleLayerItem(
            int LayerIndex,
            TreeViewItem Item,
            LayerViewModel Node,
            FrameworkElement Header,
            double Top,
            double Height)
        {
            public double CenterY => Top + Height / 2.0;
        }

        private sealed record LayerReorderDropInfo(
            int SlotIndex,
            LayerViewModel TargetLayer,
            DropPosition Position,
            TreeViewItem AnchorItem,
            TreeViewItem? PreviousItem,
            TreeViewItem? NextItem);

        private sealed record VisibleNodeItem(
            int NodeIndex,
            TreeViewItem Item,
            INodeViewModel Node,
            FrameworkElement Header,
            double Top,
            double Height)
        {
            public double CenterY => Top + Height / 2.0;
        }

        private sealed record NodeReorderDropInfo(
            INodeViewModel Container,
            int SlotIndex,
            INodeViewModel TargetNode,
            DropPosition Position,
            TreeViewItem AnchorItem,
            TreeViewItem? PreviousItem,
            TreeViewItem? NextItem);
    }

    internal class DropIndicatorAdorner : Adorner
    {
        private readonly FrameworkElement _adornedHost;
        private readonly DropPosition _position;
        private readonly TreeViewItem _targetItem;
        private readonly TreeViewItem? _previousItem;
        private readonly TreeViewItem? _nextItem;
        private readonly double _previewGapSize;

        public DropIndicatorAdorner(
            FrameworkElement adornedHost,
            TreeViewItem targetItem,
            DropPosition position,
            TreeViewItem? previousItem,
            TreeViewItem? nextItem,
            double previewGapSize)
            : base(adornedHost)
        {
            _adornedHost = adornedHost;
            _targetItem = targetItem;
            _position = position;
            _previousItem = previousItem;
            _nextItem = nextItem;
            _previewGapSize = previewGapSize;
            IsHitTestVisible = false;
        }

        protected override void OnRender(System.Windows.Media.DrawingContext drawingContext)
        {
            var headerElement = LayerViewView.GetItemHeaderElement(_targetItem);
            if (headerElement == null)
                return;

            var bounds = new Rect(0, 0, headerElement.ActualWidth, headerElement.ActualHeight);
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;

            var point = headerElement.TransformToAncestor(_adornedHost).Transform(new WpfPoint(0, 0));

            double y = _position switch
            {
                DropPosition.Before => point.Y - 1,
                DropPosition.After => point.Y + bounds.Height + 1,
                DropPosition.Inside => point.Y + bounds.Height / 2,
                _ => point.Y
            };

            var accentBrush = new WpfSolidColorBrush(WpfColor.FromRgb(0, 120, 215));
            accentBrush.Freeze();
            var slotBrush = new WpfSolidColorBrush(WpfColor.FromArgb(64, 0, 120, 215));
            slotBrush.Freeze();

            if (_position != DropPosition.Inside)
            {
                const double slotHeight = 10.0;
                double slotTop = y - slotHeight / 2.0;
                drawingContext.DrawRectangle(
                    slotBrush,
                    null,
                    new Rect(point.X, slotTop, bounds.Width, slotHeight));
            }

            var pen = new WpfPen(accentBrush, 2);
            pen.Freeze();

            drawingContext.DrawLine(
                pen,
                new WpfPoint(point.X, y),
                new WpfPoint(point.X + bounds.Width, y));

            if (_position != DropPosition.Inside)
            {
                const double triangleSize = 4.0;
                drawingContext.DrawGeometry(
                    accentBrush,
                    null,
                    new GeometryGroup
                    {
                        Children =
                        {
                            new PathGeometry(new[]
                            {
                                new PathFigure(new WpfPoint(point.X, y), new[]
                                {
                                    new LineSegment(new WpfPoint(point.X - triangleSize, y - triangleSize), true),
                                    new LineSegment(new WpfPoint(point.X + triangleSize, y - triangleSize), true),
                                }, true)
                            })
                        }
                    });
            }
        }
    }
}
