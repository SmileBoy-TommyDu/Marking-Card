using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DrSoft.Drawing.Controls.Commands;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Model;
using SkiaSharp;
using System.Windows.Input;
using Cursor = System.Windows.Input.Cursor;
using Cursors = System.Windows.Input.Cursors;

namespace DrSoft.Drawing.Controls.Tools
{
    internal sealed class ControlPointSkewSession : IToolSelectSession
    {
        private readonly DocumentContext _context;
        private readonly SelectionControlPointService _selectionControlPointService;
        private ControlPointType _draggingControlPoint = ControlPointType.None;
        private SKPoint _dragStartPoint = SKPoint.Empty;
        private IDeferredCommand? _pendingTransformCommand;
        private SKPoint _mergedRotationCenter;
        private SKRect _skewAABB = SKRect.Empty;
        private SKPoint[]? _initSkewCorners;
        private SKRect _skewPreviewAABB = SKRect.Empty;
        private Cursor? _suggestedCursor;
        private ControlPointType? _completedControlPoint;
        private float _skewAnchorX = 0, _skewAnchorY = 0;
        private float _deltaTanX = 0, _deltaTanY = 0;
        private bool _isUpdateing;

        public ControlPointSkewSession(DocumentContext context)
        {
            _context = context;
            _selectionControlPointService = new SelectionControlPointService(context);
        }

        public string Name => "ControlPointSkew";

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
                message = "当前不是第三态剪切";
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
            bool isEdgeControlPoint = IsEdgeControlPoint(controlPointType);
            if (!isEdgeControlPoint)
            {
                message = "未命中第三态边控制点";
                return false;
            }

            SKRect originalMergedBounds = _context.CalculateMergedBounds();
            Start(controlPointType, point, originalMergedBounds);
            _suggestedCursor = ResolveThirdSelectedEdgeCursor(controlPointType);
            message = "开始第三态剪切";
            return true;
        }

        public bool TryMouseMove(SKPoint point, out string message)
        {
            if (IsDragging && _context.IsDragControlPoint)
            {
                Update(point);
                message = "更新第三态剪切预览";
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
                message = "当前不是第三态剪切";
                return false;
            }

            ControlPointType controlPointType = ResolveControlPointAt(point);
            bool isEdgeControlPoint = IsEdgeControlPoint(controlPointType);
            if (!isEdgeControlPoint)
            {
                message = "未命中第三态边控制点";
                return false;
            }

            _suggestedCursor = ResolveThirdSelectedEdgeCursor(controlPointType);
            message = "命中第三态边控制点";
            return true;
        }

        public bool TryMouseUp(SKPoint point, out string message)
        {
            if (!IsDragging)
            {
                message = "第三态剪切未处于拖拽中";
                return false;
            }

            ControlPointType draggingControlPoint = DraggingControlPoint;
            bool completed = Complete();
            if (!completed)
            {
                message = "第三态剪切提交失败";
                return false;
            }

            _completedControlPoint = draggingControlPoint;
            _suggestedCursor = null;
            message = "完成第三态剪切";
            return true;
        }

        public bool TryRightMouseDown(SKPoint point, out string message)
        {
            message = "第三态剪切不处理右键";
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
            _context.IsDragControlPoint = true;

            if (selectedShapes.Count() == 1)
            {
                var firstShape = selectedShapes.FirstOrDefault();
                if (firstShape != null)
                {
                    _skewAABB = firstShape.GetAABB2().Corners.ToRect();
                    _initSkewCorners = firstShape.GetOBB().Corners;

                    //(_skewAnchorX, _skewAnchorY) = CalculateAnchor(firstShape);
                    (_skewAnchorX, _skewAnchorY) = GetEdgeIntersection(new List<DrawObject>() { firstShape }, _skewAABB, _draggingControlPoint);

                    _skewPreviewAABB = new SKRect(_skewAABB.Left, _skewAABB.Top, _skewAABB.Right, _skewAABB.Bottom);
                    _context.RealSkewPreviewAABB = _skewPreviewAABB;
                    _context.RealSkewOBBCorners = _initSkewCorners.CloneEx();
                }
            }
            else
            {
                // 计算合并 AABB，用于后续计算倾斜角度
                _skewAABB = selectedShapes.GetUnionAABB();
                _initSkewCorners = _skewAABB.ToCorners();
                //(_skewAnchorX, _skewAnchorY) = CalculateMergedAnchor(_context.ActiveCanvas.SelectedShapes.OfType<DrawObject>().ToList());
                (_skewAnchorX, _skewAnchorY) = GetEdgeIntersection(selectedShapes.ToList(), _skewAABB, _draggingControlPoint);
                _skewPreviewAABB = new SKRect(_skewAABB.Left, _skewAABB.Top, _skewAABB.Right, _skewAABB.Bottom);
                _context.RealSkewPreviewAABB = _skewPreviewAABB;
                _context.RealSkewOBBCorners = _initSkewCorners.CloneEx();
                _mergedRotationCenter = _context.MergedRotationCenter;
            }

            _context.IsSkewPreview = true;
            _pendingTransformCommand = CreateSkewCommand(selectedShapes);

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

            float height = _skewAABB.Height > 0.001f ? _skewAABB.Height : 1f;
            float width = _skewAABB.Width > 0.001f ? _skewAABB.Width : 1f;

            float deltaPreX = point.X - _dragStartPoint.X;
            float deltaPreY = point.Y - _dragStartPoint.Y;

            // ── Step 3: 在 pre-skew 空间计算新倾斜因子 ──
            float deltaTanX = 0;
            float deltaTanY = 0;
            switch (_draggingControlPoint)
            {
                case ControlPointType.TopCenter:
                    deltaTanX = 2f * deltaPreX / height;
                    if (deltaPreX >= 0)
                    {
                        _skewPreviewAABB.Left = _skewAABB.Left;
                        _skewPreviewAABB.Right = _skewPreviewAABB.Left + 2f * (point.X - _skewPreviewAABB.Left);
                    }
                    else
                    {
                        _skewPreviewAABB.Left = _skewPreviewAABB.Right + 2f * (point.X - _skewPreviewAABB.Right);
                        _skewPreviewAABB.Right = _skewAABB.Right;
                    }
                    break;
                case ControlPointType.BottomCenter:
                    deltaTanX = -2f * deltaPreX / height;
                    if (deltaPreX >= 0)
                    {
                        _skewPreviewAABB.Left = _skewAABB.Left;
                        _skewPreviewAABB.Right = _skewPreviewAABB.Left + 2f * (point.X - _skewPreviewAABB.Left);
                    }
                    else
                    {
                        _skewPreviewAABB.Left = _skewPreviewAABB.Right + 2f * (point.X - _skewPreviewAABB.Right);
                        _skewPreviewAABB.Right = _skewAABB.Right;
                    }
                    break;
                case ControlPointType.MiddleLeft:
                    deltaTanY = -2f * deltaPreY / width;
                    if (deltaPreY >= 0)
                    {
                        _skewPreviewAABB.Top = _skewAABB.Top;
                        _skewPreviewAABB.Bottom = _skewPreviewAABB.Top + 2f * (point.Y - _skewPreviewAABB.Top);
                    }
                    else
                    {
                        _skewPreviewAABB.Top = _skewPreviewAABB.Bottom + 2f * (point.Y - _skewPreviewAABB.Bottom);
                        _skewPreviewAABB.Bottom = _skewAABB.Bottom;
                    }
                    break;
                case ControlPointType.MiddleRight:
                    deltaTanY = 2f * deltaPreY / width;
                    if (deltaPreY >= 0)
                    {
                        _skewPreviewAABB.Top = _skewAABB.Top;
                        _skewPreviewAABB.Bottom = _skewPreviewAABB.Top + 2f * (point.Y - _skewPreviewAABB.Top);
                    }
                    else
                    {
                        _skewPreviewAABB.Top = _skewPreviewAABB.Bottom + 2f * (point.Y - _skewPreviewAABB.Bottom);
                        _skewPreviewAABB.Bottom = _skewAABB.Bottom;
                    }
                    break;
            }

            _deltaTanX = deltaTanX;
            _deltaTanY = deltaTanY;

            _context.RealSkewPreviewAABB = _skewPreviewAABB;

            foreach (var shape in selectedShapes)
            {
                shape.Skew(deltaTanX, deltaTanY, new SKPoint(_skewAnchorX, _skewAnchorY));
            }

            var realSkewOBBCorners = _initSkewCorners.CloneEx();
            var deltaMatrix = SKMatrix.CreateTranslation(-_skewAnchorX, -_skewAnchorY);
            deltaMatrix = deltaMatrix.PostConcat(SKMatrix.CreateSkew(deltaTanX, deltaTanY));
            deltaMatrix = deltaMatrix.PostConcat(SKMatrix.CreateTranslation(_skewAnchorX, _skewAnchorY));
            _context.RealSkewOBBCorners = deltaMatrix.MapPoints(realSkewOBBCorners);

            if (selectedShapes.Count() > 1)
            {
                _context.MergedRotationCenter = deltaMatrix.MapPoint(_mergedRotationCenter);
            }

            _context.MarkSelectedDirty();
            _context.ReportStatus($"剪切: tanX={deltaTanX:F3}, tanY={deltaTanY:F3}");
        }

        public bool Complete()
        {
            if (_context.ActiveCanvas == null || _context.ActiveCanvas.SelectedShapeCount == 0 || !IsDragging || !_isUpdateing)
                return false;

            if (_context.ActiveCanvas.Selection.OfType<DrawObject>().Any(o => !o.CanTransform))
                return false;

            if (MathF.Abs(_deltaTanX) < 0.001f && MathF.Abs(_deltaTanY) < 0.001f)
                return false;

            var selectedShapes = _context.ActiveCanvas!.Selection.OfType<DrawObject>()
.Where(s => s.CanTransform).ToList();
            if (selectedShapes.Count == 0) return false;

            foreach (var shape in selectedShapes)
            {
                shape.Skew(_deltaTanX, _deltaTanY, new SKPoint(_skewAnchorX, _skewAnchorY), commit: true);
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
            _context.ReportStatus("调整剪切完成");
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
            _pendingTransformCommand = null;
            _skewAABB = SKRect.Empty;
            _initSkewCorners = null;

            // 清除多选倾斜预览状态
            _context.IsSkewPreview = false;
            _context.RealSkewPreviewAABB = SKRect.Empty;
            _context.RealSkewOBBCorners = null;
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

                bool canSkew = drawObject.Type != ShapeType.Point && drawObject.CanTransform;
                if (!canSkew)
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

        private static bool IsEdgeControlPoint(ControlPointType controlPointType)
        {
            bool isEdgeControlPoint = controlPointType == ControlPointType.TopCenter
                || controlPointType == ControlPointType.BottomCenter
                || controlPointType == ControlPointType.MiddleLeft
                || controlPointType == ControlPointType.MiddleRight;
            return isEdgeControlPoint;
        }

        private static Cursor ResolveThirdSelectedEdgeCursor(ControlPointType controlPointType)
        {
            Cursor cursor = controlPointType switch
            {
                ControlPointType.TopCenter or ControlPointType.BottomCenter => Cursors.SizeWE,
                ControlPointType.MiddleLeft or ControlPointType.MiddleRight => Cursors.SizeNS,
                _ => Cursors.Arrow
            };
            return cursor;
        }

        /// <summary>
        /// 计算单选倾斜的锚点：将图形局部四角变换到世界坐标，取 AABB 方向极值角点。
        /// 对矩形/平行四边形等凸多边形完全精确，比 AABB 边中点更符合视觉。
        /// TopCenter/BottomCenter 使用同侧极值点，MiddleLeft/MiddleRight 使用对边极值点。
        /// </summary>
        private (float x, float y) CalculateAnchor(DrawObject drawObject)
        {
            var aabb = drawObject.GetAABB2().Corners.ToRect();
            var obb = drawObject.GetOBB().Corners;
            float halfW = aabb.Width / 2f;
            float halfH = aabb.Height / 2f;

            // 局部四角 → 世界坐标
            var matrix = drawObject.Matrix;
            var tl = matrix.MapPoint(new SKPoint(-halfW, -halfH)); // 左上
            var tr = matrix.MapPoint(new SKPoint(halfW, -halfH)); // 右上
            var br = matrix.MapPoint(new SKPoint(halfW, halfH)); // 右下
            var bl = matrix.MapPoint(new SKPoint(-halfW, halfH)); // 左下

            //switch (_draggingControlPoint)
            //{
            //    case ControlPointType.TopCenter:
            //        // 顶边极值点（同侧）：最小 Y 的角点
            //        return MinY(tl, tr, bl, br);
            //    case ControlPointType.BottomCenter:
            //        // 底边极值点（同侧）：最大 Y 的角点
            //        return MaxY(tl, tr, bl, br);
            //    case ControlPointType.MiddleLeft:
            //        // 右边极值点（对边）：最大 X 的角点
            //        return MaxX(tl, tr, bl, br);
            //    case ControlPointType.MiddleRight:
            //        // 左边极值点（对边）：最小 X 的角点
            //        return MinX(tl, tr, bl, br);
            //    default:
            //        return (drawObject.SharpCenter.X, drawObject.SharpCenter.Y);
            //}

            switch (_draggingControlPoint)
            {
                case ControlPointType.TopCenter:
                    // 顶边极值点（同侧）：最小 Y 的角点
                    return MinY(obb);
                case ControlPointType.BottomCenter:
                    // 底边极值点（同侧）：最大 Y 的角点
                    return MaxY(obb);
                case ControlPointType.MiddleLeft:
                    // 右边极值点（对边）：最大 X 的角点
                    return MaxX(obb);
                case ControlPointType.MiddleRight:
                    // 左边极值点（对边）：最小 X 的角点
                    return MinX(obb);
                default:
                    return (drawObject.SharpCenter.X, drawObject.SharpCenter.Y);
            }
        }
        private static (float, float) MinY(params SKPoint[] pts)
        {
            var best = pts[0];
            for (int i = 1; i < pts.Length; i++)
                if (pts[i].Y < best.Y) best = pts[i];
            return (best.X, best.Y);
        }
        private static (float, float) MaxY(params SKPoint[] pts)
        {
            var best = pts[0];
            for (int i = 1; i < pts.Length; i++)
                if (pts[i].Y > best.Y) best = pts[i];
            return (best.X, best.Y);
        }
        private static (float, float) MinX(params SKPoint[] pts)
        {
            var best = pts[0];
            for (int i = 1; i < pts.Length; i++)
                if (pts[i].X < best.X) best = pts[i];
            return (best.X, best.Y);
        }
        private static (float, float) MaxX(params SKPoint[] pts)
        {
            var best = pts[0];
            for (int i = 1; i < pts.Length; i++)
                if (pts[i].X > best.X) best = pts[i];
            return (best.X, best.Y);
        }
        /// <summary>
        /// 计算多选倾斜的合并锚点：对每个图形计算其 OBB 对边中点，取平均值。
        /// 与单选 CalculateAnchor 逻辑一致，确保锚点始终在图形实际边上。
        /// </summary>
        private (float x, float y) CalculateMergedAnchor(List<DrawObject> allShapes)
        {
            float X = 0, Y = 0;
            List<(float x, float y)> anchorList = new List<(float x, float y)>();

            foreach (var shape in allShapes)
            {
                var anchor = CalculateAnchor(shape);
                Debug.WriteLine($"_draggingControlPoint:{_draggingControlPoint}，坐标: ({anchor.x}, {anchor.y})");
                anchorList.Add(anchor);
            }

            switch (_draggingControlPoint)
            {
                case ControlPointType.TopCenter:
                    // 顶边极值点（同侧）：最小 Y 的角点
                    foreach (var anchorItem in anchorList)
                    {
                        if (anchorItem.y == anchorList.Min(a => a.y))
                        {
                            X = anchorItem.x;
                            Y = anchorItem.y;
                        }
                    }
                    break;
                case ControlPointType.BottomCenter:
                    // 底边极值点（同侧）：最大 Y 的角点
                    foreach (var anchorItem in anchorList)
                    {
                        if (anchorItem.y == anchorList.Max(a => a.y))
                        {
                            X = anchorItem.x;
                            Y = anchorItem.y;
                        }
                    }
                    break;
                case ControlPointType.MiddleLeft:
                    // 右边极值点（对边）：最大 X 的角点
                    foreach (var anchorItem in anchorList)
                    {
                        if (anchorItem.x == anchorList.Min(a => a.x))
                        {
                            X = anchorItem.x;
                            Y = anchorItem.y;
                        }
                    }
                    break;
                case ControlPointType.MiddleRight:
                    // 左边极值点（对边）：最小 X 的角点
                    foreach (var anchorItem in anchorList)
                    {
                        if (anchorItem.x == anchorList.Max(a => a.x))
                        {
                            X = anchorItem.x;
                            Y = anchorItem.y;
                        }
                    }
                    break;
                default:
                    X = _skewAABB.MidX;
                    Y = _skewAABB.MidY;
                    break;
            }

            Debug.WriteLine($"_draggingControlPoint:{_draggingControlPoint}，合并锚点坐标: ({X}, {Y})");
            return (X, Y);
        }

        private (float x, float y) GetEdgeIntersection(List<DrawObject> drawObjects, SKRect aabb, ControlPointType draggingControlPoint)
        {
            float skewAnchorX = 0, skewAnchorY = 0;
            //var intersections = AABBHelper.ComputeEdgeIntersections(
            //     drawObjects, aabb);

            var intersections = EdgeIntersectionHelper.GetExtremePoints(drawObjects);

            switch (draggingControlPoint)
            {
                case ControlPointType.TopCenter:
                    {
                        // 锚点在底边：取图形轮廓与底边的交点
                        var edge = intersections.Top;
                        skewAnchorX = edge.SinglePoint.HasValue
                            ? edge.SinglePoint.Value.X
                            : edge.OverlapPoints != null
                                ? edge.OverlapPoints[1].X
                                : aabb.MidX;
                        skewAnchorY = aabb.Top;
                        break;
                    }
                case ControlPointType.BottomCenter:
                    {
                        // 锚点在顶边：取图形轮廓与顶边的交点
                        var edge = intersections.Bottom;
                        skewAnchorX = edge.SinglePoint.HasValue
                            ? edge.SinglePoint.Value.X
                            : edge.OverlapPoints != null
                                ? edge.OverlapPoints[1].X   // 重合段中点
                                : aabb.MidX;                // 无交点回退到边中点
                        skewAnchorY = aabb.Bottom;
                        break;
                    }
                case ControlPointType.MiddleLeft:
                    {
                        // 锚点在右边：取图形轮廓与右边的交点
                        var edge = intersections.Right;
                        skewAnchorX = aabb.Right;
                        skewAnchorY = edge.SinglePoint.HasValue
                            ? edge.SinglePoint.Value.Y
                            : edge.OverlapPoints != null
                                ? edge.OverlapPoints[1].Y   // 重合段中点
                                : aabb.MidY;                // 无交点回退到边中点
                        break;
                    }
                case ControlPointType.MiddleRight:
                    {
                        // 锚点在左边：取图形轮廓与左边的交点
                        var edge = intersections.Left;
                        skewAnchorX = aabb.Left;
                        skewAnchorY = edge.SinglePoint.HasValue
                            ? edge.SinglePoint.Value.Y
                            : edge.OverlapPoints != null
                                ? edge.OverlapPoints[1].Y
                                : aabb.MidY;
                        break;
                    }
            }

            return (skewAnchorX, skewAnchorY);
        }

        private static IDeferredCommand CreateSkewCommand(IEnumerable<DrawObject> selectedShapes)
        {
            var list = selectedShapes as IList<DrawObject> ?? selectedShapes.ToList();
            return new CommandTransform(CommandTransform.CollectWithChildren(list), "调整倾斜角度");
        }
    }
}
