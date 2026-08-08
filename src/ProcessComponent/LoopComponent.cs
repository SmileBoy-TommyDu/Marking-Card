using DrSoft.FlowControl.ProcessComponent;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.FlowControl.ProcessComponent
{
    /// <summary>
    /// 循环组件
    /// </summary>
    public class LoopComponent : IProcessStep
    {
        public string Id { get; } = Guid.NewGuid().ToString();
        public string Name { get; set; }
        public StepType Type => StepType.Loop;
        public IProcessStep Body { get; set; }
        public int MaxIterations { get; set; } = 1;
        public Func<ProcessContext, Task<bool>> Condition { get; set; }
        public LoopType LoopStyle { get; set; } = LoopType.CountControlled;

        private readonly ILogger _logger;

        public enum LoopType
        {
            CountControlled,    // 计数控制
            ConditionControlled // 条件控制
        }

        public LoopComponent(string name, IProcessStep body, ILogger logger, int maxIterations = 1)
        {
            Name = name;
            Body = body;
            MaxIterations = maxIterations;
            LoopStyle = LoopType.CountControlled;
            _logger = logger;
        }

        public LoopComponent(string name, IProcessStep body, ILogger logger, Func<ProcessContext, Task<bool>> condition)
        {
            Name = name;
            Body = body;
            Condition = condition;
            LoopStyle = LoopType.ConditionControlled;
            _logger = logger;
        }

        public async Task<StepResult> ExecuteAsync(ProcessContext context, IProgress<StepProgress> progress = null)
        {
            var result = new StepResult
            {
                StepId = Id,
                StepName = Name,
                StartTime = DateTime.Now
            };

            int iteration = 0;

            _logger.LogInformation($"开始循环: {Name}, 类型: {LoopStyle}");

            while (true)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                // 检查暂停状态
                while (context.State == ProcessState.Paused)
                {
                    await Task.Delay(100, context.CancellationToken);
                }

                // 检查循环条件
                bool shouldContinue;

                if (LoopStyle == LoopType.CountControlled)
                {
                    shouldContinue = iteration < MaxIterations;
                }
                else
                {
                    shouldContinue = await Condition(context);
                }

                if (!shouldContinue) break;

                iteration++;

                // 报告进度
                progress?.Report(new StepProgress
                {
                    StepId = Id,
                    StepName = Name,
                    Percentage = LoopStyle == LoopType.CountControlled
                        ? (double)iteration / MaxIterations * 100
                        : 0,
                    Status = $"循环 {iteration}"
                });

                _logger.LogDebug($"循环迭代 {iteration}: {Name}");

                var loopResult = await Body.ExecuteAsync(context, progress);

                if (!loopResult.Success)
                {
                    result.Success = false;
                    result.Message = $"循环失败于第 {iteration} 次: {loopResult.Message}";
                    break;
                }
            }

            result.Success = result.Success ? true : false;
            result.EndTime = DateTime.Now;
            result.Duration = result.EndTime - result.StartTime;

            _logger.LogInformation($"循环完成: {Name}, 共执行 {iteration} 次");

            return result;
        }

        public IProcessStep Clone()
        {
            if (LoopStyle == LoopType.CountControlled)
            {
                return new LoopComponent($"{Name}_Clone", Body.Clone(), _logger, MaxIterations);
            }
            else
            {
                return new LoopComponent($"{Name}_Clone", Body.Clone(), _logger, Condition);
            }
        }

        public Dictionary<string, object> GetParameters() => new Dictionary<string, object>
        {
            ["LoopType"] = LoopStyle.ToString(),
            ["MaxIterations"] = MaxIterations
        };

        public void SetParameters(Dictionary<string, object> parameters)
        {
            if (parameters.TryGetValue("MaxIterations", out var iterations))
                MaxIterations = Convert.ToInt32(iterations);
        }
    }
}
