using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.MarkCommand;

namespace DrSoft.MarkCard.RTC.CommandProcessors
{
    /// <summary>
    /// SkyWriting 命令处理器
    /// </summary>
    public class SkyWritingProcessor : RTC6MarkCommandProcessorBase<SkyWritingCommand>
    {
        public override MarkCommandType CommandType => MarkCommandType.SkyWritingCommand;

        protected override MarkErrorCode ProcessCore(SkyWritingCommand command, RTC6ProcessContext context)
        {
            if (command.AngleLimit <= 180 && command.AngleLimit >= 0)
            {
                double limit = 0;
                limit = Math.Cos(command.AngleLimit * Math.PI / 180);
                RTC6Wrap.n_set_sky_writing_limit_list(context.CardNo,limit);
            }
            
            RTC6Wrap.n_set_sky_writing_para_list(context.CardNo,
                (uint)command.Timelag,
                (int)command.LaserOnShift*64,
                (uint)command.Nprev/10,
                (uint)command.Npost/10);
            RTC6Wrap.n_set_sky_writing_mode_list(context.CardNo, command.SkyWritingModel);
            return MarkErrorCode.None;
        }
    }
}
