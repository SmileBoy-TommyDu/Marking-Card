using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.MarkCommand;

namespace DrSoft.MarkCard.RTC.CommandProcessors
{
    /// <summary>
    /// 点打标命令处理器
    /// </summary>
    public class MarkPointProcessor : RTC6MarkCommandProcessorBase<MarkPointCommand>
    {
        public override MarkCommandType CommandType => MarkCommandType.MarkPoint;

        protected override MarkErrorCode ProcessCore(MarkPointCommand command, RTC6ProcessContext context)
        {
            double dotDuration = Math.Round(command.DotDuration / 10.0);
            RTC6Wrap.n_laser_on_list(context.CardNo, (uint)dotDuration);
            return MarkErrorCode.None;
        }

        protected override double EstimateExecutionTimeCore(MarkPointCommand command, TimeEstimationContext timeContext)
        {
            double time = command.DotDuration / 1000.0 + (timeContext.MarkDelay + timeContext.LaserOnDelay) / 1000.0;
            timeContext.UpdatePosition(command.Point);
            return time;
        }
    }
}
