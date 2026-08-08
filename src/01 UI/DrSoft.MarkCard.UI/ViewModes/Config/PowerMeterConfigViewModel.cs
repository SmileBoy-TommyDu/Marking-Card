using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrSoft.MarkCard.Impl.PowerMeter;
using DrSoft.MarkCard.Interface;
using DrSoft.MarkCard.Model.Config;
using DrSoft.MarkCard.Model.Enum;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;

namespace DrSoft.MarkCard.UI.ViewModes.Config
{
    public partial class PowerMeterConfigViewModel : ObservableObject, IDisposable
    {
        private readonly PowerMeterConfig _config;
        private readonly IPowerMeter _powerMeter;

        public ObservableCollection<PowerMeterModel> PowerMeterModels { get; } = new();
        public ObservableCollection<ConnectType> ConnectTypes { get; } = new();

        private PowerMeterModel _selectedPowerMeterModel;
        public PowerMeterModel SelectedPowerMeterModel
        {
            get => _selectedPowerMeterModel;
            set
            {
                if (SetProperty(ref _selectedPowerMeterModel, value))
                {
                    _config.PowerMeterModel = value;
                }
            }
        }

        private ConnectType _selectedConnectType;
        public ConnectType SelectedConnectType
        {
            get => _selectedConnectType;
            set
            {
                if (SetProperty(ref _selectedConnectType, value))
                {
                    _config.ConnectType = value;
                }
            }
        }

        private string _connectString = string.Empty;
        public string ConnectString
        {
            get => _connectString;
            set
            {
                if (SetProperty(ref _connectString, value ?? string.Empty))
                {
                    _config.ConnectString = _connectString;
                    ConnectCommand.NotifyCanExecuteChanged();
                }
            }
        }

        private string _feedbackValue = "--";
        public string FeedbackValue
        {
            get => _feedbackValue;
            set => SetProperty(ref _feedbackValue, value);
        }

        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                if (SetProperty(ref _isConnected, value))
                {
                    OnPropertyChanged(nameof(ConnectionStatusText));
                    OnPropertyChanged(nameof(ConnectButtonText));
                    ConnectCommand.NotifyCanExecuteChanged();
                }
            }
        }

        private bool _isConnecting;
        public bool IsConnecting
        {
            get => _isConnecting;
            set
            {
                if (SetProperty(ref _isConnecting, value))
                {
                    OnPropertyChanged(nameof(ConnectionStatusText));
                    OnPropertyChanged(nameof(ConnectButtonText));
                    ConnectCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public string ConnectionStatusText => IsConnecting ? "连接中" : IsConnected ? "已连接" : "未连接";

        public string ConnectButtonText => IsConnected ? "断开" : "连接";

        public PowerMeterConfigViewModel(PowerMeterConfig config)
        {
            _config = config ?? new PowerMeterConfig();
            _powerMeter = new StreamingPowerMeter();
            _powerMeter.FeedbackValueReceived += OnFeedbackValueReceived;

            foreach (var model in Enum.GetValues<PowerMeterModel>())
            {
                PowerMeterModels.Add(model);
            }

            foreach (var connectType in Enum.GetValues<ConnectType>())
            {
                ConnectTypes.Add(connectType);
            }

            SelectedPowerMeterModel = _config.PowerMeterModel;
            SelectedConnectType = _config.ConnectType;
            ConnectString = _config.ConnectString ?? string.Empty;
            FeedbackValue = "--";
            IsConnected = false;
            IsConnecting = false;
        }

        [RelayCommand(CanExecute = nameof(CanConnect))]
        private async Task ConnectAsync()
        {
            if (IsConnected)
            {
                _powerMeter.Disconnect();
                IsConnected = false;
                FeedbackValue = "--";
                return;
            }

            IsConnecting = true;
            IsConnected = false;
            FeedbackValue = "--";

            try
            {
                await Task.Run(() =>
                {
                    _config.PowerMeterModel = SelectedPowerMeterModel;
                    _config.ConnectType = SelectedConnectType;
                    _config.ConnectString = ConnectString;
                });

                var result = await Task.Run(() => _powerMeter.Connect(_config));
                IsConnected = result == DrSoft.MarkCard.Model.MarkErrorCode.None;
                if (!IsConnected)
                {
                    FeedbackValue = "连接失败";
                }
            }
            finally
            {
                IsConnecting = false;
            }
        }

        private bool CanConnect()
        {
            if (IsConnecting)
            {
                return false;
            }

            return IsConnected || !string.IsNullOrWhiteSpace(ConnectString);
        }

        private void OnFeedbackValueReceived(string value)
        {
            if (Application.Current?.Dispatcher?.CheckAccess() == true)
            {
                FeedbackValue = value;
                return;
            }

            Application.Current?.Dispatcher?.Invoke(() => FeedbackValue = value);
        }

        public void Dispose()
        {
            _powerMeter.FeedbackValueReceived -= OnFeedbackValueReceived;
            _powerMeter.Dispose();
        }
    }
}
