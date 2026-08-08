using DrSoft.FlowControl.Engine;
using DrSoft.FlowControl.ProcessComponent;

namespace DrSoft.FlowControl.Steps
{
    /// <summary>
    /// 视觉检测步骤
    /// </summary>
    public class VisionInspectionStep : AtomicStep
    {
        private string _templateName;
        private double _tolerance;

        public VisionInspectionStep(string name, string templateName, double tolerance = 0.1)
        {
            Name = name;
            _templateName = templateName;
            _tolerance = tolerance;
            EstimatedDuration = TimeSpan.FromMilliseconds(500);
        }

        protected override async Task<StepResult> OnExecuteAsync(ProcessContext context,
            IProgress<StepProgress> progress = null)
        {
            var vision = context.GetService<IVisionSystem>();
            if (vision == null)
                return StepResult.Failed("视觉系统未注册");

            progress?.Report(new StepProgress
            {
                StepId = Id,
                StepName = Name,
                Percentage = 50,
                Status = "检测中..."
            });

            var result = await vision.InspectAsync(_templateName);

            if (!result.Success)
                return StepResult.Failed($"检测失败: {result.Message}");

            if (result.Score < _tolerance)
                return StepResult.Failed($"检测不合格: 得分 {result.Score:F2} < {_tolerance:F2}");

            context.SetData("InspectionScore", result.Score);
            context.SetData("InspectionResult", result);

            return StepResult.Succeeded($"检测合格: 得分 {result.Score:F2}", result);
        }

        public override IProcessStep Clone()
        {
            return new VisionInspectionStep(Name, _templateName, _tolerance);
        }
    }
}
