using DrSoft.Drawing.Controls.Commands;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Mapping;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Model;
using SkiaSharp;
using System;
using System.Windows.Input;
using System.Windows.Media;
using DrawingGroup = DrSoft.Drawing.Controls.DrawShapes.DrawingGroup;

namespace DrSoft.Drawing.Controls.Tools
{
    public abstract class ToolBase
    {
        public abstract ToolType ToolType { get; }
        public abstract string Name { get; }
        public virtual string Icon { get; } = "";

        public abstract bool OnMouseDown(SKPoint point);
        public virtual void OnMouseMove(SKPoint point)
        {
        }
        public abstract bool OnMouseUp(SKPoint point);
        public virtual bool OnLeftMounseUp(SKPoint point) { return true; }
        public abstract bool OnMouseRightDown();
        public virtual bool OnKeyDown(Key key)
        {
            return false;
        }

        /// <summary>
        /// 当前工具状态下鼠标移动是否需要重绘画布。
        /// 默认 true（绘制类工具需要预览）；选择工具在“仅悬停”时返回 false 避免无效刷新。
        /// </summary>
        public virtual bool NeedRedrawOnMove
          => context.IsDrawing
             || context.BoxSelect.IsActive;


        /// <summary>
        /// 鼠标按下后是否需要重绘画布。
        /// 默认 true（绘制类工具按下即产生预览）；选择工具仅在选中集发生变化时返回 true。
        /// </summary>
        public virtual bool NeedRedrawOnDown => true;

        /// <summary>
        /// 鼠标抬起后是否需要重绘画布。
        /// 默认 true；选择工具仅在有视觉变化（控制点/框选/拖拽完成）时返回 true。
        /// </summary>
        public virtual bool NeedRedrawOnUp => true;

        DocumentContext context = DocumentContext.Instance;
        public virtual void OnCancel()
        {
            context.CurrentShape = null;
            context.IsDrawing = false;
        }

        private SKRect CalculateMergedBounds()
        {
            var context = DocumentContext.Instance;
            if (context.ActiveCanvas.SelectedShapeCount == 0)
                return SKRect.Empty;

            var boundsList = context.ActiveCanvas.Selection
                .OfType<DrawObject>()
                .Select(shape => shape.GetAABB())
                .Where(bounds => !bounds.IsEmpty)
                .ToArray();

            if (boundsList.Length == 0)
                return SKRect.Empty;

            // 计算边界值
            float minX = boundsList.Min(b => b.Left);
            float maxX = boundsList.Max(b => b.Right);
            float minY = boundsList.Min(b => b.Top);
            float maxY = boundsList.Max(b => b.Bottom);

            // 返回标准SKRect（Top是较小的Y值，Bottom是较大的Y值）
            return new SKRect(minX, minY, maxX, maxY);
        }

        protected void FinishDrawing()
        {
            if (context.CurrentShape != null && context.ActiveCanvas != null)
            {
                if (context.CurrentShape.Points.Count > 0)
                {
                    var newShape = context.CurrentShape;

                    ((DrawObject)newShape).SetRotationCenter(new SkiaSharp.SKPoint(((DrawObject)newShape).SharpCenter.X, ((DrawObject)newShape).SharpCenter.Y));
                    context.ActiveCanvas.ClearSelectedShapes();
                    context.ActiveCanvas.CommandHistory.Execute(new CommandAdd(((DrawingCanvas)context.ActiveCanvas).ActiveLayer, new List<IShape> { newShape }));

                    context.SelectState = SelectState.FirstSelected;
                    NotifyMenuEvent();
                }
                context.CurrentShape = null;
            }
            
            context.IsDrawing = false;
        }
        public void NotifyMenuEvent()
        {
            // 兼容保留：选区主通路已收敛到 DrawingCanvas.SetSelectedShapes/ClearSelectedShapes，
            // 这里不再重复发布 CanvasChangedEvent。
        }
    }
}
