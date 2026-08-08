using System.Net;
using DrSoft.Drawing.Controls.Consts;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Event;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.Tools
{
    public class ToolCircle : ToolBase
    {
        public override ToolType ToolType => ToolType.Circle;
        public override string Name => "圆形";
        public override string Icon => "○";

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
                _startPoint = point;
                _currentPoint = point;

                context.CurrentShape = new DrawCircle(new List<SKPoint> { point, point });
                context.MarkDirty(new SKRect(point.X - 1, point.Y - 1, point.X + 1, point.Y + 1));

                context.IsDrawing = true;
                context.ReportStatus("开始绘制圆/椭圆，拖动鼠标调整大小，按住 Shift 绘制正圆，松开完成");
                return true;
            }
            else
            {
                _currentPoint = point;
                FinishCircle();
                return true;
            }
        }

        public override void OnMouseMove(SKPoint point)
        {
            if (!context.IsDrawing || _startPoint == null || context.CurrentShape == null)
            {
                return;
            }

            _currentPoint = point;
            var circle = context.CurrentShape as DrawCircle;
            circle?.UpdateSetProperty(new List<SKPoint> { _startPoint.Value, point });
            base.OnMouseMove(point);
        }

        public override bool OnMouseUp(SKPoint point)
        {
            if (!context.IsDrawing || _startPoint == null)
            {
                return false;
            }

            _currentPoint = point;
            FinishCircle();
            return true;
        }

        public override bool OnMouseRightDown()
        {
            if (context.IsDrawing)
            {
                OnCancel();
                _startPoint = null;
                _currentPoint = null;
                context.ReportStatus("圆/椭圆绘制已取消");
                return true;
            }

            return false;
        }




        private void FinishCircle()
        {
            if (_startPoint != null && _currentPoint != null && context.CurrentShape != null)
            {
                var start = _startPoint.Value;
                var end = _currentPoint.Value;

                //UpdateCircleGeometry(start, end);

                if (context.CurrentShape is DrawCircle circle)
                {
                    var bounds = circle.ResolveBounds(start, end);
                    float actualWidth = Math.Abs(bounds.Right - bounds.Left);
                    float actualHeight = Math.Abs(bounds.Top - bounds.Bottom);

                    bool widthTooSmall = actualWidth < AppConsts.MinPrecision;
                    bool heightTooSmall = actualHeight < AppConsts.MinPrecision;
                    bool isTooSmall = widthTooSmall || heightTooSmall;

                    if (isTooSmall)
                    {
                        context.ReportStatus("圆/椭圆尺寸太小，已取消");
                        EventBus.Instance.Publish(new ToastMessageEvent("圆/椭圆尺寸太小，已取消", ToastType.Warning));
                        OnCancel();
                    }
                    else
                    {
                        float radiusX = actualWidth / 2f;
                        float radiusY = actualHeight / 2f;

                        float width = bounds.Right - bounds.Left;
                        float height = bounds.Top - bounds.Bottom;
                        float centerX = (bounds.Left + bounds.Right) / 2f;
                        float centerY = (bounds.Top + bounds.Bottom) / 2f;

                        //float left = circle.SharpCenter.X - radiusX - 6f;
                        //float right = circle.SharpCenter.X + radiusX + 6f;
                        //float bottom = circle.SharpCenter.Y - radiusY - 6f;
                        //float top = circle.SharpCenter.Y + radiusY + 6f;
                        float left = centerX - radiusX - 6f;
                        float right = centerX + radiusX + 6f;
                        float bottom = centerY - radiusY - 6f;
                        float top = centerY + radiusY + 6f;

                        context.MarkDirty(new SKRect(left, bottom, right, top));
                        circle.UpdateSetProperty(new List<SKPoint> { start, end });

                        bool isEllipse = circle.IsEllipse;
                        if (isEllipse)
                        {
                            context.ReportStatus($"椭圆绘制完成，尺寸: {actualWidth:F1} x {actualHeight:F1}");
                        }
                        else
                        {
                            context.ReportStatus($"圆形绘制完成，直径: {actualWidth:F1}");
                        }
               
                        FinishDrawing();
                    }
                }
                else
                {
                    OnCancel();
                    context.ReportStatus("圆/椭圆绘制失败，已取消");
                }
            }
            else
            {
                OnCancel();
                context.ReportStatus("圆/椭圆绘制失败，已取消");
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
