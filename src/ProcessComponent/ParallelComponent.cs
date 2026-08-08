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
    /// 并行组合组件（同时执行所有子组件）
    /// </summary>
    public class ParallelComponent : IProcessStep
    {
        public string Id { get; } = Guid.NewGuid().ToString();
        public string Name { get; set; }
        public StepType Type => StepType.Parallel;
        public List<IProcessStep> Children { get; set; } = new List<IProcessStep>();

        private readonly ILogger _logger;

        public ParallelComponent(string name, ILogger logger)
        {
            Name = name;
            _logger = logger;
        }

        public ParallelComponent AddChild(IProcessStep step)
        {
            Children.Add(step);
            return this;
        }

        public async Task<StepResult> ExecuteAsync(ProcessContext context, IProgress<StepProgress> progress = null)
        {
            var result = new StepResult
            {
                StepId = Id,
                StepName = Name,
                StartTime = DateTime.Now
            };


            _logger.LogInformation($"开始执行并行组件: {Name}, 共 {Children.Count} 个分支");

            var tasks = Children.Select(step => Task.Run(() => step.ExecuteAsync(context, progress)));
            var results = await Task.WhenAll(tasks);

            var failedResults = results.Where(r => !r.Success).ToList();

            if (failedResults.Any())
            {
                result.Success = false;
                result.Message = $"并行执行失败: {failedResults.Count} 个分支失败";
                result.Data = failedResults;
            }
            else
            {
                result.Success = true;
                result.Message = "所有分支执行成功";
            }

            result.EndTime = DateTime.Now;
            result.Duration = result.EndTime - result.StartTime;

            _logger.LogInformation($"并行组件执行完成: {Name}, 成功: {result.Success}");

            return result;
        }

        public IProcessStep Clone()
        {
            var clone = new ParallelComponent($"{Name}_Clone", _logger);
            foreach (var child in Children)
            {
                clone.AddChild(child.Clone());
            }
            return clone;
        }

        public Dictionary<string, object> GetParameters() => new Dictionary<string, object>
        {
            ["ChildCount"] = Children.Count
        };

        public void SetParameters(Dictionary<string, object> parameters) { }
    }
}
