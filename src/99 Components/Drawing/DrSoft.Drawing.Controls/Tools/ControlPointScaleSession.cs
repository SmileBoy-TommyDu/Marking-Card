using DrSoft.Drawing.Controls.Commands;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using Microsoft.VisualBasic;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Cursor = System.Windows.Input.Cursor;
using Cursors = System.Windows.Input.Cursors;

namespace DrSoft.Drawing.Controls.Tools
{
    internal sealed class ControlPointScaleSession : IToolSelectSession
    {
        private readonly DocumentContext _context;
        private readonly SelectionControlPointService _selectionControlPointService;

        private ControlPointType _draggingControlPoint = ControlPointType.None;
        private SKPoint _dragStartPoint = SKPoint.Empty;
        private SKRect _originalMergedBounds = SKRect.Empty;
        private bool _hasExceededDragThreshold;
        private IDeferredCommand? _pendingTransformCommand;
        // SecondSelected AABB 变形：保存拖拽开始时的原始 AABB（不受变形矩阵影响）
        private SKRect _originalAABB = SKRect.Empty;
        private SKPoint[]? _originalPreviewCorners;
        private SKRect _scalePreviewAABB = SKRect.Empty;
        private SKPoint _mergedRotationCenter;
        private const float AnchorEpsilon = 0.0001f;
        // SecondSelected AABB 变形：拖拽过程中计算的缩放比例
        private float _scaleX = 1f;
        private float _scaleY = 1f;
        // 缩放锚点（世界坐标），拖拽过程中计算，Complete 时使用。
        private float _scaleAnchorX;
        private float _scaleAnchorY;
        private Cursor? _suggestedCursor;
        private ControlPointType? _completedControlPoint;
        private bool _isUpdateing;

        public ControlPointScaleSession(DocumentContext context)
        {
            _context = context;
            _selectionControlPointService = new SelectionControlPointService(context);
        }

        public string Name => "ControlPointScale";

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
            bool isSecondSelected = _context.SelectState == SelectState.SecondSelected;
            if (!isSecondSelected)
            {
                message = "当前不是第二态黑框缩放";
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
                message = "未命中第二态控制点";
                return false;
            }

            SKRect originalMergedBounds = _context.CalculateMergedBounds();
            Start(controlPointType, point, originalMergedBounds);
            _suggestedCursor = ResolveSecondSelectedCursor(controlPointType);
            message = "开始第二态黑框缩放";
            return true;
        }

        public bool TryMouseMove(SKPoint point, out string message)
        {
            if (IsDragging && _context.IsDragControlPoint)
            {
                float viewportScale = (float)(_context.ActiveCanvas?.Viewport.Scale ?? 1.0);
                float dragThreshold = 2f / Math.Max(viewportScale, 0.001f);
                if (!_hasExceededDragThreshold)
                {
                    float dx = point.X - _dragStartPoint.X;
                    float dy = point.Y - _dragStartPoint.Y;
                    if (dx * dx + dy * dy < dragThreshold * dragThreshold)
                    {
                        message = "第二态黑框缩放等待超过拖拽阈值";
                        return true;
                    }

                    _hasExceededDragThreshold = true;
                }

                Update(point);
                message = "更新第二态黑框缩放预览";
                return true;
            }

            if (_context.ActiveCanvas == null)
            {
                message = "没有活动画布";
                return false;
            }

            bool isSecondSelected = _context.SelectState == SelectState.SecondSelected;
            if (!isSecondSelected)
            {
                message = "当前不是第二态黑框缩放";
                return false;
            }

            ControlPointType controlPointType = ResolveControlPointAt(point);
            bool hasControlPoint = controlPointType != ControlPointType.None;
            if (!hasControlPoint)
            {
                message = "未命中第二态控制点";
                return false;
            }

            _suggestedCursor = ResolveSecondSelectedCursor(controlPointType);
            message = "命中第二态控制点";
            return true;
        }

        public bool TryMouseUp(SKPoint point, out string message)
        {
            if (!IsDragging)
            {
                message = "第二态黑框缩放未处于拖拽中";
                return false;
            }

            if (!_hasExceededDragThreshold)
            {
                Cancel();
                message = "第二态黑框缩放未达到拖拽阈值，不提交缩放";
                return true;
            }

            ControlPointType draggingControlPoint = DraggingControlPoint;
            bool completed = Complete();
            if (!completed)
            {
                message = "第二态黑框缩放提交失败";
                return false;
            }

            _completedControlPoint = draggingControlPoint;
            _suggestedCursor = null;
            message = "完成第二态黑框缩放";
            return true;
        }

        public bool TryRightMouseDown(SKPoint point, out string message)
        {
            message = "第二态黑框缩放不处理右键";
            return false;
        }

        /// <summary>
        /// 开始一次控制点拖拽，并为最终缩放命令准备 before-state。
        /// </summary>
        public void Start(ControlPointType controlPointType, SKPoint point, SKRect originalMergedBounds)
        {
            if (_context.SelectState != SelectState.SecondSelected)
                return;

            if (_context.ActiveCanvas == null || _context.ActiveCanvas.SelectedShapeCount <= 0)
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
            _hasExceededDragThreshold = false;
            _originalMergedBounds = originalMergedBounds;
            _context.IsDragControlPoint = true;

            if (selectedShapes.Count() == 1)
            {
                var drawObject = selectedShapes.FirstOrDefault();
                if (drawObject == null) return;

                _originalAABB = drawObject.GetAABB();
                (_scaleAnchorX, _scaleAnchorY) = CalculateAnchor();
                _originalPreviewCorners = drawObject.GetOBB().Corners;
            }
            else
            {
                _originalAABB = selectedShapes.GetUnionAABB();
                _originalPreviewCorners = _originalAABB.ToCorners();
                _scalePreviewAABB = new SKRect(_originalAABB.Left, _originalAABB.Top, _originalAABB.Right, _originalAABB.Bottom);
                (_scaleAnchorX, _scaleAnchorY) = CalculateAnchor();
                _mergedRotationCenter = _context.MergedRotationCenter;
            }

            _context.IsScalePreview = true;
            _context.RealScaleOBBCorners = _originalPreviewCorners.CloneEx();
            _pendingTransformCommand = CreateScaleCommand(selectedShapes);
            _context.ReportStatus($"开始剪切 (控制点: {controlPointType})");
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

            float scaleX = 1f;
            float scaleY = 1f;

            var origAABB = _originalAABB;
            float origAABBW = origAABB.Width;
            float origAABBH = origAABB.Height;
            if (origAABBW < 0.001f || origAABBH < 0.001f) return;

            switch (_draggingControlPoint)
            {
                case ControlPointType.MiddleRight:
                    scaleX = (point.X - origAABB.Left) / origAABBW;
                    break;
                case ControlPointType.MiddleLeft:
                    scaleX = (origAABB.Right - point.X) / origAABBW;
                    break;
                case ControlPointType.TopCenter:
                    scaleY = (point.Y - origAABB.Top) / origAABBH;
                    break;
                case ControlPointType.BottomCenter:
                    scaleY = (origAABB.Bottom - point.Y) / origAABBH;
                    break;
                case ControlPointType.TopRight:
                    scaleX = (point.X - origAABB.Left) / origAABBW;
                    scaleY = (point.Y - origAABB.Top) / origAABBH;
                    break;
                case ControlPointType.TopLeft:
                    scaleX = (origAABB.Right - point.X) / origAABBW;
                    scaleY = (point.Y - origAABB.Top) / origAABBH;
                    break;
                case ControlPointType.BottomRight:
                    scaleX = (point.X - origAABB.Left) / origAABBW;
                    scaleY = (origAABB.Bottom - point.Y) / origAABBH;
                    break;
                case ControlPointType.BottomLeft:
                    scaleX = (origAABB.Right - point.X) / origAABBW;
                    scaleY = (origAABB.Bottom - point.Y) / origAABBH;
                    break;
                default:

                    break;
            }

            float minScaleX = DrawObject.MinDimension / origAABBW;
            float minScaleY = DrawObject.MinDimension / origAABBH;
            scaleX = Math.Max(scaleX, minScaleX);
            scaleY = Math.Max(scaleY, minScaleY);

            if (IsCornerControlPoint(_draggingControlPoint))
            {
                float uniform = Math.Max(scaleX, scaleY);
                scaleX = uniform;
                scaleY = uniform;
            }

            _scaleX = scaleX;
            _scaleY = scaleY;

            foreach (var shape in selectedShapes)
            {
                if (shape is DrawDot dot)
                {
                    // 点必须保持圆形（不缩放尺寸），仅按缩放公式等比移动中心：
                    // newCenter = anchor + (oldCenter - anchor) * scale。
                    // 锚点侧的点位移为 0（如右拖时左侧锚点处的点不动），越远离锚点位移越大。
                    // 点包围圈为 0 时，若点正好落在锚点处（某轴距离≈0），该轴保持不动，避免浮点漂移。
                    var oldCenter = shape.GetAABB2().Center;
                    float distX = oldCenter.X - _scaleAnchorX;
                    float distY = oldCenter.Y - _scaleAnchorY;
                    dot.IsAnchorX = MathF.Abs(distX) < AnchorEpsilon;
                    dot.IsAnchorY = MathF.Abs(distY) < AnchorEpsilon;
                }

                shape.Scale(_scaleX, _scaleY, new SKPoint(_scaleAnchorX, _scaleAnchorY));
            }

            var realScaleOBBCorners = _originalPreviewCorners.CloneEx();
            var deltaMatrix = SKMatrix.CreateScale(scaleX, scaleY, _scaleAnchorX, _scaleAnchorY);
            _context.RealScaleOBBCorners = deltaMatrix.MapPoints(realScaleOBBCorners);

            if (selectedShapes.Count() > 1)
            {
                _context.MergedRotationCenter = deltaMatrix.MapPoint(_mergedRotationCenter);
                _context.RealScalePreviewAABB = deltaMatrix.MapRect(_scalePreviewAABB);
            }

            _context.MarkSelectedDirty();
            _context.ReportStatus($"AABB缩放: scale=({_scaleX:F2},{_scaleY:F2})");
        }

        public bool Complete()
        {
            if (_context.ActiveCanvas == null || _context.ActiveCanvas.SelectedShapeCount == 0 || !IsDragging || !_isUpdateing)
                return false;

            if (_context.ActiveCanvas.Selection.OfType<DrawObject>().Any(o => !o.CanTransform))
                return false;
            var selectedShapes = _context.ActiveCanvas!.Selection.OfType<DrawObject>().Where(s => s.CanTransform).ToList();
            if (selectedShapes.Count == 0) return false;

            foreach (var shape in selectedShapes)
            {
                if (shape is DrawDot dot)
                {
                    //// 点必须保持圆形（不缩放尺寸），仅按缩放公式等比移动中心：
                    //// newCenter = anchor + (oldCenter - anchor) * scale。
                    //// 锚点侧的点位移为 0（如右拖时左侧锚点处的点不动），越远离锚点位移越大。
                    //// 点包围圈为 0 时，若点正好落在锚点处（某轴距离≈0），该轴保持不动，避免浮点漂移。
                    //var oldCenter = shape.GetAABB2().Center;
                    //float distX = oldCenter.X - _scaleAnchorX;
                    //float distY = oldCenter.Y - _scaleAnchorY;
                    //dot.IsAnchorX = MathF.Abs(distX) < AnchorEpsilon;
                    //dot.IsAnchorY = MathF.Abs(distY) < AnchorEpsilon;

                    dot.IsAnchorX = false;
                    dot.IsAnchorY = false;
                }

                shape.Scale(_scaleX, _scaleY, new SKPoint(_scaleAnchorX, _scaleAnchorY), commit: true);
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
            _context.ReportStatus("调整缩放完成");
            return true;
        }

        public void Cancel()
        {
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
            _hasExceededDragThreshold = false;
            _originalAABB = SKRect.Empty;
            _scaleX = 1f;
            _scaleY = 1f;
            _pendingTransformCommand = null;
            _originalPreviewCorners = null;
            _scaleAnchorX = 0;
            _scaleAnchorY = 0;
            _context.RealScaleOBBCorners = null;
            _suggestedCursor = null;
            _isUpdateing = false;
            _scalePreviewAABB = SKRect.Empty;
            _context.IsScalePreview = false;
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

                bool canScale = drawObject.Type != ShapeType.Point && drawObject.CanTransform;
                if (!canScale)
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

        private static Cursor ResolveSecondSelectedCursor(ControlPointType controlPointType)
        {
            Cursor cursor = controlPointType switch
            {
                ControlPointType.TopLeft or ControlPointType.BottomRight => Cursors.SizeNWSE,
                ControlPointType.TopRight or ControlPointType.BottomLeft => Cursors.SizeNESW,
                ControlPointType.TopCenter or ControlPointType.BottomCenter => Cursors.SizeNS,
                ControlPointType.MiddleLeft or ControlPointType.MiddleRight => Cursors.SizeWE,
                _ => Cursors.Arrow
            };
            return cursor;
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

        private static IDeferredCommand CreateScaleCommand(IEnumerable<DrawObject> selectedShapes)
        {
            var list = selectedShapes as IList<DrawObject> ?? selectedShapes.ToList();
            return new CommandTransform(CommandTransform.CollectWithChildren(list), "调整大小");
        }

        private (float anchorX, float anchorY) CalculateAnchor()
        {
            var origAABB = _originalAABB;
            SKPoint scaleCenter = SKPoint.Empty;
            switch (_draggingControlPoint)
            {
                case ControlPointType.MiddleRight:
                    scaleCenter = new SKPoint(origAABB.Left, origAABB.MidY);
                    //scaleCenter = _context.ActiveCanvas.AllShapes.OfType<DrawDot>().FirstOrDefault().Matrix.MapPoint(new SKPoint(0, 0));
                    break;
                case ControlPointType.MiddleLeft:
                    scaleCenter = new SKPoint(origAABB.Right, origAABB.MidY);
                    break;
                case ControlPointType.TopCenter:
                    scaleCenter = new SKPoint(origAABB.MidX, origAABB.Top);
                    break;
                case ControlPointType.BottomCenter:
                    scaleCenter = new SKPoint(origAABB.MidX, origAABB.Bottom);
                    break;
                case ControlPointType.TopRight:
                    scaleCenter = new SKPoint(origAABB.Left, origAABB.Top);
                    break;
                case ControlPointType.TopLeft:
                    scaleCenter = new SKPoint(origAABB.Right, origAABB.Top);
                    break;
                case ControlPointType.BottomRight:
                    scaleCenter = new SKPoint(origAABB.Left, origAABB.Bottom);
                    break;
                case ControlPointType.BottomLeft:
                    scaleCenter = new SKPoint(origAABB.Right, origAABB.Bottom);
                    break;
                default:
                    scaleCenter = new SKPoint(origAABB.MidX, origAABB.MidY);
                    break;
            }

            return (scaleCenter.X, scaleCenter.Y);
        }
    }
}
