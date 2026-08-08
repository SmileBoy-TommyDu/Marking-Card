using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.MarkCommand;

namespace DrSoft.MarkCard.RTC.CommandProcessors
{
    /// <summary>
    /// 激光功率修改命令处理器
    /// </summary>
    public class ModifyPowerProcessor : RTC6MarkCommandProcessorBase<ModifyPowerCommand>
    {
        /// <summary>
        /// 最大激光功率值（12位DAC）
        /// </summary>
        private const uint MaxLaserPower = 4095;

        public override MarkCommandType CommandType => MarkCommandType.ModifyPower;

        protected override MarkErrorCode ProcessCore(ModifyPowerCommand command, RTC6ProcessContext context)
        {
            uint powerValue = (uint)(MaxLaserPower * command.Power / 100);
            RTC6Wrap.n_set_laser_power(context.CardNo, 0, powerValue);

            var param = GetOrCreateProcessParam(context);
            param.Power = command.Power;
            return MarkErrorCode.None;
        }
    }
}
