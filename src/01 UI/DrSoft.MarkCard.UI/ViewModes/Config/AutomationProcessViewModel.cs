using CommunityToolkit.Mvvm.ComponentModel;
using DrSoft.MarkCard.Model.Config;
using Org.BouncyCastle.Tsp;

namespace DrSoft.MarkCard.UI.ViewModes.Config
{
    public partial class AutomationProcessViewModel : ObservableObject
    {
        private readonly SystemConfig _config;

        [ObservableProperty]
        private bool _useTimeoutSetting;

        [ObservableProperty]
        private int _timeoutSeconds;

        [ObservableProperty]
        private bool _enableDirectionArrow;

        [ObservableProperty]
        private bool _enableJumpLine;

        [ObservableProperty]
        private double _resolution;

        public AutomationProcessViewModel(SystemConfig config)
        {
            _config = config;
            _useTimeoutSetting = config.EnableDownloadToBuffer;
            _timeoutSeconds = config.DownloadToBufferInterval;
            _enableDirectionArrow = config.EnableDirectionArrow;
            _enableJumpLine = config.EnableJumpLine;

            Resolution = config.Resolution;
        }

        partial void OnUseTimeoutSettingChanged(bool value) => _config.EnableDownloadToBuffer = value;
        partial void OnTimeoutSecondsChanged(int value) => _config.DownloadToBufferInterval = value;
        partial void OnEnableDirectionArrowChanged(bool value) => _config.EnableDirectionArrow = value;
        partial void OnEnableJumpLineChanged(bool value) => _config.EnableJumpLine = value;

        partial void OnResolutionChanged(double value)=> _config.Resolution = value;
    }
}
