using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrSoft.Drawing.Event;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.Config;
using DrSoft.MarkCard.Model.Enum;
using DrSoft.MarkCard.UI.Views.Calibrate;
using System.Collections.ObjectModel;
using System.Windows;

namespace DrSoft.MarkCard.UI.ViewModes.Config
{
    public partial class ScanHeadConfigViewModel : ObservableObject
    {
        private readonly List<ScanHeadConfig> _configs;
        private readonly List<CardConfig> _cardConfigs;
        private static CalibrationToolWindow? _calibrationWindow;
        private bool _isLoading;

        [ObservableProperty] private ObservableCollection<MarkCardType> _markCardTypes = new();
        [ObservableProperty] private ObservableCollection<uint> _cardNos = new();
        [ObservableProperty] private ObservableCollection<uint> _scanHeadNos = new();
        [ObservableProperty] private ObservableCollection<ScanHeadProtocol> _protocols = new();

        [ObservableProperty] private MarkCardType _markCardType;
        [ObservableProperty] private uint _cardNo = 1;
        [ObservableProperty] private uint _scanHeadNo = 1;
        [ObservableProperty] private ScanHeadProtocol _protocol;
        [ObservableProperty] private double _processingAreaX;
        [ObservableProperty] private double _processingAreaY;
        [ObservableProperty] private double _maxSpeed;
        [ObservableProperty] private double _rotationAngle;
        [ObservableProperty] private double _focalLength;
        [ObservableProperty] private double _maxTemperature;
        [ObservableProperty] private double _originX;
        [ObservableProperty] private double _originY;
        [ObservableProperty] private bool _psoEnabled;
        [ObservableProperty] private double _psoSpacing;
        [ObservableProperty] private double _psoPulseWidth;
        [ObservableProperty] private bool _mirrorX;
        [ObservableProperty] private bool _mirrorY;
        [ObservableProperty] private bool _reverseXY;
        [ObservableProperty] private float _angleOffset;
        [ObservableProperty] private int _offsetX;
        [ObservableProperty] private int _offsetY;

        public ScanHeadConfigViewModel(List<ScanHeadConfig> configs, List<CardConfig> cardConfigs)
        {
            _configs = configs ?? new List<ScanHeadConfig>();
            _cardConfigs = cardConfigs ?? new List<CardConfig>();

            foreach (var protocol in Enum.GetValues<ScanHeadProtocol>())
            {
                Protocols.Add(protocol);
            }

            RefreshMarkCardBindingData();
            LoadCurrentConfig();
        }

        partial void OnMarkCardTypeChanged(MarkCardType value)
        {
            if (_isLoading)
                return;

            RefreshCardNos();
            CardNo = CardNos.Contains(CardNo) ? CardNo : CardNos.FirstOrDefault();
            RefreshScanHeadNos(CardNo);
            ScanHeadNo = ScanHeadNos.Contains(ScanHeadNo) ? ScanHeadNo : ScanHeadNos.FirstOrDefault();
            LoadCurrentConfig();
        }

        partial void OnCardNoChanged(uint value)
        {
            if (_isLoading)
                return;

            RefreshScanHeadNos(value);
            ScanHeadNo = ScanHeadNos.Contains(ScanHeadNo) ? ScanHeadNo : ScanHeadNos.FirstOrDefault();
            LoadCurrentConfig();
        }

        partial void OnScanHeadNoChanged(uint value)
        {
            if (_isLoading)
                return;

            LoadCurrentConfig();
        }

        partial void OnProtocolChanged(ScanHeadProtocol value) => UpdateCurrentConfig(c => c.Protocol = value);
        partial void OnProcessingAreaXChanged(double value) => UpdateCurrentConfig(c => c.ProcessingAreaX = value);
        partial void OnProcessingAreaYChanged(double value) => UpdateCurrentConfig(c => c.ProcessingAreaY = value);
        partial void OnMaxSpeedChanged(double value) => UpdateCurrentConfig(c => c.MaxSpeed = value);
        partial void OnRotationAngleChanged(double value) => UpdateCurrentConfig(c => c.RotationAngle = value);
        partial void OnFocalLengthChanged(double value) => UpdateCurrentConfig(c => c.FocalLength = value);
        partial void OnMaxTemperatureChanged(double value) => UpdateCurrentConfig(c => c.MaxTemperature = value);
        partial void OnOriginXChanged(double value) => UpdateCurrentConfig(c => c.OriginX = value);
        partial void OnOriginYChanged(double value) => UpdateCurrentConfig(c => c.OriginY = value);
        partial void OnPsoEnabledChanged(bool value) => UpdateCurrentConfig(c => c.EnablePSO = value);
        partial void OnPsoSpacingChanged(double value) => UpdateCurrentConfig(c => c.PSOSpacing = value);
        partial void OnPsoPulseWidthChanged(double value) => UpdateCurrentConfig(c => c.PSOPulseWidth = value);
        partial void OnMirrorXChanged(bool value) => UpdateCurrentConfig(c => c.MirrorX = value);
        partial void OnMirrorYChanged(bool value) => UpdateCurrentConfig(c => c.MirrorY = value);
        partial void OnReverseXYChanged(bool value) => UpdateCurrentConfig(c => c.ReverseXY = value);
        partial void OnAngleOffsetChanged(float value) => UpdateCurrentConfig(c => c.AngleOffset = value);
        partial void OnOffsetXChanged(int value) => UpdateCurrentConfig(c => c.OffsetX = value);
        partial void OnOffsetYChanged(int value) => UpdateCurrentConfig(c => c.OffsetY = value);

        public void RefreshMarkCardBindingData()
        {
            var currentType = MarkCardType;
            var currentCardNo = CardNo;
            var currentScanHeadNo = ScanHeadNo;

            _isLoading = true;
            try
            {
                MarkCardTypes.Clear();
                foreach (var type in _cardConfigs.Select(x => x.MarkCardType).Distinct())
                {
                    MarkCardTypes.Add(type);
                }

                if (MarkCardTypes.Count == 0)
                {
                    foreach (var type in Enum.GetValues<MarkCardType>())
                    {
                        MarkCardTypes.Add(type);
                    }
                }

                MarkCardType = MarkCardTypes.Contains(currentType) ? currentType : MarkCardTypes.FirstOrDefault();
                RefreshCardNos();
                CardNo = CardNos.Contains(currentCardNo) ? currentCardNo : CardNos.FirstOrDefault();
                RefreshScanHeadNos(CardNo);
                ScanHeadNo = ScanHeadNos.Contains(currentScanHeadNo) ? currentScanHeadNo : ScanHeadNos.FirstOrDefault();
            }
            finally
            {
                _isLoading = false;
            }
        }

        private void RefreshCardNos()
        {
            CardNos.Clear();

            foreach (var cardNo in GetCardNumbersForType(MarkCardType))
            {
                CardNos.Add(cardNo);
            }

            if (CardNos.Count == 0)
            {
                CardNos.Add(1);
            }
        }

        private IEnumerable<uint> GetCardNumbersForType(MarkCardType markCardType)
        {
            var matchedCardConfigs = _cardConfigs.Where(x => x.MarkCardType == markCardType).ToList();
            uint totalCardCount = (uint)matchedCardConfigs.Sum(x => (int)Math.Max(x.CardCount, 1u));
            if (totalCardCount == 0)
            {
                totalCardCount = 1;
            }

            for (uint i = 1; i <= totalCardCount; i++)
            {
                yield return i;
            }
        }

        private void RefreshScanHeadNos(uint cardNo)
        {
            ScanHeadNos.Clear();

            uint expectedCount = GetConfiguredScanHeadCount(MarkCardType, cardNo);
            for (uint i = 1; i <= expectedCount; i++)
            {
                ScanHeadNos.Add(i);
            }

            foreach (var scanHeadNo in _configs.Where(x => x.CardNo == cardNo).Select(x => x.ScanHeadNo).Distinct().OrderBy(x => x))
            {
                if (!ScanHeadNos.Contains(scanHeadNo))
                {
                    ScanHeadNos.Add(scanHeadNo);
                }
            }

            if (ScanHeadNos.Count == 0)
            {
                ScanHeadNos.Add(1);
            }
        }

        private uint GetConfiguredScanHeadCount(MarkCardType markCardType, uint cardNo)
        {
            var mapping = TryGetCardMapping(markCardType, cardNo);
            if (mapping == null)
                return 1;

            var (cardConfig, localCardIndex) = mapping.Value;
            cardConfig.CardDescConfigs ??= new List<CardDescConfig>();

            while (cardConfig.CardDescConfigs.Count <= localCardIndex)
            {
                cardConfig.CardDescConfigs.Add(new CardDescConfig());
            }

            return Math.Max(cardConfig.CardDescConfigs[localCardIndex].ScanHeadCount, 1u);
        }

        private (CardConfig CardConfig, int LocalCardIndex)? TryGetCardMapping(MarkCardType markCardType, uint cardNo)
        {
            uint startNo = 1;
            foreach (var cardConfig in _cardConfigs.Where(x => x.MarkCardType == markCardType))
            {
                uint count = Math.Max(cardConfig.CardCount, 1u);
                uint endNo = startNo + count - 1;
                if (cardNo >= startNo && cardNo <= endNo)
                {
                    return (cardConfig, (int)(cardNo - startNo));
                }

                startNo = endNo + 1;
            }

            return null;
        }

        private ScanHeadConfig GetOrCreateCurrentConfig()
        {
            var current = _configs.FirstOrDefault(x => x.CardNo == CardNo && x.ScanHeadNo == ScanHeadNo);
            if (current != null)
            {
                return current;
            }

            current = new ScanHeadConfig
            {
                CardNo = CardNo,
                ScanHeadNo = ScanHeadNo,
                Protocol = Protocols.FirstOrDefault()
            };
            _configs.Add(current);
            RefreshCardNos();
            RefreshScanHeadNos(CardNo);
            return current;
        }

        private void LoadCurrentConfig()
        {
            var current = GetOrCreateCurrentConfig();

            _isLoading = true;
            try
            {
                Protocol = current.Protocol;
                ProcessingAreaX = current.ProcessingAreaX;
                ProcessingAreaY = current.ProcessingAreaY;
                MaxSpeed = current.MaxSpeed;
                RotationAngle = current.RotationAngle;
                FocalLength = current.FocalLength;
                MaxTemperature = current.MaxTemperature;
                OriginX = current.OriginX;
                OriginY = current.OriginY;
                PsoEnabled = current.EnablePSO;
                PsoSpacing = current.PSOSpacing;
                PsoPulseWidth = current.PSOPulseWidth;
                MirrorX = current.MirrorX;
                MirrorY = current.MirrorY;
                ReverseXY = current.ReverseXY;
                AngleOffset = current.AngleOffset;
                OffsetX = current.OffsetX;
                OffsetY = current.OffsetY;
            }
            finally
            {
                _isLoading = false;
            }
        }

        private void UpdateCurrentConfig(Action<ScanHeadConfig> updateAction)
        {
            if (_isLoading)
                return;

            updateAction(GetOrCreateCurrentConfig());
        }

        [RelayCommand]
        private void OpenCalibrationTool()
        {
            if (_calibrationWindow is not null)
            {
                if (_calibrationWindow.WindowState == WindowState.Minimized)
                {
                    _calibrationWindow.WindowState = WindowState.Normal;
                }

                _calibrationWindow.Activate();
                return;
            }

            var config = App.GetService<DrSoft.MarkCard.Model.Config.Config>();
            if (config?.ScanHeadConfigs == null)
            {
                EventBus.Instance.Publish(new ToastMessageEvent("未找到对应的扫描头配置，无法打开校正工具，请检查卡号和扫描头号是否正确。", ToastType.Error));
                return;
            }

            var scanHeadConfig = config.ScanHeadConfigs.Find(x => x.CardNo == CardNo && x.ScanHeadNo == ScanHeadNo);
            if (scanHeadConfig == null)
            {
                EventBus.Instance.Publish(new ToastMessageEvent("未找到对应的扫描头配置，无法打开校正工具，请检查卡号和扫描头号是否正确。", ToastType.Error));
                return;
            }

            _calibrationWindow = new CalibrationToolWindow(scanHeadConfig)
            {
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            _calibrationWindow.Closed += (_, _) => _calibrationWindow = null;
            _calibrationWindow.Show();
            _calibrationWindow.Activate();
        }
    }
}
