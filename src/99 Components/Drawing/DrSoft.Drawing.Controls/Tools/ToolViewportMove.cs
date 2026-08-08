using DrSoft.Drawing.Controls.Models;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Cursors = System.Windows.Input.Cursors;

namespace DrSoft.Drawing.Controls.Tools
{
    public class ToolViewportMove:ToolBase
    {
        public override ToolType ToolType => ToolType.ViewportMove;
        public override string Name => "视口移动";
        private bool _isDragging = false;

        protected DocumentContext context = DocumentContext.Instance;

        /// <summary>
        /// 鼠标按下时锚定的世界坐标点，拖拽期间始终跟随鼠标
        /// </summary>
        private SKPoint _anchorWorld;

        public override bool OnMouseDown(SKPoint point)
        {
            _isDragging = true;
            context.SetCursor(CanvasCursorFactory.GetMoveCursor(isActive: true));
            // point 已是世界坐标，直接记录为锚点
            _anchorWorld = point;
            return true;
        }

        public override void OnMouseMove(SKPoint point)
        {
            if (_isDragging && context.ActiveCanvas != null)
            {
                if (context.IsApplyingDeferredDragCommit) return;

                // point 是当前鼠标的世界坐标（由 CanvasViewModel 转换）
                // 计算当前鼠标的屏幕位置
                var currentScreen = context.ActiveCanvas.Viewport.WorldToScreen(point);
                // 计算锚点在屏幕上的当前位置
                var anchorScreen = context.ActiveCanvas.Viewport.WorldToScreen(_anchorWorld);

                // 屏幕像素 delta：将锚点平移到鼠标所在屏幕位置
                var dx = currentScreen.X - anchorScreen.X;
                var dy = currentScreen.Y - anchorScreen.Y;

                context.ActiveCanvas.Viewport.Pan(dx, dy);
                context.RequestRedraw();
            }
        }

        public override bool OnMouseUp(SKPoint point)
        {
            _isDragging = false;
            context.SetCursor(CanvasCursorFactory.GetMoveCursor());
            return true;
        }

        public override bool OnMouseRightDown()
        {
            context.SetCursor(Cursors.No);
            base.OnCancel();
            return true;
        }
    }
}
