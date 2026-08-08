using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.MarkCommand;
using System.Diagnostics;

namespace DrSoft.MarkCard.RTC.CommandProcessors
{
    /// <summary>
    /// 直线打标命令处理器
    /// </summary>
    public class MarkLineProcessor : RTC6MarkCommandProcessorBase<MarkLineCommand>
    {
        public override MarkCommandType CommandType => MarkCommandType.MarkLine;

        protected override MarkErrorCode ProcessCore(MarkLineCommand command, RTC6ProcessContext context)
        {
            RTC6Wrap.n_mark_abs(context.CardNo, (int)(command.EndPoint.X * context.Factor), (int)(command.EndPoint.Y * context.Factor));
            //Debug.WriteLine("line {0} {1}", (command.EndPoint.X ), (command.EndPoint.Y));
            return MarkErrorCode.None;
        }

        protected override double EstimateExecutionTimeCore(MarkLineCommand command, TimeEstimationContext timeContext)
        {
            double time = 0;
            if (timeContext.HasLastPosition && timeContext.MarkSpeed > 0)
                time += timeContext.DistanceTo(command.EndPoint) / timeContext.MarkSpeed;
            time += (timeContext.MarkDelay + timeContext.LaserOnDelay) / 1000.0;
            timeContext.UpdatePosition(command.EndPoint);
            return time;
        }
    }
}
