using DrSoft.MarkCard.Interface;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.Config;
using DrSoft.MarkCard.Model.MarkCommand;
using Microsoft.Extensions.Logging;
using System.Drawing;
using System.Runtime.InteropServices;

#if AnyCPU || x64
using AxMMMarkx641Lib;
using AxMMIOx641Lib;
using AxMMEditx641Lib;

#elif x86
using AxMMMark_1Lib;
using AxMMIO_1Lib;
using AxMMEdit_1Lib;
#endif

namespace DrSoft.MarkCard.EasternLogic
{
    /// <summary>
    /// 东方逻辑 PMC6 打标卡适配器。
    /// 基于 MarkingMate OCX 控件（MMMark.ocx / MMIO.ocx / MMEdit.ocx）实现 IMarkCardAdapter 接口。
    /// 通过即时指令（MarkLine / MarkDot / MarkArc / JumpTo）将 IMarkCommand 列表翻译为 PMC6 打标操作。
    /// 与 RTC6 的列表下发模型不同，PMC6 的即时指令为同步执行，LoadMarkData 预存命令与参数，
    /// StartMarking 在后台线程中按序执行即时指令。
    /// </summary>
    public class PMC6Adapter : IMarkCardAdapter
    {
        #region 常量定义

        /// <summary>
        /// 默认 MMCMark 配置文件名前缀（OCX 初始化用，实际路径为 /cfg_config_MM{N}）
        /// </summary>
        private string DefaultConfigPrefix = "\\cfg_config_MM";

        /// <summary>
        /// IO 端口位数
        /// </summary>
        private const int IoPortBits = 16;

        /// <summary>
        /// 状态轮询间隔（ms）
        /// </summary>
        private const int StatePollingInterval = 50;

        /// <summary>
        /// 硬件命令超时等待基数（ms）
        /// </summary>
        private const int HardwareCommandTimeoutBase = 50;

        /// <summary>
        /// 默认激光频率
        /// </summary>
        private const double DefaultFrequency = 200.0;

        /// <summary>
        /// 默认跳转速度
        /// </summary>
        private const double DefaultJumpSpeed = 1000.0;

        /// <summary>
        /// 默认打标速度
        /// </summary>
        private const double DefaultMarkSpeed = 500.0;

        #endregion

        #region 私有字段

        private readonly ILogger<PMC6Adapter> _logger;
        private readonly Config _config;
        private readonly CardConfig _cardConfig;

        private bool _isInitialized;
        private bool _isInitializing;
        private uint _cardNum;
        private MarkingMode _markingMode = MarkingMode.SoftwareMode;

        /// <summary>
        /// ActiveX 控件宿主窗体（隐藏），运行在专用 STA 线程上
        /// </summary>
        private System.Windows.Forms.Form? _hostForm;

        /// <summary>
        /// STA 线程：ActiveX 控件必须创建在 STA 线程上并具有消息循环
        /// </summary>
        private Thread? _staThread;

        /// <summary>
        /// 初始化完成信号
        /// </summary>
        private readonly ManualResetEventSlim _staReady = new ManualResetEventSlim(false);

        /// <summary>
        /// STA 线程初始化异常（若发生）
        /// </summary>
        private Exception? _staInitException;

#if AnyCPU || x64
        private List<AxMMMarkx641> _drMark = new();
        private List<AxMMIOx641> _drIo = new();
        private List<AxMMEditx641> _drEdit = new();

#elif x86
        private List<AxMMMark_1> _drMark = new();
        private List<AxMMIO_1> _drIo = new();
        private List<AxMMEdit_1> _drEdit = new();
#endif

        /// <summary>
        /// MMLensCal.ocx 镜头校正 COM 对象。
        /// 提供 LoadCorrectFile / SetCorrectDim / GridMarking 等校正计算方法，
        /// 这些方法在 AxMMEdit 控件上不可用。
        /// </summary>
        private dynamic? _lensCal;

        /// <summary>
        /// MMLensCal.ocx 是否可用
        /// </summary>
        private bool _lensCalAvailable;

        /// <summary>
        /// 每张卡的工艺参数
        /// </summary>
        private readonly Dictionary<uint, ProcessParam> _processParams = new();

        /// <summary>
        /// 每张卡待执行的打标命令列表（LoadMarkData 预存，StartMarking 执行）
        /// </summary>
        private readonly Dictionary<uint, List<IMarkCommand>> _pendingCommands = new();

        /// <summary>
        /// 每张卡的打标状态
        /// </summary>
        private readonly Dictionary<uint, MarkingState> _markingStates = new();

        /// <summary>
        /// 每张卡的校正档加载状态
        /// </summary>
        private readonly Dictionary<uint, bool> _calibrationLoaded = new();

        /// <summary>
        /// 每张卡的预估执行时间
        /// </summary>
        private readonly Dictionary<uint, int> _estimatedExecTimes = new();

        /// <summary>
        /// 每张卡的实际执行时间
        /// </summary>
        private readonly Dictionary<uint, int> _realExecTimes = new();

        /// <summary>
        /// 每张卡上一次记录的 MarkTime（用于计算差值）
        /// </summary>
        private readonly Dictionary<uint, int> _lastMarkTimes = new();

        /// <summary>
        /// 状态监控线程
        /// </summary>
        private Thread? _monitorThread;

        /// <summary>
        /// 监控线程运行标志
        /// </summary>
        private volatile bool _isMonitorRunning;

        /// <summary>
        /// 停止打标标志
        /// </summary>
        private volatile bool _isStopMarking;

        /// <summary>
        /// 正在打标中的卡号集合
        /// </summary>
        private readonly HashSet<uint> _markingCards = new();
        private readonly object _markingLock = new();

        /// <summary>
        /// 每张卡最后一次记录的振镜位置
        /// </summary>
        private readonly Dictionary<uint, PointF> _lastPositions = new();

        private readonly object _stateLock = new();

        #endregion

        #region 属性

        public uint CardNum => _cardNum;

        #endregion

        #region 构造函数

        public PMC6Adapter(ILogger<PMC6Adapter> logger, Config config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger;

            DefaultConfigPrefix = config.SystemConfig.DrMarkPath + DefaultConfigPrefix;

            _cardConfig = config.CardConfigs.Find(x => x.IsActive)
                ?? throw new Exception("未找到激活的打标卡");
        }

        #endregion

        #region 初始化

        /// <summary>
        /// 初始化打标卡：启动 STA 线程，创建并初始化 ActiveX 控件，加载校正档
        /// </summary>
        public MarkErrorCode Initialize()
        {
            if (_isInitialized)
                return MarkErrorCode.None;

            if (_isInitializing)
                return MarkErrorCode.MarkCardInitializing;

            _isInitializing = true;

            try
            {
                _logger?.LogInformation("初始化 PMC6 打标卡...");

                // 启动 STA 线程并等待 ActiveX 控件创建完成
                _staThread = new Thread(StaThreadMain)
                {
                    IsBackground = true,
                    Name = "PMC6-STA"
                };
                _staThread.SetApartmentState(ApartmentState.STA);
                _staThread.Start();

                // 等待 STA 线程完成 ActiveX 控件初始化
                if (!_staReady.Wait(TimeSpan.FromSeconds(30)))
                {
                    _logger?.LogError("PMC6 STA 线程初始化超时");
                    _isInitializing = false;
                    return MarkErrorCode.MarkCardInitializationFailed;
                }

                if (_staInitException != null)
                {
                    _logger?.LogError(_staInitException, "PMC6 ActiveX 控件初始化失败");
                    _isInitializing = false;
                    return MarkErrorCode.MarkCardInitializationFailed;
                }

                _cardNum = (uint)_cardConfig.CardCount;

                // 加载校正档
                for (int i = 0; i < _cardConfig.CardCount; i++)
                {
                    uint cardNo = (uint)(i + 1);
                    var headConfig = _config.ScanHeadConfigs?.FindAll(x => x.CardNo == cardNo);
                    if (headConfig != null && headConfig.Count > 0)
                    {
                        string? head1File = headConfig[0].HeadFilePath;
                        string? head2File = headConfig.Count > 1 ? headConfig[1].HeadFilePath : null;

                        var error = LoadCalibrationFileInternal(cardNo, head1File, head2File);
                        if (error != MarkErrorCode.None)
                        {
                            _logger?.LogError("加载打标卡{CardNo}校正档失败, 错误码{Error}", cardNo, error);
                            SetCalibrationLoaded(cardNo, false);
                        }
                        else
                        {
                            SetCalibrationLoaded(cardNo, true);
                            _logger?.LogInformation("加载卡号: {CardNo} 校正档成功", cardNo);
                        }

                        // 设置默认坐标变换（镜像、旋转等）
                        SetTransformMatrix(cardNo, headConfig[0].ScanHeadNo, 1, 0, 0, 1);
                        SetOffset(cardNo, headConfig[0].ScanHeadNo, 0, 0, 0);
                    }
                    else
                    {
                        _logger?.LogInformation("打标卡{CardNo} 未配置扫描头", cardNo);
                    }
                }

                // 设置默认工艺参数
                for (int i = 0; i < _cardConfig.CardCount; i++)
                {
                    uint cardNo = (uint)(i + 1);
                    var param = GetOrCreateProcessParam(cardNo);
                    param.MarkSpeed = DefaultMarkSpeed;
                    param.JumpSpeed = DefaultJumpSpeed;
                    param.Frequency = DefaultFrequency;

                    // 通过 OCX 设置默认参数
                    InvokeOCX(() =>
                    {
                        var mark = GetMarkControl(cardNo);
                        if (mark != null)
                        {
                            mark.SetSpeed("", DefaultMarkSpeed);
                            mark.SetJumpSpeed("", DefaultJumpSpeed);
                            mark.SetFrequency("", DefaultFrequency);
                            mark.SetPower("", 100);
                        }
                    });
                }

                // 启动状态监控线程
                _isMonitorRunning = true;
                _monitorThread = new Thread(MarkingStateMonitor)
                {
                    IsBackground = true,
                    Name = "PMC6-Monitor"
                };
                _monitorThread.Start();

                _isInitialized = true;
                _isInitializing = false;
                _logger?.LogInformation("PMC6 打标卡初始化完成, 卡数: {CardNum}", _cardNum);
                return MarkErrorCode.None;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "PMC6 打标卡初始化异常");
                _isInitializing = false;
                return MarkErrorCode.MarkCardInitializationFailed;
            }
        }

        /// <summary>
        /// STA 线程主函数：创建隐藏窗体，初始化 ActiveX 控件，运行消息循环
        /// </summary>
        private void StaThreadMain()
        {
            try
            {
                _hostForm = new System.Windows.Forms.Form
                {
                    ShowInTaskbar = false,
                    WindowState = System.Windows.Forms.FormWindowState.Minimized,
                    Opacity = 0,
                    Width = 0,
                    Height = 0
                };

                _hostForm.Load += (s, e) =>
                {
                    try
                    {
                        InitializeActiveXControls();
                        _staReady.Set();
                    }
                    catch (Exception ex)
                    {
                        _staInitException = ex;
                        _staReady.Set();
                    }
                };

                _hostForm.FormClosed += (s, e) =>
                {
                    // 退出消息循环
                };

                System.Windows.Forms.Application.Run(_hostForm);
            }
            catch (Exception ex)
            {
                _staInitException = ex;
                _staReady.Set();
            }
        }

        /// <summary>
        /// 在 STA 线程上创建并初始化所有 ActiveX 控件
        /// </summary>
        private void InitializeActiveXControls()
        {
            for (int i = 0; i < _cardConfig.CardCount; i++)
            {
                string cfgName = $"{DefaultConfigPrefix}{i + 1}";

#if AnyCPU || x64
                var mark = new AxMMMarkx641();
                var io = new AxMMIOx641();
                var edit = new AxMMEditx641();
#elif x86
                var mark = new AxMMMark_1();
                var io = new AxMMIO_1();
                var edit = new AxMMEdit_1();
#endif
                // 初始化 Mark 控件
                ((System.ComponentModel.ISupportInitialize)mark).BeginInit();
                _hostForm!.Controls.Add(mark);
                ((System.ComponentModel.ISupportInitialize)mark).EndInit();

                // 初始化 IO 控件
                ((System.ComponentModel.ISupportInitialize)io).BeginInit();
                _hostForm.Controls.Add(io);
                ((System.ComponentModel.ISupportInitialize)io).EndInit();

                // 初始化 Edit 控件
                ((System.ComponentModel.ISupportInitialize)edit).BeginInit();
                _hostForm.Controls.Add(edit);
                ((System.ComponentModel.ISupportInitialize)edit).EndInit();

                // OCX 初始化
                // 必须设置 CurrentDirectory，否则 Initial_Begin 在当前工作目录
                // 找不到 config_MM1.ini，或读取的 Directory 参数路径不存在
                if (AppDomain.CurrentDomain.BaseDirectory != null)
                    Environment.CurrentDirectory = AppDomain.CurrentDomain.BaseDirectory;

                mark.SetCloseErrorMsgBoxExt(cfgName, 1);
                mark.Initial_Begin(cfgName);
                // 等待初始化完成（轮询 IsInitializing）
                int waitCount = _cardConfig.InitTimeout / HardwareCommandTimeoutBase;
                while (mark.IsInitializing() == 1 && waitCount > 0)
                {
                    System.Windows.Forms.Application.DoEvents();
                    Thread.Sleep(HardwareCommandTimeoutBase);
                    waitCount--;
                }
                mark.Initial_End();

                edit.InitialExt(cfgName);
                io.InitialExt(cfgName);

                mark.MarkStandBy();
                mark.LaserOff();

                // 设置默认频率
                mark.SetFrequency("", DefaultFrequency);

                // 订阅 MarkEnd 事件
                int cardIndex = i;
                mark.MarkEnd += (sender, e) =>
                {
                    uint cardNo = (uint)(cardIndex + 1);
                    OnMarkingEnd?.Invoke(cardNo, MarkingState.MarkEnd);
                };

                _drMark.Add(mark);
                _drIo.Add(io);
                _drEdit.Add(edit);

                _logger?.LogInformation("打标卡{CardNo} ActiveX 控件初始化完成", i + 1);
            }

            // 初始化 MMLensCal.ocx 镜头校正控件（COM 后期绑定）
          //TryInitializeLensCal();
        }

        /// <summary>
        /// 创建并初始化 MMLensCal 镜头校正控件。
        /// MMLensCal 提供 LoadCorrectFile（下载测量座标校正表）、SetCorrectDim（设定格点总数）、
        /// GridMarking（输出格点）等校正计算函数，这些函数在 AxMMEdit 控件上不可用。
        /// 必须在 MMMark 控件初始化之后调用。
        /// </summary>
        private void TryInitializeLensCal()
        {
            try
            {
#if AnyCPU || x64
                // x64/AnyCPU：从 MarkingMate 安装目录动态加载互工程序集
                // 不使用编译时 Reference，避免 DLL 复制和 JIT 类型解析问题
                string mmAssemblyDir = Path.Combine(_config.SystemConfig.DrMarkPath, "MmAssembly");
                string axDllPath = Path.Combine(mmAssemblyDir, "AxMMLensCal_x64_1.dll");

                if (!File.Exists(axDllPath))
                {
                    _lensCalAvailable = false;
                    _logger?.LogWarning("MMLensCal 互工程序集不存在: {Path}，校正功能将不可用", axDllPath);
                    return;
                }

                // 注册 AssemblyResolve 事件，确保依赖程序集（MMLensCalx641Lib.dll）能从同目录解析
                AppDomain.CurrentDomain.AssemblyResolve += (sender, e) =>
                {
                    if (e.Name.StartsWith("MMLensCalx641Lib", StringComparison.OrdinalIgnoreCase))
                    {
                        string depPath = Path.Combine(mmAssemblyDir, "MMLensCalx641Lib.dll");
                        if (File.Exists(depPath))
                            return System.Reflection.Assembly.LoadFrom(depPath);
                    }
                    return null;
                };

                var asm = System.Reflection.Assembly.LoadFrom(axDllPath);
                Type? type = asm.GetType("AxMMLensCalx641Lib.AxMMLensCalx641");
                if (type == null)
                {
                    _lensCalAvailable = false;
                    _logger?.LogWarning("AxMMLensCalx641 类型未找到");
                    return;
                }

                dynamic lensCal = Activator.CreateInstance(type);
                ((System.ComponentModel.ISupportInitialize)lensCal).BeginInit();
                _hostForm!.Controls.Add((System.Windows.Forms.Control)lensCal);
                ((System.ComponentModel.ISupportInitialize)lensCal).EndInit();
                lensCal.Initial();
                _lensCal = lensCal;
                _lensCalAvailable = true;
                _logger?.LogInformation("MMLensCal 镜头校正控件初始化成功 (AxMMLensCalx641)");
#elif x86
                // x86：保持 COM 后期绑定方式
                string[] progIds = { "MMLensCal_1.MMLensCal_1", "MMLensCal.MMLensCal" };
                foreach (string progId in progIds)
                {
                    try
                    {
                        Type? type = Type.GetTypeFromProgID(progId, throwOnError: false);
                        if (type == null)
                            continue;

                        _lensCal = Activator.CreateInstance(type);
                        _lensCal.Initial();
                        _lensCalAvailable = true;
                        _logger?.LogInformation("MMLensCal 镜头校正控件初始化成功 (ProgID: {ProgId})", progId);
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug(ex, "尝试 MMLensCal ProgID '{ProgId}' 失败", progId);
                    }
                }

                _lensCalAvailable = false;
                _logger?.LogWarning("MMLensCal.ocx 不可用，CreateCalibrationFile 校正功能将不可用。" +
                                    "请确保 MarkingMate 软件已安装并注册了 MMLensCal.ocx");
#endif
            }
            catch (Exception ex)
            {
                _lensCalAvailable = false;
                _logger?.LogWarning(ex, "MMLensCal 镜头校正控件初始化失败，CreateCalibrationFile 校正功能将不可用。");
            }
        }

        #endregion

        #region 打标数据加载

        /// <summary>
        /// 下发打标数据：将 IMarkCommand 列表翻译为 PMC6 即时指令参数并预存几何命令。
        /// 参数类命令立即生效，几何类命令预存待 StartMarking 执行。
        /// </summary>
        public MarkErrorCode LoadMarkData(uint cardNo, List<IMarkCommand> commands)
        {
            if (!_isInitialized)
                return MarkErrorCode.Uninitialized;

            if (!GetCalibrationLoaded(cardNo))
                return MarkErrorCode.UnLoadCalibration;

            if (commands == null || commands.Count == 0)
                return MarkErrorCode.InvalidParameter;

            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
                return state;

            try
            {
                // 预存命令列表
                _pendingCommands[cardNo] = new List<IMarkCommand>(commands);

                // 异步预计算执行时间
                Task.Run(() =>
                {
                    try
                    {
                        int time = EstimateExecTime(commands);
                        _estimatedExecTimes[cardNo] = time;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "预计算打标数据执行时间异常");
                    }
                });

                // 在 STA 线程上应用参数类命令（立即生效）
                InvokeOCX(() =>
                {
                    var mark = GetMarkControl(cardNo);
                    if (mark == null) return;

                    foreach (var command in commands)
                    {
                        switch (command.MarkCommandType)
                        {
                            case MarkCommandType.ModifySpeed:
                                var speedCmd = command as ModifySpeedCommand;
                                if (speedCmd != null)
                                {
                                    mark.SetJumpSpeed("", speedCmd.JumpSpeed);
                                    mark.SetSpeed("", speedCmd.MarkSpeed);
                                    var param = GetOrCreateProcessParam(cardNo);
                                    param.JumpSpeed = speedCmd.JumpSpeed;
                                    param.MarkSpeed = speedCmd.MarkSpeed;
                                }
                                break;

                            case MarkCommandType.ModifyPower:
                                var powerCmd = command as ModifyPowerCommand;
                                if (powerCmd != null)
                                {
                                    mark.SetPower("", powerCmd.Power);
                                    GetOrCreateProcessParam(cardNo).Power = powerCmd.Power;
                                }
                                break;

                            case MarkCommandType.ModifyFrequencyAndPulsesWidth:
                                var freqCmd = command as ModifyFrequencyAndPulsesWidthCommand;
                                if (freqCmd != null)
                                {
                                    mark.SetFrequency("", freqCmd.Frequency);
                                    mark.SetPulseWidth("", freqCmd.PulsesWidth);
                                    var param = GetOrCreateProcessParam(cardNo);
                                    param.Frequency = freqCmd.Frequency;
                                    param.Pulse = freqCmd.PulsesWidth;
                                }
                                break;

                            case MarkCommandType.ModifyLaserDelay:
                                var laserDelayCmd = command as ModifyLaserDelayCommand;
                                if (laserDelayCmd != null)
                                {
                                    mark.SetLaserOnDelay("", laserDelayCmd.LaserOnDelay);
                                    mark.SetLaserOffDelay("", laserDelayCmd.LaserOffDelay);
                                    var param = GetOrCreateProcessParam(cardNo);
                                    param.LaserOnDelay = laserDelayCmd.LaserOnDelay;
                                    param.LaserOffDelay = laserDelayCmd.LaserOffDelay;
                                }
                                break;

                            case MarkCommandType.ModifyScannerDelay:
                                var scannerDelayCmd = command as ModifyScannerDelayCommand;
                                if (scannerDelayCmd != null)
                                {
                                    mark.SetMarkDelay("", scannerDelayCmd.MarkDelay);
                                    mark.SetJumpDelay("", scannerDelayCmd.JumpDelay);
                                    mark.SetPolyDelay("", scannerDelayCmd.CornerDelay);
                                    var param = GetOrCreateProcessParam(cardNo);
                                    param.MarkDelay = scannerDelayCmd.MarkDelay;
                                    param.JumpDelay = scannerDelayCmd.JumpDelay;
                                    param.PolyDelay = scannerDelayCmd.CornerDelay;
                                }
                                break;

                            case MarkCommandType.SkyWritingCommand:
                                var skyCmd = command as SkyWritingCommand;
                                if (skyCmd != null)
                                {
                                    // PMC6 通过加速度控制实现类似 SkyWriting 的功能
                                    mark.SetACCEnable(skyCmd.SkyWritingModel>0 ? 1 : 0);
                                    if (skyCmd.SkyWritingModel>0)
                                    {
                                        mark.SetACCLimitAngle(skyCmd.AngleLimit);
                                        mark.SetACCTime("", skyCmd.Timelag);
                                    }
                                }
                                break;

                            // 几何类命令预存，不在此处理
                            case MarkCommandType.MarkLine:
                            case MarkCommandType.MarkPoint:
                            case MarkCommandType.MarkCircle:
                            case MarkCommandType.JumpCommand:
                            case MarkCommandType.MarkDashedLineCommand:
                            case MarkCommandType.MarkBitmapCommand:
                                break;

                            default:
                                _logger?.LogWarning("不支持的命令类型: {Type}", command.MarkCommandType);
                                break;
                        }
                    }
                });

                _logger?.LogInformation("打标卡{CardNo} 加载打标数据完成, 命令数: {Count}", cardNo, commands.Count);
                return MarkErrorCode.None;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "加载打标数据异常");
                return MarkErrorCode.UnknownError;
            }
        }

        /// <summary>
        /// 预估打标执行时间（ms）
        /// </summary>
        private int EstimateExecTime(List<IMarkCommand> commands)
        {
            if (commands == null || commands.Count == 0)
                return 0;

            double totalTime = 0;
            double markSpeed = DefaultMarkSpeed;
            double jumpSpeed = DefaultJumpSpeed;
            PointF currentPos = PointF.Empty;

            foreach (var command in commands)
            {
                switch (command.MarkCommandType)
                {
                    case MarkCommandType.ModifySpeed:
                        var speedCmd = command as ModifySpeedCommand;
                        if (speedCmd != null)
                        {
                            markSpeed = speedCmd.MarkSpeed > 0 ? speedCmd.MarkSpeed : markSpeed;
                            jumpSpeed = speedCmd.JumpSpeed > 0 ? speedCmd.JumpSpeed : jumpSpeed;
                        }
                        break;

                    case MarkCommandType.MarkLine:
                        var lineCmd = command as MarkLineCommand;
                        if (lineCmd != null)
                        {
                            double dist = Distance(currentPos, lineCmd.EndPoint);
                            totalTime += dist / Math.Max(markSpeed, 0.1) * 1000;
                            currentPos = lineCmd.EndPoint;
                        }
                        break;

                    case MarkCommandType.JumpCommand:
                        var jumpCmd = command as JumpCommand;
                        if (jumpCmd != null)
                        {
                            double dist = Distance(currentPos, jumpCmd.Point);
                            totalTime += dist / Math.Max(jumpSpeed, 0.1) * 1000;
                            currentPos = jumpCmd.Point;
                        }
                        break;

                    case MarkCommandType.MarkPoint:
                        var pointCmd = command as MarkPointCommand;
                        if (pointCmd != null)
                        {
                            totalTime += pointCmd.DotDuration / 1000.0;
                            currentPos = pointCmd.Point;
                        }
                        break;

                    case MarkCommandType.MarkCircle:
                        var circleCmd = command as MarkCircleCommand;
                        if (circleCmd != null)
                        {
                            double arcLength = 2 * Math.PI * circleCmd.Radius * Math.Abs(circleCmd.Angle) / 360.0;
                            totalTime += arcLength / Math.Max(markSpeed, 0.1) * 1000;
                            currentPos = circleCmd.StartPoint;
                        }
                        break;
                }
            }

            return (int)Math.Round(totalTime);
        }

        private static double Distance(PointF a, PointF b)
        {
            return Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));
        }

        public int GetEstimatedExecTime(uint cardNo)
        {
            return _estimatedExecTimes.TryGetValue(cardNo, out int time) ? time : 0;
        }

        #endregion

        #region 打标控制

        public event Action<uint, MarkingState>? OnMarkingEnd;

        /// <summary>
        /// 开始打标（指定卡号）：在后台线程中按序执行预存的即时指令
        /// </summary>
        public MarkErrorCode StartMarking(uint cardNo)
        {
            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
                return state;

            if (!_isInitialized)
                return MarkErrorCode.Uninitialized;

            if (!GetCalibrationLoaded(cardNo))
                return MarkErrorCode.UnLoadCalibration;

            if (!_pendingCommands.TryGetValue(cardNo, out var commands) || commands.Count == 0)
            {
                _logger?.LogWarning("打标卡{CardNo} 无待执行打标数据", cardNo);
                return MarkErrorCode.InvalidParameter;
            }

            _isStopMarking = false;

            lock (_markingLock)
            {
                _markingCards.Add(cardNo);
            }

            lock (_stateLock)
            {
                _markingStates[cardNo] = MarkingState.Marking;
            }

            // 在后台线程执行即时指令
            Task.Run(() =>
            {
                int startTime = Environment.TickCount;
                try
                {
                    InvokeOCX(() =>
                    {
                        var mark = GetMarkControl(cardNo);
                        if (mark == null) return;

                        mark.MarkStandBy();

                        // 记录上一次位置（用于 MarkLine 的起点）
                        PointF lastPos = PointF.Empty;
                        bool firstCommand = true;

                        foreach (var command in commands)
                        {
                            if (_isStopMarking)
                                break;

                            switch (command.MarkCommandType)
                            {
                                case MarkCommandType.JumpCommand:
                                    var jumpCmd = command as JumpCommand;
                                    if (jumpCmd != null)
                                    {
                                        mark.JumpTo(jumpCmd.Point.X, jumpCmd.Point.Y);
                                        lastPos = jumpCmd.Point;
                                        firstCommand = false;
                                    }
                                    break;

                                case MarkCommandType.MarkLine:
                                    var lineCmd = command as MarkLineCommand;
                                    if (lineCmd != null)
                                    {
                                        if (firstCommand)
                                        {
                                            // 第一条命令先跳转到起点（原点）
                                            mark.JumpTo(0, 0);
                                            firstCommand = false;
                                        }
                                        mark.MarkLine(lastPos.X, lastPos.Y, lineCmd.EndPoint.X, lineCmd.EndPoint.Y);
                                        lastPos = lineCmd.EndPoint;
                                    }
                                    break;

                                case MarkCommandType.MarkPoint:
                                    var pointCmd = command as MarkPointCommand;
                                    if (pointCmd != null)
                                    {
                                        mark.JumpTo(pointCmd.Point.X, pointCmd.Point.Y);
                                        // 设置点延时
                                        mark.SetSpotDelay("", (int)Math.Round(pointCmd.DotDuration));
                                        mark.MarkDot(pointCmd.Point.X, pointCmd.Point.Y);
                                        lastPos = pointCmd.Point;
                                        firstCommand = false;
                                    }
                                    break;

                                case MarkCommandType.MarkCircle:
                                    var circleCmd = command as MarkCircleCommand;
                                    if (circleCmd != null)
                                    {
                                        mark.JumpTo(circleCmd.StartPoint.X, circleCmd.StartPoint.Y);
                                        firstCommand = false;

                                        if (Math.Abs(Math.Abs(circleCmd.Angle) - 360f) < 0.01f)
                                        {
                                            // 整圆
                                            mark.MarkCircle(circleCmd.Center.X, circleCmd.Center.Y, circleCmd.Radius);
                                        }
                                        else
                                        {
                                            // 圆弧：计算终点
                                            // 起点角度
                                            double startAngle = Math.Atan2(
                                                circleCmd.StartPoint.Y - circleCmd.Center.Y,
                                                circleCmd.StartPoint.X - circleCmd.Center.X);
                                            double endAngle = startAngle + circleCmd.Angle * Math.PI / 180.0;
                                            double endX = circleCmd.Center.X + circleCmd.Radius * Math.Cos(endAngle);
                                            double endY = circleCmd.Center.Y + circleCmd.Radius * Math.Sin(endAngle);
                                            int ccw = circleCmd.Angle > 0 ? 1 : 0;
                                            mark.MarkArc(circleCmd.StartPoint.X, circleCmd.StartPoint.Y,
                                                         endX, endY, circleCmd.Radius, ccw);
                                        }
                                        lastPos = circleCmd.StartPoint;
                                    }
                                    break;

                                case MarkCommandType.MarkDashedLineCommand:
                                    var dashCmd = command as MarkDashedLineCommand;
                                    if (dashCmd != null && dashCmd.DashArray != null && dashCmd.DashArray.Count > 0)
                                    {
                                        if (firstCommand)
                                        {
                                            var sp = dashCmd.StartPoint.GetValueOrDefault(PointF.Empty);
                                            mark.JumpTo(sp.X, sp.Y);
                                            firstCommand = false;
                                        }
                                        ExecuteDashedLine(mark, dashCmd);
                                        lastPos = dashCmd.EndPoint.GetValueOrDefault(lastPos);
                                    }
                                    break;

                                case MarkCommandType.MarkBitmapCommand:
                                    // 位图打标暂不支持即时指令方式
                                    _logger?.LogWarning("PMC6 即时指令模式暂不支持位图打标");
                                    break;
                            }
                        }

                        mark.MarkShutdown();
                    });

                    // 记录实际执行时间
                    int elapsed = Environment.TickCount - startTime;
                    _realExecTimes[cardNo] = elapsed;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "打标卡{CardNo} 执行打标异常", cardNo);
                }
                finally
                {
                    lock (_markingLock)
                    {
                        _markingCards.Remove(cardNo);
                    }

                    lock (_stateLock)
                    {
                        _markingStates[cardNo] = MarkingState.MarkEnd;
                    }

                    OnMarkingEnd?.Invoke(cardNo, MarkingState.MarkEnd);
                    _logger?.LogInformation("打标卡{CardNo} 打标完成, 耗时{Time}ms", cardNo, Environment.TickCount - startTime);
                }
            });

            return MarkErrorCode.None;
        }

        /// <summary>
        /// 开始打标（默认卡号1）
        /// </summary>
        public MarkErrorCode StartMarking()
        {
            return StartMarking(1);
        }

  
        public MarkErrorCode StopMarking(uint cardNo)
        {
            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
                return state;

            if (!_isInitialized)
                return MarkErrorCode.Uninitialized;

            _isStopMarking = true;

            try
            {
                InvokeOCX(() =>
                {
                    var mark = GetMarkControl(cardNo);
                    mark?.StopMarking();
                });

                lock (_stateLock)
                {
                    _markingStates[cardNo] = MarkingState.Ready;
                }

                _logger?.LogInformation("打标卡{CardNo} 停止打标", cardNo);
                return MarkErrorCode.None;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "停止打标异常");
                return MarkErrorCode.UnknownError;
            }
        }

        public MarkErrorCode StopMarking()
        {
            return StopMarking(1);
        }

        public MarkErrorCode Pause()
        {
            return Pause(1);
        }

        public MarkErrorCode Pause(uint cardNo)
        {
            if (!_isInitialized)
                return MarkErrorCode.Uninitialized;

            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
                return state;

            try
            {
                InvokeOCX(() =>
                {
                    var mark = GetMarkControl(cardNo);
                    mark?.PauseMarking();
                });
                return MarkErrorCode.None;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "暂停打标异常");
                return MarkErrorCode.UnknownError;
            }
        }

        public MarkErrorCode Resume()
        {
            return Resume(1);
        }

        public MarkErrorCode Resume(uint cardNo)
        {
            if (!_isInitialized)
                return MarkErrorCode.Uninitialized;

            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
                return state;

            try
            {
                InvokeOCX(() =>
                {
                    var mark = GetMarkControl(cardNo);
                    mark?.ResumeMarking();
                });
                return MarkErrorCode.None;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "恢复打标异常");
                return MarkErrorCode.UnknownError;
            }
        }

        #endregion

        #region 激光控制

        public MarkErrorCode LaserOn()
        {
            return LaserOn(1);
        }

        public MarkErrorCode LaserOn(uint cardNo)
        {
            if (!_isInitialized)
                return MarkErrorCode.Uninitialized;

            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
                return state;

            try
            {
                InvokeOCX(() =>
                {
                    var mark = GetMarkControl(cardNo);
                    mark?.MarkStandBy();
                    mark?.LaserOn();
                });
                _logger?.LogInformation("打标卡{CardNo} 激光开启", cardNo);
                return MarkErrorCode.None;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "开启激光异常");
                return MarkErrorCode.UnknownError;
            }
        }

        public MarkErrorCode LaserOff()
        {
            return LaserOff(1);
        }

        public MarkErrorCode LaserOff(uint cardNo)
        {
            if (!_isInitialized)
                return MarkErrorCode.Uninitialized;

            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
                return state;

            try
            {
                InvokeOCX(() =>
                {
                    var mark = GetMarkControl(cardNo);
                    mark?.LaserOff();
                    mark?.MarkShutdown();
                });
                _logger?.LogInformation("打标卡{CardNo} 激光关闭", cardNo);
                return MarkErrorCode.None;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "关闭激光异常");
                return MarkErrorCode.UnknownError;
            }
        }

        #endregion

        #region 参数设置

        public MarkErrorCode SetLaserPower(uint cardNo, double power)
        {
            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
                return state;

            if (!_isInitialized)
                return MarkErrorCode.Uninitialized;

            try
            {
                InvokeOCX(() =>
                {
                    var mark = GetMarkControl(cardNo);
                    mark?.SetPower("", power);
                });
                GetOrCreateProcessParam(cardNo).Power = power;
                return MarkErrorCode.None;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "设置激光功率异常");
                return MarkErrorCode.UnknownError;
            }
        }

        public MarkErrorCode SetLaserFrequency(uint cardNo, double frequency)
        {
            return SetLaserFrequencyAndPulseWidth(cardNo, frequency, 100);
        }

        public MarkErrorCode SetLaserFrequencyAndPulseWidth(uint cardNo, double frequency, double pulseWidth)
        {
            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
                return state;

            if (!_isInitialized)
                return MarkErrorCode.Uninitialized;

            try
            {
                InvokeOCX(() =>
                {
                    var mark = GetMarkControl(cardNo);
                    mark?.SetFrequency("", frequency);
                    mark?.SetPulseWidth("", pulseWidth);
                });
                var param = GetOrCreateProcessParam(cardNo);
                param.Frequency = frequency;
                param.Pulse = pulseWidth;
                _logger?.LogInformation("打标卡{CardNo} 设置频率={Freq}kHz 脉宽={Pulse}us", cardNo, frequency, pulseWidth);
                return MarkErrorCode.None;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "设置激光频率和脉宽异常");
                return MarkErrorCode.UnknownError;
            }
        }

        public MarkErrorCode SetLaserDelay(uint cardNo, int laserOnDelay, int laserOffDelay)
        {
            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
                return state;

            if (!_isInitialized)
                return MarkErrorCode.Uninitialized;

            try
            {
                InvokeOCX(() =>
                {
                    var mark = GetMarkControl(cardNo);
                    mark?.SetLaserOnDelay("", laserOnDelay);
                    mark?.SetLaserOffDelay("", laserOffDelay);
                });
                var param = GetOrCreateProcessParam(cardNo);
                param.LaserOnDelay = laserOnDelay;
                param.LaserOffDelay = laserOffDelay;
                return MarkErrorCode.None;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "设置激光延时异常");
                return MarkErrorCode.UnknownError;
            }
        }

        public MarkErrorCode SetScannerDelay(uint cardNo, int markDelay, int jumpDelay, int polygonDelay)
        {
            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
                return state;

            if (!_isInitialized)
                return MarkErrorCode.Uninitialized;

            try
            {
                InvokeOCX(() =>
                {
                    var mark = GetMarkControl(cardNo);
                    if (mark != null)
                    {
                        mark.SetMarkDelay("", markDelay);
                        mark.SetJumpDelay("", jumpDelay);
                        mark.SetPolyDelay("", polygonDelay);
                    }
                });
                var param = GetOrCreateProcessParam(cardNo);
                param.MarkDelay = markDelay;
                param.JumpDelay = jumpDelay;
                param.PolyDelay = polygonDelay;
                _logger?.LogInformation("打标卡{CardNo} 设置扫描延时: mark={Mark} jump={Jump} poly={Poly}",
                    cardNo, markDelay, jumpDelay, polygonDelay);
                return MarkErrorCode.None;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "设置扫描延时异常");
                return MarkErrorCode.UnknownError;
            }
        }

        public MarkErrorCode SetScannerSpeed(uint cardNo, double jumpSpeed, double markSpeed)
        {
            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
                return state;

            if (!_isInitialized)
                return MarkErrorCode.Uninitialized;

            try
            {
                InvokeOCX(() =>
                {
                    var mark = GetMarkControl(cardNo);
                    mark?.SetJumpSpeed("", jumpSpeed);
                    mark?.SetSpeed("", markSpeed);
                });
                var param = GetOrCreateProcessParam(cardNo);
                param.JumpSpeed = jumpSpeed;
                param.MarkSpeed = markSpeed;
                return MarkErrorCode.None;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "设置扫描速度异常");
                return MarkErrorCode.UnknownError;
            }
        }

        public MarkErrorCode SetMarkingMode(MarkingMode mode)
        {
            return SetMarkingMode(1, mode);
        }

        public MarkErrorCode SetMarkingMode(uint cardNo, MarkingMode mode)
        {
            if (!_isInitialized)
                return MarkErrorCode.Uninitialized;

            _markingMode = mode;
            _logger?.LogInformation("打标卡{CardNo} 设置打标模式: {Mode}", cardNo, mode);
            return MarkErrorCode.None;
        }

        #endregion

        #region 坐标变换

        public MarkErrorCode SetTransformMatrix(uint cardNo, uint headID, float m00, float m01, float m10, float m11)
        {
            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
                return state;

            try
            {
                // PMC6 通过 SetMatrixExt 设置平移+旋转+缩放
                // 仿射矩阵 [m00 m01; m10 m11] 转换为旋转角度和缩放比例
                double angle = Math.Atan2(m01, m00) * 180.0 / Math.PI;
                double scaleX = Math.Sqrt(m00 * m00 + m10 * m10);
                double scaleY = Math.Sqrt(m01 * m01 + m11 * m11);

                InvokeOCX(() =>
                {
                    var mark = GetMarkControl(cardNo);
                    mark?.SetMatrixExt(0, 0, 0, 0, angle, 0, 0, scaleX, scaleY);
                });

                // 设置镜像
                var scanHeadConfig = _config.ScanHeadConfigs?
                    .Find(x => x.CardNo == cardNo && x.ScanHeadNo == headID);
                if (scanHeadConfig != null)
                {
                    InvokeOCX(() =>
                    {
                        var mark = GetMarkControl(cardNo);
                        if (mark != null)
                        {
                            if (scanHeadConfig.MirrorX)
                                mark.SetLensXReverse(1);
                            if (scanHeadConfig.MirrorY)
                                mark.SetLensYReverse(1);
                            if (scanHeadConfig.ReverseXY)
                                mark.SetLensXYExchange(1);
                        }
                    });
                }

                _logger?.LogInformation("打标卡{CardNo} 扫描头{Head} 设置变换矩阵: angle={Angle} scaleX={Sx} scaleY={Sy}",
                    cardNo, headID, angle, scaleX, scaleY);
                return MarkErrorCode.None;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "设置变换矩阵异常");
                return MarkErrorCode.UnknownError;
            }
        }

        public MarkErrorCode SetOffset(uint cardNo, uint headID, double xOffset, double yOffset, double angleOffset)
        {
            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
                return state;

            if (_config.ScanHeadConfigs == null || _config.ScanHeadConfigs.Count <= 0)
            {
                _logger?.LogError("获取打标卡{CardNo}扫描头{Head}配置失败", cardNo, headID);
                return MarkErrorCode.UnFoundScanHeadConfigError;
            }

            var scanHeadConfig = _config.ScanHeadConfigs.Find(x => x.CardNo == cardNo && x.ScanHeadNo == headID);
            if (scanHeadConfig == null)
            {
                _logger?.LogError("获取打标卡{CardNo}扫描头{Head}配置失败", cardNo, headID);
                return MarkErrorCode.UnFoundScanHeadConfigError;
            }

            double totalXOffset = xOffset + scanHeadConfig.OffsetX;
            double totalYOffset = yOffset + scanHeadConfig.OffsetY;
            double totalAngleOffset = angleOffset + scanHeadConfig.AngleOffset;

            try
            {
                InvokeOCX(() =>
                {
                    var mark = GetMarkControl(cardNo);
                    mark?.SetMatrixExt(totalXOffset, totalYOffset, 0, 0, totalAngleOffset, 0, 0, 1, 1);
                });

                _logger?.LogInformation("打标卡{CardNo} 扫描头{Head} 设置偏移: X={X} Y={Y} Angle={Angle}",
                    cardNo, headID, totalXOffset, totalYOffset, totalAngleOffset);
                return MarkErrorCode.None;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "设置偏移异常");
                return MarkErrorCode.UnknownError;
            }
        }

        public MarkErrorCode SetScale(uint cardNo, uint headID, double transformScale)
        {
            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
                return state;

            if (!_isInitialized)
                return MarkErrorCode.Uninitialized;

            try
            {
                InvokeOCX(() =>
                {
                    var mark = GetMarkControl(cardNo);
                    mark?.SetMatrixExt(0, 0, 0, 0, 0, 0, 0, transformScale, transformScale);
                });
                _logger?.LogInformation("打标卡{CardNo} 扫描头{Head} 设置缩放: {Scale}", cardNo, headID, transformScale);
                return MarkErrorCode.None;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "设置缩放异常");
                return MarkErrorCode.UnknownError;
            }
        }

        public MarkErrorCode SetScannerAcc(uint cardNo, uint headID, double acc)
        {
            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
                return state;

            if (!_isInitialized)
                return MarkErrorCode.Uninitialized;

            try
            {
                InvokeOCX(() =>
                {
                    var mark = GetMarkControl(cardNo);
                    mark?.SetACCEnable(1);
                    mark?.SetACC(acc);
                });
                return MarkErrorCode.None;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "设置振镜加速度异常");
                return MarkErrorCode.UnknownError;
            }
        }

        /// <summary>
        /// 设置桶形校正，使用 MMEdit 的 SetLensCorConvert 实现。
        /// widthParam 为田字格3条横线长度（从下到上），heightParam 为田字格3条竖线高度（从左到右）。
        /// </summary>
        public MarkErrorCode SetBarrelCorrection(uint cardNo, double idealWidth, double idealHeight, double[] widthParam, double[] heightParam)
        {
            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
                return state;

            if (!_isInitialized)
                return MarkErrorCode.Uninitialized;

            if (widthParam == null || heightParam == null ||
                widthParam.Length < 3 || heightParam.Length < 3)
                return MarkErrorCode.InvalidParameter;

            try
            {
                InvokeOCX(() =>
                {
                    var edit = GetEditControl(cardNo);
                    if (edit == null) return;
                    edit.SetLensCorConvert(idealWidth,  idealHeight, widthParam[2], widthParam[1], widthParam[0],
                                           heightParam[2], heightParam[1], heightParam[0]);
                });

                _logger?.LogInformation("打标卡{CardNo} 设置桶形校正成功", cardNo);
                return MarkErrorCode.None;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "设置桶形校正异常");
                return MarkErrorCode.UnknownError;
            }
        }

        #endregion

        #region 参数查询

        public MarkErrorCode GetLaserFrequency(uint cardNo, out double frequency)
        {
            frequency = 0;
            if (!_processParams.TryGetValue(cardNo, out var param))
                return MarkErrorCode.Uninitialized;
            frequency = param.Frequency;
            return MarkErrorCode.None;
        }

        public MarkErrorCode GetLaserPulseWidth(uint cardNo, out double pulseWidth)
        {
            pulseWidth = 0;
            if (!_processParams.TryGetValue(cardNo, out var param))
                return MarkErrorCode.Uninitialized;
            pulseWidth = param.Pulse;
            return MarkErrorCode.None;
        }

        public MarkErrorCode GetJumpDelay(uint cardNo, out double jumpDelay)
        {
            jumpDelay = 0;
            if (!_processParams.TryGetValue(cardNo, out var param))
                return MarkErrorCode.Uninitialized;
            jumpDelay = param.JumpDelay;
            return MarkErrorCode.None;
        }

        public MarkErrorCode GetMarkingDelay(uint cardNo, out double markingDelay)
        {
            markingDelay = 0;
            if (!_processParams.TryGetValue(cardNo, out var param))
                return MarkErrorCode.Uninitialized;
            markingDelay = param.MarkDelay;
            return MarkErrorCode.None;
        }

        public MarkErrorCode GetPolygonDelay(uint cardNo, out double polygonDelay)
        {
            polygonDelay = 0;
            if (!_processParams.TryGetValue(cardNo, out var param))
                return MarkErrorCode.Uninitialized;
            polygonDelay = param.PolyDelay;
            return MarkErrorCode.None;
        }

        public MarkErrorCode GetLaserDelay(uint cardNo, out double laserOnDelay, out double laserOffDelay)
        {
            laserOnDelay = 0;
            laserOffDelay = 0;
            if (!_processParams.TryGetValue(cardNo, out var param))
                return MarkErrorCode.Uninitialized;
            laserOnDelay = param.LaserOnDelay;
            laserOffDelay = param.LaserOffDelay;
            return MarkErrorCode.None;
        }

        public MarkErrorCode GetMarkingMode(uint cardNo, out MarkingMode mode)
        {
            mode = _markingMode;
            return MarkErrorCode.None;
        }

        public MarkErrorCode GetMarkingState(uint cardNo, out MarkingState markState)
        {
            markState = MarkingState.None;

            if (!_isInitialized)
                return MarkErrorCode.Uninitialized;

            lock (_stateLock)
            {
                if (_markingStates.TryGetValue(cardNo, out var state))
                {
                    markState = state;
                    return MarkErrorCode.None;
                }
            }

            // 实时查询
            try
            {
                bool isMarking = false;
                InvokeOCX(() =>
                {
                    var mark = GetMarkControl(cardNo);
                    if (mark != null)
                    {
                        isMarking = mark.IsMarking() == 1;
                    }
                });

                markState = isMarking ? MarkingState.Marking : MarkingState.Ready;
                lock (_stateLock)
                {
                    _markingStates[cardNo] = markState;
                }
                return MarkErrorCode.None;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "获取打标卡{CardNo}状态失败", cardNo);
                markState = MarkingState.None;
                return MarkErrorCode.None;
            }
        }

        public MarkErrorCode GetRealExecTime(uint cardNo, out int execTime)
        {
            execTime = _realExecTimes.TryGetValue(cardNo, out int time) ? time : 0;
            return MarkErrorCode.None;
        }

        public MarkErrorCode GetScannerAcc(uint cardNo, uint headID, out double acc)
        {
            // PMC6 OCX 无直接查询加速度的方法
            acc = 0;
            return MarkErrorCode.UnsupportedFunction;
        }

        public MarkErrorCode GetScannerConnect(uint cardNo, uint headID, out bool connectFlag)
        {
            // PMC6 OCX 无直接查询振镜连接状态的方法，初始化成功即认为已连接
            connectFlag = _isInitialized;
            return MarkErrorCode.None;
        }

        public MarkErrorCode GetScannerPosion(uint cardNo, uint headID, out PointF point)
        {
            point = _lastPositions.TryGetValue(cardNo, out var p) ? p : PointF.Empty;
            return MarkErrorCode.None;
        }

        public MarkErrorCode GetScannerTemperature(uint cardNo, uint headID, out double temperatureX, out double temperatureY)
        {
            // PMC6/MarkingMate OCX 不支持温度查询（仅 RTCx 系列支持）
            temperatureX = 0;
            temperatureY = 0;
            return MarkErrorCode.UnsupportedFunction;
        }

        #endregion

        #region 校正文件

        public MarkErrorCode LoadCalibrationFile(string? head1File, string? head2File)
        {
            return LoadCalibrationFile(1, head1File, head2File);
        }

        public MarkErrorCode LoadCalibrationFile(uint cardNo, string? head1File, string? head2File)
        {
            if (!_isInitialized)
                return MarkErrorCode.Uninitialized;

            return LoadCalibrationFileInternal(cardNo, head1File, head2File);
        }

        private MarkErrorCode LoadCalibrationFileInternal(uint cardNo, string? head1File, string? head2File)
        {
            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
                return state;

            try
            {
                if (!string.IsNullOrEmpty(head1File))
                {
                    bool success = false;
                    InvokeOCX(() =>
                    {
                        var edit = GetEditControl(cardNo);
                        if (edit != null)
                        {
                            
                            int rtn = edit.ChangeLens(head1File);
                            success = rtn == 0;
                        }
                    });

                    if (success)
                    {
                        SetCalibrationLoaded(cardNo, true);
                        _logger?.LogInformation("打标卡{CardNo} 加载校正档成功: {File}", cardNo, head1File);
                    }
                    else
                    {
                        SetCalibrationLoaded(cardNo, false);
                        _logger?.LogError("打标卡{CardNo} 加载校正档失败: {File}", cardNo, head1File);
                        return MarkErrorCode.LoadCalibrationFailed;
                    }
                }

                return MarkErrorCode.None;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "加载校正档异常");
                SetCalibrationLoaded(cardNo, false);
                return MarkErrorCode.LoadCalibrationFailed;
            }
        }

        /// <summary>
        /// 创建校正文件。使用 MMLensCal.ocx 镜头校正控件实现。
        /// 流程：
        /// 1. ChangeLens 加载源镜头档
        /// 2. SetCorrectDim 设定格点总数
        /// 3. 将实测座标写入临时 TXT 文件（格式：索引 X Y）
        /// 4. LoadCorrectFile 下载座标校正表，OCX 内部计算校正矩阵
        /// 5. DuplicationCorrectTable 将校正结果保存到目标文件
        /// </summary>
        public MarkErrorCode CreateCalibrationFile(string srcFile, string dstFile,
            double[] targetPostX, double[] targetPostY,
            double[] realsPostX, double[] realsPostY)
        {
            if (!_isInitialized)
                return MarkErrorCode.Uninitialized;

            // 参数校验
            if (string.IsNullOrEmpty(srcFile) || string.IsNullOrEmpty(dstFile))
                return MarkErrorCode.InvalidParameter;

            if (targetPostX == null || targetPostY == null ||
                realsPostX == null || realsPostY == null)
                return MarkErrorCode.InvalidParameter;

            if (targetPostX.Length != targetPostY.Length ||
                targetPostX.Length != realsPostX.Length ||
                targetPostX.Length != realsPostY.Length)
                return MarkErrorCode.InvalidParameter;

            if (targetPostX.Length == 0)
                return MarkErrorCode.InvalidParameter;

            // 检查 MMLensCal.ocx 是否可用
            if (!_lensCalAvailable || _lensCal == null)
            {
                _logger?.LogError("MMLensCal.ocx 未初始化，无法创建校正文件。" +
                                  "请确保 MarkingMate 软件已安装并注册了 MMLensCal.ocx");
                return MarkErrorCode.UnsupportedFunction;
            }

            // 检查源文件是否存在
            if (!File.Exists(srcFile))
            {
                _logger?.LogError("源校正文件不存在: {File}", srcFile);
                return MarkErrorCode.FileError;
            }

            string tempTxt = string.Empty;

            try
            {
                // 生成临时校正数据文件（TXT 格式：索引 X座标 Y座标）
                // LoadCorrectFile 要求的 TXT 格式：
                // 格點索引值(空格)X軸公厘座標(空格)Y軸公厘座標(換行)
                // 索引範圍為 1~(總格點數)
                tempTxt = Path.Combine(Path.GetTempPath(), $"lenscor_{Guid.NewGuid():N}.txt");
                using (var writer = new StreamWriter(tempTxt, false, System.Text.Encoding.ASCII))
                {
                    for (int i = 0; i < realsPostX.Length; i++)
                    {
                        writer.WriteLine($"{i + 1} {realsPostX[i]} {realsPostY[i]}");
                    }
                }

                _logger?.LogInformation("校正数据文件已生成: {File}, 格点数: {Count}",
                    tempTxt, realsPostX.Length);

                bool success = false;
                string errorMsg = string.Empty;

                InvokeOCX(() =>
                {
                    // 1. 加载源镜头档
                    //long rtn = _lensCal.ChangeLens(srcFile);
                    //if (rtn != 0)
                    //{
                    //    errorMsg = $"ChangeLens 失败, 返回值={rtn}";
                    //    return;
                    //}

                    // 2. 设定格点总数
                    long dimRtn = _lensCal.SetCorrectDim(targetPostX.Length);
                    // SetCorrectDim 返回 0 为失败，非零值为实际设定的格点总数
                    if (dimRtn == 0)
                    {
                        errorMsg = $"SetCorrectDim 失败, 格点数={targetPostX.Length}";
                        return;
                    }

                    _logger?.LogInformation("设定格点总数: 请求={Req}, 实际={Actual}",
                        targetPostX.Length, dimRtn);

                    // 3. 下载座标校正表（OCX 内部计算校正矩阵）
                    long loadRtn = _lensCal.LoadCorrectFile(tempTxt);
                    if (loadRtn != 0)
                    {
                        errorMsg = $"LoadCorrectFile 失败, 返回值={loadRtn}";
                        return;
                    }

                    // 4. 将校正结果保存到目标文件
                    // DuplicationCorrectTable(head, filePath): 复制最后一次的镜头校正表
                    long dupRtn = _lensCal.DuplicationCorrectTable(1, dstFile);
                    if (dupRtn != 0)
                    {
                        errorMsg = $"DuplicationCorrectTable 失败, 返回值={dupRtn}";
                        return;
                    }

                    success = true;
                });

                if (success)
                {
                    _logger?.LogInformation("校正文件创建成功: {Src} -> {Dst}", srcFile, dstFile);
                    return MarkErrorCode.None;
                }
                else
                {
                    _logger?.LogError("创建校正文件失败: {Error}", errorMsg);
                    return MarkErrorCode.UnknownError;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "创建校正文件异常");
                return MarkErrorCode.UnknownError;
            }
            finally
            {
                // 清理临时文件
                if (!string.IsNullOrEmpty(tempTxt) && File.Exists(tempTxt))
                {
                    try { File.Delete(tempTxt); } catch { /* 忽略清理失败 */ }
                }
            }
        }

        #endregion

        #region IO 读写

        public MarkErrorCode ReadDigitalInput(uint cardNo, out bool[] value)
        {
            value = new bool[IoPortBits];
            bool[] input = new bool[IoPortBits];

            if (!_isInitialized)
                return MarkErrorCode.Uninitialized;

            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
                return state;

            try
            {
                bool ioAvailable = false;
                InvokeOCX(() =>
                {
                    var io = GetIoControl(cardNo);
                    if (io == null) return;
                    ioAvailable = true;
                    for (int i = 0; i < IoPortBits; i++)
                    {
                        int rtn = io.GetInput(i + 1);
                        input[i] = rtn != 0;
                    }
                });

                if (!ioAvailable)
                {
                    _logger?.LogError("打标卡{CardNo} IO控件未初始化", cardNo);
                    return MarkErrorCode.UnknownError;
                }

                value = input;
                return MarkErrorCode.None;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "读取数字输入异常");
                return MarkErrorCode.UnknownError;
            }
        }

        public MarkErrorCode ReadDigitalOutput(uint cardNo, out bool[] value)
        {
            value = new bool[IoPortBits];

            bool[] output = new bool[IoPortBits];

            if (!_isInitialized)
                return MarkErrorCode.Uninitialized;

            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
                return state;

            try
            {
                bool ioAvailable = false;
                InvokeOCX(() =>
                {
                    var io = GetIoControl(cardNo);
                    if (io == null) return;
                    ioAvailable = true;
                    for (int i = 0; i < IoPortBits; i++)
                    {
                        int rtn = io.GetOutput(i + 1);
                        output[i] = rtn != 0;
                    }
                });

                if (!ioAvailable)
                {
                    _logger?.LogError("打标卡{CardNo} IO控件未初始化", cardNo);
                    return MarkErrorCode.UnknownError;
                }
                
                value = output;
                return MarkErrorCode.None;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "读取数字输出异常");
                return MarkErrorCode.UnknownError;
            }
        }

        public MarkErrorCode WriteDigitalOutput(uint cardNo, uint signalIndex, bool setParam)
        {
            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
                return state;

            if (!_isInitialized)
                return MarkErrorCode.Uninitialized;

            if (signalIndex >= IoPortBits)
                return MarkErrorCode.InvalidParameter;

            try
            {
                InvokeOCX(() =>
                {
                    var io = GetIoControl(cardNo);
                    io?.SetOutput((int)(signalIndex + 1), setParam ? 1 : 0);
                });
                return MarkErrorCode.None;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "写入数字输出异常");
                return MarkErrorCode.UnknownError;
            }
        }

        public MarkErrorCode WriteDigitalOutput(uint cardNo, bool[] setParam)
        {
            var state = CheckCardNo(cardNo);
            if (state != MarkErrorCode.None)
                return state;

            if (!_isInitialized)
                return MarkErrorCode.Uninitialized;

            if (setParam == null || setParam.Length == 0)
                return MarkErrorCode.InvalidParameter;

            try
            {
                InvokeOCX(() =>
                {
                    var io = GetIoControl(cardNo);
                    if (io != null)
                    {
                        int count = Math.Min(setParam.Length, IoPortBits);
                        for (int i = 0; i < count; i++)
                        {
                            io.SetOutput(i + 1, setParam[i] ? 1 : 0);
                        }
                    }
                });
                return MarkErrorCode.None;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "批量写入数字输出异常");
                return MarkErrorCode.UnknownError;
            }
        }

        #endregion

        #region 状态监控

        /// <summary>
        /// 打标状态监控线程
        /// </summary>
        private void MarkingStateMonitor()
        {
            while (_isMonitorRunning)
            {
                for (int i = 0; i < _cardConfig.CardCount; i++)
                {
                    uint cardNo = (uint)(i + 1);

                    try
                    {
                        lock (_stateLock)
                        {
                            // 如果该卡正在后台线程中打标，保持 Marking 状态
                            bool isMarkingInBg;
                            lock (_markingLock)
                            {
                                isMarkingInBg = _markingCards.Contains(cardNo);
                            }

                            if (!isMarkingInBg)
                            {
                                // 非后台打标时，查询 OCX 实际状态
                                bool isMarking = false;
                                try
                                {
                                    InvokeOCX(() =>
                                    {
                                        var mark = GetMarkControl(cardNo);
                                        if (mark != null)
                                        {
                                            isMarking = mark.IsMarking() == 1;
                                        }
                                    });
                                }
                                catch { /* 忽略查询异常 */ }

                                if (isMarking)
                                {
                                    _markingStates[cardNo] = MarkingState.Marking;
                                }
                                else if (_markingStates.TryGetValue(cardNo, out var currentState) &&
                                         currentState == MarkingState.Marking)
                                {
                                    // 从打标状态变为非打标，说明打标完成
                                    _markingStates[cardNo] = MarkingState.MarkEnd;
                                }
                                else if (currentState != MarkingState.MarkEnd)
                                {
                                    _markingStates[cardNo] = MarkingState.Ready;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "监控打标卡{CardNo}状态异常", cardNo);
                    }
                }

                Thread.Sleep(StatePollingInterval);
            }
        }

        #endregion

        #region 资源释放

        public void Dispose()
        {
            // 停止监控线程
            _isMonitorRunning = false;
            if (_monitorThread != null && _monitorThread.IsAlive)
            {
                _monitorThread.Join(200);
            }

            // 在 STA 线程上释放 ActiveX 控件
            try
            {
                InvokeOCX(() =>
                {
                    // 释放 MMLensCal.ocx 镜头校正控件
                    if (_lensCalAvailable && _lensCal != null)
                    {
                        try
                        {
                            _lensCal.Finish();
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "释放 MMLensCal.ocx 异常");
                        }
                        // 如果是 AxHost 控件，从窗体移除并 Dispose
                        try
                        {
                            if (_lensCal is System.Windows.Forms.Control ctrl)
                            {
                                _hostForm?.Controls.Remove(ctrl);
                                ctrl.Dispose();
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "释放 MMLensCal AxHost 控件异常");
                        }
                        _lensCal = null;
                        _lensCalAvailable = false;
                    }

                    for (int i = 0; i < _drMark.Count; i++)
                    {
                        try
                        {
                            // 1. 调用 OCX API 进行硬件级清理
                            _drMark[i]?.LaserOff();
                            _drMark[i]?.MarkShutdown();
                            _drIo[i]?.Finish();
                            _drEdit[i]?.Finish();
                            _drMark[i]?.Finish();
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "释放打标卡{CardNo} OCX资源异常", i + 1);
                        }

                        // 2. 从宿主窗体移除控件并释放 AxHost（释放底层 COM 对象）
                        try
                        {
                            if (_drMark[i] != null)
                            {
                                _hostForm!.Controls.Remove(_drMark[i]);
                                _drMark[i].Dispose();
                                Marshal.ReleaseComObject(_drMark[i]);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "释放打标卡{CardNo} Mark控件异常", i + 1);
                        }

                        try
                        {
                            if (_drIo[i] != null)
                            {
                                _hostForm.Controls.Remove(_drIo[i]);
                                _drIo[i].Dispose();
                                Marshal.ReleaseComObject(_drIo[i]);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "释放打标卡{CardNo} IO控件异常", i + 1);
                        }

                        try
                        {
                            if (_drEdit[i] != null)
                            {
                                _hostForm.Controls.Remove(_drEdit[i]);
                                _drEdit[i].Dispose();
                                Marshal.ReleaseComObject(_drEdit[i]);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "释放打标卡{CardNo} Edit控件异常", i + 1);
                        }
                    }

                    _drMark.Clear();
                    _drIo.Clear();
                    _drEdit.Clear();
                });
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "释放 ActiveX 控件异常");
            }

            // 关闭 STA 线程
            try
            {
                if (_hostForm != null && !_hostForm.IsDisposed)
                {
                    _hostForm.Invoke(new Action(() =>
                    {
                        System.Windows.Forms.Application.ExitThread();
                    }));
                }

                if (_staThread != null && _staThread.IsAlive)
                {
                    _staThread.Join(1000);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "关闭 STA 线程异常");
            }

            _isInitialized = false;
            _staReady.Dispose();
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 检查打标卡号是否有效
        /// </summary>
        private MarkErrorCode CheckCardNo(uint cardNo)
        {
            if (_cardConfig == null || _cardConfig.CardCount <= 0)
                return MarkErrorCode.InvalidParameter;

            return (cardNo > 0 && cardNo <= _cardConfig.CardCount)
                ? MarkErrorCode.None
                : MarkErrorCode.UnmatchedMarkCardNo;
        }

        /// <summary>
        /// 获取或创建工艺参数
        /// </summary>
        private ProcessParam GetOrCreateProcessParam(uint cardNo)
        {
            if (!_processParams.TryGetValue(cardNo, out var param))
            {
                param = new ProcessParam();
                _processParams[cardNo] = param;
            }
            return param;
        }

        /// <summary>
        /// 设置校正档加载状态
        /// </summary>
        private void SetCalibrationLoaded(uint cardNo, bool loaded)
        {
            _calibrationLoaded[cardNo] = loaded;
        }

        /// <summary>
        /// 获取校正档加载状态
        /// </summary>
        private bool GetCalibrationLoaded(uint cardNo)
        {
            if (!_isInitialized) return false;
            return _calibrationLoaded.TryGetValue(cardNo, out bool loaded) && loaded;
        }

#if AnyCPU || x64
        /// <summary>
        /// 获取指定卡号的 Mark 控件
        /// </summary>
        private AxMMMarkx641? GetMarkControl(uint cardNo)
        {
            int index = (int)cardNo - 1;
            if (index >= 0 && index < _drMark.Count)
                return _drMark[index];
            return null;
        }

        /// <summary>
        /// 获取指定卡号的 IO 控件
        /// </summary>
        private AxMMIOx641? GetIoControl(uint cardNo)
        {
            int index = (int)cardNo - 1;
            if (index >= 0 && index < _drIo.Count)
                return _drIo[index];
            return null;
        }

        /// <summary>
        /// 获取指定卡号的 Edit 控件
        /// </summary>
        private AxMMEditx641? GetEditControl(uint cardNo)
        {
            int index = (int)cardNo - 1;
            if (index >= 0 && index < _drEdit.Count)
                return _drEdit[index];
            return null;
        }
#elif x86
        /// <summary>
        /// 获取指定卡号的 Mark 控件
        /// </summary>
        private AxMMMark_1? GetMarkControl(uint cardNo)
        {
            int index = (int)cardNo - 1;
            if (index >= 0 && index < _drMark.Count)
                return _drMark[index];
            return null;
        }

        /// <summary>
        /// 获取指定卡号的 IO 控件
        /// </summary>
        private AxMMIO_1? GetIoControl(uint cardNo)
        {
            int index = (int)cardNo - 1;
            if (index >= 0 && index < _drIo.Count)
                return _drIo[index];
            return null;
        }

        /// <summary>
        /// 获取指定卡号的 Edit 控件
        /// </summary>
        private AxMMEdit_1? GetEditControl(uint cardNo)
        {
            int index = (int)cardNo - 1;
            if (index >= 0 && index < _drEdit.Count)
                return _drEdit[index];
            return null;
        }
#endif

        /// <summary>
        /// 执行虚线打标：DashArray 中偶数索引为实线段（MarkLine），奇数索引为空白段（JumpTo）。
        /// 实-空交替排列：[实线终点, 空白终点, 实线终点, 空白终点, ...]
        /// </summary>
        private void ExecuteDashedLine(dynamic mark, MarkDashedLineCommand dashCmd)
        {
            if (dashCmd.DashArray == null || dashCmd.DashArray.Count == 0)
                return;

            PointF current = dashCmd.StartPoint.GetValueOrDefault(PointF.Empty);

            for (int i = 0; i < dashCmd.DashArray.Count; i++)
            {
                PointF pt = dashCmd.DashArray[i];
                if (i % 2 == 0)
                {
                    // 实线段：激光开启打标
                    mark.MarkLine(current.X, current.Y, pt.X, pt.Y);
                }
                else
                {
                    // 空白段：激光关闭跳转
                    mark.JumpTo(pt.X, pt.Y);
                }
                current = pt;
            }
        }

        /// <summary>
        /// 在 STA 线程上同步执行操作（线程安全的 ActiveX 调用）
        /// </summary>
        private void InvokeOCX(Action action)
        {
            if (_hostForm == null || _hostForm.IsDisposed)
                throw new InvalidOperationException("PMC6 ActiveX 控件未初始化");

            if (_hostForm.InvokeRequired)
                _hostForm.Invoke(action);
            else
                action();
        }

        /// <summary>
        /// 在 STA 线程上同步执行带返回值的操作
        /// </summary>
        private T InvokeOCX<T>(Func<T> func)
        {
            if (_hostForm == null || _hostForm.IsDisposed)
                throw new InvalidOperationException("PMC6 ActiveX 控件未初始化");

            if (_hostForm.InvokeRequired)
                return (T)_hostForm.Invoke(func);
            return func();
        }

        #endregion
    }
}
