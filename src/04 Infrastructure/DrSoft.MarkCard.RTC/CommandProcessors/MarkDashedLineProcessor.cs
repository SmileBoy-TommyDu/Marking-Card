using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.MarkCommand;
using Microsoft.Extensions.Logging;
using RTC6ADDONImport;
using System.Drawing;

namespace DrSoft.MarkCard.RTC.CommandProcessors
{
    /// <summary>
    /// 虚线打标命令处理器（基于 RTC6ADDON ShortVector 短向量功能）。
    /// <para>
    /// 核心设计：
    /// 1. activateShortVectors 全局仅激活一次
    /// 2. 多条虚线在同一个 ShortVector 会话中批量处理
    /// 3. 虚线之间的 JumpCommand 通过 AddShortVectorJump 插入 Type=0 跳转
    /// 4. LoadMarkData 结束后调用 FlushShortVectors 统一发送命令到 RTC 列表
    /// </para>
    /// <para>
    /// DashArray 中的点两两交替表示实线段和空白段的终点：
    /// [实线终点, 空白终点, 实线终点, 空白终点, ...]
    /// </para>
    /// </summary>
    public class MarkDashedLineProcessor : RTC6MarkCommandProcessorBase<MarkDashedLineCommand>
    {
        public override MarkCommandType CommandType => MarkCommandType.MarkDashedLineCommand;

        #region ShortVector 状态

        /// <summary>
        /// 已添加的 ShortVector 命令总数
        /// </summary>
        private int _commandCount;



        /// <summary>
        /// 默认振镜时间滞后（μs）
        /// </summary>
        private const double DefaultTimeLag = 100.0;

        /// <summary>
        /// 默认传输延迟（μs）
        /// </summary>
        private const uint DefaultTransportDelay = 200;

        #endregion

        #region 公共方法（供 RTC6Adapter 调用）

       


        /// <summary>
        /// 将积累的短向量命令刷新到 RTC 列表。
        /// 在 LoadMarkData 末尾或遇到不兼容命令时调用。
        /// 流程: finalizeShortVectorInput → sendShortVectorCommandsToRtc → resetShortVectors
        /// </summary>
        public MarkErrorCode FlushShortVectors(uint cardNo, ILogger? logger)
        {
            
                
                logger?.LogInformation("[ShortVector] 开始刷新短向量命令, 共 {0} 条命令", _commandCount);

                // Step 1: 完成输入
                int finalizeResult = RTC6ADDONWrap.n_finalizeShortVectorInput(cardNo);
                if (finalizeResult != 0)
                {
                    logger?.LogError("[ShortVector] finalizeShortVectorInput 失败, 错误码: {0}", finalizeResult);
                    RTC6ADDONWrap.n_resetShortVectors(cardNo);
          ;
                    return MarkErrorCode.UnknownError;
                }

                // Step 2: 发送 SVP 处理后的命令到 RTC 执行列表
                int sendResult = RTC6ADDONWrap.n_sendShortVectorCommandsToRtc(cardNo);
                if (sendResult != 0)
                {
                    logger?.LogError("[ShortVector] sendShortVectorCommandsToRtc 失败, 错误码: {0}", sendResult);
                    RTC6ADDONWrap.n_resetShortVectors(cardNo);
            
                    return MarkErrorCode.UnknownError;
                }

                // Step 3: 释放短向量资源（仅释放当前会话，不解除全局激活）
                RTC6ADDONWrap.n_resetShortVectors(cardNo);

                logger?.LogInformation("[ShortVector] 短向量命令刷新完成, 共发送 {0} 条命令", _commandCount);

                return MarkErrorCode.None;
        
        }

        #endregion

        #region 核心处理

        protected override MarkErrorCode ProcessCore(MarkDashedLineCommand command, RTC6ProcessContext context)
        {
            var dashArray = command.DashArray;
            if (dashArray == null || dashArray.Count == 0)
                return MarkErrorCode.None;

          
                var factor = context.Factor > 0 ? context.Factor : 1;

                // 获取工艺参数并计算速度
                var processParam = GetOrCreateProcessParam(context);
                double markSpeed = processParam.MarkSpeed;
                if (markSpeed <= 0) markSpeed = 1.0;


            //RTC6ADDONWrap.n_addShortVectorCommand(context.CardNo,(int)(dashArray[0].X * factor), (int)(dashArray[0].Y * factor),0);

            // 逐点添加 ShortVector 命令
            for (int i = 0; i < dashArray.Count; i++)
                {
                    double xBits = dashArray[i].X * factor;
                    double yBits = dashArray[i].Y * factor;

                    if (i % 2 == 0)
                    {
                        // 偶数索引：实线段终点 → Mark（Type=1，出光）
                        RTC6ADDONWrap.n_addShortVectorCommand(context.CardNo, xBits, yBits, 0);
                    }
                    else
                    {
                        // 奇数索引：空白段终点 → Jump（Type=0，不出光）
                        RTC6ADDONWrap.n_addShortVectorCommand(context.CardNo, xBits, yBits, 1);
                    }
                }

        
                _commandCount += dashArray.Count;

                context.Logger?.LogDebug(
                    "[ShortVector] 添加虚线命令: {0} 个点, 累计 {1} 条命令",
                    dashArray.Count, _commandCount);
        

            return MarkErrorCode.None;
        }

        protected override double EstimateExecutionTimeCore(MarkDashedLineCommand command, TimeEstimationContext timeContext)
        {
            var dashArray = command.DashArray;
            if (dashArray == null || dashArray.Count == 0)
                return 0;

            double totalSolidLength = 0;
            var lastPos = timeContext.HasLastPosition ? timeContext.LastPosition : PointF.Empty;

            for (int i = 0; i < dashArray.Count; i++)
            {
                if (i % 2 == 0)
                {
                    // 实线段：累计长度
                    double dx = dashArray[i].X - lastPos.X;
                    double dy = dashArray[i].Y - lastPos.Y;
                    totalSolidLength += Math.Sqrt(dx * dx + dy * dy);
                }
                lastPos = dashArray[i];
            }

            double time = 0;
            if (timeContext.MarkSpeed > 0)
                time += totalSolidLength / timeContext.MarkSpeed;
            time += (timeContext.MarkDelay + timeContext.LaserOnDelay) / 1000.0;

            timeContext.UpdatePosition(dashArray[dashArray.Count - 1]);
            return time;
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pixelPitch">像素间距（bit），即相邻像素中心的距离。
        /// 该值决定了虚线的分辨率，通常设置为光斑直径。</param>
        /// <param name="speed_bit_per_us"></param>
        /// <returns></returns>
        private uint CalculateHalfPeriod(double pixelPitch, double speed_bit_per_us = 1.0)
        {
            if (speed_bit_per_us <= 0) speed_bit_per_us = 1.0;

            uint halfPeriod = (uint)Math.Round(pixelPitch * 32.0 / speed_bit_per_us);

            return halfPeriod;
        }

        /// <summary>
        /// 初始化 ShortVector 会话（每张卡每次打标只调用一次 n_initShortVectors）
        /// </summary>
        public MarkErrorCode InitShortVectorSession(uint cardNo, float factor, RTC6ProcessContext context,
            ILogger? logger)
        {

            var markSpeed = GetOrCreateProcessParam(context).MarkSpeed;


                var halfPeriod = CalculateHalfPeriod(50, markSpeed);
                uint pulseOn = Math.Max(1, halfPeriod / 4);

                RTC6Wrap.n_set_laser_pulses(cardNo, halfPeriod, pulseOn);

                RTC6Wrap.n_set_laser_delays(cardNo, 0, 0);
                double speedBitsPerMs = markSpeed * factor;

                int initResult = RTC6ADDONWrap.n_initShortVectors(cardNo, DefaultTimeLag, DefaultTransportDelay, speedBitsPerMs);
                if (initResult != 0)
                {
                    logger?.LogError("[ShortVector] 初始化短向量失败, 错误码: {0}", initResult);
                    return MarkErrorCode.UnknownError;
                }

               
                logger?.LogInformation("[ShortVector] 初始化成功, Card={0}, Speed={1:F2} bits/ms",
                    cardNo, speedBitsPerMs);
            

           
            return MarkErrorCode.None;
        }

  

        #endregion
    }
}
