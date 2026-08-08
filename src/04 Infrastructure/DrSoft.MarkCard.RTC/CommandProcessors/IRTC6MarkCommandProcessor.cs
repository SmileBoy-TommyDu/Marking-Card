using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.MarkCommand;
using Microsoft.Extensions.Logging;
using System.Drawing;

namespace DrSoft.MarkCard.RTC.CommandProcessors
{
    /// <summary>
    /// RTC6 打标命令处理器上下文，携带处理器所需的适配器状态
    /// </summary>
    public class RTC6ProcessContext
    {
        /// <summary>
        /// 打标卡编号
        /// </summary>
        public uint CardNo { get; set; }

        /// <summary>
        /// 当前频率（可变，供 ModifyFrequencyAndPulsesWidth 处理器更新）
        /// </summary>
        public double Frequency { get; set; }

        /// <summary>
        /// 坐标缩放因子
        /// </summary>
        public float Factor { get; set; }

        /// <summary>
        /// 日志记录器
        /// </summary>
        public ILogger? Logger { get; set; }

        /// <summary>
        /// 获取或创建工艺参数的委托
        /// </summary>
        public Func<uint, ProcessParam> GetOrCreateProcessParam { get; set; } = null!;

       
    }

    /// <summary>
    /// 预估执行时间上下文，跟踪打标过程中的可变状态
    /// </summary>
    public class TimeEstimationContext
    {
        /// <summary>
        /// 当前打标速度（mm/ms）
        /// </summary>
        public double MarkSpeed { get; set; } = 1.0;

        /// <summary>
        /// 当前跳转速度（mm/ms）
        /// </summary>
        public double JumpSpeed { get; set; } = 10.0;

        /// <summary>
        /// 打标延时（μs）
        /// </summary>
        public double MarkDelay { get; set; } = 20.0;

        /// <summary>
        /// 跳转延时（μs）
        /// </summary>
        public double JumpDelay { get; set; } = 200.0;

        /// <summary>
        /// 开光延时（μs）
        /// </summary>
        public double LaserOnDelay { get; set; } = 100.0;

        /// <summary>
        /// 当前频率（Hz）
        /// </summary>
        public double Frequency { get; set; } = 200.0;

        /// <summary>
        /// 上一个位置
        /// </summary>
        public PointF LastPosition { get; set; } = PointF.Empty;

        /// <summary>
        /// 是否已有上一个位置
        /// </summary>
        public bool HasLastPosition { get; set; }

        /// <summary>
        /// 更新上一个位置
        /// </summary>
        public void UpdatePosition(PointF position)
        {
            LastPosition = position;
            HasLastPosition = true;
        }

        /// <summary>
        /// 计算到目标点的距离（mm）
        /// </summary>
        public double DistanceTo(PointF target)
        {
            if (!HasLastPosition) return 0;
            double dx = target.X - LastPosition.X;
            double dy = target.Y - LastPosition.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    /// <summary>
    /// RTC6 打标命令处理器接口
    /// </summary>
    public interface IRTC6MarkCommandProcessor
    {
        /// <summary>
        /// 支持的命令类型
        /// </summary>
        MarkCommandType CommandType { get; }

        /// <summary>
        /// 处理打标命令
        /// </summary>
        MarkErrorCode Process(IMarkCommand command, RTC6ProcessContext context);

        /// <summary>
        /// 预估命令执行时间（毫秒），同时更新时间估算上下文中的可变状态
        /// </summary>
        double EstimateExecutionTime(IMarkCommand command, TimeEstimationContext timeContext);
    }

    /// <summary>
    /// RTC6 打标命令处理器泛型基类，提供类型安全和通用逻辑
    /// </summary>
    public abstract class RTC6MarkCommandProcessorBase<T> : IRTC6MarkCommandProcessor where T : IMarkCommand
    {
        public abstract MarkCommandType CommandType { get; }

        public MarkErrorCode Process(IMarkCommand command, RTC6ProcessContext context)
        {
            if (command is not T typed)
                return MarkErrorCode.None;

            return ProcessCore(typed, context);
        }

        public double EstimateExecutionTime(IMarkCommand command, TimeEstimationContext timeContext)
        {
            if (command is not T typed)
                return 0;

            return EstimateExecutionTimeCore(typed, timeContext);
        }

        /// <summary>
        /// 处理类型化的打标命令（由子类实现）
        /// </summary>
        protected abstract MarkErrorCode ProcessCore(T command, RTC6ProcessContext context);

        /// <summary>
        /// 预估类型化命令的执行时间（由子类实现，默认返回 0）
        /// </summary>
        protected virtual double EstimateExecutionTimeCore(T command, TimeEstimationContext timeContext) => 0;

        /// <summary>
        /// 获取或创建当前打标卡的工艺参数
        /// </summary>
        protected static ProcessParam GetOrCreateProcessParam(RTC6ProcessContext context)
            => context.GetOrCreateProcessParam(context.CardNo);
    }
}
