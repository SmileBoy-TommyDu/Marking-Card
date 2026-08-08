using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.MarkCommand;

namespace DrSoft.MarkCard.RTC.CommandProcessors
{
    /// <summary>
    /// 振镜延时修改命令处理器
    /// </summary>
    public class ModifyScannerDelayProcessor : RTC6MarkCommandProcessorBase<ModifyScannerDelayCommand>
    {
        public override MarkCommandType CommandType => MarkCommandType.ModifyScannerDelay;

        protected override MarkErrorCode ProcessCore(ModifyScannerDelayCommand command, RTC6ProcessContext context)
        {
            RTC6Wrap.n_set_scanner_delays(context.CardNo,
                (uint)command.JumpDelay / 10,
                (uint)command.MarkDelay / 10,
                (uint)command.CornerDelay / 10);

            var param = GetOrCreateProcessParam(context);
            param.MarkDelay = command.MarkDelay;
            param.JumpDelay = command.JumpDelay;
            param.PolyDelay = command.CornerDelay;
            return MarkErrorCode.None;
        }

        protected override double EstimateExecutionTimeCore(ModifyScannerDelayCommand command, TimeEstimationContext timeContext)
        {
            timeContext.MarkDelay = command.MarkDelay;
            timeContext.JumpDelay = command.JumpDelay;
            return 0;
        }
    }
}
