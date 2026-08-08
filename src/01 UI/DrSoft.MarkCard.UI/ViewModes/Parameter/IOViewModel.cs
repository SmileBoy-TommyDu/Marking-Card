using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Utility;
using DrSoft.MarkCard.Event;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.Enum;
using DrSoft.MarkCard.Model.Parameter;
using DrSoft.MarkCard.Service;
using System.Collections.ObjectModel;
using System.Windows;
using ConfigModel = DrSoft.MarkCard.Model.Config.Config;

namespace DrSoft.MarkCard.UI.ViewModes
{
    public partial class IOViewModel : BaseParamViewModel<IOState>
    {
        private readonly MarkService? _markService;
        private readonly ConfigModel? _appConfig;

        [ObservableProperty] private ObservableCollection<Led> _inputLED;
        [ObservableProperty] private ObservableCollection<Led> _outputLED;

        // 当前悬浮指示灯的说明文字
        [ObservableProperty] private string _iODescription = string.Empty;

        private CancellationTokenSource? _pollCts;
        private const int PollIntervalMs = 200;

        // 保存订阅 handler 引用，用于 Unsubscribe
        private readonly Action<MarkCardConfigEvent<ConfigModel>> _onConfigChanged;

        public IOViewModel() : base()
        {
            _markService = App.GetService<MarkService>();
            _appConfig = App.GetService<ConfigModel>();

            InputLED = new ObservableCollection<Led>();
            OutputLED = new ObservableCollection<Led>();

            // 从 IOConfig 联动构建初始 LED 列表
            RebuildLeds();

            // 订阅配置应用事件，配置对话框点"应用"后同步更新 LED 列表
            _onConfigChanged = OnMarkCardConfigChanged;
            EventBus.Instance.Subscribe(_onConfigChanged);

            // 注意：轮询不在此处启动，由 IOView 的 IsVisibleChanged 控制
        }

        private void OnMarkCardConfigChanged(MarkCardConfigEvent<ConfigModel> e)
        {
            // 切换到 UI 线程重建 LED（因为 ObservableCollection 操作需在 UI 线程）
            Application.Current?.Dispatcher.BeginInvoke(RebuildLeds);
        }

        // ── 从 IOConfig 联动重建 LED ──────────────────────────────────────────

        /// <summary>
        /// 从 Config.IOConfigs 读取第一个有效的 IOConfig，
        /// 按 InputCount / OutputCount 构建指示灯列表，并填充 Description。
        /// </summary>
        public void RebuildLeds()
        {
            var ioConfig = _appConfig?.IOConfigs?.FirstOrDefault();

            int inputCount = ioConfig?.InputCount ?? 16;
            int outputCount = ioConfig?.OutputCount ?? 16;

            InputLED.Clear();
            for (int i = 0; i < inputCount; i++)
            {
                var func = (ioConfig?.InputFunctions != null && i < ioConfig.InputFunctions.Length)
                    ? ioConfig.InputFunctions[i]
                    : IOInputFunctionEnum.None;

                var customName = (ioConfig?.InputCustomNames != null && i < ioConfig.InputCustomNames.Length)
                    ? ioConfig.InputCustomNames[i]
                    : string.Empty;

                // 优先自定义名称，否则用枚举描述
                var desc = !string.IsNullOrWhiteSpace(customName)
                    ? customName
                    : func.GetDescription();

                InputLED.Add(new Led(i, false, desc));
            }

            OutputLED.Clear();
            for (int i = 0; i < outputCount; i++)
            {
                var func = (ioConfig?.OutputFunctions != null && i < ioConfig.OutputFunctions.Length)
                    ? ioConfig.OutputFunctions[i]
                    : IOOutputFunctionEnum.None;

                var customName = (ioConfig?.OutputCustomNames != null && i < ioConfig.OutputCustomNames.Length)
                    ? ioConfig.OutputCustomNames[i]
                    : string.Empty;

                var desc = !string.IsNullOrWhiteSpace(customName)
                    ? customName
                    : func.GetDescription();

                OutputLED.Add(new Led(i, false, desc));
            }
        }

        // ── 轮询 ReadDigitalInput / ReadDigitalOutput ─────────────────────────

        /// <summary>界面可见时调用，启动 IO 轮询</summary>
        public void StartPolling()
        {
            if (_pollCts != null && !_pollCts.IsCancellationRequested)
                return; // 已在轮询中，不重复启动

            _pollCts?.Dispose();
            _pollCts = new CancellationTokenSource();
            var token = _pollCts.Token;

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        PollIO();
                    }
                    catch { /* 忽略轮询异常，避免崩溃 */ }

                    await Task.Delay(PollIntervalMs, token).ContinueWith(_ => { });
                }
            }, token);
        }

        /// <summary>界面隐藏时调用，停止 IO 轮询</summary>
        public void StopPolling()
        {
            _pollCts?.Cancel();
            _pollCts?.Dispose();
            _pollCts = null;
        }

        /// <summary>ViewModel 生命周期结束时调用，取消事件订阅</summary>
        public void Dispose()
        {
            StopPolling();
            EventBus.Instance.Unsubscribe(_onConfigChanged);
        }

        private void PollIO()
        {
            if (_markService == null) return;

            // 取第一个有效 IOConfig 的 cardNo，默认 1
            uint cardNo = _appConfig?.IOConfigs?.FirstOrDefault()?.CardNo ?? 1;

            // 读输入
            if (_markService.ReadDigitalInput(cardNo, out bool[] inputs) == MarkErrorCode.None && inputs != null)
            {
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    for (int i = 0; i < InputLED.Count && i < inputs.Length; i++)
                        InputLED[i].IsLit = inputs[i];
                });
            }

            // 读输出
            if (_markService.ReadDigitalOutput(cardNo, out bool[] outputs) == MarkErrorCode.None && outputs != null)
            {
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    for (int i = 0; i < OutputLED.Count && i < outputs.Length; i++)
                        OutputLED[i].IsLit = outputs[i];
                });
            }
        }

        // ── 输出点击：切换并写入硬件 ──────────────────────────────────────────

        [RelayCommand]
        private void SetOutput(int index)
        {
            if (index < 0 || index >= OutputLED.Count) return;

            var led = OutputLED[index];
            bool newState = !led.IsLit;
            led.IsLit = newState;

            if (_markService == null) return;

            uint cardNo = _appConfig?.IOConfigs?.FirstOrDefault()?.CardNo ?? 1;
            _markService.WriteDigitalOutput(cardNo, (uint)index, newState);
        }
    }

    public partial class Led : ObservableObject
    {
        public Led(int index, bool isLit, string description = "")
        {
            _index = index;
            _isLit = isLit;
            _description = description;
        }

        [ObservableProperty] private bool _isLit;
        [ObservableProperty] private int _index;
        /// <summary>对应 IOInputFunctionEnum / IOOutputFunctionEnum 的说明文字</summary>
        [ObservableProperty] private string _description;
    }
}



