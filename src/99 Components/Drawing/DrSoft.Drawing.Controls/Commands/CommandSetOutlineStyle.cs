using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.Commands
{
    /// <summary>
    /// 外框样式设置命令：支持外框颜色和样式的撤销/重做。
    /// 外框颜色优先级高于图层颜色：设置自定义画笔（_pen）覆盖 LayerPen。
    /// 短虚线和点虚线通过 PathEffect 实现。
    /// </summary>
    internal class CommandSetOutlineStyle : IDrawingCommand
    {
        public string Description => "设置外框样式";

        private readonly OutlineSnapshot[] _snapshots;

        public CommandSetOutlineStyle(IEnumerable<DrawObject> shapes, string? outlineColor, int outlineStyleIndex)
        {
            _snapshots = shapes.Select(s => new OutlineSnapshot(
                s,
                s.CustomPen?.Clone(),   // 捕获操作前的自定义画笔（null 表示使用图层画笔）
                s.Pen.StrokeWidth,       // 保留当前线宽
                s is IHatchable h1 ? h1.HatchParamInfo?.OutlineColor : null,
                s is IHatchable h2 ? h2.HatchParamInfo?.OutlineStyleIndex ?? 0 : 0,
                outlineColor,
                outlineStyleIndex
            )).ToArray();
        }

        /// <summary>
        /// 执行（Redo）：应用新的外框样式
        /// </summary>
        public void Execute()
        {
            foreach (var s in _snapshots)
            {
                ApplyOutlineStyle(s.Shape, s.NewOutlineColor, s.NewOutlineStyleIndex, s.OriginalStrokeWidth);
            }
        }

        /// <summary>
        /// 撤销：恢复操作前的画笔状态和 HatchParamInfo
        /// </summary>
        public bool Undo()
        {
            foreach (var s in _snapshots)
            {
                // 恢复自定义画笔：null 表示回退到图层共享画笔
                s.Shape.Pen = s.BeforeCustomPen?.Clone();

                // 恢复 HatchParamInfo（Execute 中同步修改了此字段，Undo 也需还原）
                if (s.Shape is IHatchable hatchable)
                {
                    hatchable.HatchParamInfo ??= new HatchParamDto();
                    hatchable.HatchParamInfo.OutlineColor = s.BeforeHatchOutlineColor ?? "#000000";
                    hatchable.HatchParamInfo.OutlineStyleIndex = s.BeforeHatchOutlineStyleIndex;
                }
            }
            return true;
        }

        /// <summary>
        /// 应用外框样式到指定图形。
        /// OutlineStyleIndex: 0=实线, 1=短虚线, 2=点虚线, 3=无外框
        /// </summary>
        private static void ApplyOutlineStyle(DrawObject shape, string? outlineColor, int outlineStyleIndex, float strokeWidth)
        {
            if (outlineColor != null)
            {
                // 外框颜色优先级高于图层颜色：设置自定义画笔
                var color = SKColor.Parse(outlineColor);
                var pen = new SKPaint
                {
                    Color = color,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = strokeWidth,
                    IsAntialias = true,
                };

                // 短虚线或点虚线：设置 PathEffect
                switch (outlineStyleIndex)
                {
                    case 1: // 短虚线
                        pen.StrokeCap = SKStrokeCap.Round;
                        pen.PathEffect = SKPathEffect.CreateDash(new float[] { 0.1f, 0.1f }, 0);
                        break;
                    case 2: // 点虚线
                        pen.StrokeCap = SKStrokeCap.Round;
                        pen.PathEffect = SKPathEffect.CreateDash(new float[] { 0f, 0.1f }, 0);
                        break;
                }

                shape.Pen = pen;
            }
            else
            {
                // 清除自定义画笔，回退到图层颜色
                shape.Pen = null;
            }

            // 同步到 HatchParamDto（用于持久化和填充渲染）
            if (shape is IHatchable hatchable)
            {
                hatchable.HatchParamInfo ??= new HatchParamDto();
                hatchable.HatchParamInfo.OutlineColor = outlineColor ?? "#000000";
                hatchable.HatchParamInfo.OutlineStyleIndex = outlineStyleIndex;
            }
        }

        private record OutlineSnapshot(
            DrawObject Shape,
            SKPaint? BeforeCustomPen,
            float OriginalStrokeWidth,
            string? BeforeHatchOutlineColor,
            int BeforeHatchOutlineStyleIndex,
            string? NewOutlineColor,
            int NewOutlineStyleIndex);
    }
}
