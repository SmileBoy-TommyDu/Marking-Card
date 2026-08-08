using Microsoft.Extensions.Logging;

namespace DrSoft.FlowControl.ProcessComponent
{
    /// <summary>
    /// 顺序组合组件（按顺序执行子组件）
    /// </summary>
    public class SequenceComponent : IProcessStep
    {
        public string Id { get; } = Guid.NewGuid().ToString();
        public string Name { get; set; }
        public StepType Type => StepType.Sequence;
        public List<IProcessStep> Children { get; set; } = new List<IProcessStep>();

        private readonly ILogger _logger;

        public SequenceComponent(string name, ILogger logger)
        {
            Name = name;
            _logger = logger;
        }

        public SequenceComponent AddChild(IProcessStep step)
        {
            Children.Add(step);
            return this;
        }

        public SequenceComponent InsertChild(int index, IProcessStep step)
        {
            Children.Insert(index, step);
            return this;
        }

        public bool RemoveChild(string stepId)
        {
            var step = Children.FirstOrDefault(s => s.Id == stepId);
            return step != null && Children.Remove(step);
        }

        public void MoveChild(int fromIndex, int toIndex)
        {
            var step = Children[fromIndex];
            Children.RemoveAt(fromIndex);
            Children.Insert(toIndex, step);
        }

        public async Task<StepResult> ExecuteAsync(ProcessContext context, IProgress<StepProgress> progress = null)
        {
            var result = new StepResult
            {
                StepId = Id,
                StepName = Name,
                StartTime = DateTime.Now
            };

            _logger.LogInformation($"开始执行顺序组件: {Name}, 共 {Children.Count} 个步骤");

            for (int i = 0; i < Children.Count; i++)
            {
                // 检查取消
                context.CancellationToken.ThrowIfCancellationRequested();

                // 检查暂停状态
                while (context.State == ProcessState.Paused)
                {
                    await Task.Delay(100, context.CancellationToken);
                }

                // 检查报警状态
                if (context.State == ProcessState.Alarm)
                {
                    result.Success = false;
                    result.Message = "流程因报警暂停";
                    break;
                }

                var step = Children[i];
                context.CurrentStepIndex = i;
                context.CurrentStepId = step.Id;

                // 报告进度
                progress?.Report(new StepProgress
                {
                    StepId = step.Id,
                    StepName = step.Name,
                    Percentage = (double)i / Children.Count * 100,
                    Status = "执行中"
                });

                _logger.LogDebug($"执行步骤 {i + 1}/{Children.Count}: {step.Name}");

                var stepResult = await step.ExecuteAsync(context, progress);
                context.OnStepCompleted?.Invoke(stepResult);

                if (!stepResult.Success)
                {
                    if (stepResult.IsAlarm)
                    {
                        // 报警已由上下文处理，等待消除
                        result.IsAlarm = true;
                        result.AlarmCode = stepResult.AlarmCode;
                    }

                    result.Success = false;
                    result.Message = stepResult.Message;
                    break;
                }
            }

            result.Success = result.Success ? true : false;
            result.EndTime = DateTime.Now;
            result.Duration = result.EndTime - result.StartTime;

            if (result.Success)
            {
                _logger.LogInformation($"顺序组件执行完成: {Name}, 耗时: {result.Duration.TotalMilliseconds}ms");
            }
            else
            {
                _logger.LogError($"顺序组件执行失败: {Name}, 原因: {result.Message}");
            }

            return result;
        }

        public IProcessStep Clone()
        {
            var clone = new SequenceComponent($"{Name}_Clone", _logger);
            foreach (var child in Children)
            {
                clone.AddChild(child.Clone());
            }
            return clone;
        }

        public Dictionary<string, object> GetParameters()
        {
            return new Dictionary<string, object>
            {
                ["ChildCount"] = Children.Count,
                ["Children"] = Children.Select(c => c.GetParameters()).ToList()
            };
        }

        public void SetParameters(Dictionary<string, object> parameters) { }

        public IReadOnlyList<IProcessStep> GetChildren() => Children.AsReadOnly();
    }
}
