using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.MarkCommand;
using System.Drawing;


namespace DrSoft.MarkCard.Interface
{
    public interface IMarkCardAdapter: IDisposable
    {

        event Action<uint, MarkingState> OnMarkingEnd;

        uint CardNum { get; }

        /// <summary>
        /// 初始化打标卡
        /// </summary>
        MarkErrorCode Initialize();

        /// <summary>
        /// 暂停打标
        /// </summary>
        MarkErrorCode Pause();

        /// <summary>
        /// 暂停打标
        /// </summary>
        /// <param name="cardNo">打标编号</param>
        MarkErrorCode Pause(uint cardNo);

        //恢复打标
        MarkErrorCode Resume();

        /// <summary>
        /// 恢复打标
        /// </summary>
        /// <param name="cardNo"></param>
        MarkErrorCode Resume(uint cardNo);

        /// <summary>
        /// 长出光
        /// </summary>
        /// <returns></returns>
        MarkErrorCode LaserOn();

        /// <summary>
        /// 长出光
        /// </summary>
        /// <param name="cardNo"></param>
        MarkErrorCode LaserOn(uint cardNo);


        MarkErrorCode LaserOff();

        MarkErrorCode LaserOff(uint cardNo);


        /// <summary>
        /// 下发打标数据
        /// </summary>
        /// <param name="cardNo"></param>
        /// <param name="condition"></param>
        /// <param name="layer"></param>
        /// <returns></returns>
        public MarkErrorCode LoadMarkData(uint cardNo, List<IMarkCommand> commands);

        /// <summary>
        /// 设置打标模式，IO模式或软件模式
        /// </summary>
        /// <param name="mode"></param>
        MarkErrorCode SetMarkingMode(MarkingMode mode);

        /// <summary>
        /// 设置打标模式
        /// </summary>
        /// <param name="cardNo"></param>
        /// <param name="mode"></param>
        MarkErrorCode SetMarkingMode(uint cardNo, MarkingMode mode);

        /// <summary>
        /// 设置振镜加速度
        /// </summary>
        /// <param name="cardNo"></param>
        /// <param name="headID"></param>
        /// <param name="acc"></param>
        MarkErrorCode SetScannerAcc(uint cardNo, uint headID, double acc);

        /// <summary>
        /// 设置矩阵旋转
        /// </summary>
        /// <param name="cardNo"></param>
        /// <param name="headID"></param>
        /// <param name="m00"></param>
        /// <param name="m01"></param>
        /// <param name="m10"></param>
        /// <param name="m11"></param>
        MarkErrorCode SetTransformMatrix(uint cardNo, uint headID, float m00, float  m01, float m10, float m11);

        ///// <summary>
        ///// 设置旋转角度
        ///// </summary>
        ///// <param name="cardNo"></param>
        ///// <param name="headID"></param>
        ///// <param name="transformAngle"></param>
        //MarkStandardError SetScannerTransformAngle(uint cardNo, uint headID, double transformAngle);

        /// <summary>
        /// 设置缩放比例
        /// </summary>
        /// <param name="cardNo"></param>
        /// <param name="headID"></param>
        /// <param name="transformScale"></param>
        MarkErrorCode SetScale(uint cardNo, uint headID, double transformScale);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="cardNo"></param>
        /// <param name="headID"></param>
        /// <param name="xOffset"></param>
        /// <param name="yOffset"></param>
        MarkErrorCode SetOffset(uint cardNo, uint headID, double xOffset, double yOffset,double angleOffset);

        MarkErrorCode GetLaserFrequency(uint cardNo,out double frequency);

        MarkErrorCode GetLaserPulseWidth(uint cardNo,out double pulseWidth);

        MarkErrorCode SetScannerSpeed(uint cardNo, double jumpSpeed, double markSpeed);


        MarkErrorCode GetJumpDelay(uint cardNo,out double jumpDelay);

        MarkErrorCode GetMarkingDelay(uint cardNo,out double markingDelay);

        MarkErrorCode GetPolygonDelay(uint cardNo,out double polygonDelay);

      

        MarkErrorCode GetLaserDelay(uint cardNo, out double laserOnDelay,out double laserOffDelay);

        /// <summary>
        /// 查询打标卡振镜连接状态
        /// </summary>
        /// <param name="cardNo"></param>
        /// <param name="headID"></param>
        /// <returns></returns>
        MarkErrorCode GetScannerConnect(uint cardNo, uint headID,out bool connectFlag);

        /// <summary>
        /// 获取扫描头位置，单位mm
        /// </summary>
        /// <param name="cardNo"></param>
        /// <param name="headID"></param>
        /// <param name="point"></param>
        /// <returns></returns>
        MarkErrorCode GetScannerPosion(uint cardNo, uint headID,out PointF point);

        /// <summary>
        /// 获取扫描头温度
        /// </summary>
        /// <param name="cardNo"></param>
        /// <param name="headID"></param>
        /// <param name="temperatureX"></param>
        /// <param name="temperatureY"></param>
        /// <returns></returns>
        MarkErrorCode GetScannerTemperature(uint cardNo, uint headID, out double temperatureX, out double temperatureY);

        /// <summary>
        /// 获取预计打标时间，单位msD
        /// </summary>
        /// <param name="cardNo"></param>
        /// <returns></returns>
        int GetEstimatedExecTime(uint cardNo);

        /// <summary>
        /// 获取实际打标时间，ms
        /// </summary>
        /// <param name="cardNo"></param>
        /// <returns></returns>
        MarkErrorCode GetRealExecTime(uint cardNo, out int execTime);


        MarkErrorCode ReadDigitalInput(uint cardNo,out bool[] value);

        MarkErrorCode ReadDigitalOutput(uint cardNo, out bool[] value);

        MarkErrorCode WriteDigitalOutput(uint cardNo,uint signalIndex, bool setParam);

        MarkErrorCode WriteDigitalOutput(uint cardNo, bool[] setParam);

        /// <summary>
        /// 获取振镜加速度
        /// </summary>
        /// <param name="cardNo"></param>
        /// <param name="headID"></param>
        /// <returns></returns>
        MarkErrorCode GetScannerAcc(uint cardNo, uint headID,out double acc);

        /// <summary>
        /// 导入校正档
        /// </summary>
        /// <param name="headsFile"></param>
        /// <returns></returns>
        MarkErrorCode LoadCalibrationFile(string? head1File, string? head2File);

        MarkErrorCode GetMarkingMode(uint cardNo,out MarkingMode mode);

        MarkErrorCode GetMarkingState(uint cardNo,out MarkingState state);

        MarkErrorCode LoadCalibrationFile(uint cardNo, string? head1File, string? head2File);

        /// <summary>
        /// 创建校正档
        /// </summary>
        /// <param name="cardNo">打标卡编号，从1开始</param>
        /// <param name="srcFile"></param>
        /// <param name="dstFile"></param>
        /// <param name="targetPostX"></param>
        /// <param name="targetPostY"></param>
        /// <param name="realsPostX"></param>
        /// <param name="realsPostY"></param>
        /// <returns></returns>
        MarkErrorCode CreateCalibrationFile(string srcFile, string dstFile, double[] targetPostX, double[] targetPostY, double[] realsPostX, double[] realsPostY);


        /// <summary>
        /// 设置桶形校正，RTC打标卡不支持该功能
        /// </summary>
        /// <param name="cardNo">打标卡编号，从1开始</param>
        /// <param name="widthParam">田字格3条横线长度(从下到上)</param>
        /// <param name="heightParam">田字格3条竖线高度(从左到右)</param>
        /// <returns></returns>
        MarkErrorCode SetBarrelCorrection(uint cardNo,double idealWidth,double idealHeight, double[] widthParam, double[] heightParam);
      


        /// <summary>
        /// 设置扫描器延时，单位ms
        /// </summary>
        /// <param name="cardNo">打标卡编号，从1开始</param>
        /// <param name="markDelay">打标延时</param>
        /// <param name="jumpDelay">跳转延时</param>
        /// <param name="polygonDelay">多边形延时</param>
        /// <returns></returns>
        MarkErrorCode SetScannerDelay(uint cardNo, int markDelay, int jumpDelay,  int polygonDelay);

        /// <summary>
        /// 设置激光延时，单位ms
        /// </summary>
        /// <param name="cardNo">打标卡编号，从1开始</param>
        /// <param name="laserOnDelay">激光开启延时</param>
        /// <param name="laserOffDelay">激光关闭延时</param>
        /// <returns></returns>
        MarkErrorCode SetLaserDelay(uint cardNo, int laserOnDelay, int laserOffDelay);

        /// <summary>
        /// 设置激光频率和脉宽
        /// </summary>
        /// <param name="cardNo">打标卡编号，从1开始</param>
        /// <param name="frequency">激光频率</param>
        /// <param name="pulseWidth">激光脉宽</param>
        /// <returns></returns>
        MarkErrorCode SetLaserFrequencyAndPulseWidth(uint cardNo, double frequency, double pulseWidth);

        MarkErrorCode SetLaserFrequency(uint cardNo,double frequency);

        MarkErrorCode StartMarking(uint cardNo);

        MarkErrorCode StopMarking(uint cardNo);

        MarkErrorCode StartMarking();

        MarkErrorCode StopMarking();

        MarkErrorCode SetLaserPower(uint cardNo, double power);

      
    }
}
