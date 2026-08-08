using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.MarkCommand;
using System.Drawing;

namespace DrSoft.MarkCard.RTC.CommandProcessors
{
    /// <summary>
    /// 圆弧打标命令处理器
    /// </summary>
    public class MarkCircleProcessor : RTC6MarkCommandProcessorBase<MarkCircleCommand>
    {
        public override MarkCommandType CommandType => MarkCommandType.MarkCircle;

        protected override MarkErrorCode ProcessCore(MarkCircleCommand command, RTC6ProcessContext context)
        {
            if (command.StartPoint == null)
            {
                command.StartPoint = new PointF(command.Center.X + command.Radius, command.Center.Y);
            }

            RTC6Wrap.n_arc_abs(context.CardNo,
                (int)(command.Center.X * context.Factor),
                (int)(command.Center.Y * context.Factor),
                (int)-command.Angle);
            return MarkErrorCode.None;
        }

        protected override double EstimateExecutionTimeCore(MarkCircleCommand command, TimeEstimationContext timeContext)
        {
            double arcLength = Math.Abs(command.Angle) / 360.0 * 2.0 * Math.PI * command.Radius;
            double time = 0;
            if (timeContext.MarkSpeed > 0)
                time += arcLength / timeContext.MarkSpeed;
            time += (timeContext.MarkDelay + timeContext.LaserOnDelay) / 1000.0;

            // 计算圆弧打标结束后的坐标位置（基于当前圆弧起点与夹角）
            var angleRad = command.Angle * Math.PI / 180.0;
            var startPoint = timeContext.HasLastPosition
                ? timeContext.LastPosition
                : new PointF(command.Center.X + command.Radius, command.Center.Y);

            var vx = startPoint.X - command.Center.X;
            var vy = startPoint.Y - command.Center.Y;

            var cos = Math.Cos(angleRad);
            var sin = Math.Sin(angleRad);

            var endX = command.Center.X + (float)(vx * cos - vy * sin);
            var endY = command.Center.Y + (float)(vx * sin + vy * cos);

            timeContext.UpdatePosition(new PointF(endX, endY));
            return time;
        }
    }
}
