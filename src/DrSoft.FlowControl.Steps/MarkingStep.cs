using DrSoft.FlowControl.Engine;
using DrSoft.FlowControl.ProcessComponent;
using DrSoft.MarkCard.Interface;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.DTO;

namespace DrSoft.FlowControl.Steps
{
    /// <summary>
    /// 打标步骤
    /// </summary>
    public class MarkingStep : AtomicStep
    {
        private byte[] _markingData;
        private double _speed;
        private double _power;
        private double _frequency;

        private uint _cardNo;
        private MarkingJobDto _markData;
        private string _fileName;
        private bool _dataOrFile;

        public MarkingStep(string name, byte[] markingData, double speed = 1000, double power = 80, double frequency = 20000)
        {
            Name = name;
            _markingData = markingData;
            _speed = speed;
            _power = power;
            _frequency = frequency;
            EstimatedDuration = TimeSpan.FromSeconds(1);
        }

        /// <summary>
        /// 打标步骤构造函数，支持从文件或数据结构加载打标数据
        /// </summary>
        /// <param name="name"></param>
        /// <param name="cardNo">打标卡编号</param>
        /// <param name="markData">打标数据</param>
        /// <param name="fileName">打标文件，带图形和参数</param>
        /// <param name="dataOrFile">true：用markData，false：fileName</param>
        public MarkingStep(string name, uint cardNo, MarkingJobDto markData,string fileName, bool dataOrFile)
        {
            Name = name;
            _cardNo = cardNo;
            _markData = markData;
            _fileName = fileName;
            _dataOrFile = dataOrFile;
            EstimatedDuration = TimeSpan.FromSeconds(1);
        }

        protected override async Task<StepResult> OnExecuteAsync(ProcessContext context,
            IProgress<StepProgress> progress = null)
        {
            var marker = context.GetService<IMarkController>();
            if (marker == null)
                return StepResult.Failed("打标控制器未注册");

            //// 设置参数
            //marker.SetPower(_power);
            //marker.SetFrequency(_frequency);
            //marker.SetSpeed(_speed);

            // 加载数据
            if (_dataOrFile)
            {
                var loadResult = marker.LoadMarkData(_cardNo, _markData);
                if (loadResult != MarkErrorCode.None)
                    return StepResult.Failed("加载打标数据失败");
            }
            else
            {
                var loadResult = marker.LoadMarkData(_cardNo, _markData);
                if (loadResult != MarkErrorCode.None)
                    return StepResult.Failed("加载打标文件失败");
            }

            // 开始打标
            if (marker.StartMarking(_cardNo) != MarkErrorCode.None)
                return StepResult.Failed("启动打标失败");

            //// 监控进度
            //while (marker.IsBusy)
            //{
            //    context.CancellationToken.ThrowIfCancellationRequested();

            //    var progressPercent = marker.GetProgress();
            //    progress?.Report(new StepProgress
            //    {
            //        StepId = Id,
            //        StepName = Name,
            //        Percentage = progressPercent,
            //        Status = $"打标中... {progressPercent:F1}%"
            //    });

            //    await Task.Delay(50);
            //}

            //// 检查结果
            //var result = marker.GetResult();
            //if (!result.Success)
            //    return StepResult.Failed(result.ErrorMessage);

            //return StepResult.Succeeded($"打标完成，耗时: {result.Duration}ms", result);
            return StepResult.Succeeded($"打标完成");
        }

        public override IProcessStep Clone()
        {
            return new MarkingStep(Name, _markingData, _speed, _power, _frequency);
        }
    }
}
