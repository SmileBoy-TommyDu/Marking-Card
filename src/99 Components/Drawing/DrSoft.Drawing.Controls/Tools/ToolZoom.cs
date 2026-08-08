using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Model;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace DrSoft.Drawing.Controls.Tools
{
    /// <summary>
    /// 画布缩放工具。
    /// 统一处理点缩放、全屏/适屏、选区缩放以及缩放历史回退。
    /// </summary>
    public class ToolZoom : ToolBase
    {
        public override ToolType ToolType => ToolType.Zoom;

        public override string Name { get; }

        private bool _isCanZoomBack = false;

        public bool CanZoomBack
        {
            get => _isCanZoomBack;
            set => _isCanZoomBack = value;
        }

        /// <summary>
        /// 保存历史视口状态（scale, offsetX, offsetY），以便回退缩放
        /// </summary>
        private Stack<(float scale, float offsetX, float offsetY)> zoomLists = new Stack<(float, float, float)>();

        public string ZoomName { get; set; }

        protected DocumentContext context = DocumentContext.Instance;
        internal int ZoomHistoryCount => zoomLists.Count;
        public override bool OnMouseDown(SKPoint point)
        {
            return true;
        }

        public override void OnMouseMove(SKPoint point)
        {
            
        }

        public override bool OnMouseRightDown()
        {
            return true;
        }

        /// <summary>
        /// 根据当前缩放命令执行对应视口操作。
        /// 所有会改变视口的分支都会先记录旧状态，保证 ZoomBack 可回退。
        /// </summary>
        public override bool OnMouseUp(SKPoint point)
        {
            if (ZoomName == "ZoomIn")
            {
                SaveViewportState();
                ZoomAt(point, 1.25f); // 放大 25%
                CanZoomBack = true;
            }
            else if (ZoomName == "ZoomOut")
            {
                SaveViewportState();
                ZoomAt(point, 1.0f / 1.25f); // 缩小 20%
                CanZoomBack = true;
            }
            else if (ZoomName == "ZoomBack")
            {
                if (zoomLists.Any())
                {
                    // 回退时直接恢复完整视口状态，避免重复推导缩放中心点。
                    var (scale, offsetX, offsetY) = zoomLists.Pop();
                    context.ActiveCanvas?.Viewport.SetState(scale, offsetX, offsetY);
                    NotifyZoomChanged();

                    if (!zoomLists.Any()) CanZoomBack = false;
                }
            }
            else if (ZoomName == "ZoomToFullScreen")
            {
                SaveViewportState();
                context.ActiveCanvas?.Viewport.Reset();
                NotifyZoomChanged();
                CanZoomBack = true;
            }
            else if (ZoomName == "ZoomToFit")
            {
                SaveViewportState();
                ZoomToFit();
                CanZoomBack = true;
            }
            else if (ZoomName == "ZoomToSelection")
            {
                if (context.ActiveCanvas?.Selection.OfType<DrawObject>().Count() > 0)
                {
                    SaveViewportState();
                    if (ZoomToSelection()) { CanZoomBack = true; }
                }
            }

            return true;
        }

        /// <summary>
        /// 保存当前视口状态到历史栈
        /// </summary>
        public void SaveViewportState()
        {
            var viewport = context.ActiveCanvas?.Viewport;
            if (viewport == null) return;
            zoomLists.Push((viewport.Scale, viewport.OffsetX, viewport.OffsetY));
        }

        internal void ResetViewportState()
        {
            zoomLists.Clear();
            CanZoomBack = false;
        }

        /// <summary>
        /// 缩放到适合所有图形
        /// </summary>
        private void ZoomToFit()
        {
            var canvas = context.ActiveCanvas;
            if (canvas == null) return;

            var allShapes = canvas.AllShapes.OfType<DrawObject>().ToList();
            if (allShapes.Count == 0)
            {
                // 没有图形时，回退到机台范围
                canvas.Viewport.Reset();
                NotifyZoomChanged();
                return;
            }

            var bounds = CalculateBounds(allShapes);
            if (bounds.IsEmpty)
            {
                canvas.Viewport.Reset();
                NotifyZoomChanged();
                return;
            }

            canvas.Viewport.ZoomToFitRect(bounds);
            NotifyZoomChanged();
        }

        /// <summary>
        /// 缩放到适合选中的图形
        /// </summary>
        private bool ZoomToSelection()
        {
            var canvas = context.ActiveCanvas;
            if (canvas == null) return false;

            var selectedShapes = canvas.Selection.OfType<DrawObject>().ToList();
            if (selectedShapes.Count == 0)
            {
                // 没有选中图形时，不操作
               // ZoomToFit();
                return false;
            }

            var bounds = CalculateBounds(selectedShapes);
            if (bounds.IsEmpty)
            {
                //ZoomToFit();
                return false;
            }

            canvas.Viewport.ZoomToFitRect(bounds);
            NotifyZoomChanged();
            return true;
        }

        /// <summary>
        /// 计算图形集合的合并边界
        /// </summary>
        private SKRect CalculateBounds(List<DrawObject> shapes)
        {
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            foreach (var shape in shapes)
            {
                var b = shape.GetAABB();
                if (b.IsEmpty) continue;
                if (b.Left < minX) minX = b.Left;
                if (b.Top < minY) minY = b.Top;
                if (b.Right > maxX) maxX = b.Right;
                if (b.Bottom > maxY) maxY = b.Bottom;
            }

            if (minX == float.MaxValue) return SKRect.Empty;
            return new SKRect(minX, minY, maxX, maxY);
        }

        /// <summary>
        /// 通知缩放变化并触发重绘。可外部调用（如滚轮缩放的防抖计时器）。
        /// </summary>
        internal void NotifyZoomChanged()
        {
            var activeCanvas = context.ActiveCanvas;
            var zoomPercent = CalculateZoomPercent(activeCanvas);
            EventBus.Instance.Publish(new ViewportChangedEvent
            {
                ZoomPercent = zoomPercent,
                CanZoomBack = CanZoomBack
            });
            (activeCanvas as DrawingCanvas)?.ScaleChangeVisibleCache();
            context.InvalidateSelectionBoundsCache();
            context.RequestFullRedraw();
            context.RequestRedraw();
        }

        /// <summary>
        /// 以世界坐标中的锚点执行缩放，并同步画布缓存与缩放事件。
        /// </summary>
        public void ZoomAt(SKPoint point, float zoomFactor)
        {
            var p = context.ActiveCanvas?.Viewport.WorldToScreen(point) ?? new SKPoint(0, 0);
            if (context.IsApplyingDeferredDragCommit) return;
            context.ActiveCanvas?.Viewport.ZoomAt(zoomFactor, p.X, p.Y);
            NotifyZoomChanged();
        }

        internal static double CalculateZoomPercent(ICanvas? canvas)
        {
            if (canvas == null)
            {
                return 100d;
            }

            var baseScale = canvas.InitZoomPercent > 0 ? canvas.InitZoomPercent : 1f;
            return Math.Round((canvas.Viewport.Scale / baseScale) * 100d);
        }
    }
}
