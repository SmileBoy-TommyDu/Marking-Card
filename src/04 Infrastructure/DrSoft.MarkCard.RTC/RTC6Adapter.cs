using DrSoft.MarkCard.Interface;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.Config;
using DrSoft.MarkCard.Model.Enum;
using DrSoft.MarkCard.Model.MarkCommand;
using DrSoft.MarkCard.RTC.CommandProcessors;
using Microsoft.Extensions.Logging;
using RTC6ADDONImport;
using System.Drawing;


namespace DrSoft.MarkCard.RTC
{
    public class RTC6Adapter : IMarkCardAdapter
    {
        #region 常量定义

        /// <summary>
        /// 默认校正文件路径
        /// </summary>
        private readonly string _defaultCalibrationFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"package", "D2_2045.ct5");

        /// <summary>
        /// 校准公差（微米）
        /// </summary>
        private const double ToleranceUM = 10.0;

        /// <summary>
        /// 校准选项：中心偏移置零
        /// </summary>
        private const uint CalibrationOptions = (uint)RtcCalibrationOptions.SET_CENTER_OFFSET_TO_ZERO;

  

        /// <summary>
        /// 激光功率最大值（12位DAC）
        /// </summary>
        private const uint MaxLaserPower = 4095;

 

        /// <summary>
        /// 状态轮询间隔（ms）
        /// </summary>
        private const int StatePollingInterval = 10;

        /// <summary>
        /// 硬件命令超时等待基数（ms）
        /// </summary>
        private const int HardwareCommandTimeoutBase = 50;

        /// <summary>
        /// 温度转换因子
        /// </summary>
        private const int TemperatureShift = 4;

        /// <summary>
        /// 温度精度（0.1度）
        /// </summary>
        private const double TemperaturePrecision = 10.0;

        /// <summary>
        /// 位置掩码
        /// </summary>
        private const int PositionMask = 0x000FFFF0;

        /// <summary>
        /// 位置移位
        /// </summary>
        private const int PositionShift = 4;

        /// <summary>
        /// IO端口位数
        /// </summary>
        private const int IoPortBits = 16;

      

        /// <summary>
        /// IO控制命令常量
        /// </summary>
        private const uint SendStatus = 0x0500;
        private const uint SendRealPos = 0x0501;
        private const uint SendStatus2 = 0x0512;
        private const uint SendGalvoTemp = 0x0514;

        private readonly CardConfig cardConfig;

        #endregion

        #region 私有字段

        private bool _isInitialized;
        private bool _isInitializing;
        private uint _cardNum;
        private Config _config;
        private readonly ILogger<RTC6Adapter> _logger;
        private readonly object _lock = new object();
        private readonly object _stateLock = new object();
        private readonly Dictionary<uint, int> _lastLapTimes = new Dictionary<uint, int>();
        private readonly Dictionary<uint, MarkingState> _markingStates = new Dictionary<uint, MarkingState>();
        private readonly Dictionary<uint, ProcessParam> _processParams = new Dictionary<uint, ProcessParam>();
        private readonly Dictionary<uint, bool[]> _lastInputStates = new Dictionary<uint, bool[]>();
        private readonly Dictionary<uint, bool[]> _lastOutputStates = new Dictionary<uint, bool[]>();
        private Thread _monitorThread;
        private volatile bool _isMonitorRunning = true;
        private volatile bool _isStopMarking;
     
        private MarkingMode _markingMode = MarkingMode.SoftwareMode;
        private readonly Dictionary<MarkCommandType, IRTC6MarkCommandProcessor> _processors;

        private ProcessParam GetOrCreateProcessParam(uint cardNo)
        {
            if (!_processParams.TryGetValue(cardNo, out var param))
            {
                param = new ProcessParam();
                _processParams[cardNo] = param;
            }
            return param;
        }

        #endregion

        #region 属性

        public uint CardNum => _cardNum;

        #endregion



        #region 构造函数

        public RTC6Adapter(ILogger<RTC6Adapter> logger, Config config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));

            cardConfig = config.CardConfigs.Find(x => x.IsActive);
            if (cardConfig == null) throw new Exception("未找到激活的打标卡");
            _logger = logger;

            // 通过反射自动加载所有实现了 IRTC6MarkCommandProcessor 接口的处理器，并根据 CommandType 构建字典
            var processorInterfaceType = typeof(IRTC6MarkCommandProcessor);
            var processorInstances = typeof(RTC6Adapter).Assembly
                .GetTypes()
                .Where(t => processorInterfaceType.IsAssignableFrom(t)
                            && !t.IsAbstract
                            && !t.IsInterface
                            && t.GetConstructor(Type.EmptyTypes) != null)
                .Select(t => Activator.CreateInstance(t) as IRTC6MarkCommandProcessor)
                .Where(p => p != null)
                .Cast<IRTC6MarkCommandProcessor>()
                .ToList();

            _processors = processorInstances
                .GroupBy(p => p.CommandType)
                .ToDictionary(g => g.Key, g => g.First());

            if (_config != null && _config.ScanHeadConfigs == null)
            {
                for (int i = 0; i < _config.ScanHeadConfigs.Count; i++)
                {
                    _config.ScanHeadConfigs[i].HeadFilePath = _defaultCalibrationFilePath;
                }
            }
        }

        #endregion

        #region 初始化

        /// <summary>
        /// 初始化打标卡
        /// </summary>
        public MarkErrorCode Initialize()
        {
            //已经初始化完成
            if (_isInitialized)
            {
                return MarkErrorCode.None;
            }

            //防止重复初始化
            if (_isInitializing) return MarkErrorCode.MarkCardInitializing;

            _isInitializing = true;

            if (_config == null)
            {
                _logger?.LogError("缺少打标卡配置文件");
                return MarkErrorCode.InvalidParameter;
            }

            uint errCode = 0;

            _logger?.LogInformation("初始化RTC6 DLL...");
            errCode = RTC6Wrap.init_rtc6_dll();
            if (errCode != 0)
            {
                _logger?.LogError("打标卡DLL初始化失败");
                _isInitialized = false;
                return CtlGetErrMsg(errCode);
            }

            for (int i = 0; i < cardConfig.CardCount; i++)
            {
                try
                {
                    

                    SelectRtc(i + 1);
                }
                catch (Exception ex)
                {
                    RTC6Wrap.free_rtc6_dll();
                    _isInitialized = false;
                    _logger?.LogError(ex, "选择打标卡失败");
                    return MarkErrorCode.MarkCardInitializationFailed;
                }
            }

            string rtcProgramFilePath = Path.Combine(Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory), "package");
            errCode = RTC6Wrap.load_program_file(rtcProgramFilePath);
            if (errCode != 0)
            {
                RTC6Wrap.free_rtc6_dll();
                _isInitialized = false;
                _logger?.LogError($"加载打标卡程序文件失败, 错误码{errCode}");
                return MarkErrorCode.MarkCardInitializationFailed;
            }

            _cardNum = (uint)cardConfig.CardCount;
            
            for (int i = 0; i < cardConfig.CardCount; i++)
            {
                try
                {
                    uint cardNo = (uint)i + 1;
                    var headConfig = _config.ScanHeadConfigs.FindAll(x => x.CardNo == cardNo);
                    if (headConfig != null && headConfig.Count > 0)
                    {
                        headConfig = headConfig.OrderBy(x => x.ScanHeadNo).ToList();
                        string? head1File = headConfig[0].HeadFilePath ?? _defaultCalibrationFilePath;
                        string? head2File =  null;
                        if(headConfig.Count > 1)
                        {
                            head2File = headConfig[1].HeadFilePath ?? _defaultCalibrationFilePath;
                        }

                        var error = LoadCalibrationFileInternal(cardNo, head1File, head2File);
                        if (error != MarkErrorCode.None)
                        {
                           // RTC6Wrap.free_rtc6_dll();
                            _logger?.LogError($"加载打标卡{cardNo}校正档失败, 错误码{error}");
                            //return error;

                            SetLoadCalibrationFileSuccess(cardNo, false);
                        }
                        else
                        {
                            SetLoadCalibrationFileSuccess(cardNo, true);
                        }
                        
                        if(headConfig[0].HeadFilePath==null)
                        {
                            headConfig[0].HeadFilePath = _defaultCalibrationFilePath;
                        }

                        if(headConfig.Count > 1&& string.IsNullOrEmpty(headConfig[1].HeadFilePath))
                        {
                            headConfig[1].HeadFilePath = _defaultCalibrationFilePath;
                        }

                        _logger?.LogInformation($"加载卡号: {cardNo} 校正档成功, 扫描头1文件: {head1File}, {(head2File != null ? $"扫描头2文件: {head2File}" : "无扫描头2")}");

                        //设置默认坐标变换矩阵
                        var affineMatrix = GetAffineMatrix(cardNo, headConfig[0].ScanHeadNo);
                        SetTransformMatrix(cardNo, headConfig[0].ScanHeadNo, 1, 0, 0, 1);
                        SetOffset(cardNo, headConfig[0].ScanHeadNo, 0, 0, 0);
                        _logger?.LogInformation($"设置卡号: {cardNo}, 扫描头号: {headConfig[0].ScanHeadNo} 默认坐标变换矩阵m00={affineMatrix.m00}, m01={affineMatrix.m01}, m10={affineMatrix.m10}, m11={affineMatrix.m11}");
                        if (headConfig.Count > 1)
                        {
                            affineMatrix = GetAffineMatrix(cardNo, headConfig[1].ScanHeadNo);
                            SetTransformMatrix(cardNo, headConfig[1].ScanHeadNo, 1, 0, 0, 1);
                            SetOffset(cardNo, headConfig[1].ScanHeadNo, 0, 0, 0);
                            _logger?.LogInformation($"设置卡号: {cardNo}, 扫描头号: {headConfig[1].ScanHeadNo} 默认坐标变换矩阵m00={affineMatrix.m00}, m01={affineMatrix.m01}, m10={affineMatrix.m10}, m11={affineMatrix.m11}");
                        }

                    }
                    else
                    {
                        _logger.LogInformation("未配置扫描头");
                    }
                }
                catch (Exception ex)
                {
                    RTC6Wrap.free_rtc6_dll();
                    _logger?.LogError(ex, "加载校正档失败");
                    return MarkErrorCode.MarkCardInitializationFailed;
                }
            }

            // 设置激光模式
            var laserConfig = _config.LaserConfigs.FirstOrDefault();
            RTC6Wrap.set_laser_mode(laserConfig != null ? (uint)laserConfig.LaserType : (uint)LaserType.CO2);

            // 设置IO触发模式：下降沿触发打标
            RTC6Wrap.set_laser_control(0x00);

            // 配置短列表命令
            RTC6Wrap.set_standby_list(800, 8);
            RTC6Wrap.set_laser_delays(100, 100);
            RTC6Wrap.config_list(8388000, 0);
            RTC6Wrap.set_sky_writing_mode(0);

            SetMarkingMode(MarkingMode.SoftwareMode);
            RTC6Wrap.enable_laser();

            // 等待初始化完成
            int waitCount = cardConfig.InitTimeout / HardwareCommandTimeoutBase;
            while (waitCount > 0)
            {
                waitCount--;
                RTC6Wrap.get_status(out uint status, out uint pos);

                if (status == 0)
                {
                    _isInitialized = true;
                    _logger?.LogInformation("打标卡初始化完成");
                    _isInitializing = false;

                    // 启动状态监控线程
                    _monitorThread = new Thread(MarkingStateMonitor) { IsBackground = true };
                    _monitorThread.Start();

                    return MarkErrorCode.None;
                }
                Thread.Sleep(HardwareCommandTimeoutBase);
            }

            _isInitializing = false;
            return MarkErrorCode.None;
        }

        #endregion

        #region 打标数据加载

    

        private Dictionary<uint, double> estimatedExecTimes = new Dictionary<uint, double>();


        private Dictionary<uint,bool> isLoadCalibrationFileSuccess = new Dictionary<uint,bool>();

        private void SetLoadCalibrationFileSuccess(uint cardNo,bool isSuccess)
        {
            if (isLoadCalibrationFileSuccess.ContainsKey(cardNo))
            {
                isLoadCalibrationFileSuccess[cardNo] = isSuccess;
            }
            else
            {
                isLoadCalibrationFileSuccess.Add(cardNo, isSuccess);
            }
        }

        private bool GetLoadCalibrationFileSuccess(uint cardNo)
        {
            if(!_isInitialized) return false;
            if (isLoadCalibrationFileSuccess.ContainsKey(cardNo))
            {
                return isLoadCalibrationFileSuccess[cardNo];
            }
            return false;
        }


        public MarkErrorCode LoadMarkData(uint cardNo, List<IMarkCommand> commands)
        {
            if (!_isInitialized)
            {
                return MarkErrorCode.Uninitialized;
            }

            if (!GetLoadCalibrationFileSuccess(cardNo))
            {
                return MarkErrorCode.UnLoadCalibration;
            }

            if (commands == null || commands.Count == 0)
            {
                return MarkErrorCode.InvalidParameter;
            }

            double frequency = 200; // 默认频率

            Task.Run(() => {

                try
                {
                    // 预计算执行时间
                    int time = GetEstimatedExecTime(commands);
                    //将time存储到Dictionary中，供外部查询
                    if (estimatedExecTimes.ContainsKey(cardNo))
                    {
                        estimatedExecTimes[cardNo] = time;
                    }
                    else
                    {
                        estimatedExecTimes.Add(cardNo, time);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "预计算打标数据执行时间时发生异常");
                }

            });

            try
            {
                RTC6Wrap.n_set_start_list(cardNo, 1);

                var context = new RTC6ProcessContext
                {
                    CardNo = cardNo,
                    Frequency = frequency,
                    Factor = factor,
                    Logger = _logger,
                    GetOrCreateProcessParam = GetOrCreateProcessParam
                };

                List<IMarkCommand> dashListCommands = new List<IMarkCommand>();

                foreach (var command in commands)
                {
                    if (command.MarkCommandType == MarkCommandType.MarkDashedLineCommand)
                    {
                        dashListCommands.Add(command);
                        continue;
                    }
                    var errorCode = ProcessMarkCommand(command, context);
                    if (errorCode != MarkErrorCode.None)
                    {
                        return errorCode;
                    }
                }

                if (dashListCommands.Count > 0)
                {
                    if(!isActiveRTC6AddOn)
                    {
                        isActiveRTC6AddOn = ActiveRTC6AddOn();

                        if (!isActiveRTC6AddOn)
                        {
                            _logger?.LogError("RTC6向量功能激活失败");
                            return MarkErrorCode.UnknownError;
                        }
                    }

                  

                    MarkDashedLineProcessor? dashedLineProcessor = null;

                    if (_processors.TryGetValue(MarkCommandType.MarkDashedLineCommand, out var dashedProc))
                    {
                        dashedLineProcessor = dashedProc as MarkDashedLineProcessor;
                    }

                    var initError = dashedLineProcessor.InitShortVectorSession(context.CardNo, factor, context, context.Logger);
                    if (initError != MarkErrorCode.None)
                        return initError;

                    foreach (var command in dashListCommands)
                    {
                        // 虚线命令 → 累积到 ShortVector 会话
                        var errorCode = dashedLineProcessor.Process(command, context);
                    }

                    dashedLineProcessor.FlushShortVectors(cardNo, _logger);
                }

                var headConfig = _config?.ScanHeadConfigs?.First(x => x.CardNo == cardNo);

                if (headConfig != null)
                {
                    RTC6Wrap.n_jump_abs(cardNo, (int)(headConfig.OriginX*factor), (int)(headConfig.OriginY*factor));
                }
                else
                {
                    RTC6Wrap.n_jump_abs(cardNo, 0, 0);
                }
                

                RTC6Wrap.n_set_end_of_list(cardNo);
                return MarkErrorCode.None;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "加载打标数据时发生异常");
                return MarkErrorCode.UnknownError;
            }
        }

        private bool isActiveRTC6AddOn = false;

        private bool ActiveRTC6AddOn()
        {
                    
        uint[] FeatureKey = { 0x4EAC935B, 0xC8924D1F, 0x00000000, 0x00000000 };

        var result = RTC6ADDONWrap.activateShortVectors(FeatureKey);
            if (result != 0)
            {
                return false;
            }

            return true;
        }
        /// <summary>
        /// 处理单条打标命令（通过字典查找分发到具体处理器）
        /// </summary>
        private MarkErrorCode ProcessMarkCommand(IMarkCommand command, RTC6ProcessContext context)
        {
            if (_processors.TryGetValue(command.MarkCommandType, out var processor))
            {
                return processor.Process(command, context);
            }

            _logger?.LogWarning($"不支持的打标命令类型: {command.MarkCommandType}");
            return MarkErrorCode.None;
        }

        #endregion

       

        #region 校正文件生成

        public MarkErrorCode CreateCalibrationFile(string srcFile, string dstFile, double[] targetPostX, double[] targetPostY, double[] realsPostX, double[] realsPostY)
        {
            try
            {
                // 根据进程位数选择对应的库
                if (IntPtr.Size == 4) // 32位
                {
                    return GenerateCalibrationFile32(srcFile, dstFile, targetPostX, targetPostY, realsPostX, realsPostY);
                }
                else // 64位
                {
                    return GenerateCalibrationFile64(srcFile, dstFile, targetPostX, targetPostY, realsPostX, realsPostY);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "生成校正文件时发生异常");
                return MarkErrorCode.GenerateCalibrationFileFailed;
            }
        }

        /// <summary>
        /// 生成校正文件（32位）
        /// </summary>
        private MarkErrorCode GenerateCalibrationFile32(string srcFile, string dstFile, double[] targetPostX, double[] targetPostY, double[] realsPostX, double[] realsPostY)
        {
            uint handle = 0;
            try
            {
                uint errorCode = CalLib32.slcl.slcl_activate(0xBFBE0E531E10CE76U);
                if (errorCode != 0)
                {
                    _logger?.LogError($"激光校正库激活失败, errorCode={errorCode}");
                    return MarkErrorCode.CalibrationLibraryUnauthorized;
                }

                _logger?.LogInformation($"加载原始校正文件: {srcFile}");
                errorCode = CalLib32.slcl.slcl_load_correction_table(out handle, srcFile, null);
                if (errorCode != 0 || handle == 0)
                {
                    _logger?.LogError($"加载原始校正文件失败, errorCode={errorCode}");
                    return MarkErrorCode.LoadOriginalCalibrationFileFailed;
                }

                var settings = new CalLib32.slcl_xy_calibration_settings
                {
                    XYCalibrationOptions = CalibrationOptions,
                    ToleranceUM = ToleranceUM
                };
                
                var results = new CalLib32.slcl_xy_calibration_interpolation_results();
                
                _logger?.LogInformation($"生成校正文件, Tolerance={settings.ToleranceUM}, Options={settings.XYCalibrationOptions}");
                
                errorCode = CalLib32.slcl.slcl_xy_calibration_mm_targets(
                    handle,
                    (ushort)targetPostX.Length,
                    targetPostX, targetPostY,
                    realsPostX, realsPostY,
                    settings, results, dstFile);
                
                if (errorCode == 0)
                {
                    _logger?.LogInformation($"生成校正文件成功: {dstFile}");
                    return MarkErrorCode.None;
                }
                else
                {
                    _logger?.LogError($"生成校正文件失败, errorCode={errorCode}");
                    return MarkErrorCode.GenerateCalibrationFileFailed;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "生成校正文件失败");
                return MarkErrorCode.GenerateCalibrationFileFailed;
            }
            finally
            {
                if (handle != 0)
                {
                    CalLib32.slcl.slcl_delete_correction_table_handle(handle);
                }
            }
        }

        /// <summary>
        /// 生成校正文件（64位）
        /// </summary>
        private MarkErrorCode GenerateCalibrationFile64(string srcFile, string dstFile, double[] targetPostX, double[] targetPostY, double[] realsPostX, double[] realsPostY)
        {
            ulong handle = 0;
            try
            {
                uint errorCode = CalLib64.slcl.slcl_activate(0xBFBE0E531E10CE76U);
                if (errorCode != 0)
                {
                    _logger?.LogError($"激光校正库激活失败, errorCode={errorCode}");
                    return MarkErrorCode.CalibrationLibraryUnauthorized;
                }

                _logger?.LogInformation($"加载原始校正文件: {srcFile}");
                errorCode = CalLib64.slcl.slcl_load_correction_table(out handle, srcFile, null);
                if (errorCode != 0 || handle == 0)
                {
                    _logger?.LogError($"加载原始校正文件失败, errorCode={errorCode}");
                    return MarkErrorCode.LoadOriginalCalibrationFileFailed;
                }

                var settings = new CalLib64.slcl_xy_calibration_settings
                {
                    XYCalibrationOptions = CalibrationOptions,
                    ToleranceUM = ToleranceUM
                };
                
                var results = new CalLib64.slcl_xy_calibration_interpolation_results();
                
                _logger?.LogInformation($"生成校正文件, Tolerance={settings.ToleranceUM}, Options={settings.XYCalibrationOptions}");
                
                errorCode = CalLib64.slcl.slcl_xy_calibration_mm_targets(
                    handle,
                    (ushort)targetPostX.Length,
                    targetPostX, targetPostY,
                    realsPostX, realsPostY,
                    settings, results, dstFile);
                
                if (errorCode == 0)
                {
                    _logger?.LogInformation($"生成校正文件成功: {dstFile}");
                    return MarkErrorCode.None;
                }
                else
                {
                    _logger?.LogError($"生成校正文件失败, errorCode={errorCode}");
                    return MarkErrorCode.GenerateCalibrationFileFailed;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "生成校正文件失败");
                return MarkErrorCode.GenerateCalibrationFileFailed;
            }
            finally
            {
                if (handle != 0)
                {
                    CalLib64.slcl.slcl_delete_correction_table_handle(handle);
                }
            }
        }

        #endregion


        /// <summary>
        /// 预估标定用时（通过各命令处理器计算）
        /// </summary>
        /// <param name="commands">打标命令列表</param>
        /// <returns>预估执行时间（毫秒）</returns>
        private int GetEstimatedExecTime(List<IMarkCommand> commands)
        {
            if (commands == null || commands.Count == 0)
                return 0;

            double totalTime = 0.0;
            var timeContext = new TimeEstimationContext();

            foreach (var command in commands)
            {
                if (_processors.TryGetValue(command.MarkCommandType, out var processor))
                {
                    totalTime += processor.EstimateExecutionTime(command, timeContext);
                }
            }

            return (int)Math.Round(totalTime);
        }

        public int GetEstimatedExecTime(uint cardNo)
        {
            if (estimatedExecTimes.ContainsKey(cardNo))
            {
                return (int)estimatedExecTimes[cardNo];
            }
            return 0;
        }

        public MarkErrorCode GetJumpDelay(uint cardNo, out double jumpDelay)
        {
            jumpDelay = 0;
            if (!_processParams.TryGetValue(cardNo, out var param))
            {
                return MarkErrorCode.Uninitialized;
            }
            jumpDelay = param.JumpDelay;
            return MarkErrorCode.None;
        }

        public MarkErrorCode GetJumpSpeed(uint cardNo, out double jumpSpeed)
        {
            jumpSpeed = 0;
            if (!_processParams.TryGetValue(cardNo, out var param))
            {
                return MarkErrorCode.Uninitialized;
            }
            jumpSpeed = param.JumpSpeed;
            return MarkErrorCode.None;
        }

        public MarkErrorCode GetLaserFrequency(uint cardNo, out double frequency)
        {
            frequency = 0;
            if (!_processParams.TryGetValue(cardNo, out var param))
            {
                return MarkErrorCode.Uninitialized;
            }
            frequency = param.Frequency;
            return MarkErrorCode.None;
        }

    

        public MarkErrorCode GetLaserDelay(uint cardNo, out double laserOnDelay, out double laserOffDelay)
        {
            laserOnDelay = 0;
            laserOffDelay = 0;
            if (!_processParams.TryGetValue(cardNo, out var param))
            {
                return MarkErrorCode.Uninitialized;
            }
            laserOnDelay = param.LaserOnDelay;
            laserOffDelay = param.LaserOffDelay;
            return MarkErrorCode.None;
        }

       

        public MarkErrorCode GetMarkingDelay(uint cardNo, out double markingDelay)
        {
            markingDelay = 0;
            if (!_processParams.TryGetValue(cardNo, out var param))
            {
                return MarkErrorCode.Uninitialized;
            }
            markingDelay = param.MarkDelay;
            return MarkErrorCode.None;
        }

        public MarkErrorCode GetMarkingMode(uint cardNo, out MarkingMode mode)
        {
            mode = markingMode;
            return MarkErrorCode.None;
           
        }

        /// <summary>
        /// 获取打标状态
        /// </summary>
        public MarkErrorCode GetMarkingState(uint cardNo, out MarkingState markState)
        {
            markState = MarkingState.None;
            
            if (!_isInitialized)
            {
                return MarkErrorCode.Uninitialized;
            }

            lock (_stateLock)
            {
                // 如果监控线程正在运行，直接从字典读取
                if (_monitorThread != null && _monitorThread.IsAlive)
                {
                    if (_markingStates.TryGetValue(cardNo, out var state))
                    {
                        markState = state;
                        return MarkErrorCode.None;
                    }
                }
                else
                {
                    // 否则实时读取
                    try
                    {
                        var state = ReadCardState(cardNo);
                        _markingStates[cardNo] = state;
                        markState = state;
                        return MarkErrorCode.None;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, $"获取打标卡{cardNo}状态失败");
                        markState = MarkingState.None;
                        return MarkErrorCode.None;
                    }
                }
            }
            
            return MarkErrorCode.None;
        }

        public MarkErrorCode GetMarkSpeed(uint cardNo, out double markSpeed)
        {
            markSpeed = 0;
            if (!_processParams.TryGetValue(cardNo, out var param))
            {
                return MarkErrorCode.Uninitialized;
            }
            markSpeed = param.MarkSpeed;
            return MarkErrorCode.None;
        }

        public MarkErrorCode GetPolygonDelay(uint cardNo, out double polygonDelay)
        {
            polygonDelay = 0;
            if (!_processParams.TryGetValue(cardNo, out var param))
            {
                return MarkErrorCode.Uninitialized;
            }
            polygonDelay = param.PolyDelay;
            return MarkErrorCode.None;
        }

        /// <summary>
        /// 获取实际执行时间（ms）
        /// </summary>
        public MarkErrorCode GetRealExecTime(uint cardNo, out int execTime)
        {
            execTime = 0;
            
            if (!_isInitialized)
            {
                return MarkErrorCode.Uninitialized;
            }

            lock (_lastLapTimes)
            {
                int currentLapTime = (int)(RTC6Wrap.n_get_lap_time(cardNo)*1000);
                
                if (_lastLapTimes.TryGetValue(cardNo, out int lastLapTime))
                {
                    _lastLapTimes[cardNo] = currentLapTime;
                    execTime = currentLapTime - lastLapTime;
                }
                else
                {
                    _lastLapTimes.Add(cardNo, currentLapTime);
                    execTime = 0; // 第一次调用，返回0
                }
            }
            
            return MarkErrorCode.None;
        }

        public MarkErrorCode GetScannerAcc(uint cardNo, uint headID, out double acc)
        {
            throw new NotImplementedException();
        }

        public MarkErrorCode GetScannerConnect(uint cardNo, uint headID, out bool connectFlag)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// 获取扫描头位置（mm）
        /// </summary>
        public MarkErrorCode GetScannerPosion(uint cardNo, uint headID, out PointF point)
        {
            point = new PointF();
            
            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
            {
                return state;
            }

            lock (_lock)
            {
                try
                {
                    // 更新状态
                    RTC6Wrap.n_control_command(cardNo, headID, 1, SendRealPos);
                    RTC6Wrap.n_control_command(cardNo, headID, 2, SendRealPos);
                    Thread.Sleep(StatePollingInterval);

                    int xResult = RTC6Wrap.get_value(1);
                    int yResult = RTC6Wrap.get_value(2);

                    float x = (float)(((xResult & PositionMask) >> PositionShift) / factor);
                    float y = (float)(((yResult & PositionMask) >> PositionShift) / factor);

                    point = new PointF(x, y);
                    return MarkErrorCode.None;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, $"获取打标卡{cardNo}扫描头{headID}位置失败");
                    return MarkErrorCode.UnknownError;
                }
            }
        }

        /// <summary>
        /// 获取扫描头温度
        /// </summary>
        public MarkErrorCode GetScannerTemperature(uint cardNo, uint headID, out double temperatureX, out double temperatureY)
        {
            temperatureX = 0;
            temperatureY = 0;
            
            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
            {
                return state;
            }

            lock (_lock)
            {
                try
                {
                    RTC6Wrap.n_control_command(cardNo, headID, 1, SendGalvoTemp);
                    RTC6Wrap.n_control_command(cardNo, headID, 2, SendGalvoTemp);
                    Thread.Sleep(StatePollingInterval);
                    
                    int result1 = RTC6Wrap.get_value(1);
                    int result2 = RTC6Wrap.get_value(2);
                    
                    temperatureX = (result1 >> TemperatureShift) / TemperaturePrecision;
                    temperatureY = (result2 >> TemperatureShift) / TemperaturePrecision;
                    
                    return MarkErrorCode.None;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, $"获取打标卡{cardNo}扫描头{headID}温度失败");
                    return MarkErrorCode.UnknownError;
                }
            }
        }

      

        #region 状态监控

        public event Action<uint, MarkingState> OnMarkingEnd;

        /// <summary>
        /// 打标状态监控线程
        /// </summary>
        private void MarkingStateMonitor()
        {
            while (_isMonitorRunning)
            {
                for (int i = 0; i < cardConfig.CardCount; i++)
                {
                    uint cardNo = (uint)i + 1;
                    
                    GetMarkingState(cardNo, out MarkingState oldState);
                    var state = ReadCardState(cardNo);
                    
                    lock (_stateLock)
                    {
                        _markingStates[cardNo] = state;
                    }

                    MonitorInputSignals(cardNo);
                    SyncOutputSignals(cardNo, state);

                   
                    // 检测打标完成事件
                    if (oldState == MarkingState.Marking && state == MarkingState.MarkEnd)
                    {
                        OnMarkingEnd?.Invoke(cardNo, MarkingState.MarkEnd);
                    }
                }
                Thread.Sleep(StatePollingInterval);
            }
        }

        private void MonitorInputSignals(uint cardNo)
        {
            var ioConfig = GetIOConfig(cardNo);
            if (ioConfig == null || !ioConfig.EnableIO)
            {
                return;
            }

            if (ReadDigitalInput(cardNo, out bool[] currentInputs) != MarkErrorCode.None || currentInputs == null || currentInputs.Length == 0)
            {
                return;
            }

            if (!_lastInputStates.TryGetValue(cardNo, out var previousInputs) || previousInputs == null || previousInputs.Length != currentInputs.Length)
            {
                _lastInputStates[cardNo] = (bool[])currentInputs.Clone();
                return;
            }

            int monitorCount = Math.Min(Math.Min(ioConfig.InputCount, ioConfig.InputFunctions?.Length ?? 0), currentInputs.Length);
            for (int i = 0; i < monitorCount; i++)
            {
                bool isRisingEdge = !previousInputs[i] && currentInputs[i];
                if (isRisingEdge)
                {
                    ExecuteInputAction(cardNo, i, ioConfig.InputFunctions[i]);
                }
            }

            _lastInputStates[cardNo] = (bool[])currentInputs.Clone();
        }

        private void ExecuteInputAction(uint cardNo, int signalIndex, IOInputFunctionEnum inputFunction)
        {
            MarkErrorCode result = MarkErrorCode.None;
            switch (inputFunction)
            {
                case IOInputFunctionEnum.TriggerMark:
                    result = StartMarking(cardNo);
                    break;
                case IOInputFunctionEnum.PauseMark:
                    result = Pause(cardNo);
                    break;
                case IOInputFunctionEnum.ResumeMark:
                    result = Resume(cardNo);
                    break;
                case IOInputFunctionEnum.StopMark:
                    result = StopMarking(cardNo);
                    break;
                case IOInputFunctionEnum.None:
                default:
                    return;
            }

            if (result == MarkErrorCode.None)
            {
                _logger?.LogInformation($"打标卡{cardNo}输入信号{signalIndex}上升沿触发动作: {inputFunction}");
            }
            else
            {
                _logger?.LogWarning($"打标卡{cardNo}输入信号{signalIndex}上升沿触发动作失败: {inputFunction}, 错误码={result}");
            }
        }

        private void SyncOutputSignals(uint cardNo, MarkingState state)
        {
            var ioConfig = GetIOConfig(cardNo);
            if (ioConfig == null || !ioConfig.EnableIO)
            {
                return;
            }

            var desiredOutputs = new bool[IoPortBits];
            int outputCount = Math.Min(Math.Min(ioConfig.OutputCount, ioConfig.OutputFunctions?.Length ?? 0), IoPortBits);
            for (int i = 0; i < outputCount; i++)
            {
                desiredOutputs[i] = GetOutputValue(ioConfig.OutputFunctions[i], state);
            }

            if (_lastOutputStates.TryGetValue(cardNo, out var previousOutputs) && previousOutputs != null && previousOutputs.SequenceEqual(desiredOutputs))
            {
                return;
            }

            var writeResult = WriteDigitalOutput(cardNo, desiredOutputs);
            if (writeResult == MarkErrorCode.None)
            {
                _lastOutputStates[cardNo] = (bool[])desiredOutputs.Clone();
            }
            else
            {
                _logger?.LogWarning($"同步打标卡{cardNo}输出信号失败, 错误码={writeResult}");
            }
        }

        private bool GetOutputValue(IOOutputFunctionEnum outputFunction, MarkingState state)
        {
            return outputFunction switch
            {
                IOOutputFunctionEnum.Ready => state == MarkingState.Ready,
                IOOutputFunctionEnum.MarkEnd => state == MarkingState.MarkEnd,
                IOOutputFunctionEnum.MarkRunning => state == MarkingState.Marking,
                
                _ => false
            };
        }

        private IOConfig? GetIOConfig(uint cardNo)
        {
            return _config?.IOConfigs?.FirstOrDefault(x => x.MarkCardType == cardConfig.MarkCardType && x.CardNo == cardNo);
        }

        #endregion
        
     

        public MarkErrorCode LaserOff()
        {
            if (!_isInitialized)
            {
                return MarkErrorCode.Uninitialized;
            }

            RTC6Wrap.set_start_list(1);
            RTC6Wrap.jump_abs(0, 0);
            RTC6Wrap.set_end_of_list();
            RTC6Wrap.execute_list(1);
            RTC6Wrap.laser_signal_off();
            _logger?.LogInformation("laser signal off");
            return MarkErrorCode.None;
        }

        public MarkErrorCode LaserOff(uint cardNo)
        {
            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
            {
                return state;
            }

            if (!_isInitialized)
            {
                return MarkErrorCode.Uninitialized;
            }

            RTC6Wrap.n_set_start_list((uint)cardNo, 1);
            RTC6Wrap.jump_abs(0, 0);
            RTC6Wrap.n_set_end_of_list((uint)cardNo);
            RTC6Wrap.n_execute_list((uint)cardNo, 1);
            RTC6Wrap.n_laser_signal_off((uint)cardNo);
            _logger?.LogInformation($"set card{cardNo} laser signal off");
            return MarkErrorCode.None;
        }

        public MarkErrorCode LaserOn()
        {
            if(!_isInitialized)
            {
                return MarkErrorCode.Uninitialized;
            }
            RTC6Wrap.set_start_list(1);
            RTC6Wrap.jump_abs(0, 0);
            RTC6Wrap.set_end_of_list();
            RTC6Wrap.execute_list(1);

            RTC6Wrap.laser_signal_on();
            _logger?.LogInformation("laser signal on");
            return MarkErrorCode.None;  
        }

        public MarkErrorCode LaserOn(uint cardNo)
        {
            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
            {
                return state;
            }
            if (!_isInitialized)
            {
                return MarkErrorCode.Uninitialized;
            }
            RTC6Wrap.n_set_start_list((uint)cardNo, 1);
            RTC6Wrap.jump_abs(0, 0);
            RTC6Wrap.n_set_end_of_list((uint)cardNo);
            RTC6Wrap.n_execute_list((uint)cardNo, 1);
            RTC6Wrap.n_laser_signal_on((uint)cardNo);
            _logger?.LogInformation($"set card{cardNo} laser signal on");
            return MarkErrorCode.None;
        }

        public MarkErrorCode LoadCalibrationFile(string? head1File,string? head2File)
        {
            uint errorCode = 0;

            if (!_isInitialized)
            {
                return MarkErrorCode.Uninitialized;
            }

            if (!string.IsNullOrEmpty(head1File))
            {
                errorCode = RTC6Wrap.load_correction_file(head1File, 1, 2);

                if (errorCode == 0)
                {
                    factor = (float)RTC6Wrap.get_head_para( 1, 1);
                    SetLoadCalibrationFileSuccess(1, true);
                }

                if (errorCode != 0)
                {
                    return GetLoadCalibrationFileError(errorCode);
                }
            }

            if (!string.IsNullOrEmpty(head2File))
            {
                errorCode = RTC6Wrap.load_correction_file(head2File, 2, 2);
                if (errorCode == 0)
                {
                    factor = (float)RTC6Wrap.get_head_para( 2, 1);
                    SetLoadCalibrationFileSuccess(1, true);
                }
            }

            return GetLoadCalibrationFileError(errorCode);
        }

        public MarkErrorCode LoadCalibrationFile(uint cardNo, string? head1File,string? head2File)
        {
            if (!_isInitialized)
            {
                return MarkErrorCode.Uninitialized;
            }

         

            return LoadCalibrationFileInternal(cardNo, head1File,head2File);
        }

        private MarkErrorCode LoadCalibrationFileInternal(uint cardNo, string? head1File, string? head2File)
        {
            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
            {
                return state;
            }
            uint errorCode = 0;

            if (!string.IsNullOrEmpty(head1File))
            {
                errorCode = RTC6Wrap.n_load_correction_file(cardNo, head1File, 1, 2);

                if (errorCode == 0)
                {
                    factor = (float)RTC6Wrap.n_get_head_para((uint)cardNo, 1, 1);
                    SetLoadCalibrationFileSuccess(cardNo, true);
                    
                }

                if (errorCode != 0)
                {
                    return GetLoadCalibrationFileError(errorCode);
                }
            }

            if (!string.IsNullOrEmpty(head2File))
            {
                errorCode = RTC6Wrap.n_load_correction_file(cardNo, head2File, 2, 2);
                if (errorCode == 0)
                {
                    SetLoadCalibrationFileSuccess(cardNo, true);
                    factor = (float)RTC6Wrap.n_get_head_para((uint)cardNo, 2, 1);
                }
            }

            return GetLoadCalibrationFileError(errorCode);
        }


        private MarkErrorCode GetLoadCalibrationFileError(uint errorCode)
        {
            switch (errorCode)
            {
                case 0:
                    return MarkErrorCode.None;
                case 3:
                   _logger?.LogError("校正档文件打开失败（文件不存在或路径错误）");
                    return MarkErrorCode.FileOpenError;
                case 1:
                    _logger?.LogError("校正档文件错误（文件损坏或不完整）");
                    return MarkErrorCode.FileError;
                case 2:
                    _logger?.LogError("校正档内存错误（内存不足或分配失败）");
                    return MarkErrorCode.MemoryError;
                case 4:
                    _logger?.LogError("DSP内存错误（DSP内存不足或分配失败）");
                    return MarkErrorCode.DspMemoryError;
                 case 5:
                        _logger?.LogError("PCI下载错误（仅下载校验时）或以太网下载错误");
                        return MarkErrorCode.DownloadError;
                case 8:
                    _logger?.LogError("驱动程序错误或访问被拒绝（权限不足或驱动不兼容）");
                    return MarkErrorCode.DriverOrAccessDenied;
                case 10:
                    _logger?.LogError("参数错误");
                    return MarkErrorCode.InvalidParameter;
                case 11:
                    _logger?.LogError("板卡已被另一个用户程序占用或版本兼容性错误");
                    return MarkErrorCode.DriverOrAccessDenied;
                case 12:
                    _logger?.LogError("选项3D未启用（尝试加载3D校正档时发生）");
                    return MarkErrorCode.Option3DNotEnabledWarning;
                case 13:
                    _logger?.LogError("板卡Busy中");
                    return MarkErrorCode.MarkCardBusy;
                case 14:
                    _logger?.LogError("PCI上传错误（仅下载校验时）");
                    return MarkErrorCode.UploadError;
                case 15:
                    _logger?.LogError("RTC6校正档验证错误（校正档与硬件不匹配）");
                    return MarkErrorCode.VerifyError;
                default:
                    return MarkErrorCode.None;
            }
        }

        public MarkErrorCode Pause()
        {
            if (!_isInitialized)
            {
                return MarkErrorCode.Uninitialized;
            }

            RTC6Wrap.pause_list();
            return MarkErrorCode.None;
        }

        public MarkErrorCode Pause(uint cardNo)
        {
            if (!_isInitialized)
            {
                return MarkErrorCode.Uninitialized;
            }

            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
            {
                return state;
            }
            RTC6Wrap.n_pause_list(cardNo);
            return MarkErrorCode.None;
        }

        #region IO读写
        public MarkErrorCode ReadDigitalInput(uint cardNo, out bool[] value)
        {
            value = new bool[16];
            if (!_isInitialized)
            {
                return MarkErrorCode.Uninitialized;
            }

            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
            {
                return state;
            }

            uint outputValue = RTC6Wrap.n_read_io_port(cardNo);
            for (int i = 0; i < 16; i++)
            {
                value[i] = (outputValue & (1 << i)) != 0;
            }
            return MarkErrorCode.None;
        }

        public MarkErrorCode ReadDigitalOutput(uint cardNo, out bool[] value)
        {
            value = new bool[16];
            if (!_isInitialized)
            {
                return MarkErrorCode.Uninitialized;
            }
            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
            {
                return state;
            }

            uint outputValue = RTC6Wrap.n_get_io_status(cardNo);
            for (int i = 0; i < 16; i++)
            {
                value[i] = (outputValue & (1 << i)) != 0;
            }
            return MarkErrorCode.None;
        }

        public MarkErrorCode WriteDigitalOutput(uint cardNo, uint signalIndex, bool setParam)
        {
            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
            {
                return state;
            }
            if (!_isInitialized)
            {
                return MarkErrorCode.Uninitialized;
            }

            uint mask = (uint)(1 << (int)signalIndex);
            int value = setParam ? 1 : 0;
            value = value << (int)signalIndex;
            RTC6Wrap.n_write_io_port_mask(cardNo, (uint)value, mask);
            return MarkErrorCode.None;
        }

        public MarkErrorCode WriteDigitalOutput(uint cardNo,bool[] setParam)
        {
            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
            {
                return state;
            }
            if (!_isInitialized)
            {
                return MarkErrorCode.Uninitialized;
            }
            uint value = 0;
            for(int i =0;i< setParam.Length;i++)
            {
                if (setParam[i])
                {
                    value |= (uint)(1 << i);
                }
            }
             RTC6Wrap.n_write_io_port(cardNo, value);
    
            return MarkErrorCode.None;
        }

        #endregion
        public MarkErrorCode Resume()
        {
            if (!_isInitialized)
            {
                return MarkErrorCode.Uninitialized;
            }
            RTC6Wrap.restart_list();
            return MarkErrorCode.None;
        }

        public MarkErrorCode Resume(uint cardNo)
        {
            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
            {
                return state;
            }
            if (!_isInitialized)
            {
                return MarkErrorCode.Uninitialized;
            }
            RTC6Wrap.n_restart_list(cardNo);
            return MarkErrorCode.None;
        }

        public MarkErrorCode SetScannerSpeed(uint cardNo, double jumpSpeed,double markSpeed)
        {
            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
            {
                return state;
            }
            if (!_isInitialized)
            {
                return MarkErrorCode.Uninitialized;
            }
            RTC6Wrap.n_set_mark_speed_ctrl((uint)(cardNo), markSpeed * factor);
            RTC6Wrap.n_set_jump_speed_ctrl((uint)(cardNo), jumpSpeed * factor);

            var param = GetOrCreateProcessParam(cardNo);
            param.MarkSpeed = markSpeed;
            param.JumpSpeed = jumpSpeed;
            return MarkErrorCode.None;
        }

     

    

        public MarkErrorCode SetLaserDelay(uint cardNo, int laserOnDelay, int laserOffDelay)
        {
            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
            {
                return state;
            }
            if (!_isInitialized)
            {
                return MarkErrorCode.Uninitialized;
            }
            RTC6Wrap.n_set_laser_delays_ctrl(cardNo, (int)laserOnDelay * 64, (uint)laserOffDelay * 64);

            var param = GetOrCreateProcessParam(cardNo);
            param.LaserOnDelay = laserOnDelay;
            param.LaserOffDelay = laserOffDelay;
            return MarkErrorCode.None;
        }

        public MarkErrorCode SetLaserPower(uint cardNo, double power)
        {
            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
            {
                return state;
            }
            if (!_isInitialized)
            {
                return MarkErrorCode.Uninitialized;
            }

            RTC6Wrap.n_set_start_list(cardNo, 1);
            RTC6Wrap.n_set_laser_power(cardNo, 0, (uint)(4095 * power / 100));
            RTC6Wrap.n_set_end_of_list(cardNo);
            RTC6Wrap.n_execute_list(cardNo, 1);

            var param = GetOrCreateProcessParam(cardNo);
            param.Power = power;
            return MarkErrorCode.None;
        }

        public MarkErrorCode GetLaserPulseWidth(uint cardNo, out double pulseWidth)
        {
            pulseWidth = 0;
            if (!_processParams.TryGetValue(cardNo, out var param))
            {
                return MarkErrorCode.Uninitialized;
            }
            pulseWidth = param.Pulse;
            return MarkErrorCode.None;
        }

        /// <summary>
        /// 设置激光频率和脉宽，RTC6的激光频率和脉宽是通过设置激光脉冲参数来实现的
        /// </summary>
        /// <param name="cardNo"></param>
        /// <param name="frequency"></param>
        /// <param name="pulseWidth">脉宽(单位:μs）</param>
        /// <returns></returns>
        public MarkErrorCode SetLaserFrequencyAndPulseWidth(uint cardNo, double frequency, double pulseWidth)
        {
            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
            {
                return state;
            }
            if (!_isInitialized)
            {
                return MarkErrorCode.Uninitialized;
            }
            double period = 1.0f / frequency * (double)1.0e6;
            double halfPeriod = period / 2.0f;
            RTC6Wrap.n_set_start_list(cardNo, 1);
            RTC6Wrap.n_set_laser_pulses((uint)cardNo, (uint)(halfPeriod * 64.0), (uint)(pulseWidth * 64.0));
            RTC6Wrap.n_set_end_of_list(cardNo);
            RTC6Wrap.n_execute_list(cardNo, 1);
            _logger?.LogInformation($"set fre/pulsewidth finish,fre={frequency} pulseWidth={pulseWidth}");

            var param = GetOrCreateProcessParam(cardNo);
            param.Frequency = frequency;
            param.Pulse = pulseWidth;
            return MarkErrorCode.None;
        }

        public MarkErrorCode SetLaserFrequency(uint cardNo, double frequency)
        {
            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
            {
                return state;
            }

            if (!_isInitialized)
            {
                return MarkErrorCode.Uninitialized;
            }

            //固定脉宽为100
            uint pulseWidth = 100;
            
            double period = 1.0f / frequency * (double)1.0e6;
            double halfPeriod = period / 2.0f;
            RTC6Wrap.n_set_start_list(cardNo, 1);
            RTC6Wrap.n_set_laser_pulses((uint)cardNo, (uint)(halfPeriod * 64.0), (uint)(pulseWidth * 64.0));
            RTC6Wrap.n_set_end_of_list(cardNo);
            RTC6Wrap.n_execute_list(cardNo, 1);
            _logger?.LogInformation($"set fre/pulsewidth finish,fre={frequency} pulseWidth={pulseWidth}");

            var param = GetOrCreateProcessParam(cardNo);
            param.Frequency = frequency;
            param.Pulse = pulseWidth;
            return MarkErrorCode.None;
        }

        private MarkingMode markingMode = MarkingMode.SoftwareMode;

        public MarkErrorCode SetMarkingMode(MarkingMode mode)
        {
            if (!_isInitialized)
            {
                return MarkErrorCode.Uninitialized;
            }

            if (mode == MarkingMode.IOMode)
            {
                RTC6Wrap.set_control_mode(3); //IO触发
                _logger?.LogInformation("set marking mode,mode=IO Control");
            }
            else
            {
                _logger?.LogInformation("set marking mode,mode=Software Control");
                RTC6Wrap.set_control_mode(0); //软件触发
            }
            markingMode = mode;
            return MarkErrorCode.None;  
        }

        public MarkErrorCode SetMarkingMode(uint cardNo, MarkingMode mode)
        {
            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
            {
                return state;
            }
            if (!_isInitialized)
            {
                return MarkErrorCode.Uninitialized;
            }
            markingMode = mode;
            if (mode == MarkingMode.IOMode)
            {
                RTC6Wrap.n_set_control_mode(cardNo,3); //IO触发
                _logger?.LogInformation("set marking mode,mode=IO Control");
            }
            else
            {
                _logger?.LogInformation("set marking mode,mode=Software Control");
                RTC6Wrap.n_set_control_mode(cardNo,0); //软件触发
            }
            return MarkErrorCode.None;
        }

     
        //to do
        public MarkErrorCode SetScannerAcc(uint cardNo, uint headID, double acc)
        {
            throw new NotImplementedException();
        }

      

        public MarkErrorCode SetScannerDelay(uint cardNo, int markDelay, int jumpDelay, int polygonDelay)
        {
            if (!_isInitialized)
            {
                return MarkErrorCode.Uninitialized;
            }
            RTC6Wrap.n_set_scanner_delays_ctrl(cardNo, (uint)(jumpDelay / 10), (uint)(markDelay / 10), (uint)(polygonDelay / 10));
            _logger?.LogInformation($"set card{cardNo} scanner delay finish,jumDelay={jumpDelay} markDelay={markDelay} polygonDelay={polygonDelay}");

            var param = GetOrCreateProcessParam(cardNo);
            param.MarkDelay = markDelay;
            param.JumpDelay = jumpDelay;
            param.PolyDelay = polygonDelay;
            return MarkErrorCode.None;
        }



        public MarkErrorCode SetTransformMatrix(uint cardNo, uint headID, float m00, float m01, float m10, float m11)
        {
           
            var M = (1f, 0f, 0f, 1f);
            try
            {
                M = GetAffineMatrix(cardNo, headID);
            }
            catch (Exception e)
            {
                _logger?.LogError(e.Message);
                return MarkErrorCode.UnFoundScanHeadConfigError;
            }

            M.Item1 = M.Item1 * m00 + M.Item2 * m10;
            M.Item2 = M.Item1 * m01 + M.Item2 * m11;
            M.Item3 = M.Item3 * m00 + M.Item4 * m10;
            M.Item4 = M.Item3 * m01 + M.Item4 * m11;

            RTC6Wrap.n_set_matrix((uint)cardNo, headID, M.Item1, M.Item2, M.Item3, M.Item4, 0);
            _logger?.LogInformation($"设置打标卡{cardNo}扫描头{headID}仿射变换矩阵，M00:{M.Item1}, M01:{M.Item2}, M10:{M.Item3}, M11:{M.Item4}");
            return MarkErrorCode.None;
        }

        float factor = 1f;

        /// <summary>
        /// 设置偏移，多次设置不会叠加
        /// </summary>
        /// <param name="cardNo"></param>
        /// <param name="headID"></param>
        /// <param name="xOffset"></param>
        /// <param name="yOffset"></param>
        /// <param name="angleOffset"></param>
        /// <returns></returns>
        public MarkErrorCode SetOffset(uint cardNo, uint headID, double xOffset, double yOffset,double angleOffset)
        {
            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
            {
                return state;
            }
           
            if (_config == null || _config.ScanHeadConfigs == null || _config.ScanHeadConfigs.Count <= 0)
            {
                _logger?.LogError($"获取打标卡{cardNo}扫描头{headID}配置失败");
                return MarkErrorCode.UnFoundScanHeadConfigError;
            }
            var scanHeadConfig = _config.ScanHeadConfigs.Find(x => x.CardNo == cardNo && x.ScanHeadNo == headID);
            if (scanHeadConfig == null)
            {
                _logger?.LogError($"获取打标卡{cardNo}扫描头{headID}配置失败");
                return MarkErrorCode.UnFoundScanHeadConfigError;
            }

            double totalXOffset = xOffset + scanHeadConfig.OffsetX;
            double totalYOffset = yOffset + scanHeadConfig.OffsetY;
            double totalAngleOffset = angleOffset + scanHeadConfig.AngleOffset;
         
            RTC6Wrap.n_set_offset((uint)cardNo, headID, (int)((totalXOffset) * factor), (int)((totalYOffset) * factor), 0);
            
            RTC6Wrap.n_set_angle((uint)cardNo, headID, totalAngleOffset, 0);
          
            _logger?.LogInformation($"设置Card {cardNo} Head {headID} X方向偏移{totalXOffset} Y方向偏移{totalYOffset} 角度偏移{totalAngleOffset} 成功");
            return MarkErrorCode.None;
        }

        public MarkErrorCode SetScale(uint cardNo, uint headID, double transformScale)
        {
            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
            {
                return state;
            }
            if (!_isInitialized)
            {
                return MarkErrorCode.Uninitialized;
            }
            RTC6Wrap.n_set_scale((uint)cardNo, headID, transformScale, 0);
            _logger?.LogInformation($"设置Card {cardNo} Head {headID} 缩放因子{transformScale}");
            return MarkErrorCode.None;
        }

        /// <summary>
        /// 设置桶形校正，RTC6打标卡不支持该功能。
        /// </summary>
        public MarkErrorCode SetBarrelCorrection(uint cardNo, double idealWidth, double idealHeight, double[] widthParam, double[] heightParam)
        {
            throw new NotSupportedException("RTC6打标卡不支持桶形校正功能");
        }

        public MarkErrorCode StartMarking(uint cardNo)
        {
            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
            {
                return state;
            }
            if (!_isInitialized)
            {
                return MarkErrorCode.Uninitialized;
            }

            if (!GetLoadCalibrationFileSuccess(cardNo))
            {
                return MarkErrorCode.UnLoadCalibration;
            }

            isStopMarking = false;
          

            if (markingMode == MarkingMode.SoftwareMode)
            {
                RTC6Wrap.n_execute_list(cardNo,1);
                _logger?.LogInformation("execute list");
                return MarkErrorCode.None;

            }
            else
            {
                SetMarkingMode(cardNo,MarkingMode.IOMode);
                int n = cardConfig.MarkingTimeout/10;
                bool isMarking = false;
                while (n > 0)
                {
                    Thread.Sleep(10);
                    if (GetMarkingState(cardNo, out MarkingState markState) == MarkErrorCode.None)
                    {
                        if (markState == MarkingState.Marking)
                        {
                            isMarking = true;
                            break;
                        }
                    }

                }
                if (!isMarking) return MarkErrorCode.WaitIOTriggerTimeout;
                return MarkErrorCode.None;
            }
        }

        //记录是否执行了停止打标
        private bool isStopMarking = false;

        public MarkErrorCode StartMarking()
        {
            isStopMarking = false;

            if (!_isInitialized)
            {
                return MarkErrorCode.Uninitialized;
            }

            if (!GetLoadCalibrationFileSuccess(1))
            {
                return MarkErrorCode.UnLoadCalibration;
            }

            if (markingMode == MarkingMode.SoftwareMode)
            {
                RTC6Wrap.execute_list(1);
                _logger?.LogInformation("execute list");
                return MarkErrorCode.None;

            }
            else
            {
                SetMarkingMode(MarkingMode.IOMode);
                int n = cardConfig.MarkingTimeout * 100;
                bool isMarking = false;
                while (n > 0)
                {
                    Thread.Sleep(10);
                    if(GetMarkingState(1,out MarkingState markState) == MarkErrorCode.None)
                    {
                        if(markState == MarkingState.Marking)
                        {
                            isMarking = true;
                            break;
                        }
                    }
                   
                }
                if (!isMarking) return MarkErrorCode.WaitIOTriggerTimeout;
                return MarkErrorCode.None;
            }
        }

        public MarkErrorCode StopMarking(uint cardNo)
        {
            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
            {
                return state;
            }
            if (!_isInitialized)
            {
                return MarkErrorCode.Uninitialized;
            }
            RTC6Wrap.n_stop_execution((uint)cardNo);
            isStopMarking = true;
            return MarkErrorCode.None;
        }

        public MarkErrorCode StopMarking()
        {
            if (!_isInitialized)
            {
                return MarkErrorCode.Uninitialized;
            }
            RTC6Wrap.stop_execution();
            isStopMarking = true;

            _logger?.LogInformation("stop execution");
            return MarkErrorCode.None;
        }

      

       

        #region 资源释放

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            // 停止监控线程
            if (_isMonitorRunning)
            {
                _isMonitorRunning = false;
                
                if (_monitorThread != null && _monitorThread.IsAlive)
                {
                    // 等待线程结束，最多等待100ms
                    _monitorThread.Join(100);
                }
            }

            // 清理资源
            try
            {
                RTC6Wrap.free_rtc6_dll();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "释放RTC6 DLL资源时发生异常");
            }
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 选择打标卡
        /// </summary>
        private void SelectRtc(int card)
        {
            
            uint errCode = RTC6Wrap.select_rtc((uint)card);
            if (errCode != card)
            {
                RTC6Wrap.free_rtc6_dll();
                
                if (errCode == 0)
                {
                    string message = $"打标卡{card}被其他程序占用或版本兼容性错误";
                    _logger?.LogError(message);
                    throw new Exception(message);
                }
                else
                {
                    string message = $"打标卡{card}选择失败, 错误码{errCode}";
                    _logger?.LogError(message);
                    throw new Exception(message);
                }
            }
        }

        /// <summary>
        /// 读取打标卡状态
        /// </summary>
        private MarkingState ReadCardState(uint cardNo)
        {
            uint status = RTC6Wrap.n_read_status(cardNo);

            // 位定义
            const int ReadyBit = 2;
            const int MarkingBit = 4;
            const int MarkEndBit = 6;

            bool ready = ((status >> ReadyBit) & 0x01) == 1;
            bool marking = ((status >> MarkingBit) & 0x01) == 1;
            bool marked = ((status >> MarkEndBit) & 0x01) == 1;

            if (marked) return MarkingState.MarkEnd;
            if (marking) return MarkingState.Marking;
            if (ready) return MarkingState.Ready;
           

            return MarkingState.None;
        }

        #endregion

        #region 校验和配置

        /// <summary>
        /// 检查打标卡号是否有效
        /// </summary>
        private MarkErrorCode CheckCardNo(uint cardNo)
        {
            if (_config == null || cardConfig == null || cardConfig.CardCount <= 0)
            {
                return MarkErrorCode.InvalidParameter;
            }
            
            return (cardNo > 0 && cardNo <= cardConfig.CardCount) 
                ? MarkErrorCode.None 
                : MarkErrorCode.UnmatchedMarkCardNo;
        }

       
        /// <summary>
        /// 获取仿射变换矩阵
        /// </summary>
        private (float m00, float m01, float m10, float m11) GetAffineMatrix(uint cardNo, uint headID)
        {
            if (_config == null || _config.ScanHeadConfigs == null || _config.ScanHeadConfigs.Count <= 0)
            {
                throw new Exception($"获取打标卡{cardNo}扫描头{headID}配置失败");
            }
            
            var scanHeadConfig = _config.ScanHeadConfigs.Find(x => x.CardNo == cardNo && x.ScanHeadNo == headID);
            if (scanHeadConfig == null)
            {
                throw new Exception($"获取打标卡{cardNo}扫描头{headID}配置失败");
            }

            // 初始化为单位矩阵
            var matrix = (m00:1f, m01: 0f, m10: 0f, m11: 1f);

            // 应用镜像变换
            if (scanHeadConfig.MirrorX)
            {
                matrix.m00 = -1f;
            }

            if (scanHeadConfig.MirrorY)
            {
                matrix.m11 = -1f;
            }

            // 应用XY反转
            if (scanHeadConfig.ReverseXY)
            {
                var reverseMatrix = (m00: 0f, m01: 1f, m10: 1f, m11: 0f);
                matrix = MultiplyMatrices(matrix, reverseMatrix);
            }

            // 应用角度旋转
            float angleOffset = (float)scanHeadConfig.AngleOffset;
            if (angleOffset != 0)
            {
                double radians = angleOffset * Math.PI / 180.0;
                var rotationMatrix = (
                    m00: (float)Math.Cos(radians), 
                    m01: (float)-Math.Sin(radians), 
                    m10: (float)Math.Sin(radians), 
                    m11: (float)Math.Cos(radians)
                );
                matrix = MultiplyMatrices(matrix, rotationMatrix);
            }

            return matrix;
        }

        /// <summary>
        /// 矩阵相乘（2x2）
        /// </summary>
        private (float m00, float m01, float m10, float m11) MultiplyMatrices(
            (float m00, float m01, float m10, float m11) a,
            (float m00, float m01, float m10, float m11) b)
        {
            return (
                a.m00 * b.m00 + a.m01 * b.m10,
                a.m00 * b.m01 + a.m01 * b.m11,
                a.m10 * b.m00 + a.m11 * b.m10,
                a.m10 * b.m01 + a.m11 * b.m11
            );
        }

        #endregion


        #region 错误码处理

        /// <summary>
        /// 获取控制错误信息
        /// </summary>
        public MarkErrorCode CtlGetErrMsg(uint errorCode)
        {
            if (errorCode == 0)
                return MarkErrorCode.None;

            // 错误码位定义
            const int NoRtcBoardBit = 0;
            const int AccessDeniedBit = 1;
            const int CommandNotForwardedBit = 2;
            const int TimeoutBit = 3;
            const int InvalidParameterBit = 4;
            const int ListProcessingNotActiveBit = 5;
            const int IllegalInputPointerBit = 6;
            const int ListCommandConvertedToNopBit = 7;
            const int VersionErrorBit = 8;
            const int DownloadVerificationErrorBit = 9;
            const int DspVersionOldBit = 10;
            const int OutOfMemoryBit = 11;
            const int EepromErrorBit = 12;
            const int UnsupportedWindowsBit = 15;

            if (IsBitSet(errorCode, NoRtcBoardBit))
            {
                _logger?.LogError("no rtc board founded via init_rtc_dll");
                return MarkErrorCode.UnFoundMarkCard;
            }
            if (IsBitSet(errorCode, AccessDeniedBit))
            {
                _logger?.LogError("access denied via init_rtc_dll, select, acquire_rtc");
                return MarkErrorCode.DriverOrAccessDenied;
            }
            if (IsBitSet(errorCode, CommandNotForwardedBit))
            {
                _logger?.LogError("command not forwarded. PCI or driver error");
                return MarkErrorCode.CommandNotForwarded;
            }
            if (IsBitSet(errorCode, TimeoutBit))
            {
                _logger?.LogError("rtc timed out. no response from board");
                return MarkErrorCode.TimeoutError;
            }
            if (IsBitSet(errorCode, InvalidParameterBit))
            {
                _logger?.LogError("invalid parameter");
                return MarkErrorCode.InvalidParameter;
            }
            if (IsBitSet(errorCode, ListProcessingNotActiveBit))
            {
                _logger?.LogError("List processing is (not) active");
                return MarkErrorCode.ListProcessingNotActive;
            }
            if (IsBitSet(errorCode, IllegalInputPointerBit))
            {
                _logger?.LogError("list command rejected, illegal input pointer");
                return MarkErrorCode.IllegalInputPointer;
            }
            if (IsBitSet(errorCode, ListCommandConvertedToNopBit))
            {
                _logger?.LogError("list command converted to List_mop");
                return MarkErrorCode.ListCommandConvertedToNop;
            }
            if (IsBitSet(errorCode, VersionErrorBit))
            {
                _logger?.LogError("dll, rtc or hex version error");
                return MarkErrorCode.VersionError;
            }
            if (IsBitSet(errorCode, DownloadVerificationErrorBit))
            {
                _logger?.LogError("download verification error. load_program_file ?");
                return MarkErrorCode.DownloadError;
            }
            if (IsBitSet(errorCode, DspVersionOldBit))
            {
                _logger?.LogError("DSP version is too old");
                return MarkErrorCode.DSPVersionOld;
            }
            if (IsBitSet(errorCode, OutOfMemoryBit))
            {
                _logger?.LogError("out of memeory. dll internal windows memory request failed");
                return MarkErrorCode.MemoryError;
            }
            if (IsBitSet(errorCode, EepromErrorBit))
            {
                _logger?.LogError("EEPROM read or write error");
                return MarkErrorCode.FlashError;
            }
            if (IsBitSet(errorCode, UnsupportedWindowsBit))
            {
                _logger?.LogError("Unsupported Windows version. reqister druing init_rtc_dll");
                return MarkErrorCode.UnsupportedWindowsVersion;
            }

            _logger?.LogError($"unknown error code : {errorCode}");
            return MarkErrorCode.UnknownError;
        }

        /// <summary>
        /// 检查指定位是否被设置
        /// </summary>
        private bool IsBitSet(uint value, int bitPosition)
        {
            return (value & (1u << bitPosition)) != 0;
        }

        #endregion

        #region 枚举定义

        /// <summary>
        /// RTC校准选项枚举
        /// </summary>
        private enum RtcCalibrationOptions
        {
            RESTRICT_CORRECTION_FILE = 0x0001,          // 限制校准文件范围为实测点覆盖区域
            USE_POLYGON_RESTRICTION = 0x0002,           // 限制区域为凸多边形（默认矩形）
            DO_AUTOMATIC_CALIBRATION_TO_RESTRICTION = 0x0004, // 自动设置校准系数适配限制区域
            SET_CENTER_OFFSET_TO_ZERO = 0x0008,         // 中心（x=0,y=0）偏移置零
            USE_IMPROVE_OLD_FILE_MODE = 0x0010,         // 基于旧文件优化（而非重新计算）
            SET_MANUAL_CALIBRATION = 0x0020,            // 手动设置校准系数
            USE_AUTO_TOLERANCE = 0x0040,                // 自动调整拟合公差（V1.1+ 已废弃）
            FASTER_RUNTIME_FIND_FIT_ORDER = 0x0080,     // 快速查找最优拟合阶数（V1.4.0+）
            USE_MAX_FIT_ORDER = 0x0100                  // 启用最大拟合阶数限制（V1.4.0+）
        }

        #endregion


     
    }

     

    /// <summary>
    /// 像素行模式（对应 set_pixel_line 的 Channel Mode 部分）
    /// </summary>
    public enum PixelMode : uint
    {
        /// <summary>经典模式，非连续振镜运动，最高 400 kHz</summary>
        Classic = 0,
        /// <summary>扩展模式，最高 800 kHz</summary>
        Extended = 16,
        /// <summary>快速模式，最高 1.6 MHz（需 UFPM 选项）</summary>
        Fast = 32,
        /// <summary>超快模式，最高 3.2 MHz（需 UFPM 选项）</summary>
        UltraFast = 64,
        /// <summary>标准连续运动模式，振镜匀速，最高 400 kHz（推荐用于虚线）</summary>
        StandardMove = 256,
    }

    /// <summary>
    /// 像素输出端口（对应 set_pixel_line 的 Channel Port 部分）
    /// 注意：此处端口编号与 set_port_default 不同，Port_pixel = Port_default + 1
    /// </summary>
    public enum PixelPort : uint
    {
        /// <summary>12 位模拟输出端口 1（LASER 连接器, ANALOG OUT1）</summary>
        AnalogOut1 = 1,
        /// <summary>12 位模拟输出端口 2（LASER 连接器, ANALOG OUT2）</summary>
        AnalogOut2 = 2,
        /// <summary>8 位数字输出端口（EXTENSION2 插座）</summary>
        Digital8Bit = 3,
        /// <summary>16 位数字输出端口（EXTENSION1 插座）</summary>
        Digital16Bit = 4,
        /// <summary>脉冲时长输出（LASER1 信号），不可与 Mode=0 或 Mode=256 搭配使用</summary>
        PulseLength = 5,
    }

    /// <summary>
    /// 虚线图案参数
    /// </summary>
    public class DashPattern
    {
        /// <summary>实线段长度（bit）</summary>
        public int DashLength { get; set; } = 1000;

        /// <summary>空白段长度（bit），匀速不出光</summary>
        public int GapLength { get; set; } = 500;

        /// <summary>像素间距（bit），即相邻像素中心的距离。
        /// 该值决定了虚线的分辨率，通常设置为光斑直径。
        /// 例如：校正后 1bit = 1um，光斑 50um → PixelPitch = 50</summary>
        public double PixelPitch { get; set; } = 50.0;

        /// <summary>
        /// 像素输出周期的一半（单位：1/64 us）。
        /// 实际像素输出周期 = 2 × HalfPeriod / 64 us。
        /// 该值与 PixelPitch 共同决定振镜扫描速度：
        ///   Speed = PixelPitch / (2 × HalfPeriod / 64) [bit/us]
        /// 最小值：Mode 0/256 → 80，Mode 16 → 40，Mode 32 → 20，Mode 64 → 10
        /// 如不确定可设为 0，由 CalculateHalfPeriod 自动计算。
        /// </summary>
        public uint HalfPeriod { get; set; } = 0;

        /// <summary>激光脉冲长度（单位：1/64 us），用于 "出光" 像素。
        /// 设置为 0 时自动使用默认值。</summary>
        public uint PulseLengthOn { get; set; } = 0;

        /// <summary>模拟端口输出值（12-bit，0~4095），用于 "出光" 像素。
        /// 控制激光功率。</summary>
        public uint AnalogValueOn { get; set; } = 2000;
    }


}
