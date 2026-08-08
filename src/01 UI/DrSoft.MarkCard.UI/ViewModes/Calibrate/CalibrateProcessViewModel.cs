using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrSoft.Drawing.Event;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.Config;
using DrSoft.MarkCard.Service;
using Microsoft.Win32;
using ConfigModel = DrSoft.MarkCard.Model.Config.Config;

namespace DrSoft.MarkCard.UI.ViewModes.Calibrate
{
    public partial class CalibrateProcessViewModel : ObservableObject
    {
        private readonly ScanHeadConfig _scanHeadConfig;

        private readonly CalibrationService _calibrationService;
        private readonly ProcessParam _calibrationProcessParam;

        private ConfigModel _config;

        public Action? CloseAction { get; set; }

        public CalibrateProcessViewModel(ScanHeadConfig scanHeadConfig)
        {
            _scanHeadConfig = scanHeadConfig;
            _calibrationFilePath = _scanHeadConfig.HeadFilePath;
            _config = App.GetService<ConfigModel>();

            _calibrationService = (CalibrationService)App.GetService<CalibrationService>();
            _calibrationProcessParam = _calibrationService.GetCalibrationProcessParam();
            _markSpeed = _calibrationProcessParam.MarkSpeed;
            _jumpSpeed = _calibrationProcessParam.JumpSpeed;
            _laserOnDelay = _calibrationProcessParam.LaserOnDelay;

            //_calibrationProcessParam 初始化属性
            _laserOffDelay = _calibrationProcessParam.LaserOffDelay;
            _jumpDelay = _calibrationProcessParam.JumpDelay;
            _power = _calibrationProcessParam.Power;
            _frequency = _calibrationProcessParam.Frequency;
            _dutyCycle = _calibrationProcessParam.Pulse;
            _cornerDelay = _calibrationProcessParam.PolyDelay;
            _markDelay = _calibrationProcessParam.MarkDelay;

            EventBus.Instance.Subscribe<BaseEvent<ScanHeadConfig>>(OnScanHeadConfigChanged);
        }

        private void OnScanHeadConfigChanged(BaseEvent<ScanHeadConfig> eventArgs)
        {
            if (eventArgs != null && eventArgs.Data != null)
            {
                if (eventArgs.EventName == "scanHeadConfigUpdated")
                {
                    var newConfig = eventArgs.Data;
                    if (newConfig.CardNo == _scanHeadConfig.CardNo && newConfig.ScanHeadNo == _scanHeadConfig.ScanHeadNo)
                    {
                        _scanHeadConfig.HeadFilePath = newConfig.HeadFilePath;
                        CalibrationFilePath = newConfig.HeadFilePath;

                        _config.SaveToFile();
                    }
                }
            }

        }

        [ObservableProperty]
        private double _markSpeed;

        partial void OnMarkSpeedChanged(double value)
        {
            _calibrationProcessParam.MarkSpeed = value;
        }

        [ObservableProperty]
        private double _jumpSpeed;

        partial void OnJumpSpeedChanged(double value)
        {
            _calibrationProcessParam.JumpSpeed = value;
        }

        [ObservableProperty]
        private double _laserOnDelay;

        partial void OnLaserOnDelayChanged(double value)
        {
            _calibrationProcessParam.LaserOnDelay = value;
        }

        [ObservableProperty]
        private double _laserOffDelay;

        partial void OnLaserOffDelayChanged(double value)
        {
            _calibrationProcessParam.LaserOffDelay = value;
        }

        [ObservableProperty]
        private double _jumpDelay;

        partial void OnJumpDelayChanged(double value)
        {
            _calibrationProcessParam.JumpDelay = value;
        }

        [ObservableProperty]
        private double _power;

        partial void OnPowerChanged(double value)
        {
            _calibrationProcessParam.Power = value;
        }

        [ObservableProperty]
        private double _frequency;

        partial void OnFrequencyChanged(double value)
        {
            _calibrationProcessParam.Frequency = value;
        }

        [ObservableProperty]
        private double _dutyCycle;

        partial void OnDutyCycleChanged(double value)
        {
            _calibrationProcessParam.Pulse = value;
        }

        [ObservableProperty]
        private double _markDelay;

        partial void OnMarkDelayChanged(double value)
        {
            _calibrationProcessParam.MarkDelay = value;
        }

        [ObservableProperty]
        private double _cornerDelay;

        partial void OnCornerDelayChanged(double value)
        {
            _calibrationProcessParam.PolyDelay = value;
        }

        [ObservableProperty]
        private string _calibrationFilePath = string.Empty;

        [RelayCommand]
        private void Import()
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "校正文件|*.ct5|所有文件|*.*",
                Title = "选择校正文件"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                CalibrationFilePath = openFileDialog.FileName;
               var errCode = _calibrationService.LoadCalibrationFile(_scanHeadConfig.CardNo, _scanHeadConfig.ScanHeadNo==1?CalibrationFilePath:null,_scanHeadConfig.ScanHeadNo==2?CalibrationFilePath:null);
                if(errCode==MarkErrorCode.None)
                {
                    _scanHeadConfig.HeadFilePath = CalibrationFilePath;
                    EventBus.Instance.Publish(new BaseEvent<ScanHeadConfig> { EventName = "scanHeadConfigUpdated", Data = _scanHeadConfig });
                    EventBus.Instance.Publish(new ToastMessageEvent("加载校正文件成功", ToastType.Info));
                }
                else
                {
                    EventBus.Instance.Publish(new ToastMessageEvent($"加载校正文件失败: {errCode}", ToastType.Error));
                }
            }
        }

        [RelayCommand]
        private void Save()
        {
            try
            {
                _calibrationService.SaveCalibrationProcessParam(_calibrationProcessParam);
                EventBus.Instance.Publish(new ToastMessageEvent("保存校正参数成功", ToastType.Info));
            }
            catch (Exception ex)
            {
                EventBus.Instance.Publish(new ToastMessageEvent($"保存校正参数失败: {ex.Message}", ToastType.Error));
            }
        }

        [RelayCommand]
        private void Cancel()
        {
            CloseAction?.Invoke();
        }
    }
}
