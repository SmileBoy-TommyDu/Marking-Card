using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.MarkCommand;

namespace DrSoft.MarkCard.RTC.CommandProcessors
{
    /// <summary>
    /// 激光延时修改命令处理器
    /// </summary>
    public class ModifyLaserDelayProcessor : RTC6MarkCommandProcessorBase<ModifyLaserDelayCommand>
    {
        public override MarkCommandType CommandType => MarkCommandType.ModifyLaserDelay;

        protected override MarkErrorCode ProcessCore(ModifyLaserDelayCommand command, RTC6ProcessContext context)
        {
            RTC6Wrap.n_set_laser_delays(context.CardNo,
                (int)command.LaserOnDelay * 64,
                (uint)command.LaserOffDelay * 64);

            var param = GetOrCreateProcessParam(context);
            param.LaserOnDelay = command.LaserOnDelay;
            param.LaserOffDelay = command.LaserOffDelay;
            return MarkErrorCode.None;
        }

        protected override double EstimateExecutionTimeCore(ModifyLaserDelayCommand command, TimeEstimationContext timeContext)
        {
            timeContext.LaserOnDelay = command.LaserOnDelay;
            return 0;
        }
    }
}
