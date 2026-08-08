using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace DrSoft.FlowControl.ProcessComponent
{
    /// <summary>
    /// 延迟组件
    /// </summary>
    public class DelayStep : AtomicStep
    {
        private TimeSpan _delay;

        public DelayStep(string name, TimeSpan delay)
            : base()
        {
            Name = name;
            _delay = delay;
        }

        public DelayStep(string name, int milliseconds, ILogger<DelayStep> logger)
            : this(name, TimeSpan.FromMilliseconds(milliseconds)) { }

        protected override async Task<StepResult> OnExecuteAsync(ProcessContext context,
            IProgress<StepProgress> progress = null)
        {
            var startTime = DateTime.Now;
            var totalMs = _delay.TotalMilliseconds;
            var interval = Math.Min(100, totalMs);

            for (double elapsed = 0; elapsed < totalMs; elapsed += interval)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                await Task.Delay((int)Math.Min(interval, totalMs - elapsed), context.CancellationToken);

                progress?.Report(new StepProgress
                {
                    StepId = Id,
                    StepName = Name,
                    Percentage = elapsed / totalMs * 100,
                    Status = $"延迟中... {(int)(elapsed / totalMs * 100)}%"
                });
            }

            return StepResult.Succeeded($"延迟 {_delay.TotalMilliseconds}ms 完成");
        }

        public override IProcessStep Clone()
        {
            // 这里需要传递 logger，假设有 _logger 字段可用
            return new DelayStep(Name, _delay);
        }
    }
}
