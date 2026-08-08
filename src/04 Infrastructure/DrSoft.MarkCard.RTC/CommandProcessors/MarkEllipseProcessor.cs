using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.MarkCommand;

namespace DrSoft.MarkCard.RTC.CommandProcessors
{
    /// <summary>
    /// 椭圆打标命令处理器
    /// </summary>
    public class MarkEllipseProcessor : RTC6MarkCommandProcessorBase<MarkEllipseCommand>
    {
        public override MarkCommandType CommandType => MarkCommandType.MarkEllipse;

        protected override MarkErrorCode ProcessCore(MarkEllipseCommand command, RTC6ProcessContext context)
        {
            double angle1 = -command.StartAngle;
            double angle2 = -(command.StartAngle + command.SweepAngle);

            RTC6Wrap.set_ellipse(
                (uint)(command.MajorRadius * context.Factor),
                (uint)(command.MinorRadius * context.Factor),
                angle1, angle2);
            RTC6Wrap.n_mark_ellipse_abs(context.CardNo,
                (int)(command.Center.X * context.Factor),
                (int)(command.Center.Y * context.Factor),
                (int)command.Alpha);
            return MarkErrorCode.None;
        }
    }
}
