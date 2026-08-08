using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.FlowControl.ProcessComponent
{
    /// <summary>
    /// 条件分支组件
    /// </summary>
    public class ConditionalComponent : IProcessStep
    {
        public string Id { get; } = Guid.NewGuid().ToString();
        public string Name { get; set; }
        public StepType Type => StepType.Conditional;
        public Func<ProcessContext, Task<bool>> Condition { get; set; }
        public IProcessStep TrueBranch { get; set; }
        public IProcessStep FalseBranch { get; set; }

        private readonly ILogger _logger;

        public ConditionalComponent(string name, Func<ProcessContext, Task<bool>> condition, ILogger logger,
            IProcessStep trueBranch, IProcessStep falseBranch = null)
        {
            Name = name;
            Condition = condition;
            TrueBranch = trueBranch;
            FalseBranch = falseBranch;
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

            var conditionResult = await Condition(context);
            var branch = conditionResult ? TrueBranch : FalseBranch;

            _logger.LogInformation($"条件判断: {Name}, 结果: {conditionResult}, 分支: {(branch != null ? branch.Name : "无")}");

            if (branch != null)
            {
                var branchResult = await branch.ExecuteAsync(context, progress);
                result.Success = branchResult.Success;
                result.Message = branchResult.Message;
                result.Data = branchResult.Data;
            }
            else
            {
                result.Success = true;
                result.Message = $"条件: {conditionResult}, 无分支执行";
            }

            result.EndTime = DateTime.Now;
            result.Duration = result.EndTime - result.StartTime;

            return result;
        }

        public IProcessStep Clone()
        {
            return new ConditionalComponent($"{Name}_Clone", Condition, _logger,
                TrueBranch?.Clone(), FalseBranch?.Clone());
        }

        public Dictionary<string, object> GetParameters() => new Dictionary<string, object>();
        public void SetParameters(Dictionary<string, object> parameters) { }
    }
}
