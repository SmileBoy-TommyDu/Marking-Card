using System.Diagnostics;
using DrSoft.Drawing.Controls.Commands;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Model;
using SkiaSharp;
using Cursor = System.Windows.Input.Cursor;
using Cursors = System.Windows.Input.Cursors;

namespace DrSoft.Drawing.Controls.Tools
{
    internal sealed class ControlPointRotationSession : IToolSelectSession
    {
        private readonly DocumentContext _context;
        private readonly SelectionControlPointService _selectionControlPointService;

        private ControlPointType _draggingControlPoint = ControlPointType.None;
        private SKPoint _dragStartPoint = SKPoint.Empty;
        private SKRect _originalMergedBounds = SKRect.Empty;
        private IDeferredCommand? _pendingTransformCommand;
        private SKPoint[]? _initRotationCorners;
        private float _startAngleDeg;
        private float _deltaAngle;
        private SKPoint _originalRotationCenter; // 拖拽前图形的旋转中心（世界坐标）
        private Cursor? _suggestedCursor;
        private ControlPointType? _completedControlPoint;
        private bool _isUpdateing;
        public ControlPointRotationSession(DocumentContext context)
        {
            _context = context;
            _selectionControlPointService = new SelectionControlPointService(context);
        }

        public string Name => "ControlPointRotation";

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

            bool isThirdSelected = _context.SelectState == SelectState.ThirdSelected;
            if (!isThirdSelected)
            {
                message = "当前不是第三态旋转";
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
            bool hasCornerControlPoint = IsCornerControlPoint(controlPointType);
            if (!hasCornerControlPoint)
            {
                message = "未命中第三态角控制点";
                return false;
            }

            SKRect originalMergedBounds = _context.CalculateMergedBounds();
            Start(controlPointType, point, originalMergedBounds);
            _suggestedCursor = Cursors.Hand;
            message = "开始第三态旋转";
            return true;
        }

        public bool TryMouseMove(SKPoint point, out string message)
        {
            if (IsDragging && _context.IsDragControlPoint)
            {
                Update(point);
                message = "更新第三态旋转预览";
                return true;
            }

            if (_context.ActiveCanvas == null)
            {
                message = "没有活动画布";
                return false;
            }

            bool isThirdSelected = _context.SelectState == SelectState.ThirdSelected;
            if (!isThirdSelected)
            {
                message = "当前不是第三态旋转";
                return false;
            }

            ControlPointType controlPointType = ResolveControlPointAt(point);
            bool hasCornerControlPoint = IsCornerControlPoint(controlPointType);
            if (!hasCornerControlPoint)
            {
                message = "未命中第三态角控制点";
                return false;
            }

            _suggestedCursor = Cursors.Hand;
            message = "命中第三态角控制点";
            return true;
        }

        public bool TryMouseUp(SKPoint point, out string message)
        {
            if (!IsDragging)
            {
                message = "第三态旋转未处于拖拽中";
                return false;
            }

            ControlPointType draggingControlPoint = DraggingControlPoint;
            bool completed = Complete();
            if (!completed)
            {
                message = "第三态旋转提交失败";
                return false;
            }

            _completedControlPoint = draggingControlPoint;
            _suggestedCursor = null;
            message = "完成第三态旋转";
            return true;
        }

        public bool TryRightMouseDown(SKPoint point, out string message)
        {
            message = "第三态旋转不处理右键";
            return false;
        }

        /// <summary>
        /// 开始一次控制点拖拽，并为最终缩放命令准备 before-state。
        /// </summary>
        public void Start(ControlPointType controlPointType, SKPoint point, SKRect originalMergedBounds)
        {
            if (_context.SelectState != SelectState.ThirdSelected)
                return;

            if (_context.ActiveCanvas == null || _context.ActiveCanvas.SelectedShapeCount == 0)
                return;

            if (_context.ActiveCanvas.Selection.OfType<DrawObject>().Any(o => !o.CanTransform))
                return;

            var selectedShapes = _context.ActiveCanvas.Selection
                .OfType<DrawObject>()
                .Where(shape => shape.CanTransform);
            if (selectedShapes.Count() == 0) return;

            _isUpdateing = false;
            _completedControlPoint = null;
            _draggingControlPoint = controlPointType;
            _dragStartPoint = point;
            _originalMergedBounds = originalMergedBounds;
            _context.IsDragControlPoint = true;

            if (selectedShapes.Count() == 1)
            {
                var firstShape = selectedShapes.FirstOrDefault();
                if (firstShape == null) return;

                _initRotationCorners = firstShape.GetOBB().Corners;
                _context.RealRotationCorners = _initRotationCorners.CloneEx();

                // 捕获当前旋转中心；若尚未设置（0,0），退回图形中心
                _originalRotationCenter = firstShape.RotationCenter;

                // 围绕旋转中心计算起始角度，而非图形中心
                _startAngleDeg = ComputeWorldAngle(_originalRotationCenter, point);
            }
            else
            {
                var combineAABB = selectedShapes.GetUnionAABB();
                if (combineAABB.IsEmpty) return;
                _initRotationCorners = combineAABB.ToCorners();
                _context.RealRotationCorners = _initRotationCorners.CloneEx();
                // 捕获当前旋转中心；若尚未设置（0,0），退回图形中心
                if (_context.MergedRotationCenter.X == float.PositiveInfinity || _context.MergedRotationCenter.Y == float.PositiveInfinity)
                {
                    _context.MergedRotationCenter = new SKPoint(_originalMergedBounds.MidX, _originalMergedBounds.MidY);
                    //_context.MergedRotationCenter = new SKPoint(combineAABB.MidX, combineAABB.MidY);
                }
                _originalRotationCenter = _context.MergedRotationCenter;

                // 围绕旋转中心计算起始角度，而非图形中心
                _startAngleDeg = ComputeWorldAngle(_originalRotationCenter, point);
            }

            // 设置旋转预览状态：拖拽期间图形不动，仅渲染旋转后的 OBB 和 AABB 控制点
            _context.IsRotationPreview = true;
            _pendingTransformCommand = CreateRotationCommand(selectedShapes);
            _context.ReportStatus($"开始旋转 (控制点: {controlPointType})");
        }

        public void Update(SKPoint point)
        {
            if (_context.ActiveCanvas == null || _context.ActiveCanvas.SelectedShapeCount == 0 || !IsDragging)
                return;

            if (_context.ActiveCanvas.Selection.OfType<DrawObject>().Any(o => !o.CanTransform))
                return;

            var selectedShapes = _context.ActiveCanvas!.Selection.OfType<DrawObject>()
    .Where(s => s.CanTransform).ToList();
            if (selectedShapes.Count == 0) return;

            _isUpdateing = true;

            // 围绕旋转中心计算鼠标角度增量
            float currentAngle = ComputeWorldAngle(_originalRotationCenter, point);
            float deltaAngle = currentAngle - _startAngleDeg;

            // 归一化到 [-180, 180]
            while (deltaAngle > 180f) deltaAngle -= 360f;
            while (deltaAngle < -180f) deltaAngle += 360f;

            // 仅更新旋转预览状态，不修改任何图形属性
            foreach (var shape in selectedShapes)
            {
                shape.Rotate(deltaAngle, _originalRotationCenter);
            }

            _deltaAngle = deltaAngle;

            var realRotationCorners = _initRotationCorners.CloneEx();
            var deltaMatrix = SKMatrix.CreateRotationDegrees(deltaAngle, _originalRotationCenter.X, _originalRotationCenter.Y);
            _context.RealRotationCorners = deltaMatrix.MapPoints(realRotationCorners);

            if (selectedShapes.Count() > 1)
            {
                _context.MergedRotationCenter = deltaMatrix.MapPoint(_originalRotationCenter);
            }

            _context.MarkSelectedDirty();
            _context.ReportStatus($"旋转: {deltaAngle:F1}°");
        }

        public bool Complete()
        {
            if (_context.ActiveCanvas == null || _context.ActiveCanvas.SelectedShapeCount == 0 || !IsDragging || !_isUpdateing)
                return false;

            if (_context.ActiveCanvas.Selection.OfType<DrawObject>().Any(o => !o.CanTransform))
                return false;
            if (MathF.Abs(_deltaAngle) < 0.001f) return false;

            var selectedShapes = _context.ActiveCanvas!.Selection.OfType<DrawObject>()
.Where(s => s.CanTransform).ToList();
            if (selectedShapes.Count == 0) return false;

            // 拖拽期间图形未被修改，现在调用 SetRotation 一次性应用最终旋转
            foreach (var shape in selectedShapes)
            {
                shape.Rotate(_deltaAngle, _originalRotationCenter, commit: true);
            }

            _context.IsDragControlPoint = false;
            _context.PublishTransformChange();
            _context.MarkSelectedDirty();

            if (_pendingTransformCommand != null)
            {
                _pendingTransformCommand.CaptureAfterState();
                _context.ActiveCanvas.CommandManager.Execute(_pendingTransformCommand);
            }

            ResetState();
            _context.ReportStatus("旋转完成");
            return true;
        }

        public void Cancel()
        {
            // 拖拽期间图形未被修改，直接清除预览状态
            _context.IsRotationPreview = false;
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
            _initRotationCorners = null;
            _context.RealRotationCorners = null;
            // 清除旋转预览上下文
            _context.IsRotationPreview = false;
            _suggestedCursor = null;
            _isUpdateing = false;
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

                bool canRotate = drawObject.Type != ShapeType.Point && drawObject.CanTransform;
                if (!canRotate)
                {
                    return ControlPointType.None;
                }

                ControlPointType controlPointType = _selectionControlPointService.GetControlPointAtAABB(drawObject, point);
                return controlPointType;
            }

            SKRect mergedBounds = _context.CalculateMergedBounds();
            ControlPointType mergedControlPointType = _selectionControlPointService
                .GetControlPointAtForMultipleSelection(mergedBounds, point);
            return mergedControlPointType;
        }

        private static IDeferredCommand CreateRotationCommand(IEnumerable<DrawObject> selectedShapes)
        {
            return new CommandTransform(CommandTransform.CollectWithChildren(selectedShapes), "调整旋转角度");
        }
        /// <summary>
        /// 计算世界坐标系中从 center 到 point 的角度（度）。
        /// </summary>
        private static float ComputeWorldAngle(SKPoint center, SKPoint point)
        {
            float dx = point.X - center.X;
            float dy = point.Y - center.Y;
            return (float)(Math.Atan2(dy, dx) * 180.0 / Math.PI);
        }

        /// <summary>
        /// 判断控制点是否为角点（非角点即为边中点）。
        /// </summary>
        private static bool IsCornerControlPoint(ControlPointType cp) => cp switch
        {
            ControlPointType.TopLeft or ControlPointType.TopRight
                or ControlPointType.BottomLeft or ControlPointType.BottomRight => true,
            _ => false
        };
    }
}
