using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.MarkCommand;

namespace DrSoft.MarkCard.RTC.CommandProcessors
{
    /// <summary>
    /// 速度修改命令处理器
    /// </summary>
    public class ModifySpeedProcessor : RTC6MarkCommandProcessorBase<ModifySpeedCommand>
    {
        public override MarkCommandType CommandType => MarkCommandType.ModifySpeed;

        protected override MarkErrorCode ProcessCore(ModifySpeedCommand command, RTC6ProcessContext context)
        {
            RTC6Wrap.n_set_jump_speed(context.CardNo, command.JumpSpeed * context.Factor);
            RTC6Wrap.n_set_mark_speed(context.CardNo, command.MarkSpeed * context.Factor);

            var param = GetOrCreateProcessParam(context);
            param.MarkSpeed = command.MarkSpeed;
            param.JumpSpeed = command.JumpSpeed;
            return MarkErrorCode.None;
        }

        protected override double EstimateExecutionTimeCore(ModifySpeedCommand command, TimeEstimationContext timeContext)
        {
            timeContext.JumpSpeed = command.JumpSpeed;
            timeContext.MarkSpeed = command.MarkSpeed;
            return 0;
        }
    }
}
