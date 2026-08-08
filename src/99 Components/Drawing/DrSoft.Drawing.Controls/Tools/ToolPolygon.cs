using System;
using System.Diagnostics;
using System.Windows.Shapes;
using DrSoft.Drawing.Controls.Consts;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Models;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.Tools
{
    public class ToolPolygon : ToolBase
    {
        public override ToolType ToolType => ToolType.Polygon;
        public override string Name => "多边形";
        public override string Icon => "⬡";

        private const int MinimumSideCount = 3;
        private SKPoint? _startPoint = null;
        private SKPoint? _currentPoint = null;
        /// <summary>当前边数（默认 5 边）</summary>
        private int _sideCount = 5;
        /// <summary>true = 五角星；false = 正多边形</summary>
        private bool _isStar = false;

        private readonly DocumentContext _context = DocumentContext.Instance;

        public override bool OnMouseDown(SKPoint point)
        {
            if (_context.ActiveCanvas == null)
            {
                _context.ReportStatus("错误：没有激活的画布");
                return false;
            }

            if (!_context.IsDrawing)
            {
                _startPoint = point;
                _currentPoint = point;
                _context.IsDrawing = true;
                // 创建初始预览图形（尺寸极小）
                _sideCount = 5;
                _context.CurrentShape = CreatePolygonPreview(point, point);
            }
            else
            {
                _currentPoint = point;
                return true;
            }
            return true;
        }

        public override void OnMouseMove(SKPoint point)
        {
            if (!_context.IsDrawing || _startPoint == null) return;

            _currentPoint = point;

            // 实时更新预览：以鼠标按下点为中心，当前点到按下点的距离为半径
            _context.CurrentShape = CreatePolygonPreview(_startPoint.Value, point);
            _context.CurrentShape.Translate(_startPoint.Value.X, _startPoint.Value.Y, true);
            base.OnMouseMove(point);
        }

        public override bool OnMouseUp(SKPoint point)
        {
            if (!_context.IsDrawing || _startPoint == null) return false;

            double radius = Math.Sqrt(Math.Pow(_currentPoint.Value.X - _startPoint.Value.X, 2.0) + Math.Pow(_currentPoint.Value.Y - _startPoint.Value.Y, 2.0));
            if (radius < AppConsts.MinPrecision) return false;

            var polygon = CreatePolygonPreview(_startPoint.Value, point);

            polygon.Translate(_startPoint.Value.X, _startPoint.Value.Y, true);

            _context.CurrentShape = polygon;
            _context.ReportStatus($"{(_isStar ? "五角星" : "多边形")}绘制完成，{_sideCount} 边");
            FinishDrawing();
            Reset();
            return true;
        }

        public override bool OnKeyDown(System.Windows.Input.Key key)
        {
            if (!_context.IsDrawing || _startPoint == null)
            {
                return false;
            }

            var nextSideCount = _sideCount;

            if (key == System.Windows.Input.Key.Up)
            {
                nextSideCount++;
            }
            else if (key == System.Windows.Input.Key.Down)
            {
                nextSideCount--;
            }
            else
            {
                return false;
            }

            if (nextSideCount < MinimumSideCount)
            {
                nextSideCount = MinimumSideCount;
            }

            var sideCountChanged = nextSideCount != _sideCount;
            if (!sideCountChanged)
            {
                ReportAdjustStatus();
                return true;
            }

            _sideCount = nextSideCount;

            var previewPoint = _currentPoint ?? _startPoint.Value;
            _context.CurrentShape = CreatePolygonPreview(_startPoint.Value, previewPoint);
            _context.CurrentShape.Translate(_startPoint.Value.X, _startPoint.Value.Y, true);
            ReportAdjustStatus();
            return true;
        }

        public override bool OnMouseRightDown()
        {
            if (_context.IsDrawing)
            {
                OnCancel();
                return true;
            }
            return false;
        }

        public override void OnCancel()
        {
            base.OnCancel();
            Reset();
        }

        private void Reset()
        {
            _startPoint = null;
            _currentPoint = null;
        }

        /// <summary>
        /// 以 startPoint 为圆心，计算 startPoint 到 currentPoint 的距离为外接圆半径，生成预览多边形。
        /// </summary>
        private DrawPolygon CreatePolygonPreview(SKPoint center, SKPoint edgePoint)
        {
            float dx = edgePoint.X - center.X;
            float dy = edgePoint.Y - center.Y;
            float radius = (float)Math.Sqrt(dx * dx + dy * dy);
            if (radius < 0.5f) radius = 0.5f;

            List<SKPoint> pts = _isStar
                ? DrawPolygon.GenerateStarPoints(center, radius, _sideCount)
                : DrawPolygon.GenerateRegularPolygonPoints(center, radius, _sideCount);

            var polygon = new DrawPolygon(pts)
            {
                SideCount = _sideCount,
                IsStar = _isStar,
            };

            // 继承当前画布笔刷样式
            if (_context.CurrentShape is DrawObject prev)
                polygon.Pen = prev.Pen;

            return polygon;
        }

        private void ReportAdjustStatus()
        {
            var statusText = _isStar
                ? $"五角星预览：当前 {_sideCount} 边"
                : $"多边形预览：当前 {_sideCount} 边";

            _context.ReportStatus(statusText);
        }
    }
}
