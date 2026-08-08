using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Model;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.Tools
{
    public class ToolLine : ToolBase
    {
        public override ToolType ToolType => ToolType.Line;
        public override string Name => "多线段";
        public override string Icon => "━";
        public List<SKPoint> currentCurvePoints = new();

        DocumentContext context = DocumentContext.Instance;

        /// <summary>
        /// 吸附判定半径（世界坐标），鼠标距离起始点小于此值时显示吸附框。
        /// 该值会根据视口缩放比例动态计算，保持屏幕上的视觉大小一致。
        /// </summary>
        private const float SnapScreenRadius = 8f; // 屏幕像素半径

        public override bool OnMouseDown(SKPoint point)
        {
            if (context.ActiveCanvas == null)
            {
                context.ReportStatus("错误：没有激活的画布");
                return false;
            }

            // 检测是否吸附到起始点（首尾闭合）
            if (currentCurvePoints.Count >= 2 && IsNearStartPoint(point))
            {
                // 首尾吸附闭合：不添加新点，直接闭合多段线
                FinishLineAsClosed();
                return true;
            }

            var resolvedPoint = ResolveLinePoint(point);

            // 如果是第一次点击，创建 DrawPolyLines
            if (context.CurrentShape == null)
            {
                currentCurvePoints.Clear();
                currentCurvePoints.Add(resolvedPoint);
                context.CurrentShape = new DrawPolyLines(new List<SKPoint>(currentCurvePoints));
                context.MarkDirty(new SKRect(resolvedPoint.X - 1, resolvedPoint.Y - 1, resolvedPoint.X + 1, resolvedPoint.Y + 1));
                context.IsDrawing = true;
                context.ReportStatus("开始绘制多线段，继续点击添加点，右键完成，靠近起点自动闭合");
            }
            else
            {
                // 添加新点到当前多线段
                currentCurvePoints.Add(resolvedPoint);
                context.CurrentShape.Points = new List<SKPoint>(currentCurvePoints);
                context.MarkDirty(new SKRect(resolvedPoint.X - 1, resolvedPoint.Y - 1, resolvedPoint.X + 1, resolvedPoint.Y + 1));
                context.ReportStatus($"已添加 {currentCurvePoints.Count} 个点，继续点击或右键完成");
            }

            return true;
        }

        public override void OnMouseMove(SKPoint point)
        {
            if (!context.IsDrawing || context.CurrentShape == null) return;

            // 更新预览点（最后一个点是当前鼠标位置）
            if (currentCurvePoints.Count > 0)
            {
                // 检测是否靠近起始点，更新吸附状态
                bool isNearStart = currentCurvePoints.Count >= 2 && IsNearStartPoint(point);
                context.IsSnapToStart = isNearStart;

                if (isNearStart)
                {
                    context.SnapStartPoint = currentCurvePoints[0];
                    // 吸附时，预览线段末端直接连到起始点
                    var allPoints = new List<SKPoint>(currentCurvePoints);
                    allPoints.Add(currentCurvePoints[0]); // 预览闭合
                    context.CurrentShape.Points = allPoints;
                }
                else
                {
                    var resolvedPoint = ResolveLinePoint(point);
                    var allPoints = new List<SKPoint>(currentCurvePoints);
                    allPoints.Add(resolvedPoint); // 添加当前鼠标位置作为预览点
                    context.CurrentShape.Points = allPoints;
                }
            }

            base.OnMouseMove(point);
        }

        public override bool OnMouseUp(SKPoint point)
        {
            return true;
        }

        public override bool OnMouseRightDown()
        {
            FinishLine();
            return true;
        }

        public override void OnCancel()
        {
            context.IsSnapToStart = false;
            base.OnCancel();
        }

        private SKPoint ResolveLinePoint(SKPoint point)
        {
            bool hasAnchorPoint = currentCurvePoints.Count > 0;
            bool shouldSnapAngle = context.IsShiftPressed();
            if (!hasAnchorPoint || !shouldSnapAngle)
            {
                return point;
            }

            var anchorPointIndex = currentCurvePoints.Count - 1;
            var anchorPoint = currentCurvePoints[anchorPointIndex];

            float dx = point.X - anchorPoint.X;
            float dy = point.Y - anchorPoint.Y;

            bool isStationary = Math.Abs(dx) < float.Epsilon && Math.Abs(dy) < float.Epsilon;
            if (isStationary)
            {
                return point;
            }

            // Shift 吸附采用“最近 45° 方向”语义：
            // 1. 先把鼠标向量换算成极角；
            // 2. 把角度量化到 45° 的整数倍；
            // 3. 再把原始向量投影到这个量化后的方向上。
            // 这样可以保持“离哪条约束方向最近，就吸到哪条方向”的连续手感，
            // 同时保证预览点和最终落点使用同一套几何规则。
            float angle = MathF.Atan2(dy, dx);
            float step = MathF.PI / 4f;
            float snappedAngle = MathF.Round(angle / step) * step;

            float directionX = MathF.Cos(snappedAngle);
            float directionY = MathF.Sin(snappedAngle);

            // 投影长度使用原始鼠标向量在目标方向上的标量投影，
            // 避免仅仅改角度却保留错误的轴向长度。
            float projectedLength = dx * directionX + dy * directionY;

            float snappedX = anchorPoint.X + projectedLength * directionX;
            float snappedY = anchorPoint.Y + projectedLength * directionY;

            var resolvedPoint = new SKPoint(snappedX, snappedY);

            return resolvedPoint;
        }

        /// <summary>
        /// 判断当前鼠标位置是否靠近多段线的起始点。
        /// 吸附半径随视口缩放调整，保持屏幕上的视觉大小一致。
        /// </summary>
        private bool IsNearStartPoint(SKPoint currentPoint)
        {
            if (currentCurvePoints.Count < 2) return false;

            var startPoint = currentCurvePoints[0];
            float dx = currentPoint.X - startPoint.X;
            float dy = currentPoint.Y - startPoint.Y;
            float distance = MathF.Sqrt(dx * dx + dy * dy);

            // 根据视口缩放比例将屏幕像素半径转换为世界坐标半径
            float scale = (float)(context.ActiveCanvas?.Viewport.Scale ?? 1.0);
            float worldRadius = SnapScreenRadius / scale;

            return distance <= worldRadius;
        }

        /// <summary>
        /// 首尾吸附闭合完成多段线绘制。
        /// 设置 IsClosed=true 并完成绘制。
        /// </summary>
        private void FinishLineAsClosed()
        {
            context.IsSnapToStart = false;

            if (context.CurrentShape != null && currentCurvePoints.Count >= 2)
            {
                // 完成时移除预览点，只保存已确认的点
                context.CurrentShape.Points = new List<SKPoint>(currentCurvePoints);

                if (context.CurrentShape is DrawPolyLines polyLine)
                {
                    polyLine.IsClosed = true;
                    polyLine.UpdateSetProperty(new List<SKPoint>(currentCurvePoints));
                }

                context.ReportStatus($"多线段绘制完成（首尾闭合），共 {context.CurrentShape.Points.Count} 个点");
                currentCurvePoints.Clear();
                FinishDrawing();
            }
            else
            {
                OnCancel();
                currentCurvePoints.Clear();
                context.ReportStatus("线段点太少，已取消");
            }
        }
        
        public void FinishLine()
        {
            context.IsSnapToStart = false;

            if (context.CurrentShape != null && currentCurvePoints.Count >= 2)
            {
                // 完成时移除预览点，只保存已确认的点
                context.CurrentShape.Points = new List<SKPoint>(currentCurvePoints);

                if (context.CurrentShape is DrawPolyLines polyLine)
                {
                    polyLine.UpdateSetProperty(new List<SKPoint> (currentCurvePoints));
                }

                context.ReportStatus($"多线段绘制完成，共 {context.CurrentShape.Points.Count} 个点");
                currentCurvePoints.Clear();
                FinishDrawing();
            }
            else
            {
                OnCancel();
                currentCurvePoints.Clear();
                context.ReportStatus("线段点太少，已取消");
            }
        }
    }
}
