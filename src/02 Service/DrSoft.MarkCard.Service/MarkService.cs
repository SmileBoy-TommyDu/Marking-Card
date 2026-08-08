using DrSoft.MarkCard.Interface;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.DTO;
using Microsoft.Extensions.Logging;

namespace DrSoft.MarkCard.Service
{
    public class MarkService: IDisposable
    {
        private readonly IMarkController _markController;
      
        private readonly ILogger<MarkService> _logger;

        public event Action<uint, MarkingState> OnMarkingEnd;
        

        public MarkService(IMarkController markController,ILogger<MarkService> logger)
        {
            _markController = markController;

            
            _markController.OnMarkingEnd += (cardNo, state) =>
            {
                OnMarkingEnd?.Invoke(cardNo, state);
                _logger.LogInformation("Marking process ended for card number {CardNo} with state {State}.", cardNo, state);
            };

            _logger = logger;
        }

        public MarkErrorCode Initialize()
        {
            _logger.LogInformation("Initializing marking service.");
            return _markController.Initialize();
        }

        public MarkErrorCode StopMarking(uint cardNo)
        {
            _logger.LogInformation("Stopping marking process for card number {CardNo}.", cardNo);
            return _markController.StopMarking(cardNo);
        }

        public MarkErrorCode LoadMarkData(uint cardNo, MarkingJobDto markData)
        {
            _logger.LogInformation("Loading mark data for card number {CardNo}.", cardNo);
            return _markController.LoadMarkData(cardNo, markData);
        }

        public MarkErrorCode StartMarking(uint cardNo)
        {
            _logger.LogInformation("Starting marking process for card number {CardNo}.", cardNo);
            return _markController.StartMarking(cardNo);
        }

        public MarkErrorCode GetRealExecTime(uint cardNo, out int execTime)
        {
           return _markController.GetRealExecTime(cardNo, out execTime);
        }

        public int GetEstimatedExecTime(uint cardNo)
        {
            return _markController.GetEstimatedExecTime(cardNo);
        }

        /// <summary>
        /// 暂停打标
        /// </summary>
        public MarkErrorCode Pause(uint cardNo)
        {
            _logger.LogInformation("Pausing marking process for card number {CardNo}.", cardNo);
            return _markController.Pause(cardNo);
        }


        public MarkErrorCode ReadDigitalInput(uint cardNo, out bool[] value)
        {
            return _markController.ReadDigitalInput(cardNo, out value);
        }

        public MarkErrorCode ReadDigitalOutput(uint cardNo, out bool[] value)
        {
            return _markController.ReadDigitalOutput(cardNo, out value);
        }

        public MarkErrorCode WriteDigitalOutput(uint cardNo, uint signalIndex, bool setParam)
        {
            return _markController.WriteDigitalOutput(cardNo, signalIndex, setParam);
        }

        public MarkErrorCode LaserOn(uint cardNo)
        {
            return _markController.LaserOn(cardNo);
        }

        public MarkErrorCode LaserOff(uint cardNo)
        {
            return _markController.LaserOff(cardNo);
        }

        public MarkErrorCode SetLaserFrequency(uint cardNo, double frequency)
        {
            return _markController.SetLaserFrequency(cardNo, frequency);
        }
        public MarkErrorCode SetLaserPower(uint cardNo, double power)
        {
            return _markController.SetLaserPower(cardNo, power);
        }

        public MarkErrorCode SetOffset(uint cardNo, uint headID, double xOffset, double yOffset, double angleOffset)
        {
            return _markController.SetOffset(cardNo, headID, xOffset, yOffset, angleOffset);
        }

        public MarkErrorCode SetScale(uint cardNo, uint headID, double transformScale)
        {
            return _markController.SetScale(cardNo, headID, transformScale);
        }

        public MarkErrorCode SetOffsetScale(uint cardNo, uint headID, double xOffset, double yOffset, double angleOffset,double scaleX,double scaleY) 
        {
            MarkErrorCode errorCode = MarkErrorCode.None;

            //初始化缩放
            errorCode = _markController.SetScale(cardNo, headID, 1);
            if (errorCode != MarkErrorCode.None) return errorCode;
            // 使用 SetTransformMatrix 方法设置缩放矩阵
            errorCode = _markController.SetTransformMatrix(cardNo, headID, (float)scaleX, 0, 0, (float)scaleY);
           
            if (errorCode != MarkErrorCode.None) return errorCode;
            
             return   _markController.SetOffset(cardNo, headID, xOffset, yOffset, angleOffset);
        }

        public MarkErrorCode SetLaserDelay(uint cardNo, int laserOnDelay, int laserOffDelay)
        {
            return _markController.SetLaserDelay( cardNo,  laserOnDelay,  laserOffDelay);
        }

        /// <summary>
        /// 设置矩阵旋转
        /// </summary>
        /// <param name="cardNo"></param>
        /// <param name="headID"></param>
        /// <param name="m00"></param>
        /// <param name="m01"></param>
        /// <param name="m10"></param>
        /// <param name="m11"></param>
        public MarkErrorCode SetTransformMatrix(uint cardNo, uint headID, float m00, float m01, float m10, float m11)
        {
            _logger.LogInformation("set maxtrix for card number {CardNo}. {headId} m00:{m00}, m01:{m01}, m10:{m10}, m11:{m11}", cardNo,headID, m00, m01, m10, m11);
            return _markController.SetTransformMatrix(cardNo, headID, m00, m01, m10, m11);
        
        }

        public MarkErrorCode SetBarrelCorrection(uint cardNo, double idealWidth, double idealHeight, double[] widthParam, double[] heightParam)
        {
            return _markController.SetBarrelCorrection(cardNo, idealWidth,  idealHeight, widthParam, heightParam);
        }

        public void Dispose()
        {
            _markController.Dispose();
        }
    }
}
