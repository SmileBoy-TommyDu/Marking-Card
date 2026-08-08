using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Utility;
using DrSoft.MarkCard.Event;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.Enum;
using DrSoft.MarkCard.Service;
using System.Collections.ObjectModel;
using System.Windows;
using ConfigModel = DrSoft.MarkCard.Model.Config.Config;

namespace DrSoft.MarkCard.UI.ViewModes
{
    public partial class LaserTestViewModel : ObservableObject
    {
        [ObservableProperty]
        private double _power = 30;

        [ObservableProperty]
        private double _time = 10;

        [ObservableProperty]
        private double _frequency = 20;

        private readonly MarkService? _markService;
        private readonly ConfigModel? _appConfig;
        private readonly Action<MarkCardConfigEvent<ConfigModel>> _onConfigChanged;
        private uint cardNo;

        [ObservableProperty] private ObservableCollection<Led> _inputLED;
        [ObservableProperty] private ObservableCollection<string> _inputLEDDescrpition;
        [ObservableProperty] private bool _isOutControl;
        [ObservableProperty] private int _selectInputID;

        // 用于取消上一次 ManualLaser 计时
        private CancellationTokenSource? _laserCts;

        public LaserTestViewModel()
        {
            _markService = App.GetService<MarkService>();
            _appConfig = App.GetService<ConfigModel>();

            InputLED = new ObservableCollection<Led>();
            InputLEDDescrpition = new ObservableCollection<string>();

            RebuildLeds();

            // 订阅配置应用事件，配置对话框点"应用"后同步更新 LED 列表
            _onConfigChanged = OnMarkCardConfigChanged;
            EventBus.Instance.Subscribe(_onConfigChanged);
        }

        // ── 属性变更响应 ──────────────────────────────────────────────────────

        /// <summary>功率改变时立即下发到硬件</summary>
        partial void OnPowerChanged(double value)
        {
            if (_markService != null && cardNo > 0)
                _markService.SetLaserPower(cardNo, value);
        }

        /// <summary>频率改变时立即下发到硬件</summary>
        partial void OnFrequencyChanged(double value)
        {
            if (_markService != null && cardNo > 0)
                _markService.SetLaserFrequency(cardNo, value);
        }

        /// <summary>外部输入控制开关改变时，立即执行一次输入读取与激光状态同步</summary>
        partial void OnIsOutControlChanged(bool value)
        {
            SyncLaserByExternalInput();
        }

        // ── 配置热更新 ────────────────────────────────────────────────────────

        private void OnMarkCardConfigChanged(MarkCardConfigEvent<ConfigModel> e)
        {
            Application.Current?.Dispatcher.BeginInvoke(RebuildLeds);
        }

        public void RebuildLeds()
        {
            var ioConfig = _appConfig?.IOConfigs?.FirstOrDefault();
            cardNo = _appConfig?.IOConfigs?.FirstOrDefault()?.CardNo ?? 1;

            int inputCount = ioConfig?.InputCount ?? 16;

            InputLED.Clear();
            for (int i = 0; i < inputCount; i++)
            {
                var func = (ioConfig?.InputFunctions != null && i < ioConfig.InputFunctions.Length)
                    ? ioConfig.InputFunctions[i]
                    : IOInputFunctionEnum.None;

                var customName = (ioConfig?.InputCustomNames != null && i < ioConfig.InputCustomNames.Length)
                    ? ioConfig.InputCustomNames[i]
                    : string.Empty;

                var desc = !string.IsNullOrWhiteSpace(customName)
                    ? customName
                    : func.GetDescription();

                InputLED.Add(new Led(i, false, desc));
            }

            InputLEDDescrpition = new ObservableCollection<string>(InputLED.Select(x => x.Description));
        }

        // ── 手动激光（模式一） ────────────────────────────────────────────────

        /// <summary>
        /// 点击"手动开激光"：LaserOn → 等待 Time 秒 → LaserOff。
        /// 若在计时期间再次点击，取消上一次计时并重新开始。
        /// </summary>
        [RelayCommand]
        private async Task ManualLaser()
        {
            // 取消上一次未完成的计时
            _laserCts?.Cancel();
            _laserCts?.Dispose();
            _laserCts = new CancellationTokenSource();
            var token = _laserCts.Token;

            try
            {
                _markService?.LaserOn(cardNo);

                int delayMs = (int)(Time * 1000);
                await Task.Delay(delayMs, token);

                _markService?.LaserOff(cardNo);
            }
            catch (TaskCanceledException)
            {
                // 被新的点击取消，不做处理（新的调用会重新 LaserOn）
            }
        }

        [RelayCommand]
        private void ManualCloseLaser()
        {
            // 点击手动关激光：取消计时并立即关闭
            _laserCts?.Cancel();
            _markService?.LaserOff(cardNo);
        }

        // ── 外部输入触发（模式二） ────────────────────────────────────────────

        private void SyncLaserByExternalInput()
        {
            if (_markService == null) return;

            if (_markService.ReadDigitalInput(cardNo, out bool[] inputs) == MarkErrorCode.None && inputs != null)
            {
                for (int i = 0; i < InputLED.Count && i < inputs.Length; i++)
                    InputLED[i].IsLit = inputs[i];

                bool triggered = SelectInputID >= 0
                    && SelectInputID < InputLED.Count
                    && InputLED[SelectInputID].IsLit;

                if (IsOutControl && triggered)
                    _markService.LaserOn(cardNo);
                else
                    _markService.LaserOff(cardNo);
            }
        }
    }
}

