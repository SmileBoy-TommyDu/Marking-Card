using DrSoft.Drawing.Controls.Commands;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Event.Tool;
using DrSoft.Drawing.Model;
using SkiaSharp;
using System.Diagnostics;
using System.Windows.Input;
using Cursor = System.Windows.Input.Cursor;

namespace DrSoft.Drawing.Controls.Tools;

/// <summary>
/// 路径节点编辑会话。
/// 封装节点拖拽、加点、删点、分离节点以及“移动节点”选中态的完整流程。
/// </summary>
internal sealed class PathNodeEditSession : IToolSelectSession
{
    private readonly DocumentContext _context;
    private readonly double _controlPointSize;

    private bool _isDraggingPathNode;
    private DrawCombination? _pathNodeCombo;
    private DrawObject? _pathNodeChildShape;
    private int _pathNodeChildPointIndex = -1;
    private SKPoint _pathNodeDragStartWorld = SKPoint.Empty;
    private SKMatrix _pathNodeComboInverse = SKMatrix.CreateIdentity();

    private DrawCombination? _moveNodeCombo;
    private DrawObject? _moveNodeChildShape;
    private int _moveNodeChildPointIndex = -1;
    private readonly List<(DrawCombination Combo, DrawObject Child, int PointIndex)> _selectedPathNodes = new();
    private bool _isNodeBoxSelecting;

    private CommandEdit? _pendingEditCommand;
    private bool _lastMouseDownNeedRedraw;
    private bool _lastMouseUpNeedRedraw;

    public PathNodeEditSession(DocumentContext context, double controlPointSize)
    {
        _context = context;
        _controlPointSize = controlPointSize;
    }

    public string Name => "PathNodeEdit";

    public bool IsActive => _isDraggingPathNode || _isNodeBoxSelecting;

    public Cursor? SuggestedCursor => null;

    public ControlPointType? CompletedControlPoint => null;

    internal bool LastMouseDownNeedRedraw => _lastMouseDownNeedRedraw;

    internal bool LastMouseUpNeedRedraw => _lastMouseUpNeedRedraw;

    public bool IsDraggingPathNode => _isDraggingPathNode;
    public bool IsNodeEditing => _context.IsNodeEditing;
    public NodeEditSubMode NodeEditSubMode => _context.IsNodeEditing ? _context.NodeEditSubMode : NodeEditSubMode.None;
    public bool IsAddNodesMode => NodeEditSubMode == NodeEditSubMode.Add;
    public bool IsDeleteNodesMode => NodeEditSubMode == NodeEditSubMode.Delete;
    public bool IsSeparateNodesMode => NodeEditSubMode == NodeEditSubMode.Separate;
    public bool IsSelectNodesMode => NodeEditSubMode == NodeEditSubMode.Select;
    public bool IsNodeBoxSelecting => _isNodeBoxSelecting;

    public bool TryMouseDown(SKPoint point, out string message)
    {
        _lastMouseDownNeedRedraw = false;
        Debug.WriteLine($"[MouseDown] ({point.X}, {point.Y})");
        bool isSelecting = true;
        bool needRedrawOnDown = false;

        bool handledDelete = TryHandlePathNodeClickToDelete(point, ref isSelecting);
        if (handledDelete)
        {
            message = "删除节点子模式已处理按下";
            return true;
        }

        bool handledSeparate = TryHandlePathNodeClickToSeparate(point, ref isSelecting);
        if (handledSeparate)
        {
            message = "分离节点子模式已处理按下";
            return true;
        }

        bool handledSelect = TryHandlePathNodeClickToSelect(point, ref isSelecting, ref needRedrawOnDown);
        if (handledSelect)
        {
            _lastMouseDownNeedRedraw = needRedrawOnDown;
            message = "节点选择子模式已处理按下";
            return true;
        }

        bool handledDrag = TryHandlePathNodeClick(point, ref isSelecting, ref needRedrawOnDown);
        if (handledDrag)
        {
            _lastMouseDownNeedRedraw = needRedrawOnDown;
            message = "开始拖拽路径节点";
            return true;
        }

        bool handledAddNode = TryHandlePathLineClickToAddNode(point, ref isSelecting);
        if (handledAddNode)
        {
            message = "加点子模式已处理按下";
            return true;
        }

        bool shouldSwallowSubMode = _context.IsNodeEditing
            && _context.NodeEditSubMode != NodeEditSubMode.None;
        if (shouldSwallowSubMode)
        {
            message = "节点编辑子模式吞掉本次按下";
            return true;
        }

        message = "路径节点会话未命中";
        return false;
    }

    public bool TryMouseMove(SKPoint point, out string message)
    {
        if (_isDraggingPathNode)
        {
            HandlePathNodeDrag(point);
            message = "更新路径节点拖拽";
            return true;
        }

        if (_isNodeBoxSelecting)
        {
            HandleNodeBoxSelection(point);
            message = "更新节点框选";
            return true;
        }

        message = "路径节点会话未激活";
        return false;
    }

    public bool TryMouseUp(SKPoint point, out string message)
    {
        _lastMouseUpNeedRedraw = false;

        if (_isDraggingPathNode)
        {
            bool completedDrag = CompletePathNodeDrag();
            message = completedDrag
                ? "完成路径节点拖拽"
                : "路径节点拖拽完成失败";
            return completedDrag;
        }

        if (_isNodeBoxSelecting)
        {
            bool completedSelection = CompleteNodeBoxSelection(point);
            _lastMouseUpNeedRedraw = completedSelection;
            message = completedSelection
                ? "完成节点框选"
                : "节点框选完成失败";
            return completedSelection;
        }

        message = "路径节点会话未激活";
        return false;
    }

    public bool TryRightMouseDown(SKPoint point, out string message)
    {
        message = "路径节点会话不处理右键";
        return false;
    }

    public void EnterNodeEditMode()
    {
        _context.IsNodeEditing = true;
        SetNodeEditSubMode(NodeEditSubMode.None);
        ClearSelectedMoveNode();
        ClearSelectedPathNodes();
    }

    public void ExitNodeEditMode()
    {
        _context.IsNodeEditing = false;
        SetNodeEditSubMode(NodeEditSubMode.None);
        _context.SelectedSeparateNodeWorldPosition = null;
        ClearSelectedMoveNode();
        ClearSelectedPathNodes();
        Cancel();
    }

    public void SetNodeEditSubMode(NodeEditSubMode subMode)
    {
        _context.NodeEditSubMode = _context.IsNodeEditing ? subMode : NodeEditSubMode.None;
        if (_context.NodeEditSubMode != NodeEditSubMode.Separate)
        {
            _context.SelectedSeparateNodeWorldPosition = null;
        }

        bool keepsPathNodeSelection = _context.NodeEditSubMode == NodeEditSubMode.Select;
        if (!keepsPathNodeSelection)
        {
            ClearSelectedPathNodes();
        }

        PublishNodeEditStateChanged();
    }

    public void SetDeleteNodesMode(bool turnOn) => SetNodeEditSubMode(turnOn ? NodeEditSubMode.Delete : NodeEditSubMode.None);

    public void SetAddNodesMode(bool turnOn) => SetNodeEditSubMode(turnOn ? NodeEditSubMode.Add : NodeEditSubMode.None);

    public void SetSeparateNodesMode(bool turnOn) => SetNodeEditSubMode(turnOn ? NodeEditSubMode.Separate : NodeEditSubMode.None);

    /// <summary>
    /// 命中路径节点后进入拖拽会话，并为最终 CommandEdit 捕获 before-state。
    /// </summary>
    public bool TryHandlePathNodeClick(SKPoint point, ref bool isSelecting, ref bool needRedrawOnDown)
    {
        // 加点/删除/分离模式下不进入拖拽：模式专属逻辑已在前序处理，
        // 此处若再命中节点会抢先拦截，导致节点被移动而非执行模式操作
        // （如加点时旁边节点被拖到点击处、点的个数未增多）。
        if (IsDeleteNodesMode || IsSeparateNodesMode)
            return false;

        if (_context.ActiveCanvas?.SelectedShapeCount != 1)
            return false;

        var shape = _context.ActiveCanvas.Selection.First();
        if (shape is not DrawObject drawObject || !drawObject.IsPathEditing)
            return false;
        if (drawObject is not DrawCombination combo)
            return false;

        var (child, childIdx) = GetPathNodeChildAt(combo, point);
        if (child == null)
            return false;

        _isDraggingPathNode = true;
        isSelecting = false;
        _pathNodeCombo = combo;
        _pathNodeChildShape = child;
        _pathNodeChildPointIndex = childIdx;
        _pathNodeDragStartWorld = combo.GetInverseMatrix().MapPoint(point);
        _pathNodeComboInverse = combo.GetInverseMatrix();
        _pendingEditCommand = new CommandEdit(new[] { combo }, "拖动节点");

        needRedrawOnDown = true;
        _context.ReportStatus($"拖动节点 [{child.GetType().Name}][{childIdx}]");
        return true;
    }

    public void HandlePathNodeDrag(SKPoint point)
    {
        if (!_isDraggingPathNode || _pathNodeCombo == null || _pathNodeChildShape == null)
            return;

        SKPoint newWorldPos = new((float)point.X, (float)point.Y);
        // 真实拖拽逻辑委托给组合图形自身，保持"节点规则归图形"的边界。
        UpdateChildPointByDrag(_pathNodeCombo, _pathNodeChildShape, _pathNodeChildPointIndex, newWorldPos);

        // 注意：不再重新评估 _pathNodeChildPointIndex。
        // MoveChildPathNodeToWorldPosition 会触发 combo.UpdateSetProperty 重算 SharpCenter，
        // 导致 _pathNodeComboInverse 过期。若用过期矩阵重新匹配索引，
        // 在密集节点处会错选其他节点，造成螺旋状路径。
        // 拖动期间应保持用户最初选定的 pointIndex 不变。

        _context.SelectedMoveNodeWorldPosition = newWorldPos;

        _context.MarkSelectedDirty();
        _context.ReportStatus($"移动节点: ({newWorldPos.X:F1}, {newWorldPos.Y:F1})");
    }

    public bool CompletePathNodeDrag()
    {
        if (!_isDraggingPathNode || _pathNodeCombo == null || _pathNodeChildShape == null)
            return false;

        _isDraggingPathNode = false;
        SetMoveNodeSelection(_pathNodeCombo, _pathNodeChildShape, _pathNodeChildPointIndex);

        if (_pendingEditCommand != null)
        {
            _pendingEditCommand.CaptureAfterState();
            _context.ActiveCanvas?.CommandManager.PushExecutedCommand(_pendingEditCommand);
            _pendingEditCommand = null;
        }

        var canvas = _context.ActiveCanvas as DrawingCanvas;
        canvas?.InvalidateVisibleCache();
        canvas?.InvalidateGeometryCaches(new List<DrawObject> { _pathNodeCombo });
        _context.PublishSelectChanged();
        _context.PublishTransformChange();
        return true;
    }

    public void ResetTransientState()
    {
        _pendingEditCommand = null;
    }

    public void Cancel()
    {
        _pendingEditCommand = null;
        _isDraggingPathNode = false;
        _lastMouseDownNeedRedraw = false;
        _lastMouseUpNeedRedraw = false;
        if (_isNodeBoxSelecting)
        {
            _isNodeBoxSelecting = false;
            _context.BoxSelect.Reset();
        }
    }

    public bool TryHandlePathNodeClickToDelete(SKPoint point, ref bool isSelecting)
    {
        if (!IsDeleteNodesMode) return false;
        if (_context.ActiveCanvas?.SelectedShapeCount != 1) return false;

        var shape = _context.ActiveCanvas.Selection.First();
        if (shape is not DrawObject drawObject || !drawObject.IsPathEditing) return false;
        if (drawObject is not DrawCombination combo) return false;

        var worldPositions = combo.GetPathNodeWorldPositions();
        if (worldPositions.Count == 0) return false;

        float tolerance = (float)(_controlPointSize * 2) / (_context.ActiveCanvas?.Viewport.Scale ?? 1.0f);
        float toleranceSq = tolerance * tolerance;
        int hitIndex = -1;
        for (int i = 0; i < worldPositions.Count; i++)
        {
            float dx = worldPositions[i].X - point.X;
            float dy = worldPositions[i].Y - point.Y;
            if (dx * dx + dy * dy <= toleranceSq) { hitIndex = i; break; }
        }
        if (hitIndex < 0) return false;

        isSelecting = false;

        // 当节点数已经不足以维持合法路径时，现有语义是直接删除整个组合容器。
        if (worldPositions.Count <= 2)
        {
            if (_context.ActiveCanvas is DrawingCanvas canvas)
            {
                var removeCommand = new CommandRemove(canvas.LayerViewModels, new[] { (IShape)combo });
                canvas.CommandManager.Execute(removeCommand);
                canvas.ClearSelectedShapes();
            }
            return true;
        }

        // 创建 CommandEdit 捕获 before 快照，支持撤销/重做
        var editCommand = new CommandEdit(new[] { combo }, "删除节点");

        if (!combo.DeletePathNodeAtWorldPosition(worldPositions[hitIndex]))
            return false;

        editCommand.CaptureAfterState();
        (_context.ActiveCanvas as DrawingCanvas)?.CommandManager.PushExecutedCommand(editCommand);

        var canvasForDelete = _context.ActiveCanvas as DrawingCanvas;
        canvasForDelete?.InvalidateVisibleCache();
        canvasForDelete?.InvalidateGeometryCaches(new List<DrawObject> { combo });
        _context.PublishTransformChange();
        _context.ReportStatus($"已删除节点 {hitIndex}");
        return true;
    }

    public bool TryHandlePathNodeClickToSeparate(SKPoint point, ref bool isSelecting)
    {
        if (!IsSeparateNodesMode) return false;
        if (_context.ActiveCanvas?.SelectedShapeCount != 1) return false;

        var shape = _context.ActiveCanvas.Selection.First();
        if (shape is not DrawObject drawObject || !drawObject.IsPathEditing) return false;
        if (drawObject is not DrawCombination combo) return false;

        var (child, childIdx) = GetPathNodeChildAt(combo, point);
        if (child == null) return false;

        float distance = _context.SeparateNodeDistance;
        if (distance <= 0) return false;

        isSelecting = false;

        // 创建 CommandEdit 捕获 before 快照，支持撤销/重做
        var editCommand = new CommandEdit(new[] { combo }, "分离节点");

        combo.SeparatePathNode(child, childIdx, distance);

        editCommand.CaptureAfterState();
        (_context.ActiveCanvas as DrawingCanvas)?.CommandManager.PushExecutedCommand(editCommand);

        SetSeparateNodeSelection(combo, child, childIdx);
        var canvas = _context.ActiveCanvas as DrawingCanvas;
        canvas?.InvalidateVisibleCache();
        canvas?.InvalidateGeometryCaches(new List<DrawObject> { combo });
        _context.PublishTransformChange();
        _context.ReportStatus($"已分离节点 [{child.GetType().Name}][{childIdx}]，距离 {distance:F2} mm");
        return true;
    }

    public bool TryHandlePathLineClickToAddNode(SKPoint point, ref bool isSelecting)
    {
        if (!IsAddNodesMode) return false;
        if (_context.ActiveCanvas?.SelectedShapeCount != 1) return false;

        var shape = _context.ActiveCanvas.Selection.First();
        if (shape is not DrawObject drawObject || !drawObject.IsPathEditing) return false;
        if (drawObject is not DrawCombination combo) return false;

        var worldNodes = combo.GetPathNodeWorldPositions();
        if (worldNodes.Count < 2) return false;

        // 构建世界坐标系路径，直接在世界空间做最近点搜索：
        // 无需 pathScale 换算，容差 = 屏幕像素 / 视口缩放（纯世界量），与图形缩放无关。
        using var localPath = drawObject.GetPath();
        if (localPath == null || localPath.IsEmpty) return false;
        using var worldPath = new SKPath(localPath);
        worldPath.Transform(drawObject.GetTransformMatrix());

        float tol = 6f / (_context.ActiveCanvas?.Viewport?.Scale ?? 1f);
        if (!FindNearestPointOnPath(worldPath, point, tol, out var bestWorld, out _))
            return false;

        SKPoint newWorldPos = bestWorld;

        // 防止在已有节点位置重复添加：如果最近点与已有节点距离过近（< 0.01mm），拒绝添加
        // FindNearestPointOnPath 的投影算法可能返回恰好落在端点上的点
        const float minNodeDistSq = 1e-6f; // 0.001mm 的平方
        foreach (var existingNode in worldNodes)
        {
            float dx = newWorldPos.X - existingNode.X;
            float dy = newWorldPos.Y - existingNode.Y;
            if (dx * dx + dy * dy < minNodeDistSq)
                return false; // 太接近已有节点，不添加
        }

        // 创建 CommandEdit 捕获 before 快照，支持撤销/重做
        var editCommand = new CommandEdit(new[] { combo }, "添加节点");

        if (!combo.InsertPathNodeAtWorldPosition(newWorldPos))
            return false;

        editCommand.CaptureAfterState();
        (_context.ActiveCanvas as DrawingCanvas)?.CommandManager.PushExecutedCommand(editCommand);

        isSelecting = false;

        var canvas = _context.ActiveCanvas as DrawingCanvas;
        canvas?.InvalidateVisibleCache();
        canvas?.InvalidateGeometryCaches(new List<DrawObject> { combo });
        _context.PublishTransformChange();
        return true;
    }

    public bool TryHandlePathNodeClickToSelect(SKPoint point, ref bool isSelecting, ref bool needRedrawOnDown)
    {
        bool canSelectPathNodeByClick = CanSelectPathNodeByClick();
        if (!canSelectPathNodeByClick)
        {
            return false;
        }

        if (!TryGetEditableCombination(out var combo))
        {
            return false;
        }

        var (child, childIdx) = GetPathNodeChildAt(combo, point);
        if (child != null)
        {
            isSelecting = false;
            needRedrawOnDown = true;
            // 路径节点蓝色多选与默认红点选中共享同一套节点命中，
            // 命中蓝色多选时先清掉红点状态，避免 Move 与一次性动作同时指向不同节点。
            ClearSelectedMoveNode();
            bool isShiftPressed = _context.IsShiftPressed();
            if (isShiftPressed)
            {
                ToggleSelectedPathNode(combo, child, childIdx);
                bool isSelected = ContainsSelectedPathNode(combo, child, childIdx);
                string actionText = isSelected ? "追加选中节点" : "取消选中节点";
                _context.ReportStatus($"{actionText} [{child.GetType().Name}][{childIdx}]");
                return true;
            }

            SetSelectedPathNodes([(combo, child, childIdx)]);
            _context.ReportStatus($"选中节点 [{child.GetType().Name}][{childIdx}]");
            return true;
        }

        StartNodeBoxSelection(point);
        isSelecting = false;
        needRedrawOnDown = true;
        ClearSelectedMoveNode();
        return true;
    }

    public void HandleNodeBoxSelection(SKPoint point)
    {
        if (!_isNodeBoxSelecting)
        {
            return;
        }

        _context.BoxSelect.Current = point;
        _context.ReportStatus($"框选节点: ({point.X:F1}, {point.Y:F1})");
    }

    public bool CompleteNodeBoxSelection(SKPoint point)
    {
        if (!_isNodeBoxSelecting)
        {
            return false;
        }

        _context.BoxSelect.Current = point;
        _isNodeBoxSelecting = false;

        if (!TryGetEditableCombination(out var combo))
        {
            _context.BoxSelect.Reset();
            return false;
        }

        var selectionRect = BuildNodeSelectionRect();
        var matchedNodes = new List<(DrawCombination Combo, DrawObject Child, int PointIndex)>();
        var localPositions = combo.GetPathNodeLocalPositions();

        foreach (var (worldPos, child, pointIndex) in localPositions)
        {
            // localPositions 已经是世界坐标
            bool containsNode = selectionRect.Contains(worldPos.X, worldPos.Y);
            if (!containsNode)
            {
                continue;
            }

            matchedNodes.Add((combo, child, pointIndex));
        }

        SetSelectedPathNodes(matchedNodes);
        _context.BoxSelect.Reset();
        _context.ReportStatus($"框选节点完成: 选中 {matchedNodes.Count} 个节点");
        return true;
    }

    public bool ExtendSelectedPathNodes()
    {
        if (!CanExtendSelectedPathNodes())
        {
            return false;
        }

        TryGetEditableCombination(out var combo);
        var dialogResult = _context.RequestExtendNodeDialog();
        if (dialogResult == null || !dialogResult.Confirmed)
        {
            return true;
        }

        var selectedNodeSnapshots = CaptureSelectedPathNodeSnapshots();
        var orderedSnapshots = selectedNodeSnapshots
            .OrderBy(node => node.PointIndex)
            .ToList();
        var firstSelectedSnapshot = selectedNodeSnapshots[0];
        var firstContinuousSnapshot = orderedSnapshots[0];
        var lastContinuousSnapshot = orderedSnapshots[^1];

        // 得到变化坐标
        var targetWorldPos = !dialogResult.IsRelativeToPrevious ? new SKPoint(dialogResult.X, dialogResult.Y)
            : new SKPoint(
                firstSelectedSnapshot.WorldPos.X + dialogResult.X,
                firstSelectedSnapshot.WorldPos.Y + dialogResult.Y);

        var delta = new SKPoint(
            targetWorldPos.X - firstSelectedSnapshot.WorldPos.X,
            targetWorldPos.Y - firstSelectedSnapshot.WorldPos.Y);

        var editCommand = new CommandEdit(new[] { combo }, "延伸节点");
        var newSelectedNodes = new List<(DrawCombination Combo, DrawObject Child, int PointIndex)>();
        bool extended = TryExtendSelectedPathNodes(
            combo,
            firstContinuousSnapshot.Child,
            firstContinuousSnapshot.PointIndex,
            lastContinuousSnapshot.PointIndex,
            delta,
            newSelectedNodes);
        if (!extended)
        {
            return false;
        }

        var beforeSelectionNodes = selectedNodeSnapshots
            .Select(node => (node.Combo, node.Child, node.PointIndex))
            .ToList();
        var afterSelectionNodes = newSelectedNodes.ToList();
        editCommand.SetSelectionRestoreActions(
            () => RestoreSelectedPathNodes(beforeSelectionNodes),
            () => RestoreSelectedPathNodes(afterSelectionNodes));
        editCommand.CaptureAfterState();
        (_context.ActiveCanvas as DrawingCanvas)?.CommandManager.PushExecutedCommand(editCommand);
        SetSelectedPathNodes(newSelectedNodes);

        InvalidateAfterPathStructureChanged(combo);
        _context.PublishTransformChange();
        _context.ReportStatus($"已延伸 {newSelectedNodes.Count} 个节点");
        return true;
    }

    private bool CanSelectPathNodeByClick()
    {
        bool isPathNodeSelectMode = IsSelectNodesMode;
        if (isPathNodeSelectMode)
        {
            return true;
        }

        bool isDefaultNodeMode = NodeEditSubMode == NodeEditSubMode.None;
        bool isShiftPressed = _context.IsShiftPressed();
        bool canUseShiftToAccumulateSelection = isDefaultNodeMode && isShiftPressed;
        return canUseShiftToAccumulateSelection;
    }

    public bool ConnectSelectedPathNodes()
    {
        if (!CanConnectSelectedPathNodes())
        {
            return false;
        }

        TryGetEditableCombination(out var combo);
        var selectedNodeSnapshots = CaptureSelectedPathNodeSnapshots();
        var firstSelectedNode = selectedNodeSnapshots[0];
        var secondSelectedNode = selectedNodeSnapshots[1];

        var editCommand = new CommandEdit(new[] { combo }, "连接节点");
        bool connected = combo.TryConnectPathNodes(
            firstSelectedNode.Child,
            firstSelectedNode.PointIndex,
            firstSelectedNode.WorldPos,
            secondSelectedNode.Child,
            secondSelectedNode.PointIndex,
            secondSelectedNode.WorldPos);
        if (!connected)
        {
            return false;
        }

        editCommand.CaptureAfterState();
        (_context.ActiveCanvas as DrawingCanvas)?.CommandManager.PushExecutedCommand(editCommand);
        ClearSelectedPathNodes();
        InvalidateAfterPathStructureChanged(combo);
        _context.PublishTransformChange();
        _context.ReportStatus("已连接节点");
        return true;
    }

    public void SetMoveNodeSelection(DrawCombination combo, DrawObject child, int pointIndex)
    {
        if (combo == null || child == null || child.Points == null
            || pointIndex < 0 || pointIndex >= child.Points.Count)
        {
            ClearSelectedMoveNode();
            return;
        }

        _moveNodeCombo = combo;
        _moveNodeChildShape = child;
        _moveNodeChildPointIndex = pointIndex;

        var localPositions = combo.GetPathNodeLocalPositions();
        SKPoint nodeWorldPos = SKPoint.Empty;
        foreach (var (worldPos, lpChild, lpIdx) in localPositions)
        {
            if (lpChild == child && lpIdx == pointIndex)
            {
                // localPositions 已经是世界坐标
                nodeWorldPos = worldPos;
                break;
            }
        }

        if (nodeWorldPos.IsEmpty)
        {
            nodeWorldPos = combo.GetTransformMatrix().MapPoint(new SKPoint(
                child.Points[pointIndex].X - child.SharpCenter.X,
                child.Points[pointIndex].Y - child.SharpCenter.Y));
        }

        // 默认红点选中与一次性动作的合法性判断必须共享同一批节点快照，
        // 否则会出现“红点已选中，但延伸/连接仍是灰的”的状态分叉。
        _selectedPathNodes.Clear();
        _selectedPathNodes.Add((combo, child, pointIndex));
        _context.SelectedMoveNodeWorldPosition = nodeWorldPos;
        _context.SelectedPathNodeWorldPositions = new List<SKPoint> { nodeWorldPos };
        PublishNodeEditStateChanged();
        _context.PublishSelectChanged();
        _context.ReportStatus($"选中节点 ({nodeWorldPos.X:F2}, {nodeWorldPos.Y:F2}) [{child.GetType().Name}][{pointIndex}]");
    }

    public void ClearSelectedMoveNode()
    {
        _moveNodeCombo = null;
        _moveNodeChildShape = null;
        _moveNodeChildPointIndex = -1;
        _context.SelectedMoveNodeWorldPosition = null;
        PublishNodeEditStateChanged();
    }

    public void ClearSelectedPathNodes()
    {
        _selectedPathNodes.Clear();
        _context.SelectedPathNodeWorldPositions = new List<SKPoint>();
        PublishNodeEditStateChanged();
        _context.PublishSelectChanged();
    }

    public (DrawCombination combo, DrawObject child, int pointIndex, SKPoint currentWorldPos)? GetSelectedMoveNodeInfo()
    {
        if (_moveNodeCombo == null || _moveNodeChildShape == null || _moveNodeChildPointIndex < 0)
            return null;

        if (_moveNodeChildShape.Points == null || _moveNodeChildPointIndex >= _moveNodeChildShape.Points.Count)
            return null;

        var localPositions = _moveNodeCombo.GetPathNodeLocalPositions();
        SKPoint nodeWorldPos = SKPoint.Empty;
        foreach (var (worldPos, lpChild, lpIdx) in localPositions)
        {
            if (lpChild == _moveNodeChildShape && lpIdx == _moveNodeChildPointIndex)
            {
                // localPositions 已经是世界坐标
                nodeWorldPos = worldPos;
                break;
            }
        }

        if (nodeWorldPos.IsEmpty)
        {
            var pathLocal = new SKPoint(
                _moveNodeChildShape.Points[_moveNodeChildPointIndex].X - _moveNodeChildShape.SharpCenter.X,
                _moveNodeChildShape.Points[_moveNodeChildPointIndex].Y - _moveNodeChildShape.SharpCenter.Y);
            nodeWorldPos = _moveNodeChildShape.GetTransformMatrix().MapPoint(pathLocal);
        }

        return (_moveNodeCombo, _moveNodeChildShape, _moveNodeChildPointIndex, nodeWorldPos);
    }

    public bool HasSelectedMoveNode()
    {
        return _moveNodeCombo != null && _moveNodeChildShape != null && _moveNodeChildPointIndex >= 0;
    }

    public bool HasSelectedPathNodes()
    {
        return _selectedPathNodes.Count > 0;
    }

    public int SelectedPathNodeCount => _selectedPathNodes.Count;

    public void RefreshSelectionVisualState()
    {
        RefreshSelectedMoveNodeVisualState();
        RefreshSelectedPathNodesVisualState();
        PublishNodeEditStateChanged();
        _context.PublishSelectChanged();
    }

    public bool CanExtendSelectedPathNodes()
    {
        if (!TryGetEditableCombination(out var combo))
        {
            return false;
        }

        if (_selectedPathNodes.Count == 0)
        {
            return false;
        }

        var selectedNodeSnapshots = CaptureSelectedPathNodeSnapshots();
        if (selectedNodeSnapshots.Count == 0)
        {
            return false;
        }

        bool canExtend = CanExtendSelectedPathNodes(combo, selectedNodeSnapshots);
        return canExtend;
    }

    public bool CanConnectSelectedPathNodes()
    {
        if (!TryGetEditableCombination(out var combo))
        {
            return false;
        }

        if (_selectedPathNodes.Count != 2)
        {
            return false;
        }

        var selectedNodeSnapshots = CaptureSelectedPathNodeSnapshots();
        if (selectedNodeSnapshots.Count != 2)
        {
            return false;
        }

        var firstSelectedNode = selectedNodeSnapshots[0];
        var secondSelectedNode = selectedNodeSnapshots[1];

        bool canConnect = combo.CanConnectPathNodes(
            firstSelectedNode.Child,
            firstSelectedNode.PointIndex,
            firstSelectedNode.WorldPos,
            secondSelectedNode.Child,
            secondSelectedNode.PointIndex,
            secondSelectedNode.WorldPos);
        return canConnect;
    }

    private void PublishNodeEditStateChanged()
    {
        bool canExtendSelectedPathNodes = CanExtendSelectedPathNodes();
        bool canConnectSelectedPathNodes = CanConnectSelectedPathNodes();
        EventBus.Instance.Publish(new EditNodesModeChangedEvent
        {
            IsEditing = _context.IsNodeEditing,
            SubMode = _context.IsNodeEditing ? _context.NodeEditSubMode : NodeEditSubMode.None,
            HasSelectedMoveNode = HasSelectedMoveNode(),
            CanExtendSelectedPathNodes = canExtendSelectedPathNodes,
            CanConnectSelectedPathNodes = canConnectSelectedPathNodes
        });
    }

    public int GetPathNodeAt(DrawObject drawObject, SKPoint mouseWorldPoint)
    {
        if (drawObject is DrawCombination combo)
        {
            float tolerance = (float)(_controlPointSize * 2) / (_context.ActiveCanvas?.Viewport.Scale ?? 1.0f);
            float toleranceSq = tolerance * tolerance;
            var worldPositions = combo.GetPathNodeWorldPositions();
            for (int i = 0; i < worldPositions.Count; i++)
            {
                float dx = worldPositions[i].X - mouseWorldPoint.X;
                float dy = worldPositions[i].Y - mouseWorldPoint.Y;
                if (dx * dx + dy * dy <= toleranceSq)
                    return i;
            }
            return -1;
        }

        if (drawObject.PathNodes == null || drawObject.PathNodes.Count == 0)
            return -1;

        float tol = (float)(_controlPointSize * 2) / (_context.ActiveCanvas?.Viewport.Scale ?? 1.0f);
        float tolSq = tol * tol;
        var transform = drawObject.GetTransformMatrix();
        for (int i = 0; i < drawObject.PathNodes.Count; i++)
        {
            var worldPos = transform.MapPoint(drawObject.PathNodes[i]);
            float dx = worldPos.X - mouseWorldPoint.X;
            float dy = worldPos.Y - mouseWorldPoint.Y;
            if (dx * dx + dy * dy <= tolSq)
                return i;
        }

        return -1;
    }

    private (DrawObject? child, int pointIndex) GetPathNodeChildAt(DrawCombination combo, SKPoint mouseWorldPoint)
    {
        float tolerance = (float)(_controlPointSize * 2) / (_context.ActiveCanvas?.Viewport.Scale ?? 1.0f);
        float toleranceSq = tolerance * tolerance;

        var nodePositions = combo.GetPathNodeLocalPositions();

        float bestDistSq = float.MaxValue;
        DrawObject? bestChild = null;
        int bestIdx = -1;

        foreach (var (worldPos, child, childPointIndex) in nodePositions)
        {
            // nodePositions 已经是世界坐标，直接比较
            float dx = worldPos.X - mouseWorldPoint.X;
            float dy = worldPos.Y - mouseWorldPoint.Y;
            float d = dx * dx + dy * dy;
            if (d <= toleranceSq && d < bestDistSq)
            {
                bestDistSq = d;
                bestChild = child;
                bestIdx = childPointIndex;
            }
        }

        return (bestChild, bestIdx);
    }

    private static int FindClosestPointInChildLocal(DrawObject child, SKPoint comboLocalPos, DrawCombination combo)
    {
        if (child?.Points == null || child.Points.Count == 0) return -1;

        var childLocalToWorld = child.GetTransformMatrix();
        var comboWorldToLocal = combo.GetInverseMatrix();
        var childToComboLocal = SKMatrix.Concat(comboWorldToLocal, childLocalToWorld);

        int best = 0;
        float bestDist = float.MaxValue;
        for (int i = 0; i < child.Points.Count; i++)
        {
            var pathLocal = new SKPoint(child.Points[i].X - child.SharpCenter.X, child.Points[i].Y - child.SharpCenter.Y);
            var comboLocal = childToComboLocal.MapPoint(pathLocal);
            float dx = comboLocal.X - comboLocalPos.X;
            float dy = comboLocal.Y - comboLocalPos.Y;
            float d = dx * dx + dy * dy;
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    private void UpdateChildPointByDrag(DrawCombination combo, DrawObject child, int pointIndex, SKPoint newWorld)
    {
        combo.MoveChildPathNodeToWorldPosition(child, pointIndex, newWorld);
    }

    private bool TryGetEditableCombination(out DrawCombination combo)
    {
        combo = null!;

        if (_context.ActiveCanvas?.SelectedShapeCount != 1)
        {
            return false;
        }

        var selectedShape = _context.ActiveCanvas.Selection[0];
        bool isEditableShape = selectedShape is DrawObject drawObject && drawObject.IsPathEditing;
        if (!isEditableShape)
        {
            return false;
        }

        if (selectedShape is not DrawCombination selectedCombo)
        {
            return false;
        }

        combo = selectedCombo;
        return true;
    }

    private void StartNodeBoxSelection(SKPoint point)
    {
        _isNodeBoxSelecting = true;
        _context.BoxSelect.IsActive = true;
        _context.BoxSelect.Start = point;
        _context.BoxSelect.Current = point;
        ClearSelectedPathNodes();
    }

    private SKRect BuildNodeSelectionRect()
    {
        var start = _context.BoxSelect.Start;
        var current = _context.BoxSelect.Current;

        float left = Math.Min(start.X, current.X);
        float top = Math.Min(start.Y, current.Y);
        float right = Math.Max(start.X, current.X);
        float bottom = Math.Max(start.Y, current.Y);

        var selectionRect = SKRect.Create(left, top, right - left, bottom - top);
        return selectionRect;
    }

    private void SetSelectedPathNodes(IEnumerable<(DrawCombination Combo, DrawObject Child, int PointIndex)> nodes)
    {
        _selectedPathNodes.Clear();

        var selectedNodes = nodes.ToList();
        var worldPositions = new List<SKPoint>();
        foreach (var (combo, child, pointIndex) in selectedNodes)
        {
            bool hasWorldPos = TryGetPathNodeWorldPosition(combo, child, pointIndex, out var nodeWorldPos);
            if (!hasWorldPos)
            {
                continue;
            }

            _selectedPathNodes.Add((combo, child, pointIndex));
            worldPositions.Add(nodeWorldPos);
        }

        // 蓝色路径点多选只维护路径点列表，不写入红点坐标；
        // 红点语义由 SetMoveNodeSelection 单独维护。
        _context.SelectedPathNodeWorldPositions = worldPositions;
        PublishNodeEditStateChanged();
        _context.PublishSelectChanged();
    }

    private bool ContainsSelectedPathNode(DrawCombination combo, DrawObject child, int pointIndex)
    {
        bool containsNode = _selectedPathNodes.Any(node =>
            ReferenceEquals(node.Combo, combo)
            && ReferenceEquals(node.Child, child)
            && node.PointIndex == pointIndex);
        return containsNode;
    }

    private void ToggleSelectedPathNode(DrawCombination combo, DrawObject child, int pointIndex)
    {
        bool isAlreadySelected = ContainsSelectedPathNode(combo, child, pointIndex);

        var updatedNodes = new List<(DrawCombination Combo, DrawObject Child, int PointIndex)>(_selectedPathNodes);
        if (isAlreadySelected)
        {
            updatedNodes.RemoveAll(node =>
                ReferenceEquals(node.Combo, combo)
                && ReferenceEquals(node.Child, child)
                && node.PointIndex == pointIndex);
        }
        else
        {
            updatedNodes.Add((combo, child, pointIndex));
        }

        SetSelectedPathNodes(updatedNodes);
    }

    private void InvalidateAfterPathStructureChanged(DrawCombination combo)
    {
        var canvas = _context.ActiveCanvas as DrawingCanvas;
        var affectedShapes = new List<DrawObject> { combo };
        canvas?.InvalidateVisibleCache();
        canvas?.InvalidateGeometryCaches(affectedShapes);
        _context.PublishSelectChanged();
    }

    private bool TryGetPathNodeWorldPosition(
        DrawCombination combo,
        DrawObject child,
        int pointIndex,
        out SKPoint nodeWorldPos)
    {
        nodeWorldPos = SKPoint.Empty;

        var localPositions = combo.GetPathNodeLocalPositions();
        foreach (var (worldPos, localChild, localPointIndex) in localPositions)
        {
            bool isSameNode =
                ReferenceEquals(localChild, child)
                && localPointIndex == pointIndex;
            if (!isSameNode)
            {
                continue;
            }

            // localPositions 已经是世界坐标
            nodeWorldPos = worldPos;
            return true;
        }

        bool hasOriginalPoint =
            child.Points != null
            && pointIndex >= 0
            && pointIndex < child.Points.Count;
        if (!hasOriginalPoint)
        {
            return false;
        }

        var pathLocal = new SKPoint(
            child.Points[pointIndex].X - child.SharpCenter.X,
            child.Points[pointIndex].Y - child.SharpCenter.Y);
        nodeWorldPos = child.GetTransformMatrix().MapPoint(pathLocal);
        return true;
    }

    private List<(DrawCombination Combo, DrawObject Child, int PointIndex, SKPoint WorldPos)>
        CaptureSelectedPathNodeSnapshots()
    {
        var snapshots = new List<(DrawCombination Combo, DrawObject Child, int PointIndex, SKPoint WorldPos)>();
        foreach (var (combo, child, pointIndex) in _selectedPathNodes)
        {
            bool hasWorldPos = TryGetNodeWorldPosition(child, pointIndex, out var worldPos);
            if (!hasWorldPos)
            {
                continue;
            }

            snapshots.Add((combo, child, pointIndex, worldPos));
        }

        return snapshots;
    }

    private bool CanExtendSelectedPathNodes(
        DrawCombination combo,
        List<(DrawCombination Combo, DrawObject Child, int PointIndex, SKPoint WorldPos)> selectedNodeSnapshots)
    {
        bool hasEnoughSelectedNodes = selectedNodeSnapshots.Count >= 2;
        if (!hasEnoughSelectedNodes)
        {
            return false;
        }

        bool allNodesBelongToSameChild = selectedNodeSnapshots.All(node =>
            ReferenceEquals(node.Child, selectedNodeSnapshots[0].Child));
        if (!allNodesBelongToSameChild)
        {
            return false;
        }

        var orderedIndices = selectedNodeSnapshots
            .Select(node => node.PointIndex)
            .OrderBy(index => index)
            .ToList();

        // 多点延伸只允许同一路径上的连续点段，
        // 避免一次动作跨断点或跨子图形破坏已有拓扑关系。
        for (int i = 1; i < orderedIndices.Count; i++)
        {
            bool isConsecutive = orderedIndices[i] == orderedIndices[i - 1] + 1;
            if (!isConsecutive)
            {
                return false;
            }
        }

        return true;
    }

    private bool TryExtendSelectedPathNodes(
        DrawCombination combo,
        DrawObject child,
        int startPointIndex,
        int endPointIndex,
        SKPoint delta,
        List<(DrawCombination Combo, DrawObject Child, int PointIndex)> newSelectedNodes)
    {
        bool extended = combo.TryExtendContinuousPathNodes(
            child,
            startPointIndex,
            endPointIndex,
            delta,
            out var extendedChild,
            out var movedPointIndices);
        if (!extended)
        {
            return false;
        }

        foreach (var movedPointIndex in movedPointIndices)
        {
            newSelectedNodes.Add((combo, extendedChild, movedPointIndex));
        }

        return newSelectedNodes.Count > 0;
    }

    private bool TryGetNodeWorldPosition(DrawObject child, int pointIndex, out SKPoint worldPos)
    {
        worldPos = SKPoint.Empty;

        bool hasPoint =
            child.Points != null
            && pointIndex >= 0
            && pointIndex < child.Points.Count;
        if (!hasPoint)
        {
            return false;
        }

        var pathLocal = new SKPoint(
            child.Points[pointIndex].X - child.SharpCenter.X,
            child.Points[pointIndex].Y - child.SharpCenter.Y);
        worldPos = child.GetTransformMatrix().MapPoint(pathLocal);
        return true;
    }

    private void RefreshSelectedMoveNodeVisualState()
    {
        bool hasMoveNode =
            _moveNodeCombo != null
            && _moveNodeChildShape != null
            && _moveNodeChildPointIndex >= 0;
        if (!hasMoveNode)
        {
            _context.SelectedMoveNodeWorldPosition = null;
            return;
        }

        bool hasWorldPos = TryGetPathNodeWorldPosition(
            _moveNodeCombo!,
            _moveNodeChildShape!,
            _moveNodeChildPointIndex,
            out var moveNodeWorldPos);
        if (hasWorldPos)
        {
            _context.SelectedMoveNodeWorldPosition = moveNodeWorldPos;
            return;
        }

        _moveNodeCombo = null;
        _moveNodeChildShape = null;
        _moveNodeChildPointIndex = -1;
        _context.SelectedMoveNodeWorldPosition = null;
    }

    private void RefreshSelectedPathNodesVisualState()
    {
        if (_selectedPathNodes.Count == 0)
        {
            _context.SelectedPathNodeWorldPositions = new List<SKPoint>();
            return;
        }

        var refreshedNodes = new List<(DrawCombination Combo, DrawObject Child, int PointIndex)>();
        var refreshedWorldPositions = new List<SKPoint>();

        foreach (var (combo, child, pointIndex) in _selectedPathNodes)
        {
            bool hasWorldPos = TryGetPathNodeWorldPosition(
                combo,
                child,
                pointIndex,
                out var nodeWorldPos);
            if (!hasWorldPos)
            {
                continue;
            }

            refreshedNodes.Add((combo, child, pointIndex));
            refreshedWorldPositions.Add(nodeWorldPos);
        }

        _selectedPathNodes.Clear();
        _selectedPathNodes.AddRange(refreshedNodes);
        _context.SelectedPathNodeWorldPositions = refreshedWorldPositions;
    }

    private void RestoreSelectedPathNodes(
        List<(DrawCombination Combo, DrawObject Child, int PointIndex)> selectedNodes)
    {
        var restoredNodes = new List<(DrawCombination Combo, DrawObject Child, int PointIndex)>();
        foreach (var (combo, child, pointIndex) in selectedNodes)
        {
            bool childStillExists = combo.Children.OfType<DrawObject>().Any(existingChild =>
                ReferenceEquals(existingChild, child));
            if (!childStillExists)
            {
                continue;
            }

            bool pointIndexIsValid =
                child.Points != null
                && pointIndex >= 0
                && pointIndex < child.Points.Count;
            if (!pointIndexIsValid)
            {
                continue;
            }

            restoredNodes.Add((combo, child, pointIndex));
        }

        SetSelectedPathNodes(restoredNodes);
    }

    private void SetSeparateNodeSelection(DrawCombination combo, DrawObject child, int pointIndex)
    {
        if (combo == null || child == null || child.Points == null
            || pointIndex < 0 || pointIndex >= child.Points.Count)
        {
            _context.SelectedSeparateNodeWorldPosition = null;
            return;
        }

        var pathLocal = new SKPoint(
            child.Points[pointIndex].X - child.SharpCenter.X,
            child.Points[pointIndex].Y - child.SharpCenter.Y);
        var nodeWorldPos = child.GetTransformMatrix().MapPoint(pathLocal);

        _context.SelectedSeparateNodeWorldPosition = nodeWorldPos;
    }

    /// <summary>
    /// 在路径上查找距 queryPt 最近的点。坐标系无关：path 和 queryPt 必须在同一坐标系
    /// （通常为世界坐标系），tol 也必须是该坐标系的距离量。返回最近点及所在段索引。
    /// </summary>
    private bool FindNearestPointOnPath(SKPath path, SKPoint queryPt, float tol, out SKPoint bestPoint, out int insertIndex)
    {
        bestPoint = SKPoint.Empty;
        insertIndex = -1;

        // 曲线段采样密度：放大后路径段很长，需要足够密度覆盖容差范围。
        // 按「采样间隔 < 容差」的原则动态确定采样数，确保任意曲线上
        // 的点到某个采样点的距离不超过容差的一半。
        // 直线段使用线段投影算法，无需采样。
        float minDistSq = tol * tol;
        bool found = false;
        int segIndex = 0;

        using var iter = path.CreateRawIterator();
        var pts = new SKPoint[4];
        SKPathVerb verb;
        SKPoint currentPt = SKPoint.Empty;
        SKPoint movePt = SKPoint.Empty;

        while ((verb = iter.Next(pts)) != SKPathVerb.Done)
        {
            switch (verb)
            {
                case SKPathVerb.Move:
                    currentPt = pts[0];
                    movePt = pts[0];
                    break;
                case SKPathVerb.Line:
                    {
                        // 直线段：用线段投影计算精确最近点，不再依赖采样。
                        // 解决放大后长线段采样间隔远超容差导致大面积无法匹配的问题。
                        var p0 = currentPt;
                        var p1 = pts[1];
                        float dSq = DistanceToSegmentSquaredWithProjection(queryPt, p0, p1, out var projPt);
                        if (dSq < minDistSq)
                        {
                            minDistSq = dSq;
                            bestPoint = projPt;
                            insertIndex = segIndex + 1;
                            found = true;
                        }
                        currentPt = p1;
                        segIndex++;
                        break;
                    }
                case SKPathVerb.Cubic:
                    {
                        var p0 = currentPt;
                        var p1 = pts[1]; var p2 = pts[2]; var p3 = pts[3];
                        int samples = ComputeCurveSamples(p0, p1, p2, p3, tol);
                        float bestSegT = 0f;
                        SKPoint bestSegPt = p0;
                        float bestSegDSq = float.MaxValue;
                        for (int s = 0; s <= samples; s++)
                        {
                            float t = s / (float)samples;
                            float mt = 1 - t;
                            var sample = new SKPoint(
                                mt * mt * mt * p0.X + 3 * mt * mt * t * p1.X + 3 * mt * t * t * p2.X + t * t * t * p3.X,
                                mt * mt * mt * p0.Y + 3 * mt * mt * t * p1.Y + 3 * mt * t * t * p2.Y + t * t * t * p3.Y);
                            float dSq = DistSq(queryPt, sample);
                            if (dSq < bestSegDSq)
                            {
                                bestSegDSq = dSq;
                                bestSegT = t;
                                bestSegPt = sample;
                            }
                        }
                        // 牛顿迭代精化：最小化 |C(t)-P|²，精度达浮点极限
                        {
                            float t = bestSegT;
                            for (int ni = 0; ni < 20; ni++)
                            {
                                float u = 1 - t;
                                float cx = u * u * u * p0.X + 3 * u * u * t * p1.X + 3 * u * t * t * p2.X + t * t * t * p3.X;
                                float cy = u * u * u * p0.Y + 3 * u * u * t * p1.Y + 3 * u * t * t * p2.Y + t * t * t * p3.Y;
                                float d1x = 3 * ((-3 * u * u) * p0.X + (3 * u * u - 6 * u * t) * p1.X + (6 * u * t - 3 * t * t) * p2.X + (3 * t * t) * p3.X);
                                float d1y = 3 * ((-3 * u * u) * p0.Y + (3 * u * u - 6 * u * t) * p1.Y + (6 * u * t - 3 * t * t) * p2.Y + (3 * t * t) * p3.Y);
                                float d2x = 6 * ((p0.X - 2 * p1.X + p2.X) * u + (-p1.X + 2 * p2.X - p3.X) * t);
                                float d2y = 6 * ((p0.Y - 2 * p1.Y + p2.Y) * u + (-p1.Y + 2 * p2.Y - p3.Y) * t);
                                float ex = cx - queryPt.X, ey = cy - queryPt.Y;
                                float fDer = d1x * d1x + d1y * d1y + ex * d2x + ey * d2y;
                                if (Math.Abs(fDer) < 1e-12f) break;
                                float step = (ex * d1x + ey * d1y) / fDer;
                                t -= step;
                                t = Math.Clamp(t, 0f, 1f);
                                if (Math.Abs(step) < 1e-7f) break;
                            }
                            float u2 = 1 - t;
                            var refined = new SKPoint(
                                u2 * u2 * u2 * p0.X + 3 * u2 * u2 * t * p1.X + 3 * u2 * t * t * p2.X + t * t * t * p3.X,
                                u2 * u2 * u2 * p0.Y + 3 * u2 * u2 * t * p1.Y + 3 * u2 * t * t * p2.Y + t * t * t * p3.Y);
                            float refinedDSq = DistSq(queryPt, refined);
                            if (refinedDSq < bestSegDSq) { bestSegDSq = refinedDSq; bestSegPt = refined; }
                        }
                        if (bestSegDSq < minDistSq)
                        {
                            minDistSq = bestSegDSq;
                            bestPoint = bestSegPt;
                            insertIndex = segIndex + 1;
                            found = true;
                        }
                        currentPt = p3;
                        segIndex++;
                        break;
                    }
                case SKPathVerb.Quad:
                    {
                        var p0 = currentPt;
                        var p1 = pts[1]; var p2 = pts[2];
                        int samples = ComputeCurveSamples(p0, p1, p2, tol);
                        float bestSegT = 0f;
                        SKPoint bestSegPt = p0;
                        float bestSegDSq = float.MaxValue;
                        for (int s = 0; s <= samples; s++)
                        {
                            float t = s / (float)samples;
                            float mt = 1 - t;
                            var sample = new SKPoint(
                                mt * mt * p0.X + 2 * mt * t * p1.X + t * t * p2.X,
                                mt * mt * p0.Y + 2 * mt * t * p1.Y + t * t * p2.Y);
                            float dSq = DistSq(queryPt, sample);
                            if (dSq < bestSegDSq)
                            {
                                bestSegDSq = dSq;
                                bestSegT = t;
                                bestSegPt = sample;
                            }
                        }
                        // 牛顿迭代精化（二次贝塞尔）：最小化 |Q(t)-P|²
                        // Q'(t)  = 2*[(1-t)*(p1-p0) + t*(p2-p1)]
                        // Q''(t) = 2*(p2 - 2*p1 + p0)
                        {
                            float t = bestSegT;
                            for (int ni = 0; ni < 20; ni++)
                            {
                                float u = 1 - t;
                                float cx = u * u * p0.X + 2 * u * t * p1.X + t * t * p2.X;
                                float cy = u * u * p0.Y + 2 * u * t * p1.Y + t * t * p2.Y;
                                float d1x = 2 * (u * (p1.X - p0.X) + t * (p2.X - p1.X));
                                float d1y = 2 * (u * (p1.Y - p0.Y) + t * (p2.Y - p1.Y));
                                float d2x = 2 * (p2.X - 2 * p1.X + p0.X);
                                float d2y = 2 * (p2.Y - 2 * p1.Y + p0.Y);
                                float ex = cx - queryPt.X, ey = cy - queryPt.Y;
                                float fDer = d1x * d1x + d1y * d1y + ex * d2x + ey * d2y;
                                if (Math.Abs(fDer) < 1e-12f) break;
                                float step = (ex * d1x + ey * d1y) / fDer;
                                t -= step;
                                t = Math.Clamp(t, 0f, 1f);
                                if (Math.Abs(step) < 1e-7f) break;
                            }
                            float u2 = 1 - t;
                            var refined = new SKPoint(
                                u2 * u2 * p0.X + 2 * u2 * t * p1.X + t * t * p2.X,
                                u2 * u2 * p0.Y + 2 * u2 * t * p1.Y + t * t * p2.Y);
                            float refinedDSq = DistSq(queryPt, refined);
                            if (refinedDSq < bestSegDSq) { bestSegDSq = refinedDSq; bestSegPt = refined; }
                        }
                        if (bestSegDSq < minDistSq)
                        {
                            minDistSq = bestSegDSq;
                            bestPoint = bestSegPt;
                            insertIndex = segIndex + 1;
                            found = true;
                        }
                        currentPt = p2;
                        segIndex++;
                        break;
                    }
                case SKPathVerb.Conic:
                    {
                        var p0 = currentPt;
                        var p1 = pts[1]; var p2 = pts[2];
                        float w = iter.ConicWeight();
                        int samples = ComputeCurveSamples(p0, p1, p2, tol);
                        float bestSegT = 0f;
                        SKPoint bestSegPt = p0;
                        float bestSegDSq = float.MaxValue;
                        for (int s = 0; s <= samples; s++)
                        {
                            float t = s / (float)samples;
                            float mt = 1 - t;
                            float denom = mt * mt + 2 * mt * t * w + t * t;
                            if (denom < 1e-10f) continue;
                            float invDenom = 1f / denom;
                            var sample = new SKPoint(
                                (mt * mt * p0.X + 2 * mt * t * w * p1.X + t * t * p2.X) * invDenom,
                                (mt * mt * p0.Y + 2 * mt * t * w * p1.Y + t * t * p2.Y) * invDenom);
                            float dSq = DistSq(queryPt, sample);
                            if (dSq < bestSegDSq)
                            {
                                bestSegDSq = dSq;
                                bestSegT = t;
                                bestSegPt = sample;
                            }
                        }
                        // 牛顿迭代精化（有理二次贝塞尔/Conic）：最小化 |R(t)-P|²
                        // R(t) = N(t)/D(t)，其中 N(t) = (1-t)²p0 + 2(1-t)t·w·p1 + t²p2，D(t) = (1-t)² + 2(1-t)t·w + t²
                        // R'(t) = (N'(t)·D(t) - N(t)·D'(t)) / D(t)²
                        {
                            float t = bestSegT;
                            for (int iter2 = 0; iter2 < 20; iter2++)
                            {
                                float u = 1 - t;
                                float D = u * u + 2 * u * t * w + t * t;
                                if (D < 1e-12f) break;
                                float invD = 1f / D;
                                float Nx = u * u * p0.X + 2 * u * t * w * p1.X + t * t * p2.X;
                                float Ny = u * u * p0.Y + 2 * u * t * w * p1.Y + t * t * p2.Y;
                                float cx = Nx * invD, cy = Ny * invD;
                                // D'(t) = 2*(-u + t + w*(u-t)) = 2*((1-w)*(2t-1) + ... )，直接展开：
                                float Dprime = 2 * (-(u) + t + w * (u - t));  // = 2(t - u + w(u-t))
                                                                              // N'x = 2*(-u*p0.X + (u-t)*w*p1.X + t*p2.X)
                                float NpX = 2 * (-u * p0.X + (u - t) * w * p1.X + t * p2.X);
                                float NpY = 2 * (-u * p0.Y + (u - t) * w * p1.Y + t * p2.Y);
                                float d1x = (NpX - cx * Dprime) * invD;
                                float d1y = (NpY - cy * Dprime) * invD;
                                float ex = cx - queryPt.X, ey = cy - queryPt.Y;
                                // 近似 f'(t) ≈ |R'(t)|² (忽略 R''(t) 项，对收敛影响极小)
                                float fDer = d1x * d1x + d1y * d1y;
                                if (fDer < 1e-12f) break;
                                float step = (ex * d1x + ey * d1y) / fDer;
                                t -= step;
                                t = Math.Clamp(t, 0f, 1f);
                                if (Math.Abs(step) < 1e-7f) break;
                            }
                            {
                                float u = 1 - t;
                                float D = u * u + 2 * u * t * w + t * t;
                                if (D > 1e-12f)
                                {
                                    var refined = new SKPoint(
                                        (u * u * p0.X + 2 * u * t * w * p1.X + t * t * p2.X) / D,
                                        (u * u * p0.Y + 2 * u * t * w * p1.Y + t * t * p2.Y) / D);
                                    float refinedDSq = DistSq(queryPt, refined);
                                    if (refinedDSq < bestSegDSq) { bestSegDSq = refinedDSq; bestSegPt = refined; }
                                }
                            }
                        }
                        if (bestSegDSq < minDistSq)
                        {
                            minDistSq = bestSegDSq;
                            bestPoint = bestSegPt;
                            insertIndex = segIndex + 1;
                            found = true;
                        }
                        currentPt = p2;
                        segIndex++;
                        break;
                    }
                case SKPathVerb.Close:
                    {
                        // Close 也是直线段，用线段投影代替采样
                        var p0 = currentPt;
                        var p1 = movePt;
                        float closeSq = DistSq(p0, p1);
                        if (closeSq > 1e-6f)
                        {
                            float dSq = DistanceToSegmentSquaredWithProjection(queryPt, p0, p1, out var projPt);
                            if (dSq < minDistSq)
                            {
                                minDistSq = dSq;
                                bestPoint = projPt;
                                insertIndex = segIndex + 1;
                                found = true;
                            }
                            segIndex++;
                        }
                        currentPt = movePt;
                        break;
                    }
            }
        }

        return found;
    }

    /// <summary>
    /// 计算点 pt 到线段 (p0→p1) 的最短距离²，同时返回投影点。
    /// 投影点 = p0 + clamp(t, 0, 1) * (p1 - p0)，其中 t = dot(pt-p0, p1-p0) / |p1-p0|²
    /// </summary>
    private static float DistanceToSegmentSquaredWithProjection(SKPoint pt, SKPoint p0, SKPoint p1, out SKPoint projection)
    {
        float dx = p1.X - p0.X, dy = p1.Y - p0.Y;
        float lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-10f)
        {
            // 线段长度为零，距离即为到端点的距离
            projection = p0;
            return DistSq(pt, p0);
        }
        float t = ((pt.X - p0.X) * dx + (pt.Y - p0.Y) * dy) / lenSq;
        if (t < 0f) t = 0f;
        else if (t > 1f) t = 1f;
        projection = new SKPoint(p0.X + t * dx, p0.Y + t * dy);
        return DistSq(pt, projection);
    }

    /// <summary>
    /// 根据曲线段的估算弧长与容差，动态计算采样数。
    /// 保证采样间隔不超过容差，确保放大后仍能命中曲线上任意点。
    /// </summary>
    private static int ComputeCurveSamples(SKPoint p0, SKPoint p1, SKPoint p2, SKPoint p3, float tol)
    {
        // 用弦长 + 偏移量估算弧长
        float chordLen = (float)Math.Sqrt(DistSq(p0, p3));
        float ctrlOffset = (float)Math.Sqrt(DistSq(p0, p1)) + (float)Math.Sqrt(DistSq(p1, p2)) + (float)Math.Sqrt(DistSq(p2, p3));
        float estimatedArcLen = Math.Max(chordLen, ctrlOffset * 0.5f);
        int samples = Math.Max(32, (int)(estimatedArcLen / tol) + 1);
        return Math.Min(samples, 256); // 上限防止性能问题
    }

    private static int ComputeCurveSamples(SKPoint p0, SKPoint p1, SKPoint p2, float tol)
    {
        float chordLen = (float)Math.Sqrt(DistSq(p0, p2));
        float ctrlOffset = (float)Math.Sqrt(DistSq(p0, p1)) + (float)Math.Sqrt(DistSq(p1, p2));
        float estimatedArcLen = Math.Max(chordLen, ctrlOffset * 0.5f);
        int samples = Math.Max(32, (int)(estimatedArcLen / tol) + 1);
        return Math.Min(samples, 256);
    }

    private static float DistSq(SKPoint a, SKPoint b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }
}
