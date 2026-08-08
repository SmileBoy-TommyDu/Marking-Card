using DrSoft.Drawing.Controls.Consts;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Utility;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.Tools
{
    public class ToolRectangle : ToolBase
    {
        public override ToolType ToolType => ToolType.Rectangle;
        public override string Name => "矩形";
        public override string Icon => "▭";

        private SKPoint? _startPoint = null;
        private SKPoint? _currentPoint = null;




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
                // 开始绘制矩形
                _startPoint = point;
                _currentPoint = point;
                // 创建初始矩形（四个顶点都指向同一个点，将在拖动时更新）
                context.CurrentShape = new DrawRectangle(new List<SKPoint> { point, new SKPoint(point.X + 0.1f, point.Y + 0.1f) });

                context.MarkDirty(new SKRect(point.X - 1, point.Y - 1, point.X + 1f, point.Y + 1f));

                context.IsDrawing = true;
                context.ReportStatus("开始绘制矩形，拖动鼠标调整大小，松开完成");
                return true;
            }
            else
            {
                // 已经有一个矩形正在绘制，点击完成
                FinishRectangle();
                return true;
            }
        }

        public override void OnMouseMove(SKPoint point)
        {
            if (!context.IsDrawing || _startPoint == null || context.CurrentShape == null) return;

            _currentPoint = point;

            // 更新矩形的大小 - 根据两个对角点计算四个顶点
            UpdateRectanglePoints(_startPoint.Value, point);

            base.OnMouseMove(point);

        }


        /// <summary>
        /// 根据两个对角点更新矩形的四个顶点
        /// </summary>
        private void UpdateRectanglePoints(SKPoint startPoint, SKPoint endPoint)
        {
            if (context.CurrentShape is DrawRectangle rectangle)
            {
                // 计算矩形的四个顶点
                // 注意：在Y轴向上为正的坐标系中，top是较大的Y值，bottom是较小的Y值
                float left = Math.Min(startPoint.X, endPoint.X);
                float right = Math.Max(startPoint.X, endPoint.X);
                float bottom = Math.Min(startPoint.Y, endPoint.Y);  // 较小的Y值（下边界）
                float top = Math.Max(startPoint.Y, endPoint.Y);     // 较大的Y值（上边界）

                // 按住shift绘制正方形
                if (context.IsShiftPressed())
                {
                    float width = right - left;
                    float height = top - bottom;
                    float size = Math.Max(width, height);

                    // 根据鼠标拖动方向调整边界，保持起始点不动
                    if (endPoint.X >= startPoint.X)
                        right = left + size;
                    else
                        left = right - size;

                    if (endPoint.Y >= startPoint.Y)
                        top = bottom + size;
                    else
                        bottom = top - size;
                }


                rectangle.Points.Clear();
                rectangle.Points.Add(new SKPoint(left, top));      // 左上角
                rectangle.Points.Add(new SKPoint(right, top));     // 右上角
                rectangle.Points.Add(new SKPoint(right, bottom));  // 右下角
                rectangle.Points.Add(new SKPoint(left, bottom));   // 左下角
            }


        }

        public override bool OnMouseUp(SKPoint point)
        {
            if (!context.IsDrawing || _startPoint == null) return false;

            // 鼠标松开时完成矩形绘制
            FinishRectangle();

            //context.sh
            return true;
        }

        public override bool OnMouseRightDown()
        {
            if (context.IsDrawing)
            {
                // 右键取消绘制
                OnCancel();
                _startPoint = null;
                _currentPoint = null;
                context.ReportStatus("矩形绘制已取消");
                return true;
            }
            return false;
        }

        private void FinishRectangle()
        {
            if (_startPoint != null && _currentPoint != null && context.CurrentShape != null)
            {
                var start = _startPoint.Value;
                var end = _currentPoint.Value;

                // 先按 Shift 修正为正方形，得到最终的点
                UpdateRectanglePoints(start, end);

                // 用修正后的矩形点计算实际尺寸
                if (context.CurrentShape is DrawRectangle rect)
                {
                    var points = rect.Points;
                    float actualWidth = Math.Abs(points[1].X - points[0].X);
                    float actualHeight = Math.Abs(points[2].Y - points[1].Y);

                    if (actualWidth.Lt(AppConsts.MinPrecision) || actualHeight.Lt(AppConsts.MinPrecision))
                    {
                        //context.ReportStatus("矩形尺寸太小，已取消");
                        //EventBus.Instance.Publish(new ToastMessageEvent("矩形尺寸太小，已取消", ToastType.Warning));
                        OnCancel();
                    }
                    else
                    {
                        float left = Math.Min(points[0].X, points[2].X) - 8;
                        float right = Math.Max(points[0].X, points[2].X) + 8;
                        float bottom = Math.Min(points[0].Y, points[2].Y) - 8;
                        float top = Math.Max(points[0].Y, points[2].Y) + 8;

                        var centerX = (float)(points[0].X + points[2].X) / 2.0f;
                        var centerY = (float)(points[0].Y + points[2].Y) / 2.0f;
                        rect.UpdateSetProperty(new List<SKPoint>() { points[0], points[2] });
                        rect.Translate(centerX, centerY, true);
                        context.MarkDirty(new SKRect(left, bottom, right, top));
                        context.ReportStatus($"矩形绘制完成，尺寸: {actualWidth:F1} x {actualHeight:F1}");

                        FinishDrawing();
                    }
                }
                else
                {
                    OnCancel();
                    context.ReportStatus("矩形绘制失败，已取消");
                }
            }
            else
            {
                OnCancel();
                context.ReportStatus("矩形绘制失败，已取消");
            }

            _startPoint = null;
            _currentPoint = null;
        }

        public override void OnCancel()
        {
            base.OnCancel();
            _startPoint = null;
            _currentPoint = null;
        }
    }
}
