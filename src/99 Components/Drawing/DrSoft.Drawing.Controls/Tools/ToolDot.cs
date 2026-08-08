using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Model;
using SkiaSharp;
using System.Collections.Generic;

namespace DrSoft.Drawing.Controls.Tools
{
    public class ToolDot : ToolBase
    {
        public override ToolType ToolType => ToolType.Point;
        public override string Name => "点";
        public override string Icon => "•";

        private SKPoint? _startPoint = null;

        DocumentContext context = DocumentContext.Instance;

        public override bool OnMouseDown(SKPoint point)
        {
            if (context.ActiveCanvas == null)
            {
                context.ReportStatus("错误：没有激活的画布");
                return false;
            }

            if (!context.IsDrawing)
            {
                // 开始绘制点
                _startPoint = point;
                
                // 创建点对象
                context.CurrentShape = new DrawDot(point);
                context.MarkDirty(new SKRect(point.X - 1, point.Y - 1, point.X + 1, point.Y + 1));
                context.IsDrawing = true;
                context.ReportStatus("点已创建，右键取消");
                return true;
            }
            return true;
        }

        public override void OnMouseMove(SKPoint point)
        {
            if (!context.IsDrawing || _startPoint == null || context.CurrentShape == null) return;
        }

        public override bool OnMouseUp(SKPoint point)
        {
            if (!context.IsDrawing || _startPoint == null) return false;

            // 鼠标松开时完成点绘制
            FinishDot();
            return true;
        }

        public override bool OnMouseRightDown()
        {
            return true;
        }

        private void FinishDot()
        {
            if (_startPoint != null && context.CurrentShape != null)
            {
                if (context.CurrentShape is DrawDot dot)
                {
                    // 确保点有有效的位置
                    dot.UpdateSetProperty(new List<SKPoint> { _startPoint ?? new SKPoint() });
                    dot.Translate(_startPoint.Value.X, _startPoint.Value.Y, true);
                    context.ReportStatus($"点已创建于位置: {dot.X:F1}, {dot.Y:F1}");
                    FinishDrawing();
                }
            }
            else
            {
                OnCancel();
                context.ReportStatus("点绘制失败，已取消");
            }

            _startPoint = null;
        }

        public override void OnCancel()
        {
            base.OnCancel();
            _startPoint = null;
        }
    }
}
