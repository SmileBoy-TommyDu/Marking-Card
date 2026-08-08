using DrSoft.MarkCard.Interface;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.Config;
using DrSoft.MarkCard.Model.Enum;
using DrSoft.MarkCard.Model.MarkCommand;
using Microsoft.Extensions.Logging;
using System.Drawing;
using static DrSoft.MarkCard.BoChu.InvokeGalvoApiDll;

namespace DrSoft.MarkCard.BoChu
{
    public class BCGAdapter : IMarkCardAdapter
    {
        private uint _cardNum;

        public uint CardNum => _cardNum;

        public event Action<uint, MarkingState> OnMarkingEnd;

        private Config config;

        private CardConfig cardConfig;

        public BCGAdapter(ILogger<BCGAdapter> logger, Config config)
        {
            //this.cardParam = cardParam;
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.cardConfig = config.CardConfigs.Find(x=>x.IsActive);
            if (cardConfig == null) throw new Exception("未找到激活的打标卡");
            _logger = logger;
        }

        private readonly ILogger<BCGAdapter> _logger;


     


        public MarkErrorCode CreateCalibrationFile(string srcFile, string dstFile, double[] targetPostX, double[] targetPostY, double[] realsPostX, double[] realsPostY)
        {
            // 根据GalvoAPI2文档，创建校正文件需要使用BC2_ExecRoughCorrection或BC2_ExecFineCorrection
            // 这里实现粗校正逻辑
            if (targetPostX == null || targetPostY == null || realsPostX == null || realsPostY == null)
            {
                _logger.LogError("校正数据不能为空");
                return MarkErrorCode.InvalidParameter;
            }

            if (targetPostX.Length != targetPostY.Length || realsPostX.Length != realsPostY.Length)
            {
                _logger.LogError("校正数据数组长度不匹配");
                return MarkErrorCode.InvalidParameter;
            }

            // 这里需要先将数据写入srcFile，然后执行校正
            // 由于API没有直接提供创建校正文件的方法，这里返回不支持
            _logger.LogWarning("创建校正文件功能需要手动准备数据文件");
            return MarkErrorCode.UnknownError;
        }

     
        //
        public void Dispose()
        {
            FreeCardResource();
        }

        public int GetEstimatedExecTime(uint cardNo)
        {
            int cardID = -1;
            try
            {
                cardID = GetCardID(cardNo);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"未找到对应的卡号: {cardNo}");
                return 0;
            }

            int workTime = 0;
            int errCode = BC2_GetListWorkTime(cardID, workTime, 1);
            if (errCode != 0)
            {
                _logger.LogError($"获取预计执行时间失败，ErrCode:{errCode}");
                return 0;
            }

            return workTime;
        }

        public MarkErrorCode GetJumpDelay(uint cardNo, out double jumpDelay)
        {
            jumpDelay = 0;
            // GalvoAPI2没有直接获取jump delay的API，返回默认值
            _logger.LogWarning("获取JumpDelay功能未实现，返回默认值");
            return MarkErrorCode.None;
        }

        public MarkErrorCode GetLaserDelay(uint cardNo, out double laserOnDelay, out double laserOffDelay)
        {
            laserOnDelay = 0;
            laserOffDelay = 0;
            // GalvoAPI2没有直接获取laser delay的API，返回默认值
            _logger.LogWarning("获取LaserDelay功能未实现，返回默认值");
            return MarkErrorCode.None;
        }

        public MarkErrorCode GetLaserFrequency(uint cardNo, out double frequency)
        {
            frequency = 0;
            // GalvoAPI2没有直接获取频率的API，返回默认值
            _logger.LogWarning("获取LaserFrequency功能未实现，返回默认值");
            return MarkErrorCode.None;
        }

        public MarkErrorCode GetLaserPulseWidth(uint cardNo, out double pulseWidth)
        {
            pulseWidth = 0;
            // GalvoAPI2没有直接获取脉宽的API，返回默认值
            _logger.LogWarning("获取LaserPulseWidth功能未实现，返回默认值");
            return MarkErrorCode.None;
        }

        public MarkErrorCode GetMarkingDelay(uint cardNo, out double markingDelay)
        {
            markingDelay = 0;
            // GalvoAPI2没有直接获取marking delay的API，返回默认值
            _logger.LogWarning("获取MarkingDelay功能未实现，返回默认值");
            return MarkErrorCode.None;
        }

        public MarkErrorCode GetMarkingMode(uint cardNo, out MarkingMode mode)
        {
            mode = MarkingMode.SoftwareMode;
            // GalvoAPI2没有直接获取marking mode的API，返回默认软件模式
            _logger.LogWarning("获取MarkingMode功能未实现，返回默认软件模式");
            return MarkErrorCode.None;
        }

        public MarkErrorCode GetMarkingState(uint cardNo, out MarkingState state)
        {
            int cardID = -1;
            try
            {
                cardID = GetCardID(cardNo);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"未找到对应的卡号: {cardNo}");
                state = MarkingState.None;
                return MarkErrorCode.UnmatchedMarkCardNo;
            }

            int listState = 0;
            int errCode = BC2_GetListState(cardID, ref listState, 1);
            if (errCode != 0)
            {
                _logger.LogError($"获取打标状态失败，ErrCode:{errCode}");
                state = MarkingState.None;
                return GetErrorCode(errCode);
            }

            // 0 空闲， 1 正在执行
            state = listState == 1 ? MarkingState.Marking : MarkingState.None;
            return MarkErrorCode.None;
        }

        public MarkErrorCode GetPolygonDelay(uint cardNo, out double polygonDelay)
        {
            polygonDelay = 0;
            // GalvoAPI2没有直接获取polygon delay的API，返回默认值
            _logger.LogWarning("获取PolygonDelay功能未实现，返回默认值");
            return MarkErrorCode.None;
        }

        public MarkErrorCode GetRealExecTime(uint cardNo, out int execTime)
        {
            int cardID = -1;
            try
            {
                cardID = GetCardID(cardNo);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"未找到对应的卡号: {cardNo}");
                execTime = 0;
                return MarkErrorCode.UnmatchedMarkCardNo;
            }

            int workTime = 0;
            int errCode = BC2_GetListWorkTime(cardID, workTime, 1);
            if (errCode != 0)
            {
                _logger.LogError($"获取实际执行时间失败，ErrCode:{errCode}");
                execTime = 0;
                return GetErrorCode(errCode);
            }

            execTime = workTime;
            return MarkErrorCode.None;
        }

        public MarkErrorCode GetScannerAcc(uint cardNo, uint headID, out double acc)
        {
            acc = 0;
            // GalvoAPI2没有直接获取振镜加速度的API，返回默认值
            _logger.LogWarning("获取ScannerAcc功能未实现，返回默认值");
            return MarkErrorCode.None;
        }

        public MarkErrorCode GetScannerConnect(uint cardNo, uint headID, out bool connectFlag)
        {
            int cardID = -1;
            try
            {
                cardID = GetCardID(cardNo);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"未找到对应的卡号: {cardNo}");
                connectFlag = false;
                return MarkErrorCode.UnmatchedMarkCardNo;
            }

            // 通过获取固件版本来判断是否连接
            IntPtr versionPtr = IntPtr.Zero;
            int errCode = BC2_GetFirmWareVer(cardID, ref versionPtr);
            connectFlag = (errCode == 0 && versionPtr != IntPtr.Zero);
            
            if (errCode != 0)
            {
                _logger.LogWarning($"获取振镜连接状态失败，ErrCode:{errCode}");
                return GetErrorCode(errCode);
            }

            return MarkErrorCode.None;
        }

        public MarkErrorCode GetScannerPosion(uint cardNo, uint headID, out PointF point)
        {
            point = PointF.Empty;
            // GalvoAPI2没有直接获取振镜当前位置的API，需要通过监控功能实现
            // 这里返回默认值
            _logger.LogWarning("获取ScannerPosition功能未完全实现，返回默认值");
            return MarkErrorCode.None;
        }

        public MarkErrorCode GetScannerTemperature(uint cardNo, uint headID, out double temperatureX, out double temperatureY)
        {
            temperatureX = 0;
            temperatureY = 0;
            // GalvoAPI2没有直接获取振镜温度的API
            _logger.LogWarning("获取ScannerTemperature功能未实现");
            return MarkErrorCode.UnknownError;
        }

        private string CardConfigFliePath = @"D:\fsdata\UltraScan\7\machine.config";

        private Dictionary<uint, int> cardNoList = new Dictionary<uint, int>(); 

        public MarkErrorCode Initialize()
        {
            int CardID;
            int ListPos = 1;    //图纸缓存位置
            int Count = 0;

            TGalvoCardInfo[] CardInfo;

            if (config != null && cardConfig != null && !string.IsNullOrEmpty(cardConfig.CardConfigFliePath))
            {
                CardConfigFliePath = cardConfig.CardConfigFliePath;
            }

            try
            {
                CardInfo = GetAllCardInfo(ref Count);        //扫卡
            }
            catch (Exception e)
            {
                _logger.LogError(e, "扫描振镜卡失败");
                return MarkErrorCode.UnFoundMarkCard;
            }
            cardNoList = new Dictionary<uint, int>();
            if (Count != 0)
            {
                var errorCode = BC2_InitGalvoSystem();
                if (errorCode != 0)
                {
                    FreeCardResource();
                    return GetErrorCode(errorCode);
                }
                for (int i = 0; i < CardInfo.Length; i++)
                {
                    CardID = -1;

                    errorCode = BC2_InitGalvoCard(CardInfo[i].SerialNum, CardInfo[i].CardIP, ref CardID);
                    if (errorCode != 0)
                    {
                        FreeCardResource();
                        return GetErrorCode(errorCode);
                    }

                    if (CardID >= 1)
                    {
                        cardNoList.Add((uint)(i + 1), CardID);
                        //Console.WriteLine($"初始化成功");
                        BC2_ClearErr(CardID);
                        if (File.Exists(CardConfigFliePath))
                        {
                            ErrCode = BC2_ImportCardConfig(CardID, CardConfigFliePath);    //导入配置
                            if (ErrCode != 0)
                            {

                                _logger.LogError($"导入配置失败，ErrCode:{ErrCode}");
                                return MarkErrorCode.ImportConfigError;
                            }
                        }
                        else
                        {
                            _logger.LogError($"未找到配置文件，路径：{CardConfigFliePath}");
                        }

                        BC2_EnableLaserMO(CardID, 1, 1);
                        BC2_EnableLaserShutter(CardID, 1, 1, 1);
                        BC2_LoadList(CardID, ListPos);              //开始缓存指令
                        BC2_SetPOFConfig_Import_List(CardID);       //设置配置的POF模式
                        BC2_SetEncoderPos_List(CardID, 1, 0, 0);    //编码器1清零
                        BC2_SetEncoderPos_List(CardID, 2, 0, 0);    //编码器2清零
                        if (config != null && config.ScanHeadConfigs != null)
                        {

                            var list = config.ScanHeadConfigs.FindAll(x => x.CardNo == CardID);
                            if (list != null && list.Count > 0)
                            {
                                {
                                    foreach (var scanHeadConfig in list)
                                    {
                                        uint protocol = 0;
                                        if (scanHeadConfig.Protocol == ScanHeadProtocol.XY2_100)
                                        {
                                            protocol = 1;
                                        }
                                        else if (scanHeadConfig.Protocol == ScanHeadProtocol.SL2_100)
                                        {
                                            protocol = 2;
                                        }

                                        else
                                        {
                                            _logger.LogError($"不支持的振镜协议，CardID:{CardID} ScanHeadNo:{scanHeadConfig.ScanHeadNo} Protocol:{scanHeadConfig.Protocol}");
                                            continue;
                                        }
                                        BC2_SetGalvoMechParams(CardID, (int)scanHeadConfig.ScanHeadNo, 100, 0.1, protocol);
                                    }
                                }
                            }
                        }
                    }

                }
            }
            
            else
            {
                _logger.LogError("未扫描到振镜卡");
                return MarkErrorCode.UnFoundMarkCard;
            }

            return MarkErrorCode.None;
        }
 

        public MarkErrorCode LaserOff()
        {
            return LaserOff(1);
        }

        public MarkErrorCode LaserOff(uint cardNo)
        {
            int cardID = -1;

            try
            {
                cardID = GetCardID(cardNo);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "未找到对应的卡号: 1");
                return MarkErrorCode.UnmatchedMarkCardNo;
            }
            int errCode = BC2_EnableLaserAP(cardID, 1, 0);
            if (errCode != 0)
            {
                _logger.LogError($"关闭激光长出光失败，ErrCode:{errCode}");
                return GetErrorCode(errCode);
            }


            return MarkErrorCode.None;
        }

        public MarkErrorCode LaserOn()
        {
            return LaserOn(1);
        }

        public MarkErrorCode LaserOn(uint cardNo)
        {
            int cardID = -1;

            try
            {
                cardID = GetCardID(cardNo);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "未找到对应的卡号: 1");
                return MarkErrorCode.UnmatchedMarkCardNo;
            }
            int errCode = BC2_EnableLaserAP(cardID, 1, 1);
            if (errCode != 0)
            {
                _logger.LogError($"开启激光长出光失败，ErrCode:{errCode}");
                return GetErrorCode(errCode);
            }


            return MarkErrorCode.None;
        }

        public MarkErrorCode LoadCalibrationFile(string? head1File, string? head2File)
        {
            return LoadCalibrationFile(1, head1File, head2File);
        }

        public MarkErrorCode LoadCalibrationFile(uint cardNo, string? head1File, string? head2File)
        {
            int cardID = -1;
            try
            {
                cardID = GetCardID(cardNo);
            } catch (Exception e) {
                _logger.LogError(e, $"未找到对应的卡号: {cardNo}");
                return MarkErrorCode.UnmatchedMarkCardNo;
            }
            if (!string.IsNullOrEmpty(head1File))
            {
                int errCode = BC2_Set2DCorrectionTable(cardID, 1, 1, head1File);
                if (errCode != 0)
                {
                    _logger.LogError($"加载振镜1校正文件失败，ErrCode:{errCode}");
                    return GetErrorCode(errCode);
                }
                errCode = BC2_SelectCorrectionNum(cardID, 1,1);
                if (errCode != 0)
                {
                    _logger.LogError($"选择振镜1校正文件失败，ErrCode:{errCode}");
                    return GetErrorCode(errCode);
                }
            }

            if (!string.IsNullOrEmpty(head2File))
            {
                int errCode = BC2_Set2DCorrectionTable(cardID, 2, 2, head2File);
                if (errCode != 0)
                {
                    _logger.LogError($"加载振镜2校正文件失败，ErrCode:{errCode}");
                    return GetErrorCode(errCode);
                }
                errCode = BC2_SelectCorrectionNum(cardID, 2, 2);
                if (errCode != 0)
                {
                    _logger.LogError($"选择振镜2校正文件失败，ErrCode:{errCode}");
                    return GetErrorCode(errCode);
                }
            }

            return MarkErrorCode.None;
        }

        public MarkErrorCode LoadMarkData(uint cardNo, List<IMarkCommand> commands)
        {
           
            int cardID = -1;
            try
            {
                cardID = GetCardID(cardNo);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"未找到对应的卡号: {cardNo}");
                return MarkErrorCode.UnmatchedMarkCardNo;
            }

            BC2_LoadList(cardID, 1, 1);
            ModifyLaserDelayCommand laserDelayCommand = new ModifyLaserDelayCommand();
            foreach (var command in commands)
            {
                switch (command.MarkCommandType)
                {
                    case MarkCommandType.ModifyLaserDelay:
                        // Handle ModifyLaserDelayCommand
                        laserDelayCommand = command as ModifyLaserDelayCommand;
                        BC2_SetLaserDelay_List(cardID, laserDelayCommand.LaserOffDelay*1000, laserDelayCommand.LaserOffDelay*1000);
                        break;

                    case MarkCommandType.JumpCommand:
                        // Handle JumpCommand
                        var jumpCommand = command as JumpCommand;
                        BC2_JumpLineAbs_List(cardID,jumpCommand.Point.X, jumpCommand.Point.Y);
                        break;
                    case MarkCommandType.MarkLine:
                        // Handle MarkLineCommand
                        var markLineCommand = command as MarkLineCommand;
                        BC2_MarkLineAbs_List(cardID, markLineCommand.EndPoint.X, markLineCommand.EndPoint.Y);
                        break;
                    case MarkCommandType.MarkPoint:
                        // Handle MarkPointCommand
                        var markPointCommand = command as MarkPointCommand;
                        BC2_SetLaserDelay_List(cardID, laserDelayCommand.LaserOnDelay * 1000, (long)markPointCommand.DotDuration * 1000);
                        BC2_JumpLineAbs_List(cardID, markPointCommand.Point.X, markPointCommand.Point.Y);
                        BC2_MarkLineAbs_List(cardID, markPointCommand.Point.X, markPointCommand.Point.Y);

                        BC2_SetLaserDelay_List(cardID, laserDelayCommand.LaserOffDelay * 1000, laserDelayCommand.LaserOffDelay * 1000);
                        //BC2_MarkPointAbs_List(cardID, markPointCommand.Point.X, markPointCommand.Point.Y);
                        break;
                    case MarkCommandType.MarkCircle:
                        // Handle MarkCircleCommand
                        var markCircleCommand = command as MarkCircleCommand;
                        BC2_MarkArcAbs_List(cardID, markCircleCommand.Center.X, markCircleCommand.Center.Y, markCircleCommand.Radius);
                        break;
                    case MarkCommandType.ModifyFrequencyAndPulsesWidth:
                        var modifyFrequencyAndPulsesWidthCommand = command as ModifyFrequencyAndPulsesWidthCommand;
                        //BC2_SetLaserFrequency_List(cardID, modifyFrequencyAndPulsesWidthCommand.Frequency);
                        //BC2_SetLaserPulseWidth_List(cardID, modifyFrequencyAndPulsesWidthCommand.PulseWidth);
                        break;
                }
            }
            BC2_SetEndOfList_List(cardID);

            return MarkErrorCode.None;
        }

        public MarkErrorCode Pause()
        {
            return Pause(1);
        }

        public MarkErrorCode Pause(uint cardNo)
        {
            int cardID = -1;
            try
            {
                cardID = GetCardID(cardNo);

            }
            catch (Exception e)
            {
                _logger.LogError(e, $"未找到对应的卡号: {cardNo}");
                return MarkErrorCode.UnmatchedMarkCardNo;
            }

            int errCode = BC2_StopList(cardID);
            if (errCode != 0)
            {
                _logger.LogError($"暂停激光失败，ErrCode:{errCode}");
                return GetErrorCode(errCode);
            }
            return MarkErrorCode.None;
        }

        public MarkErrorCode ReadDigitalInput(uint cardNo, out bool[] value)
        {
            int cardID = -1;
            try
            {
                cardID = GetCardID(cardNo);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"未找到对应的卡号: {cardNo}");
                value = new bool[8];
                return MarkErrorCode.UnmatchedMarkCardNo;
            }

            int inputState = 0;
            int errCode = BC2_GetInputState(cardID, ref inputState);
            if (errCode != 0)
            {
                _logger.LogError($"读取数字输入失败，ErrCode:{errCode}");
                value = new bool[8];
                return GetErrorCode(errCode);
            }

            // 将int转换为bool数组，8个通道
            value = new bool[8];
            for (int i = 0; i < 8; i++)
            {
                value[i] = (inputState & (1 << i)) != 0;
            }

            return MarkErrorCode.None;
        }

        public MarkErrorCode ReadDigitalOutput(uint cardNo, out bool[] value)
        {
            int cardID = -1;
            try
            {
                cardID = GetCardID(cardNo);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"未找到对应的卡号: {cardNo}");
                value = new bool[8];
                return MarkErrorCode.UnmatchedMarkCardNo;
            }

            int outputState = 0;
            int errCode = BC2_GetOutputState(cardID, ref outputState);
            if (errCode != 0)
            {
                _logger.LogError($"读取数字输出失败，ErrCode:{errCode}");
                value = new bool[8];
                return GetErrorCode(errCode);
            }

            // 将int转换为bool数组，8个通道
            value = new bool[8];
            for (int i = 0; i < 8; i++)
            {
                value[i] = (outputState & (1 << i)) != 0;
            }

            return MarkErrorCode.None;
        }

        public MarkErrorCode Resume()
        {
            return Resume(1);
        }

        public MarkErrorCode Resume(uint cardNo)
        {
            int cardID = -1;    
            try
            {
                cardID = GetCardID(cardNo);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"未找到对应的卡号: {cardNo}");
                return MarkErrorCode.UnmatchedMarkCardNo;
            }

            int listPos = 0;
            int errCode= BC2_GetListPos(cardID, ref listPos, 1);
            if (errCode != 0)
            {
                _logger.LogError($"获取列表位置失败，ErrCode:{errCode}");
                return GetErrorCode(errCode);
            }
            errCode = BC2_StartExecuteList(cardID, listPos);
            if (errCode != 0)
            {
                _logger.LogError($"开始执行列表失败，ErrCode:{errCode}");
                return GetErrorCode(errCode);
            }
            return MarkErrorCode.None;
        }

        public MarkErrorCode SetLaserDelay(uint cardNo, int laserOnDelay, int laserOffDelay)
        {
            int cardID = -1;
            try
            {
                cardID = GetCardID(cardNo);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"未找到对应的卡号: {cardNo}");
                return MarkErrorCode.UnmatchedMarkCardNo;
            }

            // 转换为ns单位 (GalvoAPI2使用ns单位)
            long onDelayNs = laserOnDelay * 1000000; // ms转ns
            long offDelayNs = laserOffDelay * 1000000; // ms转ns
            
            int errCode = BC2_SetLaserDelay_List(cardID, onDelayNs, offDelayNs);
            if (errCode != 0)
            {
                _logger.LogError($"设置激光延时失败，ErrCode:{errCode}");
                return GetErrorCode(errCode);
            }

            return MarkErrorCode.None;
        }

        public MarkErrorCode SetLaserFrequency(uint cardNo, double frequency)
        {
            int cardID = -1;
            try
            {
                cardID = GetCardID(cardNo);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"未找到对应的卡号: {cardNo}");
                return MarkErrorCode.UnmatchedMarkCardNo;
            }

            // 设置AP频率，占空比默认50%
            int errCode = BC2_SetLaserAP_List(cardID, frequency, 0.5);
            if (errCode != 0)
            {
                _logger.LogError($"设置激光频率失败，ErrCode:{errCode}");
                return GetErrorCode(errCode);
            }

            return MarkErrorCode.None;
        }

        public MarkErrorCode SetLaserFrequencyAndPulseWidth(uint cardNo, double frequency, double pulseWidth)
        {
            int cardID = -1;
            try
            {
                cardID = GetCardID(cardNo);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"未找到对应的卡号: {cardNo}");
                return MarkErrorCode.UnmatchedMarkCardNo;
            }

            // 计算占空比：pulseWidth单位为us，需要根据频率计算
            double dutyCycle = pulseWidth * frequency / 1000000.0; // 脉宽(us)*频率(Hz)/1000000
            dutyCycle = Math.Max(0, Math.Min(1, dutyCycle)); // 限制在0-1范围内
            
            int errCode = BC2_SetLaserAP_List(cardID, frequency, dutyCycle);
            if (errCode != 0)
            {
                _logger.LogError($"设置激光频率和脉宽失败，ErrCode:{errCode}");
                return GetErrorCode(errCode);
            }

            return MarkErrorCode.None;
        }

        public MarkErrorCode SetLaserPower(uint cardNo, double power)
        {
            int cardID = -1;
            try
            {
                cardID = GetCardID(cardNo);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"未找到对应的卡号: {cardNo}");
                return MarkErrorCode.UnmatchedMarkCardNo;
            }

            // power范围0-100，转换为0-255的数字功率值
            int digitalValue = (int)Math.Max(0, Math.Min(255, power * 255 / 100.0));
            
            int errCode = BC2_SetLaserDigital_List(cardID, digitalValue);
            if (errCode != 0)
            {
                _logger.LogError($"设置激光功率失败，ErrCode:{errCode}");
                return GetErrorCode(errCode);
            }

            return MarkErrorCode.None;
        }

        public MarkErrorCode SetMarkingMode(MarkingMode mode)
        {
            return SetMarkingMode(1, mode);
        }

        public MarkErrorCode SetMarkingMode(uint cardNo, MarkingMode mode)
        {
            // GalvoAPI2不直接支持设置marking mode
            // 这里可以根据实际需要实现逻辑
            _logger.LogWarning("SetMarkingMode功能在GalvoAPI2中不直接支持");
            return MarkErrorCode.UnknownError;
        }

        public MarkErrorCode SetOffset(uint cardNo, uint headID, double xOffset, double yOffset, double angleOffset)
        {
            int cardID = -1;
            try
            {
                cardID = GetCardID(cardNo);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"未找到对应的卡号: {cardNo}");
                return MarkErrorCode.UnmatchedMarkCardNo;
            }

            // 设置图形偏移
            int errCode = BC2_SetGraphicOffset_List(cardID, (int)headID, xOffset, yOffset);
            if (errCode != 0)
            {
                _logger.LogError($"设置振镜偏移失败，ErrCode:{errCode}");
                return GetErrorCode(errCode);
            }

            // 设置图形旋转（角度偏移）
            if (angleOffset != 0)
            {
                errCode = BC2_SetGraphicRotation_List(cardID, (int)headID, angleOffset);
                if (errCode != 0)
                {
                    _logger.LogError($"设置振镜旋转失败，ErrCode:{errCode}");
                    return GetErrorCode(errCode);
                }
            }

            return MarkErrorCode.None;
        }

        public MarkErrorCode SetScale(uint cardNo, uint headID, double transformScale)
        {
            int cardID = -1;
            try
            {
                cardID = GetCardID(cardNo);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"未找到对应的卡号: {cardNo}");
                return MarkErrorCode.UnmatchedMarkCardNo;
            }

            // 使用BC2_SetGraphicScale_List设置图元缩放比例
            // transformScale是统一缩放比例，X和Y方向使用相同的缩放因子
            int errCode = BC2_SetGraphicScale_List(cardID, (int)headID, transformScale, transformScale);
            if (errCode != 0)
            {
                _logger.LogError($"设置图元缩放失败，ErrCode:{errCode}");
                return GetErrorCode(errCode);
            }

            _logger.LogInformation($"设置图元缩放成功，CardNo:{cardNo}, HeadID:{headID}, Scale:{transformScale}");
            return MarkErrorCode.None;
        }

        public MarkErrorCode SetScannerAcc(uint cardNo, uint headID, double acc)
        {
           

            return MarkErrorCode.UnsupportedFunction;
        }

        public MarkErrorCode SetScannerDelay(uint cardNo, int markDelay, int jumpDelay, int polygonDelay)
        {
            int cardID = -1;
            try
            {
                cardID = GetCardID(cardNo);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"未找到对应的卡号: {cardNo}");
                return MarkErrorCode.UnmatchedMarkCardNo;
            }

            // 转换为10us单位
            long markDelay10us = markDelay * 100; // ms转10us
            long jumpDelay10us = jumpDelay * 100; // ms转10us
            long cornerDelay10us = polygonDelay * 100; // ms转10us
            BC2_LoadList(cardID, 20, 1);
            
            int errCode = BC2_SetScannerDelay_List(cardID, jumpDelay10us, markDelay10us, cornerDelay10us);
            BC2_SetEndOfList_List(cardID);

            errCode = BC2_StartExecuteList(cardID, 1);
            if (errCode != 0)
            {
                _logger.LogError($"设置振镜延时失败，ErrCode:{errCode}");
                return GetErrorCode(errCode);
            }

            return MarkErrorCode.None;
        }

        public MarkErrorCode SetScannerSpeed(uint cardNo, double jumpSpeed, double markSpeed)
        {
            int cardID = -1;
            try
            {
                cardID = GetCardID(cardNo);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"未找到对应的卡号: {cardNo}");
                return MarkErrorCode.UnmatchedMarkCardNo;
            }

            // 设置跳转速度
            int errCode = BC2_SetJumpSpeed_List(cardID, jumpSpeed);
            if (errCode != 0)
            {
                _logger.LogError($"设置跳转速度失败，ErrCode:{errCode}");
                return GetErrorCode(errCode);
            }

            // 设置打标速度
            errCode = BC2_SetMarkSpeed_List(cardID, markSpeed);
            if (errCode != 0)
            {
                _logger.LogError($"设置打标速度失败，ErrCode:{errCode}");
                return GetErrorCode(errCode);
            }

            return MarkErrorCode.None;
        }

        public MarkErrorCode SetTransformMatrix(uint cardNo, uint headID, float m00, float m01, float m10, float m11)
        {
            int cardID = -1;
            try
            {
                cardID = GetCardID(cardNo);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"未找到对应的卡号: {cardNo}");
                return MarkErrorCode.UnmatchedMarkCardNo;
            }

            // 2D变换矩阵分解:
            // | m00 m01 |   | scaleX*cos(angle)  -scaleY*sin(angle) |
            // | m10 m11 | = | scaleX*sin(angle)   scaleY*cos(angle) |
            
            // 计算X和Y方向的缩放比例
            double scaleX = Math.Sqrt(m00 * m00 + m10 * m10);
            double scaleY = Math.Sqrt(m01 * m01 + m11 * m11);
            
            // 计算旋转角度
            double angle = Math.Atan2(m10, m00) * 180 / Math.PI; // 转换为角度

            // 检查是否包含镜像变换（行列式为负）
            double determinant = m00 * m11 - m01 * m10;

            // List指令需要按照以下步骤执行：
            // 1. 加载指令列表
            // 2. 添加List指令
            // 3. 结束指令列表
            // 4. 执行指令列表

            // 步骤1：开始加载指令列表（使用ListPos=20作为临时配置列表）
            int errCode = BC2_LoadList(cardID, 1, 1);
            if (errCode != 0)
            {
                _logger.LogError($"加载指令列表失败，ErrCode:{errCode}");
                return GetErrorCode(errCode);
            }

            // 步骤2：添加变换矩阵相关的List指令
            // 设置X/Y方向缩放比例
            errCode = BC2_SetGraphicScale_List(cardID, (int)headID, scaleX, scaleY);
            if (errCode != 0)
            {
                _logger.LogError($"添加缩放指令失败，ErrCode:{errCode}");
                return GetErrorCode(errCode);
            }

            // 设置旋转角度
            errCode = BC2_SetGraphicRotation_List(cardID, (int)headID, angle);
            if (errCode != 0)
            {
                _logger.LogError($"添加旋转指令失败，ErrCode:{errCode}");
                return GetErrorCode(errCode);
            }

            // 步骤3：结束指令列表
            errCode = BC2_SetEndOfList_List(cardID);
            if (errCode != 0)
            {
                _logger.LogError($"结束指令列表失败，ErrCode:{errCode}");
                return GetErrorCode(errCode);
            }

            // 步骤4：执行指令列表
            errCode = BC2_StartExecuteList(cardID, 1);
            if (errCode != 0)
            {
                _logger.LogError($"执行变换矩阵指令失败，ErrCode:{errCode}");
                return GetErrorCode(errCode);
            }

            _logger.LogInformation($"设置变换矩阵成功，CardNo:{cardNo}, HeadID:{headID}, " +
                $"ScaleX:{scaleX:F3}, ScaleY:{scaleY:F3}, Angle:{angle:F2}°, Determinant:{determinant:F3}");
            
            return MarkErrorCode.None;
        }

        public MarkErrorCode StartMarking(uint cardNo)
        {
            int cardID = -1;
            try
            {
                cardID = GetCardID(cardNo);

            }
            catch (Exception e)
            {
                _logger.LogError(e, $"未找到对应的卡号: {cardNo}");
                return MarkErrorCode.UnmatchedMarkCardNo;
            }
            int errCode = BC2_StartExecuteList(cardID, 1);
            if (errCode != 0)
            {
                _logger.LogError($"开始执行列表失败，ErrCode:{errCode}");
                return GetErrorCode(errCode);
            }
            return MarkErrorCode.None;
        }

        public MarkErrorCode StartMarking()
        {
            return StartMarking(1);
        }

        public MarkErrorCode StopMarking(uint cardNo)
        {
            int cardID = -1;
            try
            {
                cardID = GetCardID(cardNo);

            }
            catch (Exception e)
            {
                _logger.LogError(e, $"未找到对应的卡号: {cardNo}");
                return MarkErrorCode.UnmatchedMarkCardNo;
            }
            int errCode = BC2_SetEndOfList_List(cardID);
            if (errCode != 0)
            {
                _logger.LogError($"结束执行列表失败，ErrCode:{errCode}");
                return GetErrorCode(errCode);
            }
            return MarkErrorCode.None;
        }

        public MarkErrorCode StopMarking()
        {
            return StopMarking(1);
        }

        public MarkErrorCode WriteDigitalOutput(uint cardNo, uint signalIndex, bool setParam)
        {
            int cardID = -1;
            try
            {
                cardID = GetCardID(cardNo);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"未找到对应的卡号: {cardNo}");
                return MarkErrorCode.UnmatchedMarkCardNo;
            }

            if (signalIndex < 1 || signalIndex > 8)
            {
                _logger.LogError($"无效的信号索引: {signalIndex}，有效范围1-8");
                return MarkErrorCode.InvalidParameter;
            }

            int outputValue = setParam ? 1 : 0;
            int errCode = BC2_SetOutputBit(cardID, (int)signalIndex, outputValue);
            if (errCode != 0)
            {
                _logger.LogError($"写入数字输出失败，ErrCode:{errCode}");
                return GetErrorCode(errCode);
            }

            return MarkErrorCode.None;
        }

        public MarkErrorCode WriteDigitalOutput(uint cardNo, bool[] setParam)
        {
            int cardID = -1;
            try
            {
                cardID = GetCardID(cardNo);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"未找到对应的卡号: {cardNo}");
                return MarkErrorCode.UnmatchedMarkCardNo;
            }

            if (setParam == null || setParam.Length > 8)
            {
                _logger.LogError($"无效的参数数组长度: {setParam?.Length ?? 0}，有效长度1-8");
                return MarkErrorCode.InvalidParameter;
            }

            // 将bool数组转换为int值
            int outputValue = 0;
            for (int i = 0; i < setParam.Length; i++)
            {
                if (setParam[i])
                {
                    outputValue |= (1 << i);
                }
            }

            int errCode = BC2_SetOutputValue(cardID, outputValue);
            if (errCode != 0)
            {
                _logger.LogError($"写入数字输出失败，ErrCode:{errCode}");
                return GetErrorCode(errCode);
            }

            return MarkErrorCode.None;
        }

        #region Private Methods
        private TGalvoCardInfo[] GetAllCardInfo(ref int Count)
        {
            int ErrCode; // 用于获取各个函数的执行状态代码
            int flag = 0;

            ErrCode = BC2_BeginScanGalvoCard(ref Count);
           

            if (Count != 0)
            {
                TGalvoCardInfo[] AllCardInfo = new TGalvoCardInfo[Count];
                for (int i = 0; i < Count; i++)
                {
                    ErrCode = BC2_GetScanGalvoInfo(i + 1, ref AllCardInfo[i]);
                    //MyAssert.AreEqual(ErrCode, 0);
                    ShowCardInfo(i + 1, AllCardInfo[i]);

                }

                ErrCode = BC2_EndScanGalvoCard();
                //MyAssert.AreEqual(ErrCode, 0);
                return AllCardInfo;
            }
            else
            {
                throw new Exception("未扫描到振镜卡");
            }

        }
        private void ShowCardInfo(int Card, TGalvoCardInfo CardInfo)
        {
            _logger?.LogInformation("Card " + Card + "：\tSN：" + CardInfo.SerialNum + "\tCardInfo：" + CardInfo.CardInfo);
            _logger?.LogInformation($"\t本机IP：{IPIntToString(CardInfo.LocalIp),-15} 子网掩码：{IPIntToString(CardInfo.LocalSubNet),-15}");
            _logger?.LogInformation($"\t板卡IP：{IPIntToString(CardInfo.CardIP),-15} 子网掩码：{IPIntToString(CardInfo.CardSubNet),-15}");
            _logger?.LogInformation("………………………………………………………………………………");
        }

        private MarkErrorCode GetErrorCode(int ErrorCode)
        {
            var errorCodeEnum = (MarkErrorCode)(ErrorCode + 1000);
            if ( ErrorCode != 0)
            {
                _logger.LogError($"{GetEnumDescription(errorCodeEnum)}");
            }
            return errorCodeEnum; ;
        }

        //获取枚举值的描述信息
        private string GetEnumDescription(Enum value)
        {
            var fieldInfo = value.GetType().GetField(value.ToString());
            var descriptionAttribute = fieldInfo.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false) as System.ComponentModel.DescriptionAttribute[];
            if (descriptionAttribute != null && descriptionAttribute.Length > 0)
            {
                return descriptionAttribute[0].Description;
            }
            else
            {
                return value.ToString();
            }
        }

        private int GetCardID(uint cardNo)
        {
            if (cardNoList != null && cardNoList.ContainsKey(cardNo))
            {
                return cardNoList[cardNo];
            }
            else
            {
                throw new Exception($"未找到对应的卡号: {cardNo}");
            }
        }

        private void FreeCardResource()
        {
            if(cardNoList == null || cardNoList.Count > 0)
            {
                foreach (var cardNo in cardNoList.Keys)
                {

                    BC2_ClearErr(cardNoList[cardNo]);
                    BC2_FreeGalvoCard(cardNoList[cardNo]);
                }
            }
            
            BC2_FreeGalvoSystem();
        }

        public MarkErrorCode SetBarrelCorrection(uint cardNo, double idealWidth, double idealHeight, double[] widthParam, double[] heightParam)
        {
            throw new NotImplementedException();
        }

        #endregion
    }
}
