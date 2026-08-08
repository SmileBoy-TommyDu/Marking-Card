using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.MarkCommand;
using Microsoft.Extensions.Logging;

namespace DrSoft.MarkCard.RTC.CommandProcessors
{
    /// <summary>
    /// 位图打标命令处理器（暂未实现）
    /// </summary>
    public class MarkBitmapProcessor : RTC6MarkCommandProcessorBase<MarkBitmapCommand>
    {
        public override MarkCommandType CommandType => MarkCommandType.MarkBitmapCommand;

        protected override MarkErrorCode ProcessCore(MarkBitmapCommand command, RTC6ProcessContext context)
        {
            context.Logger?.LogWarning($"不支持的打标命令类型: {command.MarkCommandType}");
            return MarkErrorCode.None; // TODO: 实现位图打标
        }
    }
}
