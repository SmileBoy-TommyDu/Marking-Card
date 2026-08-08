using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.MarkCommand;

namespace DrSoft.MarkCard.RTC.CommandProcessors
{
    /// <summary>
    /// 激光频率和脉宽修改命令处理器
    /// </summary>
    public class ModifyFrequencyPulsesProcessor : RTC6MarkCommandProcessorBase<ModifyFrequencyAndPulsesWidthCommand>
    {
        /// <summary>
        /// 脉宽转换因子（64 = 1/64 μs per count）
        /// </summary>
        private const double PulseWidthFactor = 64.0;

        public override MarkCommandType CommandType => MarkCommandType.ModifyFrequencyAndPulsesWidth;

        protected override MarkErrorCode ProcessCore(ModifyFrequencyAndPulsesWidthCommand command, RTC6ProcessContext context)
        {
            context.Frequency = command.Frequency;

            double period = 1.0 / command.Frequency * 1.0e6;
            double halfPeriod = period / 2.0;

            //RTC6Wrap.n_set_laser_pulses(context.CardNo,
            //    (uint)(halfPeriod * PulseWidthFactor),
            //    (uint)(command.PulsesWidth * PulseWidthFactor));

            var param = GetOrCreateProcessParam(context);
            param.Frequency = command.Frequency;
            param.Pulse = command.PulsesWidth;
            return MarkErrorCode.None;
        }

        protected override double EstimateExecutionTimeCore(ModifyFrequencyAndPulsesWidthCommand command, TimeEstimationContext timeContext)
        {
            timeContext.Frequency = command.Frequency;
            return 0;
        }
    }
}
