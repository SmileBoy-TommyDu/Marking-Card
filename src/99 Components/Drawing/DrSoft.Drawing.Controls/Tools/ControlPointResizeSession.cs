using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Documents;
using System.Windows.Input;
using DrSoft.Drawing.Controls.Commands;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using SkiaSharp;
using Cursor = System.Windows.Input.Cursor;
using Cursors = System.Windows.Input.Cursors;

namespace DrSoft.Drawing.Controls.Tools;

/// <summary>
/// 管理控制点缩放/拉伸的短生命周期会话。
/// 负责预览尺寸更新、锚点保持、最终提交与命令快照。
/// </summary>
internal sealed class ControlPointResizeSession : IToolSelectSession
{
    private readonly DocumentContext _context;
    private readonly SelectionControlPointService _selectionControlPointService;

    private ControlPointType _draggingControlPoint = ControlPointType.None;
    private SKPoint _dragStartPoint = SKPoint.Empty;
    private SKRect _originalMergedBounds = SKRect.Empty;
    private IDeferredCommand? _pendingTransformCommand;
    private bool _hasResizableSelection;
    private bool _hasMultipleSelectionPreview;
    private BatchTransformDelta _multipleSelectionDelta;
    private SKRect _multipleSelectionPreviewBounds = SKRect.Empty;

    private MultipleSelectionResizeSnapshot? _multipleSelectionResizeSnapshot;
    private SingleSelectionPreviewSnapshot? _singleSelectionPreviewSnapshot;
    private Cursor? _suggestedCursor;
    private ControlPointType? _completedControlPoint;
    // 点与锚点重合判定的世界坐标容差：距离小于此值视为重合，该轴保持不动。
    private const float AnchorEpsilon = 0.0001f;
    float scaleX = 1f, scaleY = 1f;
    SKPoint anchor = SKPoint.Empty;
    // 单选缩放的方向角（世界坐标，弧度）：取快照 OBB 的 dirR 方向，预览与提交共用，保证一致。
    float scaleDirectionRad = 0f;

    public ControlPointResizeSession(DocumentContext context)
    {
        _context = context;
        _selectionControlPointService = new SelectionControlPointService(context);
    }

    public string Name => "ControlPointResize";

    public bool IsActive => IsDragging;

    public Cursor? SuggestedCursor => _suggestedCursor;

    public ControlPointType? CompletedControlPoint
    {
        get
        {
            ControlPointType? completedControlPoint = _completedControlPoint;
            _completedControlPoint = null;
            return completedControlPoint;
        }
    }

    public bool IsDragging => _draggingControlPoint != ControlPointType.None;

    public ControlPointType DraggingControlPoint => _draggingControlPoint;

    public bool TryMouseDown(SKPoint point, out string message)
    {
        if (_context.ActiveCanvas == null)
        {
            message = "没有活动画布";
            return false;
        }

        bool isSupportedSelectState = _context.SelectState == SelectState.None
            || _context.SelectState == SelectState.FirstSelected;
        if (!isSupportedSelectState)
        {
            message = "当前不是红框缩放态";
            return false;
        }

        int selectedShapeCount = _context.ActiveCanvas.SelectedShapeCount;
        if (selectedShapeCount == 0)
        {
            message = "没有选中图形";
            return false;
        }

        bool allLocked = _context.ActiveCanvas.Selection.All(shape => shape.IsLocked);
        if (allLocked)
        {
            message = "选中图形全部已锁定";
            return false;
        }

        ControlPointType controlPointType = ResolveControlPointAt(point);
        bool hasControlPoint = controlPointType != ControlPointType.None;
        if (!hasControlPoint)
        {
            message = "未命中红框控制点";
            return false;
        }

        SKRect originalMergedBounds = _context.CalculateMergedBounds();
        Start(controlPointType, point, originalMergedBounds);
        _suggestedCursor = ResolveResizeCursor(controlPointType);
        message = "开始红框控制点拖拽";
        return true;
    }

    public bool TryMouseMove(SKPoint point, out string message)
    {
        if (IsDragging)
        {
            Update(point);
            message = "更新红框缩放预览";
            return true;
        }

        if (_context.ActiveCanvas == null)
        {
            message = "没有活动画布";
            return false;
        }

        bool isSupportedSelectState = _context.SelectState == SelectState.None
            || _context.SelectState == SelectState.FirstSelected;
        if (!isSupportedSelectState)
        {
            message = "当前不是红框缩放态";
            return false;
        }

        ControlPointType controlPointType = ResolveControlPointAt(point);
        bool hasControlPoint = controlPointType != ControlPointType.None;
        if (!hasControlPoint)
        {
            message = "未命中红框控制点";
            return false;
        }

        _suggestedCursor = ResolveResizeCursor(controlPointType);
        message = "命中红框控制点";
        return true;
    }

    public bool TryMouseUp(SKPoint point, out string message)
    {
        if (!IsDragging)
        {
            message = "红框控制点未处于拖拽中";
            return false;
        }

        ControlPointType draggingControlPoint = DraggingControlPoint;
        bool completed = Complete();
        if (!completed)
        {
            message = "红框控制点提交失败";
            return false;
        }

        _completedControlPoint = draggingControlPoint;
        _suggestedCursor = null;
        message = "完成红框控制点拖拽";
        return true;
    }

    public bool TryRightMouseDown(SKPoint point, out string message)
    {
        message = "红框控制点会话不处理右键";
        return false;
    }

    /// <summary>
    /// 开始一次控制点拖拽，并为最终缩放命令准备 before-state。
    /// </summary>
    public void Start(ControlPointType controlPointType, SKPoint point, SKRect originalMergedBounds)
    {
        if (_context.ActiveCanvas == null)
            return;

        _completedControlPoint = null;
        _draggingControlPoint = controlPointType;
        _dragStartPoint = point;
        _originalMergedBounds = originalMergedBounds;
        _context.IsDragControlPoint = true;
        _singleSelectionPreviewSnapshot = null;
        _multipleSelectionResizeSnapshot = null;
        scaleX = 1.0f;
        scaleY = 1.0f;
        anchor = SKPoint.Empty;
        scaleDirectionRad = 0f;


        // TargetShapes>0 的 Hatch 由目标图形驱动重建，不参与控制点缩放命令。
        // TargetShapes==0 的独立 Hatch（已解除关联）可直接变换。

        if (_context.ActiveCanvas.Selection.OfType<DrawObject>().Any(o => !o.CanTransform))
            return;
        var selectedShapes = _context.ActiveCanvas!.Selection.OfType<DrawObject>().Where(s => s.CanTransform).ToList();
        if (selectedShapes.Count == 0) return;
        var selectedForCommand = selectedShapes.Where(shape => shape.CanTransform)
            .ToList();
        _hasResizableSelection = selectedForCommand.Count > 0;
        _pendingTransformCommand = _hasResizableSelection
            ? CreateCommand(selectedForCommand)
            : null;


        foreach (var drawObject in selectedShapes)
        {
            if (!drawObject.CanTransform)
                continue;

            if (selectedShapes.Count > 1)
            {
                // 多选拖动期只维护合并 selection frame，不给每个对象挂 preview 状态。
                // 容器的 GetEffectiveWorldBounds 会读取 PreviewWidth/Height；若这里置位，
                // 未变更的对象 preview 会反过来污染“拖动时图形不动”的语义。
                continue;
            }

            drawObject.StartTransform();
        }

        if (!_hasResizableSelection)
        {
            ResetState();
            _context.IsDragControlPoint = false;
            return;
        }

        if (selectedShapes.Count > 1)
        {
            _multipleSelectionResizeSnapshot = CaptureMultipleSelectionResizeSnapshot(
                selectedShapes,
                selectedForCommand);
        }
        else if (selectedShapes.Count == 1)
        {
            var singleSelectedShape = selectedShapes.First();
            if (singleSelectedShape.CanTransform)
            {
                var snapshot = CreateSingleSelectionPreviewSnapshot(singleSelectedShape);
                _singleSelectionPreviewSnapshot = snapshot;
            }
        }
        _context.ReportStatus($"开始调整图形大小/形状 (控制点: {controlPointType})");
    }

    public void Update(SKPoint point)
    {
        if (_context.ActiveCanvas == null || _context.ActiveCanvas.SelectedShapeCount == 0 || !IsDragging)
            return;

        if (_context.ActiveCanvas.Selection.All(shape => shape.IsLocked))
            return;

        if (_context.ActiveCanvas.Selection.OfType<DrawObject>().Any(o => !o.CanTransform))
            return;
        var selectedShapes = _context.ActiveCanvas!.Selection.OfType<DrawObject>().Where(s => s.CanTransform).ToList();
        if (selectedShapes.Count == 0) return;

        SKRect? previousPreviewBounds = _hasMultipleSelectionPreview && !_multipleSelectionPreviewBounds.IsEmpty
            ? _multipleSelectionPreviewBounds
            : null;
        _context.MarkSelectedDirty(previousPreviewBounds);

        // 单选和多选预览规则不同：单选保留图形自身局部变换语义，多选则以共享 bbox 为基准。
        SKRect? singleSelectionPreviewBounds = null;
        if (_context.ActiveCanvas.SelectedShapeCount == 1)
        {
            var shape = _context.ActiveCanvas.Selection.First();
            if (shape is DrawObject drawObject
                && drawObject.CanTransform)
            {
                UpdateSingleShapePreview(drawObject, point);
                singleSelectionPreviewBounds = drawObject.GetPreviewOBB().Corners.ToRect();
            }
        }
        else
        {
            UpdateMultipleShapesPreview(point);
        }

        if (_hasMultipleSelectionPreview && !_multipleSelectionPreviewBounds.IsEmpty)
        {
            _context.MarkSelectedDirty(_multipleSelectionPreviewBounds);
        }
        else if (singleSelectionPreviewBounds is { } previewBounds && !previewBounds.IsEmpty)
        {
            _context.MarkSelectedDirty(previewBounds);
        }
        else
        {
            _context.MarkSelectedDirty();
        }
    }

    public bool Complete()
    {
        if (_context.ActiveCanvas == null || !IsDragging)
            return false;

        if (_context.ActiveCanvas.Selection.OfType<DrawObject>().Any(o => !o.CanTransform))
            return false;
        var selectedShapes = _context.ActiveCanvas!.Selection.OfType<DrawObject>().Where(s => s.CanTransform).ToList();
        if (selectedShapes.Count == 0) return false;

        if (_hasMultipleSelectionPreview)
        {
            _context.MarkSelectedDirty(_multipleSelectionPreviewBounds);
            ApplyMultipleShapesCommit();
            foreach (var drawObject in _context.ActiveCanvas.Selection.OfType<DrawObject>())
            {
                if (!_context.IsDragControlPoint || drawObject.IsLocked)
                    continue;

                if (drawObject is DrawDot dot)
                {
                    //// 点保持圆形，仅按缩放公式等比移动中心后提交。
                    //// 若点正好落在锚点处（某轴距离≈0），该轴保持不动。
                    //var oldCenter = drawObject.SharpCenter;
                    //float distX = oldCenter.X - anchor.X;
                    //float distY = oldCenter.Y - anchor.Y;
                    //dot.IsAnchorX = MathF.Abs(distX) < AnchorEpsilon;
                    //dot.IsAnchorY = MathF.Abs(distY) < AnchorEpsilon;
                    dot.IsAnchorX = false;
                    dot.IsAnchorY = false;
                }

                drawObject.Scale(scaleX, scaleY, anchor, commit: true);
                // 多选缩放提交也需要刷新路径节点、重新计算自交跳点。
                RecalculateSelfIntersectionSkipPoints(drawObject);
                CommitPreviewState(drawObject);
            }
        }
        else
        {
            _context.MarkSelectedDirty();

            foreach (var drawObject in _context.ActiveCanvas.Selection.OfType<DrawObject>())
            {
                if (!_context.IsDragControlPoint || drawObject.IsLocked)
                    continue;

                drawObject.Scale(scaleX, scaleY, anchor, scaleDirectionRad, true);
                // 单选仍沿用既有 preview -> commit 收口点，保留后处理和路径节点刷新契约。
                CommitPreviewState(drawObject);
            }
        }

        _context.IsDragControlPoint = false;

        if (_pendingTransformCommand != null)
        {
            _pendingTransformCommand.CaptureAfterState();
            _context.ActiveCanvas.CommandManager.PushExecutedCommand(_pendingTransformCommand);
        }

        _context.PublishTransformChange();

        ResetState();
        _context.ReportStatus("调整大小完成");
        return true;
    }

    public void Cancel()
    {
        if (_hasMultipleSelectionPreview && !_multipleSelectionPreviewBounds.IsEmpty)
        {
            _context.MarkSelectedDirty(_multipleSelectionPreviewBounds);
        }

        if (_context.ActiveCanvas != null)
        {
            var multipleSelectionSnapshot = _multipleSelectionResizeSnapshot;
            var drawObjects = multipleSelectionSnapshot is null
                ? _context.ActiveCanvas.Selection.OfType<DrawObject>()
                : multipleSelectionSnapshot.Value.Shapes.Select(snapshot => snapshot.Target);

            foreach (var drawObject in drawObjects)
            {
                if (!_context.IsDragControlPoint)
                    continue;

                ClearPreviewState(drawObject);
            }
        }

        _context.IsDragControlPoint = false;
        _context.InvalidateSelectionBoundsCache();
        _context.MarkSelectedDirty();
        _suggestedCursor = null;
        _completedControlPoint = null;
        ResetState();
    }

    private void ResetState()
    {
        _draggingControlPoint = ControlPointType.None;
        _dragStartPoint = SKPoint.Empty;
        _originalMergedBounds = SKRect.Empty;
        _pendingTransformCommand = null;
        _hasResizableSelection = false;
        _hasMultipleSelectionPreview = false;
        _multipleSelectionPreviewBounds = SKRect.Empty;
        _multipleSelectionResizeSnapshot = null;
        _singleSelectionPreviewSnapshot = null;
        _suggestedCursor = null;
    }

    private ControlPointType ResolveControlPointAt(SKPoint point)
    {
        if (_context.ActiveCanvas == null)
        {
            return ControlPointType.None;
        }

        int selectedShapeCount = _context.ActiveCanvas.SelectedShapeCount;
        if (selectedShapeCount == 1)
        {
            IShape? selectedShape = _context.ActiveCanvas.Selection.FirstOrDefault();
            if (selectedShape is not DrawObject drawObject)
            {
                return ControlPointType.None;
            }

            bool canResize = drawObject.Type != ShapeType.Point && drawObject.CanTransform;
            if (!canResize)
            {
                return ControlPointType.None;
            }

            ControlPointType controlPointType = _selectionControlPointService.GetControlPointAt(drawObject, point);
            return controlPointType;
        }

        SKRect mergedBounds = _context.CalculateMergedBounds();
        ControlPointType mergedControlPointType = _selectionControlPointService
            .GetControlPointAtForMultipleSelection(mergedBounds, point);
        return mergedControlPointType;
    }

    private Cursor ResolveResizeCursor(ControlPointType controlPointType)
    {
        if (_context.ActiveCanvas == null)
        {
            return Cursors.Arrow;
        }

        bool allLocked = _context.ActiveCanvas.Selection.All(shape => shape.IsLocked);
        if (allLocked)
        {
            return Cursors.Arrow;
        }

        if (_context.ActiveCanvas.SelectedShapeCount == 1
            && _context.ActiveCanvas.Selection.FirstOrDefault() is DrawObject drawObject)
        {
            Cursor cursor = _selectionControlPointService.GetCursorForControlPoint(drawObject, controlPointType);
            return cursor;
        }

        SKRect mergedBounds = _context.CalculateMergedBounds();
        Cursor mergedCursor = _selectionControlPointService.GetCursorForMergedBounds(
            mergedBounds,
            controlPointType);
        return mergedCursor;
    }

    /// <summary>
    /// OBB 单选缩放预览（世界坐标系）：
    /// 从快照 OBB 角点计算缩放因子和对侧锚点（均为世界坐标），
    /// 使用投影（点积）计算缩放因子，正确处理旋转情况，
    /// 调用 Scale 方法写入 _previewMatrix。
    /// </summary>
    private void UpdateSingleShapePreview(DrawObject drawObject, SKPoint mousePoint)
    {
        var snapshot = ResolveSingleSelectionPreviewSnapshot(drawObject);
        var obbCorners = snapshot.Corners;
        if (obbCorners == null || obbCorners.Length < 4) return;

        // OBB 角点: [0]=左上, [1]=右上, [2]=右下, [3]=左下
        var c0 = obbCorners[3];
        var c1 = obbCorners[2];
        var c2 = obbCorners[1];
        var c3 = obbCorners[0];

        // OBB 边长（世界坐标距离）
        float obbW = SKPoint.Distance(c0, c1);
        float obbH = SKPoint.Distance(c0, c3);
        if (obbW < 0.001f || obbH < 0.001f) return;

        // OBB 方向单位向量（世界坐标）
        var dirR = new SKPoint((c1.X - c0.X) / obbW, (c1.Y - c0.Y) / obbW); // 右方向
        var dirD = new SKPoint((c3.X - c0.X) / obbH, (c3.Y - c0.Y) / obbH); // 下方向

        // OBB 边中点（世界坐标）
        var midL = new SKPoint((c0.X + c3.X) / 2f, (c0.Y + c3.Y) / 2f);
        var midR = new SKPoint((c1.X + c2.X) / 2f, (c1.Y + c2.Y) / 2f);
        var midT = new SKPoint((c0.X + c1.X) / 2f, (c0.Y + c1.Y) / 2f);
        var midB = new SKPoint((c3.X + c2.X) / 2f, (c3.Y + c2.Y) / 2f);

        // 倾斜图形的 OBB 是平行四边形，dirR/dirD 不正交，
        // 正交投影(Dot)会把 dirD 方向分量串入 dirR，导致刚拖动时 scale 就偏离 1。
        // 改为在斜交基 {dirR, dirD} 下解线性方程组（正交时结果与 Dot 一致）。
        float det = dirR.X * dirD.Y - dirR.Y * dirD.X;
        if (MathF.Abs(det) < 1e-6f) return;
        SKPoint Decompose(SKPoint v) => new(
            (v.X * dirD.Y - v.Y * dirD.X) / det,   // 沿 dirR 分量
            (dirR.X * v.Y - dirR.Y * v.X) / det);  // 沿 dirD 分量

        switch (_draggingControlPoint)
        {
            // 边中点：单方向缩放
            case ControlPointType.MiddleRight:
                anchor = midL;
                scaleX = Decompose(mousePoint - midL).X / obbW;
                break;
            case ControlPointType.MiddleLeft:
                anchor = midR;
                scaleX = Decompose(midR - mousePoint).X / obbW;
                break;
            case ControlPointType.TopCenter:
                anchor = midB;
                scaleY = Decompose(midB - mousePoint).Y / obbH;
                break;
            case ControlPointType.BottomCenter:
                anchor = midT;
                scaleY = Decompose(mousePoint - midT).Y / obbH;
                break;

            // 角点：双向缩放，在斜交基下分解 dirR/dirD 分量
            case ControlPointType.TopRight:
                anchor = c3;
                var vTR = Decompose(mousePoint - c3);
                scaleX = vTR.X / obbW;
                scaleY = -vTR.Y / obbH;
                break;
            case ControlPointType.TopLeft:
                anchor = c2;
                var vTL = Decompose(mousePoint - c2);
                scaleX = -vTL.X / obbW;
                scaleY = -vTL.Y / obbH;
                break;
            case ControlPointType.BottomRight:
                anchor = c0;
                var vBR = Decompose(mousePoint - c0);
                scaleX = vBR.X / obbW;
                scaleY = vBR.Y / obbH;
                break;
            case ControlPointType.BottomLeft:
                anchor = c1;
                var vBL = Decompose(mousePoint - c1);
                scaleX = -vBL.X / obbW;
                scaleY = vBL.Y / obbH;
                break;

            default:
                return;
        }

        // 最小尺寸保护
        float minSx = DrawObject.MinDimension / obbW;
        float minSy = DrawObject.MinDimension / obbH;
        scaleX = Math.Max(minSx, scaleX);
        scaleY = Math.Max(minSy, scaleY);
        if (IsCornerControlPoint(_draggingControlPoint))
        {
            float uniform = Math.Max(scaleX, scaleY);
            scaleX = uniform;
            scaleY = uniform;
        }

        // 调用 Scale（世界坐标锚点 + OBB 方向角）：沿 OBB 的 X/Y 方向缩放，旋转图形拉控制点保形不剪切
        scaleDirectionRad = MathF.Atan2(dirR.Y, dirR.X);
        drawObject.Scale(scaleX, scaleY, anchor, scaleDirectionRad);

        Debug.WriteLine($"锚点为：{anchor}");
        _context.ReportStatus($"OBB缩放: scale=({scaleX:F2},{scaleY:F2})");
    }

    private SingleSelectionPreviewSnapshot ResolveSingleSelectionPreviewSnapshot(DrawObject drawObject)
    {
        var snapshot = _singleSelectionPreviewSnapshot;
        if (snapshot.HasValue && ReferenceEquals(snapshot.Value.Target, drawObject))
        {
            return snapshot.Value;
        }

        var fallbackSnapshot = CreateSingleSelectionPreviewSnapshot(drawObject);
        return fallbackSnapshot;
    }

    private static SingleSelectionPreviewSnapshot CreateSingleSelectionPreviewSnapshot(DrawObject drawObject)
    {
        var geom = SelectionGeometryBuilder.BuildForSinglePreviewOBBSelection(drawObject);
        // 锚点用图形实际边缘（offset=0），GetPreviewOBB 的 offset 已改为世界坐标扩展（不参与缩放），
        // 缩放时图形边缘固定，选择框边缘 = 图形边缘 ± 固定 offset，两者都卯住。
        return new SingleSelectionPreviewSnapshot(drawObject, drawObject.GetPreviewOBB().Corners, geom.ControlPoints);
    }

    private void UpdateMultipleShapesPreview(SKPoint mousePoint)
    {
        // 多选缩放统一到新矩阵体系：把多个图形的合并选区框当作一个整体 OBB，
        // 用与单选相同的投影逻辑算出 scaleX/scaleY/anchor，再对每个图形调用 ScaleLocalToWorld。
        // 每个图形写入 _previewMatrix 后，合并框（基于各图形 GetPreviewAABB）会自动跟随。
        // 不设置 _hasMultipleSelectionPreview，Complete 走 EndTransform + CommitPreviewState 复用单选提交路径。
        var multipleSelectionSnapshot = _multipleSelectionResizeSnapshot;
        if (multipleSelectionSnapshot == null)
            return;

        var snapshot = multipleSelectionSnapshot.Value;
        var obbCorners = snapshot.Corners;
        if (obbCorners == null || obbCorners.Length < 4) return;

        // 合并框 OBB 角点: [0]=左上, [1]=右上, [2]=右下, [3]=左下
        var c0 = obbCorners[3];
        var c1 = obbCorners[2];
        var c2 = obbCorners[1];
        var c3 = obbCorners[0];

        float obbW = SKPoint.Distance(c0, c1);
        float obbH = SKPoint.Distance(c0, c3);
        if (obbW < 0.001f || obbH < 0.001f) return;

        var dirR = new SKPoint((c1.X - c0.X) / obbW, (c1.Y - c0.Y) / obbW);
        var dirD = new SKPoint((c3.X - c0.X) / obbH, (c3.Y - c0.Y) / obbH);

        var midL = new SKPoint((c0.X + c3.X) / 2f, (c0.Y + c3.Y) / 2f);
        var midR = new SKPoint((c1.X + c2.X) / 2f, (c1.Y + c2.Y) / 2f);
        var midT = new SKPoint((c0.X + c1.X) / 2f, (c0.Y + c1.Y) / 2f);
        var midB = new SKPoint((c3.X + c2.X) / 2f, (c3.Y + c2.Y) / 2f);

        static float Dot(SKPoint a, SKPoint b) => a.X * b.X + a.Y * b.Y;

        switch (_draggingControlPoint)
        {
            case ControlPointType.MiddleRight:
                anchor = midL; scaleX = Dot(mousePoint - midL, dirR) / obbW; break;
            case ControlPointType.MiddleLeft:
                anchor = midR; scaleX = Dot(midR - mousePoint, dirR) / obbW; break;
            case ControlPointType.TopCenter:
                anchor = midB;
                scaleY = Dot(midB - mousePoint, dirD) / obbH;
                break;
            case ControlPointType.BottomCenter:
                anchor = midT;
                scaleY = Dot(mousePoint - midT, dirD) / obbH;
                break;
            case ControlPointType.TopRight:
                anchor = c3; var vTR = mousePoint - c3;
                scaleX = Dot(vTR, dirR) / obbW; scaleY = -Dot(vTR, dirD) / obbH; break;
            case ControlPointType.TopLeft:
                anchor = c2; var vTL = mousePoint - c2;
                scaleX = -Dot(vTL, dirR) / obbW; scaleY = -Dot(vTL, dirD) / obbH; break;
            case ControlPointType.BottomRight:
                anchor = c0; var vBR = mousePoint - c0;
                scaleX = Dot(vBR, dirR) / obbW; scaleY = Dot(vBR, dirD) / obbH; break;
            case ControlPointType.BottomLeft:
                anchor = c1; var vBL = mousePoint - c1;
                scaleX = -Dot(vBL, dirR) / obbW; scaleY = Dot(vBL, dirD) / obbH; break;
            default:
                return;
        }

        float minSx = DrawObject.MinDimension / obbW;
        float minSy = DrawObject.MinDimension / obbH;
        scaleX = Math.Max(minSx, scaleX);
        scaleY = Math.Max(minSy, scaleY);
        if (IsCornerControlPoint(_draggingControlPoint))
        {
            float uniform = Math.Max(scaleX, scaleY);
            scaleX = uniform;
            scaleY = uniform;
        }


        var previewBounds = ComputeMultipleSelectionPreviewBounds(
            snapshot,
            anchor,
            scaleX,
            scaleY,
            _originalMergedBounds,
            snapshot.UsePointCenterBounds);

        _multipleSelectionDelta = BatchTransformHelper.CreateResize(
            anchor,
            scaleX,
            scaleY,
            _draggingControlPoint,
            snapshot.ScaleSourceBounds);

        foreach (var shapeSnapshot in snapshot.Shapes)
        {
            var drawObject = shapeSnapshot.Target;
            if (drawObject.IsLocked) continue;

            if (drawObject is DrawDot dot)
            {
                // 点必须保持圆形（不缩放尺寸），仅按缩放公式等比移动中心：
                // newCenter = anchor + (oldCenter - anchor) * scale。
                // 锚点侧的点位移为 0（如右拖时左侧锚点处的点不动），越远离锚点位移越大。
                // 点包围圈为 0 时，若点正好落在锚点处（某轴距离≈0），该轴保持不动，避免浮点漂移。
                var oldCenter = new SKPoint(shapeSnapshot.Bounds.MidX, shapeSnapshot.Bounds.MidY);
                float distX = oldCenter.X - anchor.X;
                float distY = oldCenter.Y - anchor.Y;
                dot.IsAnchorX = MathF.Abs(distX) < AnchorEpsilon;
                dot.IsAnchorY = MathF.Abs(distY) < AnchorEpsilon;
            }

            drawObject.Scale(scaleX, scaleY, anchor);
        }

        _multipleSelectionPreviewBounds = previewBounds;
        _hasMultipleSelectionPreview = true;
        _context.ReportStatus($"多选OBB缩放: scale=({scaleX:F2},{scaleY:F2})");
    }


    private static bool IsCornerControlPoint(ControlPointType controlPointType)
    {
        var isCornerControlPoint = controlPointType is ControlPointType.TopLeft
            or ControlPointType.TopRight
            or ControlPointType.BottomLeft
            or ControlPointType.BottomRight;
        return isCornerControlPoint;
    }

    private SKRect ComputeMultipleSelectionPreviewBounds(
        MultipleSelectionResizeSnapshot snapshot,
        SKPoint scaleCenter,
        float scaleX,
        float scaleY,
        SKRect fallbackBounds,
        bool usePointCenterBounds)
    {
        var shapes = snapshot.Shapes;
        if (shapes.Count == 0)
            return fallbackBounds;

        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;
        bool hasBounds = false;
        // mixed 点选区里，点是否“贴着哪一条边”不能再参考完整 merged bounds。
        // 否则旋转后的可缩放图形把外框撑到点集合之外时，原本应保持在点集边缘的点
        // 会被误判成 interior point，preview 就会先把它往里压。
        var pointAnchorSourceBounds = snapshot.PointAnchorSourceBounds.IsEmpty
            ? snapshot.AnchorSourceBounds
            : snapshot.PointAnchorSourceBounds;

        foreach (var shapeSnapshot in shapes)
        {
            var predictedBounds = PredictMultiSelectionPreviewBounds(
                shapeSnapshot,
                snapshot.AnchorSourceBounds,
                pointAnchorSourceBounds,
                scaleCenter,
                scaleX,
                scaleY,
                usePointCenterBounds);
            if (predictedBounds.IsEmpty)
                continue;

            hasBounds = true;
            if (predictedBounds.Left < minX) minX = predictedBounds.Left;
            if (predictedBounds.Top < minY) minY = predictedBounds.Top;
            if (predictedBounds.Right > maxX) maxX = predictedBounds.Right;
            if (predictedBounds.Bottom > maxY) maxY = predictedBounds.Bottom;
        }

        foreach (var shapeSnapshot in snapshot.StaticShapes)
        {
            var bounds = shapeSnapshot.Bounds;
            if (bounds.IsEmpty)
                continue;

            hasBounds = true;
            if (bounds.Left < minX) minX = bounds.Left;
            if (bounds.Top < minY) minY = bounds.Top;
            if (bounds.Right > maxX) maxX = bounds.Right;
            if (bounds.Bottom > maxY) maxY = bounds.Bottom;
        }

        return hasBounds ? new SKRect(minX, minY, maxX, maxY) : fallbackBounds;
    }

    private SKPoint ResolveMultipleSelectionNewCenter(
        SKRect anchorSourceBounds,
        SKPoint oldCenter,
        float scaleX,
        float scaleY)
    {
        float anchorLeft = anchorSourceBounds.Left;
        float anchorRight = anchorSourceBounds.Right;
        float anchorTop = anchorSourceBounds.Top;
        float anchorBottom = anchorSourceBounds.Bottom;
        float anchorCenterY = anchorSourceBounds.MidY;

        float newCenterX = _draggingControlPoint switch
        {
            ControlPointType.TopLeft or ControlPointType.MiddleLeft or ControlPointType.BottomLeft
                => anchorRight + (oldCenter.X - anchorRight) * scaleX,
            _ => anchorLeft + (oldCenter.X - anchorLeft) * scaleX
        };

        float newCenterY = _draggingControlPoint switch
        {
            ControlPointType.TopLeft or ControlPointType.TopCenter or ControlPointType.TopRight
                => anchorTop + (oldCenter.Y - anchorTop) * scaleY,
            ControlPointType.BottomLeft or ControlPointType.BottomCenter or ControlPointType.BottomRight
                => anchorBottom + (oldCenter.Y - anchorBottom) * scaleY,
            _ => anchorCenterY + (oldCenter.Y - anchorCenterY) * scaleY
        };

        if (_draggingControlPoint is ControlPointType.MiddleLeft or ControlPointType.MiddleRight)
        {
            newCenterY = oldCenter.Y;
        }

        if (_draggingControlPoint is ControlPointType.TopCenter or ControlPointType.BottomCenter)
        {
            newCenterX = oldCenter.X;
        }

        return new SKPoint(newCenterX, newCenterY);
    }

    private SKPoint ResolvePointSelectionNewCenter(
        SKRect anchorSourceBounds,
        SKRect sourceBounds,
        SKPoint oldCenter,
        float scaleX,
        float scaleY)
    {
        float anchorLeft = anchorSourceBounds.Left;
        float anchorRight = anchorSourceBounds.Right;
        float anchorTop = anchorSourceBounds.Top;
        float anchorBottom = anchorSourceBounds.Bottom;
        float width = sourceBounds.Width;
        float height = sourceBounds.Height;
        // 二次拖拽时点对象的提交结果会带少量浮点误差。
        // 这里若只用 1e-3 级别容差，会把视觉上仍贴边的点误判成“内部点”，
        // 进而在下一次缩放里被按中心比例放飞。
        const float edgeTolerance = 0.5f;

        bool touchesLeftEdge = Math.Abs(sourceBounds.Left - anchorSourceBounds.Left) < edgeTolerance;
        bool touchesRightEdge = Math.Abs(sourceBounds.Right - anchorSourceBounds.Right) < edgeTolerance;
        bool touchesTopEdge = Math.Abs(sourceBounds.Top - anchorSourceBounds.Top) < edgeTolerance;
        bool touchesBottomEdge = Math.Abs(sourceBounds.Bottom - anchorSourceBounds.Bottom) < edgeTolerance;

        float newCenterX = _draggingControlPoint switch
        {
            ControlPointType.TopLeft or ControlPointType.MiddleLeft or ControlPointType.BottomLeft
                => ResolvePointCenterXFromLeftDrag(
                    anchorRight,
                    sourceBounds.Left,
                    sourceBounds.Right,
                    oldCenter.X,
                    width,
                    scaleX,
                    touchesLeftEdge,
                    touchesRightEdge),
            ControlPointType.TopRight or ControlPointType.MiddleRight or ControlPointType.BottomRight
                => ResolvePointCenterXFromRightDrag(
                    anchorLeft,
                    sourceBounds.Left,
                    sourceBounds.Right,
                    oldCenter.X,
                    width,
                    scaleX,
                    touchesLeftEdge,
                    touchesRightEdge),
            _ => oldCenter.X
        };

        float newCenterY = _draggingControlPoint switch
        {
            ControlPointType.TopLeft or ControlPointType.TopCenter or ControlPointType.TopRight
                => ResolvePointCenterYFromTopDrag(
                    anchorTop,
                    sourceBounds.Top,
                    sourceBounds.Bottom,
                    oldCenter.Y,
                    height,
                    scaleY,
                    touchesTopEdge,
                    touchesBottomEdge),
            ControlPointType.BottomLeft or ControlPointType.BottomCenter or ControlPointType.BottomRight
                => ResolvePointCenterYFromBottomDrag(
                    anchorBottom,
                    sourceBounds.Top,
                    sourceBounds.Bottom,
                    oldCenter.Y,
                    height,
                    scaleY,
                    touchesTopEdge,
                    touchesBottomEdge),
            _ => oldCenter.Y
        };

        return new SKPoint(newCenterX, newCenterY);
    }

    private static float ResolvePointCenterXFromRightDrag(
        float anchorLeft,
        float sourceLeft,
        float sourceRight,
        float oldCenterX,
        float width,
        float scaleX,
        bool touchesLeftEdge,
        bool touchesRightEdge)
    {
        if (touchesRightEdge)
        {
            float newRight = anchorLeft + (sourceRight - anchorLeft) * scaleX;
            return newRight - width / 2f;
        }

        if (touchesLeftEdge)
        {
            float newLeft = anchorLeft + (sourceLeft - anchorLeft) * scaleX;
            return newLeft + width / 2f;
        }

        float newCenterX = anchorLeft + (oldCenterX - anchorLeft) * scaleX;
        return newCenterX;
    }

    private static float ResolvePointCenterXFromLeftDrag(
        float anchorRight,
        float sourceLeft,
        float sourceRight,
        float oldCenterX,
        float width,
        float scaleX,
        bool touchesLeftEdge,
        bool touchesRightEdge)
    {
        if (touchesLeftEdge)
        {
            float newLeft = anchorRight + (sourceLeft - anchorRight) * scaleX;
            return newLeft + width / 2f;
        }

        if (touchesRightEdge)
        {
            float newRight = anchorRight + (sourceRight - anchorRight) * scaleX;
            return newRight - width / 2f;
        }

        float newCenterX = anchorRight + (oldCenterX - anchorRight) * scaleX;
        return newCenterX;
    }

    private static float ResolvePointCenterYFromTopDrag(
        float anchorTop,
        float sourceTop,
        float sourceBottom,
        float oldCenterY,
        float height,
        float scaleY,
        bool touchesTopEdge,
        bool touchesBottomEdge)
    {
        if (touchesTopEdge)
        {
            float newTop = anchorTop + (sourceTop - anchorTop) * scaleY;
            return newTop + height / 2f;
        }

        if (touchesBottomEdge)
        {
            float newBottom = anchorTop + (sourceBottom - anchorTop) * scaleY;
            return newBottom - height / 2f;
        }

        float newCenterY = anchorTop + (oldCenterY - anchorTop) * scaleY;
        return newCenterY;
    }

    private static float ResolvePointCenterYFromBottomDrag(
        float anchorBottom,
        float sourceTop,
        float sourceBottom,
        float oldCenterY,
        float height,
        float scaleY,
        bool touchesTopEdge,
        bool touchesBottomEdge)
    {
        if (touchesBottomEdge)
        {
            float newBottom = anchorBottom + (sourceBottom - anchorBottom) * scaleY;
            return newBottom - height / 2f;
        }

        if (touchesTopEdge)
        {
            float newTop = anchorBottom + (sourceTop - anchorBottom) * scaleY;
            return newTop + height / 2f;
        }

        float newCenterY = anchorBottom + (oldCenterY - anchorBottom) * scaleY;
        return newCenterY;
    }

    private SKRect PredictMultiSelectionPreviewBounds(
        MultipleSelectionShapeSnapshot shapeSnapshot,
        SKRect anchorSourceBounds,
        SKRect pointAnchorSourceBounds,
        SKPoint scaleCenter,
        float scaleX,
        float scaleY,
        bool usePointCenterBounds)
    {
        var currentBounds = shapeSnapshot.Bounds;
        if (currentBounds.IsEmpty)
            return SKRect.Empty;

        if (shapeSnapshot.IsLocked || shapeSnapshot.IsAssociativeHatch)
            return currentBounds;

        if (shapeSnapshot.Type == ShapeType.Point)
        {
            var currentCenter = shapeSnapshot.Center;
            SKPoint newCenter;
            if (usePointCenterBounds)
            {
                newCenter = new SKPoint(
                    scaleCenter.X + (currentCenter.X - scaleCenter.X) * scaleX,
                    scaleCenter.Y + (currentCenter.Y - scaleCenter.Y) * scaleY);
            }
            else
            {
                newCenter = ResolvePointSelectionNewCenter(
                    anchorSourceBounds: pointAnchorSourceBounds,
                    sourceBounds: currentBounds,
                    oldCenter: currentCenter,
                    scaleX: scaleX,
                    scaleY: scaleY);
            }

            var dx = newCenter.X - currentCenter.X;
            var dy = newCenter.Y - currentCenter.Y;
            return new SKRect(
                currentBounds.Left + dx,
                currentBounds.Top + dy,
                currentBounds.Right + dx,
                currentBounds.Bottom + dy);
        }

        float left = scaleCenter.X + (currentBounds.Left - scaleCenter.X) * scaleX;
        float right = scaleCenter.X + (currentBounds.Right - scaleCenter.X) * scaleX;
        float top = scaleCenter.Y + (currentBounds.Top - scaleCenter.Y) * scaleY;
        float bottom = scaleCenter.Y + (currentBounds.Bottom - scaleCenter.Y) * scaleY;
        return new SKRect(
            Math.Min(left, right),
            Math.Min(top, bottom),
            Math.Max(left, right),
            Math.Max(top, bottom));
    }

    private void ApplyMultipleShapesCommit()
    {
        var multipleSelectionSnapshot = _multipleSelectionResizeSnapshot;
        if (multipleSelectionSnapshot == null || !_hasMultipleSelectionPreview)
            return;

        var snapshot = multipleSelectionSnapshot.Value;
        // preview 和 commit 必须共用同一份点边缘判定基准，
        // 否则 preview 看起来正确，mouse-up 后点会按另一套锚边语义再次跳动。
        var pointAnchorSourceBounds = snapshot.PointAnchorSourceBounds.IsEmpty
            ? snapshot.AnchorSourceBounds
            : snapshot.PointAnchorSourceBounds;

        foreach (var shapeSnapshot in snapshot.Shapes)
        {
            var drawObject = shapeSnapshot.Target;
            if (!drawObject.CanTransform)
                continue;

            // 点图元的产品语义是“固定视觉尺寸，只移动中心”。
            // 即使点对象带着非零 Rotation，它在 mixed selection commit 时也不能走 affine 分支，
            // 否则会把点按 world transform 重新写回，最终结果就会和 preview 分叉。
            if (shapeSnapshot.Type == ShapeType.Point && snapshot.UsePointCenterBounds)
            {
                var currentCenter = shapeSnapshot.Center;
                var pointNewCenter = new SKPoint(
                    _multipleSelectionDelta.AnchorWorldPoint.X +
                    (currentCenter.X - _multipleSelectionDelta.AnchorWorldPoint.X) * _multipleSelectionDelta.ScaleX,
                    _multipleSelectionDelta.AnchorWorldPoint.Y +
                    (currentCenter.Y - _multipleSelectionDelta.AnchorWorldPoint.Y) * _multipleSelectionDelta.ScaleY);

                CommitResolvedBounds(
                    drawObject,
                    shapeSnapshot.Width,
                    shapeSnapshot.Height,
                    currentCenter,
                    shapeSnapshot.Width,
                    shapeSnapshot.Height,
                    pointNewCenter);
                continue;
            }

            if (shapeSnapshot.Type == ShapeType.Point)
            {
                var pointNewCenter = ResolvePointSelectionNewCenter(
                    anchorSourceBounds: pointAnchorSourceBounds,
                    sourceBounds: shapeSnapshot.Bounds,
                    oldCenter: shapeSnapshot.Center,
                    scaleX: _multipleSelectionDelta.ScaleX,
                    scaleY: _multipleSelectionDelta.ScaleY);

                CommitResolvedBounds(
                    drawObject,
                    shapeSnapshot.Width,
                    shapeSnapshot.Height,
                    shapeSnapshot.Center,
                    shapeSnapshot.Width,
                    shapeSnapshot.Height,
                    pointNewCenter);
                continue;
            }

            if (shapeSnapshot.NeedsAffineCommit)
            {
                BatchTransformHelper.CommitLeafWorldTransform(drawObject, _multipleSelectionDelta);
                continue;
            }

            var newCenter = ResolveMultipleSelectionNewCenter(
                anchorSourceBounds: snapshot.AnchorSourceBounds,
                oldCenter: shapeSnapshot.Center,
                scaleX: _multipleSelectionDelta.ScaleX,
                scaleY: _multipleSelectionDelta.ScaleY);

            CommitResolvedBounds(
                drawObject,
                shapeSnapshot.Width,
                shapeSnapshot.Height,
                shapeSnapshot.Center,
                shapeSnapshot.Width * _multipleSelectionDelta.ScaleX,
                shapeSnapshot.Height * _multipleSelectionDelta.ScaleY,
                newCenter);
        }
    }

    private SKPoint ResolveMultiSelectionScaleCenter(
        float originalLeft,
        float originalRight,
        float originalTop,
        float originalBottom,
        float originalCenterX,
        float originalCenterY)
    {
        float centerX = _draggingControlPoint switch
        {
            ControlPointType.TopLeft or ControlPointType.MiddleLeft or ControlPointType.BottomLeft => originalRight,
            ControlPointType.TopCenter or ControlPointType.BottomCenter => originalCenterX,
            _ => originalLeft
        };

        float centerY = _draggingControlPoint switch
        {
            ControlPointType.TopLeft or ControlPointType.TopCenter or ControlPointType.TopRight => originalTop,
            ControlPointType.MiddleLeft or ControlPointType.MiddleRight => originalCenterY,
            _ => originalBottom
        };

        return new SKPoint(centerX, centerY);
    }

    private MultipleSelectionResizeSnapshot CaptureMultipleSelectionResizeSnapshot(
        IReadOnlyList<DrawObject> selectedShapes,
        IReadOnlyCollection<DrawObject> resizableShapes)
    {
        var shapes = new List<MultipleSelectionShapeSnapshot>();
        var staticShapes = new List<MultipleSelectionShapeSnapshot>();
        var resizableShapeSet = resizableShapes.ToHashSet();
        float minPointX = float.MaxValue;
        float minPointY = float.MaxValue;
        float maxPointX = float.MinValue;
        float maxPointY = float.MinValue;
        float minScalableX = float.MaxValue;
        float minScalableY = float.MaxValue;
        float maxScalableX = float.MinValue;
        float maxScalableY = float.MinValue;
        bool hasPoint = false;
        bool hasScalableShape = false;

        foreach (var drawObject in selectedShapes)
        {
            var sourceFrame = GetPreviewSourceFrame(drawObject);
            var bounds = drawObject.GetAABB2().Corners.ToRect();
            bool isPoint = drawObject.Type == ShapeType.Point;
            bool isAssociativeHatch = drawObject.CanTransform;
            bool needsAffineCommit = BatchTransformHelper.NeedsAffineLeafCommit(drawObject);
            bool participatesInResize = resizableShapeSet.Contains(drawObject);

            var shapeSnapshot = new MultipleSelectionShapeSnapshot(
                drawObject,
                drawObject.Type,
                drawObject.IsLocked,
                isAssociativeHatch,
                needsAffineCommit,
                sourceFrame.Width,
                sourceFrame.Height,
                sourceFrame.Center,
                bounds);

            if (participatesInResize)
            {
                shapes.Add(shapeSnapshot);
            }
            else
            {
                // 锁定图形和关联填充不参与 resize 比例与提交，
                // 但仍属于当前 selection，需要并回 preview 外框。
                staticShapes.Add(shapeSnapshot);
            }

            if (!participatesInResize)
                continue;

            if (isPoint)
            {
                hasPoint = true;
                var center = drawObject.SharpCenter;
                if (center.X < minPointX) minPointX = center.X;
                if (center.Y < minPointY) minPointY = center.Y;
                if (center.X > maxPointX) maxPointX = center.X;
                if (center.Y > maxPointY) maxPointY = center.Y;
                continue;
            }

            if (bounds.IsEmpty)
                continue;

            hasScalableShape = true;
            if (bounds.Left < minScalableX) minScalableX = bounds.Left;
            if (bounds.Top < minScalableY) minScalableY = bounds.Top;
            if (bounds.Right > maxScalableX) maxScalableX = bounds.Right;
            if (bounds.Bottom > maxScalableY) maxScalableY = bounds.Bottom;
        }

        bool usePointCenterBounds = hasPoint && !hasScalableShape && shapes.Count > 1;
        bool useMixedPointScalableBounds = hasPoint && hasScalableShape;
        bool useSelectionFrameScaleForMixedHorizontalEdge = useMixedPointScalableBounds
            && _draggingControlPoint is ControlPointType.MiddleLeft or ControlPointType.MiddleRight;

        SKRect scaleSourceBounds = selectedShapes.GetUnionAABB();
        SKRect anchorSourceBounds = usePointCenterBounds
            ? scaleSourceBounds
            : _originalMergedBounds;
        SKRect pointAnchorSourceBounds = SKRect.Empty;
        var selectionConstraints = SelectionResizeConstraintResolver
            .ResolveForSelection(shapes.Select(shape => (IShape)shape.Target));
        var requiresSelectionUniformScale = selectionConstraints
            .HasFlag(SelectionResizeConstraint.RequireUniformScale);
        var isCornerControlPoint = IsCornerControlPoint(_draggingControlPoint);
        bool requiresUniformScale = requiresSelectionUniformScale || isCornerControlPoint;

        // 将多选合并框（世界 AABB）当作一个整体 OBB，供投影计算 scaleX/scaleY/anchor。
        // 角点顺序: [0]=左上, [1]=右上, [2]=右下, [3]=左下（与单选 OBB 一致）。
        SKPoint[] mergedCorners = new SKPoint[]
        {
            new SKPoint(_originalMergedBounds.Left, _originalMergedBounds.Top),
            new SKPoint(_originalMergedBounds.Right, _originalMergedBounds.Top),
            new SKPoint(_originalMergedBounds.Right, _originalMergedBounds.Bottom),
            new SKPoint(_originalMergedBounds.Left, _originalMergedBounds.Bottom)
        };

        var snapshot = new MultipleSelectionResizeSnapshot(
            mergedCorners,
            scaleSourceBounds,
            anchorSourceBounds,
            pointAnchorSourceBounds,
            requiresUniformScale,
            usePointCenterBounds,
            shapes,
            staticShapes);
        return snapshot;
    }

    private readonly record struct SingleSelectionPreviewSnapshot(
        DrawObject Target,
        SKPoint[] Corners,
        SKPoint[] ControlPoints)
    {
    }

    private static (float Width, float Height, SKPoint Center) GetPreviewSourceFrame(DrawObject drawObject)
    {
        if (drawObject is DrawCombination or DrawingGroup)
        {
            var bounds = drawObject.GetAABB();
            if (!bounds.IsEmpty)
            {
                return (bounds.Width, bounds.Height, new SKPoint(bounds.MidX, bounds.MidY));
            }
        }

        return (drawObject.Width, drawObject.Height, drawObject.SharpCenter);
    }

    private readonly record struct MultipleSelectionResizeSnapshot(
        SKPoint[] Corners,
        SKRect ScaleSourceBounds,
        SKRect AnchorSourceBounds,
        SKRect PointAnchorSourceBounds,
        bool RequiresUniformScale,
        bool UsePointCenterBounds,
        IReadOnlyList<MultipleSelectionShapeSnapshot> Shapes,
        IReadOnlyList<MultipleSelectionShapeSnapshot> StaticShapes);

    private readonly record struct MultipleSelectionShapeSnapshot(
        DrawObject Target,
        ShapeType Type,
        bool IsLocked,
        bool IsAssociativeHatch,
        bool NeedsAffineCommit,
        float Width,
        float Height,
        SKPoint Center,
        SKRect Bounds);

    private static void CommitResolvedBounds(
        DrawObject drawObject,
        float oldWidth,
        float oldHeight,
        SKPoint oldCenter,
        float newWidth,
        float newHeight,
        SKPoint newCenter)
    {
        switch (drawObject)
        {
            default:
                if (drawObject.Type != ShapeType.Point)
                {
                }

                break;
        }
    }

    private void CommitPreviewState(DrawObject drawObject)
    {
        drawObject.RefreshPathNodesAfterPreviewCommit();
        ClearPreviewState(drawObject);

        // 控制点缩放提交后，本地路径（Width/Height）已改变，
        // 自交跳点数据（IntersectionSkipPoints/BridgeDirections）仍基于旧路径的本地坐标，
        // 需要重新计算，否则桥接线段会显示在错误位置。
        RecalculateSelfIntersectionSkipPoints(drawObject);
    }

    /// <summary>
    /// 重新计算单图形的自交跳点数据。
    /// 仅在图形有自交跳点时才重算，避免不必要的开销。
    /// </summary>
    private static void RecalculateSelfIntersectionSkipPoints(DrawObject drawObject)
    {
        if (drawObject.IntersectionSkipRadius <= 0f)
            return;

        // 没有自交跳点则无需重算
        if (drawObject.SelfIntersectionSkipCount <= 0)
            return;

        float skipRadius = drawObject.IntersectionSkipRadius;

        // 保留跨图形交点（索引 >= SelfIntersectionSkipCount 的部分），
        // 只重算自交部分（索引 < SelfIntersectionSkipCount）
        var crossShapePoints = new List<SKPoint>();
        if (drawObject.IntersectionSkipPoints.Count > drawObject.SelfIntersectionSkipCount)
        {
            for (int i = drawObject.SelfIntersectionSkipCount; i < drawObject.IntersectionSkipPoints.Count; i++)
                crossShapePoints.Add(drawObject.IntersectionSkipPoints[i]);
        }

        // 重算自交部分
        var selfIntersections = drawObject.ComputeSelfIntersections();

        // 重建 IntersectionSkipPoints：新自交点 + 保留的跨图形交点
        drawObject.IntersectionSkipPoints.Clear();
        drawObject.IntersectionSkipBridgeDirections.Clear();

        foreach (var (point, direction) in selfIntersections)
        {
            drawObject.IntersectionSkipPoints.Add(point);
            drawObject.IntersectionSkipBridgeDirections.Add(direction);
        }
        drawObject.SelfIntersectionSkipCount = selfIntersections.Count;

        foreach (var pt in crossShapePoints)
            drawObject.IntersectionSkipPoints.Add(pt);

        drawObject.IntersectionSkipRadius = skipRadius;
    }

    private static void ClearPreviewState(DrawObject drawObject)
    {
        IEnumerable<DrawObject> children = drawObject switch
        {
            DrawCombination combo => combo.Children.OfType<DrawObject>(),
            DrawingGroup group => group.Children.OfType<DrawObject>(),
            _ => Array.Empty<DrawObject>()
        };

        foreach (var child in children)
        {
            ClearPreviewState(child);
        }
    }

    private static IDeferredCommand CreateCommand(List<DrawObject> list)
    {
        return new CommandTransform(CommandTransform.CollectWithChildren(list), "调整大小");
    }
}
