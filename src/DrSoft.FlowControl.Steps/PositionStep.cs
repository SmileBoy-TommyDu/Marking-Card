using DrSoft.FlowControl.Engine;
using DrSoft.FlowControl.ProcessComponent;

namespace DrSoft.FlowControl.Steps
{
    /// <summary>
    /// 定位步骤
    /// </summary>
    public class PositionStep : AtomicStep
    {
        private double _x;
        private double _y;
        private double _z;

        public PositionStep(string name, double x, double y, double z = 0)
        {
            Name = name;
            _x = x;
            _y = y;
            _z = z;
            EstimatedDuration = TimeSpan.FromMilliseconds(500);
        }

        protected override async Task<StepResult> OnExecuteAsync(ProcessContext context,
            IProgress<StepProgress> progress = null)
        {
            var stage = context.GetService<IMotionController>();
            if (stage == null)
                return StepResult.Failed("运动控制器未注册");

            // 应用补偿
            if (context.HasData("CompensationX"))
            {
                _x += context.GetData<double>("CompensationX");
                _y += context.GetData<double>("CompensationY");
            }

            // 执行移动
            await stage.MoveToAsync(_x, _y, _z);

            // 等待到位
            int timeout = 5000;
            while (!stage.IsInPosition(_x, _y, 0.01) && timeout > 0)
            {
                await Task.Delay(10);
                timeout -= 10;
            }

            if (!stage.IsInPosition(_x, _y, 0.01))
                return StepResult.Failed("定位超时");

            // 更新上下文
            context.CurrentX = _x;
            context.CurrentY = _y;
            context.CurrentZ = _z;

            return StepResult.Succeeded($"定位至 ({_x:F3}, {_y:F3}, {_z:F3})");
        }

        public override IProcessStep Clone()
        {
            return new PositionStep(Name, _x, _y, _z);
        }
    }
}
