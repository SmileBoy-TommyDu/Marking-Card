using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Models;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.Tools
{
    public class ToolBezier : ToolBase
    {
        public override ToolType ToolType => ToolType.Bezier;
        public override string Name => "贝塞尔曲线";
        public override string Icon => "〰️";

        /// <summary>
        /// 吸附判定半径（屏幕像素），鼠标距离起始点小于此值时显示吸附框。
        /// 该值会根据视口缩放比例动态计算，保持屏幕上的视觉大小一致。
        /// </summary>
        private const float SnapScreenRadius = 8f;

        private List<SKPoint> _anchors = new List<SKPoint>();
        private SKPoint _cursor = SKPoint.Empty;

        DocumentContext context = DocumentContext.Instance;

        public SKPoint Cursor => _cursor;
        public bool CanFinish => _anchors.Count >= 2;

        public override bool OnMouseDown(SKPoint point)
        {
            // 检测是否吸附到起始点（首尾闭合）
            if (_anchors.Count >= 2 && IsNearStartPoint(point))
            {
                FinishBezierAsClosed();
                return true;
            }

            AddAnchor(point);

            context.ReportStatus(CanFinish
                ? $"{_anchors.Count}个锚点 | 双击/Enter完成 | 右键/Back撤销 | Esc取消 | 靠近起点自动闭合"
                : "点击添加锚点");

            return true;
        }

        public override void OnMouseMove(SKPoint point)
        {
            if (context.CurrentShape == null) return;

            _cursor = point;

            // 实时预览：将光标位置作为临时的下一个控制点
            if (context.CurrentShape is DrawBezier bezier && _anchors.Count > 0)
            {
                // 检测是否靠近起始点，更新吸附状态
                bool isNearStart = _anchors.Count >= 2 && IsNearStartPoint(point);
                context.IsSnapToStart = isNearStart;

                if (isNearStart)
                {
                    context.SnapStartPoint = _anchors[0];
                    // 吸附时，预览线段末端直接连到起始点
                    var previewPoints = new List<SKPoint>(_anchors);
                    previewPoints.Add(_anchors[0]); // 预览闭合
                    bezier.Points = previewPoints;
                }
                else
                {
                    var previewPoints = new List<SKPoint>(_anchors);
                    previewPoints.Add(point);
                    bezier.Points = previewPoints;
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
            context.IsSnapToStart = false;

            if (context.CurrentShape is DrawBezier bezier)
            {
                bezier.Points = new List<SKPoint>(_anchors);
                CommitBezierPosition(bezier);
                _anchors.Clear();

                FinishDrawing();
            }
                
            return true;
        }

        /// <summary>
        /// 添加锚点
        /// </summary>
        public void AddAnchor(SKPoint point)
        {
            _anchors.Add(point);
            _cursor = point;

            if (context.CurrentShape == null && _anchors.Count == 1)
            {
                context.CurrentShape = new DrawBezier(new List<SKPoint>(_anchors));
                context.MarkDirty(new SKRect(point.X - 1, point.Y - 1, point.X + 1, point.Y + 1));
                context.IsDrawing = true;
            }
            else if (context.CurrentShape is DrawBezier bezier)
            {
                bezier.Points = new List<SKPoint>(_anchors);
                context.MarkDirty(new SKRect(point.X - 1, point.Y - 1, point.X + 1, point.Y + 1));
            }
        }

        public override void OnCancel()
        {
            context.IsSnapToStart = false;
            _anchors.Clear();
            _cursor = SKPoint.Empty;
            base.OnCancel();
            context.ReportStatus("贝塞尔曲线绘制已取消");
        }

        /// <summary>
        /// 判断当前鼠标位置是否靠近贝塞尔曲线的起始锚点。
        /// 吸附半径随视口缩放调整，保持屏幕上的视觉大小一致。
        /// </summary>
        private bool IsNearStartPoint(SKPoint currentPoint)
        {
            if (_anchors.Count < 2) return false;

            var startPoint = _anchors[0];
            float dx = currentPoint.X - startPoint.X;
            float dy = currentPoint.Y - startPoint.Y;
            float distance = MathF.Sqrt(dx * dx + dy * dy);

            // 根据视口缩放比例将屏幕像素半径转换为世界坐标半径
            float scale = (float)(context.ActiveCanvas?.Viewport.Scale ?? 1.0);
            float worldRadius = SnapScreenRadius / scale;

            return distance <= worldRadius;
        }

        /// <summary>
        /// 首尾吸附闭合完成贝塞尔曲线绘制。
        /// 设置 IsClosed=true 并完成绘制。
        /// </summary>
        private void FinishBezierAsClosed()
        {
            context.IsSnapToStart = false;

            if (context.CurrentShape is DrawBezier bezier && _anchors.Count >= 2)
            {
                bezier.IsClosed = true;
                bezier.Points = new List<SKPoint>(_anchors);
                CommitBezierPosition(bezier);

                context.ReportStatus($"贝塞尔曲线绘制完成（首尾闭合），共 {_anchors.Count} 个锚点");
                _anchors.Clear();
                FinishDrawing();
            }
            else
            {
                OnCancel();
                _anchors.Clear();
                context.ReportStatus("锚点太少，已取消");
            }
        }

        /// <summary>
        /// 重置工具状态
        /// </summary>
        public void Reset()
        {
            _anchors.Clear();
            _cursor = SKPoint.Empty;
            OnCancel();
        }

        private static void CommitBezierPosition(DrawBezier bezier)
        {
            if (bezier.Points == null || bezier.Points.Count < 2)
                return;

            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            for (int i = 0; i < bezier.Points.Count; i++)
            {
                var point = bezier.Points[i];
                if (point.X < minX) minX = point.X;
                if (point.X > maxX) maxX = point.X;
                if (point.Y < minY) minY = point.Y;
                if (point.Y > maxY) maxY = point.Y;
            }

            var centerX = (minX + maxX) * 0.5f;
            var centerY = (minY + maxY) * 0.5f;
            bezier.UpdateSetProperty(bezier.Points);
            bezier.Translate(centerX, centerY, true);
        }
    }
}
