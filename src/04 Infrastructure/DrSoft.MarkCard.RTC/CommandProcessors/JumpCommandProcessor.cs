using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.MarkCommand;
using System.Diagnostics;

namespace DrSoft.MarkCard.RTC.CommandProcessors
{
    /// <summary>
    /// 跳转命令处理器
    /// </summary>
    public class JumpCommandProcessor : RTC6MarkCommandProcessorBase<JumpCommand>
    {
        public override MarkCommandType CommandType => MarkCommandType.JumpCommand;

        protected override MarkErrorCode ProcessCore(JumpCommand command, RTC6ProcessContext context)
        {
            RTC6Wrap.n_jump_abs(context.CardNo, (int)(command.Point.X * context.Factor), (int)(command.Point.Y * context.Factor));
            Debug.WriteLine("jump {0} {1}", (command.Point.X), (command.Point.Y));
            return MarkErrorCode.None;
        }

        protected override double EstimateExecutionTimeCore(JumpCommand command, TimeEstimationContext timeContext)
        {
            double time = 0;
            if (timeContext.HasLastPosition && timeContext.JumpSpeed > 0)
                time += timeContext.DistanceTo(command.Point) / timeContext.JumpSpeed;
            time += timeContext.JumpDelay / 1000.0;
            timeContext.UpdatePosition(command.Point);
            return time;
        }
    }
}
