using System.Diagnostics;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Model;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.Models;

/// <summary>
/// 选区边界服务。
/// 负责维护当前选中集的订阅、脏状态、缓存的合并边界以及交互期 override 边界。
/// 约束：
/// 1. 仅订阅当前选中对象，不订阅全画布。
/// 2. 图形变化只标记 dirty，不立即重算，真正访问边界时再懒计算。
/// 3. 拖拽/缩放等交互可通过 override 直接提供本次会话的边界，避免每帧全量扫描。
/// </summary>
internal sealed class SelectionService
{
    private readonly Dictionary<int, DrawObject> _subscribedSelected = new();

    private bool _boundsDirty = true;
    private SKRect? _cachedMergedBounds;
    private SKRect? _overrideMergedBounds;

    public SelectionService(DocumentContext context)
    {
    }

    public SKRect? CachedBounds => _overrideMergedBounds ?? _cachedMergedBounds;

    public void Reset()
    {
        UnsubscribeAll();
        _boundsDirty = true;
        _cachedMergedBounds = null;
        _overrideMergedBounds = null;
    }

    public void SyncSelection(IEnumerable<IShape>? shapes)
    {
        var next = new Dictionary<int, DrawObject>();
        if (shapes != null)
        {
            foreach (var shape in shapes)
            {
                if (shape is not DrawObject drawObject)
                    continue;

                next.TryAdd(drawObject.UId, drawObject);
            }
        }

        if (HasSameSelection(next))
        {
            // 兼容旧语义：即使选中集 id 未变，重复 SetSelectedShapes() 也要清理旧 override
            // 并标记 merged bounds 失效，供后续按最新几何懒重算。
            MarkDirty(clearOverride: true);
            return;
        }

        foreach (var entry in _subscribedSelected)
        {
            if (!next.ContainsKey(entry.Key))
                entry.Value.BoundingBoxInvalidated -= OnSelectedShapeBoundingBoxInvalidated;
        }

        foreach (var entry in next)
        {
            if (!_subscribedSelected.ContainsKey(entry.Key))
                entry.Value.BoundingBoxInvalidated += OnSelectedShapeBoundingBoxInvalidated;
        }

        _subscribedSelected.Clear();
        foreach (var entry in next)
            _subscribedSelected.Add(entry.Key, entry.Value);

        MarkDirty(clearOverride: true);
    }

    public void Invalidate()
    {
        MarkDirty(clearOverride: true);
    }

    public void SetOverride(SKRect? boundsOverride)
    {
        if (boundsOverride is { } rect && !rect.IsEmpty)
        {
            _overrideMergedBounds = rect;
            _cachedMergedBounds = rect;
            _boundsDirty = false;
            return;
        }

        _overrideMergedBounds = null;
        _boundsDirty = true;
    }

    public SKRect GetMergedBounds(ISelectionSet shapes)
    {
        var geometry = DocumentContext.Instance.IsDragControlPoint
            ? shapes.GetPreviewAABB()
            : shapes.GetOBB();
        var mergedBounds = geometry.Corners.ToRect();

        _cachedMergedBounds = mergedBounds.IsEmpty ? null : mergedBounds;
        _boundsDirty = false;
        return mergedBounds;
    }

    private void OnSelectedShapeBoundingBoxInvalidated(DrawObject _)
    {
        // 只做失效标记，不在事件回调里做重计算。
        MarkDirty(clearOverride: true);
    }

    private void MarkDirty(bool clearOverride)
    {
        _boundsDirty = true;
        _cachedMergedBounds = null;
        if (clearOverride)
            _overrideMergedBounds = null;
    }

    private bool HasSameSelection(Dictionary<int, DrawObject> next)
    {
        if (_subscribedSelected.Count != next.Count)
            return false;

        foreach (var id in next.Keys)
        {
            if (!_subscribedSelected.ContainsKey(id))
                return false;
        }

        return true;
    }

    private void UnsubscribeAll()
    {
        foreach (var entry in _subscribedSelected)
            entry.Value.BoundingBoxInvalidated -= OnSelectedShapeBoundingBoxInvalidated;

        _subscribedSelected.Clear();
    }    
}
