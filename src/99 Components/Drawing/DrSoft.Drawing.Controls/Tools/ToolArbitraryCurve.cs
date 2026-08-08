using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Model;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.Tools
{
    /// <summary>
    /// 任意曲线绘制工具（自由绘制）。
    /// 按下鼠标开始绘制，移动时采样轨迹点，松开完成。
    /// 采样间距确保点密度合理，避免过密导致性能问题。
    /// </summary>
    internal class ToolArbitraryCurve : ToolBase
    {
        public override ToolType ToolType => ToolType.ArbitraryCurve;
        public override string Name => "任意曲线";
        public override string Icon => "〰️";

        /// <summary>采样间距（世界坐标 mm），鼠标移动距离超过此值才记录新点</summary>
        private const float SampleDistance = 0.05f;

        /// <summary>最小点数，少于此数量不创建图形</summary>
        private const int MinPoints = 2;

        private List<SKPoint> _points = new();
        private SKPoint _lastSampled = SKPoint.Empty;
        private bool _isDrawing = false;

        DocumentContext context = DocumentContext.Instance;

        public override bool OnMouseDown(SKPoint point)
        {
            _points.Clear();
            _points.Add(point);
            _lastSampled = point;
            _isDrawing = true;

            context.CurrentShape = new DrawArbitraryCurve(new List<SKPoint> { point });
            context.MarkDirty(new SKRect(point.X - 1, point.Y - 1, point.X + 1, point.Y + 1));
            context.IsDrawing = true;

            context.ReportStatus("移动鼠标绘制曲线 | 松开完成 | 右键取消 | Esc取消");

            return true;
        }

        public override void OnMouseMove(SKPoint point)
        {
            if (!_isDrawing || context.CurrentShape is not DrawArbitraryCurve curve) return;

            // 距离上次采样点超过阈值才记录新点
            float dx = point.X - _lastSampled.X;
            float dy = point.Y - _lastSampled.Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);

            if (dist >= SampleDistance)
            {
                _points.Add(point);
                _lastSampled = point;

                // 实时预览：更新点集并重新计算几何属性
                curve.Points = new List<SKPoint>(_points);
                if (_points.Count >= 2)
                {
                    curve.UpdateSetProperty(curve.Points);
                }
                context.MarkDirty(new SKRect(point.X - 1, point.Y - 1, point.X + 1, point.Y + 1));
            }

            base.OnMouseMove(point);
        }

        public override bool OnMouseUp(SKPoint point)
        {
            if (!_isDrawing) return true;

            // 添加最后一点（确保终点精确）
            if (_points.Count == 0 || point != _points[_points.Count - 1])
            {
                _points.Add(point);
            }

            if (context.CurrentShape is DrawArbitraryCurve curve)
            {
                if (_points.Count >= MinPoints)
                {
                    // 先更新图形的几何属性，再提交到画布
                    curve.Points = new List<SKPoint>(_points);
                    curve.UpdateSetProperty(curve.Points);
                    context.ReportStatus($"任意曲线绘制完成，共 {_points.Count} 个采样点");
                    curve.CommitTransform();
                    FinishDrawing();
                }
                else
                {
                    context.ReportStatus("点数太少，已取消");
                    context.CurrentShape = null;
                    context.IsDrawing = false;
                }
            }
            else
            {
                // 兜底：CurrentShape 已被清空，仍需结束绘制状态
                context.IsDrawing = false;
            }

            _isDrawing = false;
            _points.Clear();
            _lastSampled = SKPoint.Empty;
            return true;
        }

        public override bool OnMouseRightDown()
        {
            // 右键：完成当前绘制（与 ToolBezier 右键完成一致）
            if (_isDrawing && _points.Count >= MinPoints && context.CurrentShape is DrawArbitraryCurve curve)
            {
                curve.Points = new List<SKPoint>(_points);
                curve.UpdateSetProperty(curve.Points);
                context.ReportStatus($"任意曲线绘制完成，共 {_points.Count} 个采样点");
                FinishDrawing();
            }
            else
            {
                // 点数不足，取消
                OnCancel();
                return true;
            }

            _isDrawing = false;
            _points.Clear();
            _lastSampled = SKPoint.Empty;
            return true;
        }

        public override void OnCancel()
        {
            _isDrawing = false;
            _points.Clear();
            _lastSampled = SKPoint.Empty;
            base.OnCancel();
            context.ReportStatus("任意曲线绘制已取消");
        }
    }
}
