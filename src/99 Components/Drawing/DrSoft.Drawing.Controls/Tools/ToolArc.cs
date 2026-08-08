using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Model;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.Tools
{
    public class ToolArc : ToolBase
    {
        public override ToolType ToolType => ToolType.Arc;
        public override string Name => "圆弧";
        public override string Icon => "⌒";
        DocumentContext context = DocumentContext.Instance;

        /// <summary>
        /// 绘制状态枚举
        /// </summary>
        private enum DrawState
        {
            Idle = 0,       // 空闲，等待第一点
            P1Set = 1,      // 已设置第一点，等待第二点
            P2Set = 2,      // 已设置第二点，等待第三点（实时预览）
            Done = 3        // 已完成绘制
        }

        private DrawState _state = DrawState.Idle;
        private SKPoint? _p1, _p2, _p3;
        private SKPoint _mouse;

        public override bool OnMouseDown(SKPoint point)
        {
            if (context.ActiveCanvas == null)
            {
                context.ReportStatus("错误：没有激活的画布");
                return false;
            }

            // 将 Point2D 转换为 SKPoint
            var skPoint = new SKPoint((float)point.X, (float)point.Y);

            switch (_state)
            {
                case DrawState.Idle:
                case DrawState.Done:
                    // 开始新的圆弧
                    _p1 = skPoint;
                    _p2 = null;
                    _p3 = null;
                    _state = DrawState.P1Set;
                    context.ReportStatus("已放置 P1，等待单击第二点");
                    break;

                case DrawState.P1Set:
                    // 设置第二点
                    _p2 = skPoint;
                    _state = DrawState.P2Set;
                    // 创建圆弧对象用于预览
                    context.CurrentShape = new DrawArc(
                        new Point2D(_p1.Value.X, _p1.Value.Y),
                        new Point2D(_p2.Value.X, _p2.Value.Y),
                        new Point2D(_p2.Value.X, _p2.Value.Y));
                    context.MarkDirty(new SKRect(_p1.Value.X - 1, _p1.Value.Y - 1, _p2.Value.X + 1, _p2.Value.Y + 1));
                    context.IsDrawing = true;
                    context.ReportStatus("已放置 P2，等待单击第三点（实时预览圆弧）");
                    break;

                case DrawState.P2Set:
                    // 设置第三点，验证并完成
                    _p3 = skPoint;

                    // 验证三点不共线
                    var test = ArcMath.Circumcircle(_p1!.Value, _p2!.Value, _p3.Value);
                    if (test is null)
                    {
                        context.ReportStatus("三点共线，无法构成圆弧，请重新选择第三个点");
                        _p3 = null;
                        return true;
                    }

                    // 完成圆弧绘制
                    FinishArc();
                    break;
            }

            return true;
        }

        public override void OnMouseMove(SKPoint point)
        {
            // 记录鼠标位置，用于预览
            //_mouse = point;
            //var mousePoint = new Point2D(_mouse.X, _mouse.Y);

            // P1Set 状态：设置预览虚线端点（从 P1 到鼠标位置）
            if (_state == DrawState.P1Set && _p1.HasValue)
            {
                if (context.CurrentShape == null)
                {
                    context.CurrentShape = new DrawArc(
                        new SKPoint(_p1.Value.X, _p1.Value.Y),
                        new SKPoint(_p1.Value.X, _p1.Value.Y),
                        new SKPoint(_p1.Value.X, _p1.Value.Y));
                    context.IsDrawing = true;
                }

                if (context.CurrentShape is DrawArc arc)
                {
                    // 设置预览线端点到当前鼠标位置（P1 -> 鼠标）
                    arc.PreviewLineEndPoint = point;
                    arc.PreviewLineEndPoint2 = null; // 清除第二条预览线

                    float left = Math.Min(_p1.Value.X, point.X) - 1;
                    float top = Math.Min(_p1.Value.Y, point.Y) - 1;
                    float right = Math.Max(_p1.Value.X, point.X) + 1;
                    float bottom = Math.Max(_p1.Value.Y, point.Y) + 1;
                    context.MarkDirty(new SKRect(left, top, right, bottom));
                }
                return;
            }

            if (!context.IsDrawing || context.CurrentShape == null) return;

            // P2Set 状态下进行实时预览：显示两条辅助线 P1->鼠标 和 P2->鼠标
            if (_state == DrawState.P2Set && context.CurrentShape is DrawArc arc2 &&
                _p1.HasValue && _p2.HasValue)
            {
                // 使用当前鼠标位置作为临时第三点进行预览
                arc2.UpdateThreePointArc(_p1.Value, _p2.Value, point);

                // 设置两条预览线：P1->鼠标 和 P2->鼠标
                arc2.PreviewLineEndPoint = point;      // P1 -> 鼠标
                arc2.PreviewLineEndPoint2 = point;     // P2 -> 鼠标


            }

            base.OnMouseMove(point);
        }

        public override bool OnMouseUp(SKPoint point)
        {
            if (!context.IsDrawing) return false;

            FinishArc();
            return true;
        }

        public override bool OnMouseRightDown()
        {
            if (_state != DrawState.Idle)
            {
                // 右键取消当前绘制
                OnCancel();
                context.ReportStatus("圆弧绘制已取消");
                return true;
            }
            return false;
        }

        /// <summary>
        /// 完成圆弧绘制
        /// 参考 MainWindow2 的 FinishArc 逻辑
        /// </summary>
        private void FinishArc()
        {
            if (context.CurrentShape is DrawArc arc && _p1.HasValue && _p2.HasValue && _p3.HasValue)
            {
                // 用最终点更新圆弧
                arc.UpdateThreePointArc(_p1.Value, _p2.Value, _p3.Value);

                // 检查圆弧是否有效
                if (arc.Radius <= 0)
                {
                    context.ReportStatus("圆弧无效：半径为零");
                    OnCancel();
                    return;
                }

                // 清除预览线
                arc.PreviewLineEndPoint = null;
                arc.PreviewLineEndPoint2 = null;

                arc.UpdateSetProperty([new(_p1.Value.X, _p1.Value.Y), new(_p2.Value.X, _p2.Value.Y), new(_p3.Value.X, _p3.Value.Y)]);


                var circ = ArcMath.Circumcircle(_p1.Value, _p2.Value, _p3.Value);
                if (!circ.HasValue)
                    return;

                var (center, radius) = circ.Value;
                var CircumcircleCenter = arc.GetTransformMatrix().MapPoint(new SKPoint(0, 0));
                arc.Translate(center.X - CircumcircleCenter.X, center.Y - CircumcircleCenter.Y, true);



                // 完成绘制，添加到画布
                FinishDrawing();

                // 获取圆弧信息
                string arcInfo = ArcMath.GetArcInfo(_p1.Value, _p2.Value, _p3.Value);
                context.ReportStatus($"三点完成，显示最终圆弧 - {arcInfo}");

                // 重置状态
                _state = DrawState.Done;
                ResetState();
            }
        }

        public override void OnCancel()
        {
            // 清除预览线
            if (context.CurrentShape is DrawArc arc)
            {
                arc.PreviewLineEndPoint = null;
                arc.PreviewLineEndPoint2 = null;
            }
            ResetState();
            base.OnCancel();
        }

        /// <summary>
        /// 重置状态
        /// </summary>
        private void ResetState()
        {
            _state = DrawState.Idle;
            _p1 = null;
            _p2 = null;
            _p3 = null;
            context.ReportStatus("圆弧工具: 单击放置第一个点开始绘制");
        }

        /// <summary>
        /// 工具激活时重置状态
        /// </summary>
        public void ActivateTool()
        {
            ResetState();
        }

        /// <summary>
        /// 工具停用时清理状态
        /// </summary>
        public void DeactivateTool()
        {
            if (_state != DrawState.Idle)
            {
                OnCancel();
            }
        }
    }
}
