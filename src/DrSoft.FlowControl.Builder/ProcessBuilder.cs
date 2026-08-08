using DrSoft.FlowControl.ProcessComponent;
using DrSoft.FlowControl.Steps;
using Microsoft.Extensions.Logging;

namespace DrSoft.FlowControl.Builder
{
    /// <summary>
    /// 流程构建器（建造者模式）
    /// </summary>
    public class ProcessBuilder
    {
        private SequenceComponent _root;
        private readonly ILogger _logger;

        public ProcessBuilder(string name, ILogger logger)
        {
            _logger = logger;
            _root = new SequenceComponent(name, _logger);
        }

        public ProcessBuilder AddPosition(string name, double x, double y, double z = 0)
        {
            _root.AddChild(new PositionStep(name, x, y, z));
            return this;
        }

        public ProcessBuilder AddMarking(string name, byte[] data, double speed = 1000, double power = 80, double frequency = 20000)
        {
            _root.AddChild(new MarkingStep(name, data, speed, power, frequency));
            return this;
        }

        public ProcessBuilder AddDelay(string name, int milliseconds)
        {
            TimeSpan delay = TimeSpan.FromMilliseconds(milliseconds);
            _root.AddChild(new DelayStep(name, delay));
            return this;
        }

        public ProcessBuilder AddInspection(string name, string templateName, double tolerance = 0.1)
        {
            _root.AddChild(new VisionInspectionStep(name, templateName, tolerance));
            return this;
        }

        public ProcessBuilder AddLoop(string name, Action<ProcessBuilder> buildBody, int iterations)
        {
            var bodyBuilder = new ProcessBuilder($"{name}_Body", _logger);
            buildBody(bodyBuilder);

            var loop = new LoopComponent(name, bodyBuilder.Build(), _logger, iterations);
            _root.AddChild(loop);
            return this;
        }

        public ProcessBuilder AddConditional(string name,
            Func<ProcessContext, Task<bool>> condition,
            Action<ProcessBuilder> buildTrueBranch,
            Action<ProcessBuilder> buildFalseBranch = null)
        {
            var trueBuilder = new ProcessBuilder($"{name}_True", _logger);
            buildTrueBranch(trueBuilder);

            IProcessStep falseBranch = null;
            if (buildFalseBranch != null)
            {
                var falseBuilder = new ProcessBuilder($"{name}_False", _logger);
                buildFalseBranch(falseBuilder);
                falseBranch = falseBuilder.Build();
            }

            var conditional = new ConditionalComponent(name, condition, _logger, trueBuilder.Build(), falseBranch);
            _root.AddChild(conditional);
            return this;
        }

        public ProcessBuilder AddParallel(string name, Action<ProcessBuilder> buildBranch1, Action<ProcessBuilder> buildBranch2)
        {
            var branch1Builder = new ProcessBuilder($"{name}_Branch1", _logger);
            buildBranch1(branch1Builder);

            var branch2Builder = new ProcessBuilder($"{name}_Branch2", _logger);
            buildBranch2(branch2Builder);

            var parallel = new ParallelComponent(name, _logger);
            parallel.AddChild(branch1Builder.Build());
            parallel.AddChild(branch2Builder.Build());

            _root.AddChild(parallel);
            return this;
        }

        public IProcessStep Build()
        {
            return _root;
        }
    }
}
