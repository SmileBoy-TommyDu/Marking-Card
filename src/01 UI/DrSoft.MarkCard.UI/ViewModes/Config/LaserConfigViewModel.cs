using CommunityToolkit.Mvvm.ComponentModel;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.Config;
using DrSoft.MarkCard.Model.Enum;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace DrSoft.MarkCard.UI.ViewModes.Config
{
    public partial class LaserConfigViewModel : ObservableObject
    {
        private readonly List<LaserConfig> _configs;
        private readonly List<CardConfig> _cardConfigs;
        private bool _isLoading;

        private ObservableCollection<MarkCardType> _markCardTypes = new();
        public ObservableCollection<MarkCardType> MarkCardTypes
        {
            get => _markCardTypes;
            set => SetProperty(ref _markCardTypes, value);
        }

        private ObservableCollection<uint> _cardNos = new();
        public ObservableCollection<uint> CardNos
        {
            get => _cardNos;
            set => SetProperty(ref _cardNos, value);
        }

        private ObservableCollection<LaserModel> _laserModels = new();
        public ObservableCollection<LaserModel> LaserModels
        {
            get => _laserModels;
            set => SetProperty(ref _laserModels, value);
        }

        private ObservableCollection<LaserType> _laserTypes = new();
        public ObservableCollection<LaserType> LaserTypes
        {
            get => _laserTypes;
            set => SetProperty(ref _laserTypes, value);
        }

        private MarkCardType _selectedMarkCardType;
        public MarkCardType SelectedMarkCardType
        {
            get => _selectedMarkCardType;
            set
            {
                if (SetProperty(ref _selectedMarkCardType, value) && !_isLoading)
                {
                    RefreshCardNos();
                    SelectedCardNo = CardNos.Contains(SelectedCardNo) ? SelectedCardNo : CardNos.FirstOrDefault();
                    LoadCurrentConfig();
                }
            }
        }

        private uint _selectedCardNo = 1;
        public uint SelectedCardNo
        {
            get => _selectedCardNo;
            set
            {
                if (SetProperty(ref _selectedCardNo, value) && !_isLoading)
                {
                    LoadCurrentConfig();
                }
            }
        }

        private LaserModel _selectedLaserModel;
        public LaserModel SelectedLaserModel
        {
            get => _selectedLaserModel;
            set
            {
                if (SetProperty(ref _selectedLaserModel, value) && !_isLoading)
                {
                    UpdateCurrentConfig(c => c.LaserModel = value);
                }
            }
        }

        private LaserType _selectedLaserType;
        public LaserType SelectedLaserType
        {
            get => _selectedLaserType;
            set
            {
                if (SetProperty(ref _selectedLaserType, value) && !_isLoading)
                {
                    UpdateCurrentConfig(c => c.LaserType = value);
                }
            }
        }

        private double _configuredTheoreticalPower;
        public double ConfiguredTheoreticalPower
        {
            get => _configuredTheoreticalPower;
            set
            {
                if (SetProperty(ref _configuredTheoreticalPower, value) && !_isLoading)
                {
                    UpdateCurrentConfig(c => c.TheoreticalPower = value);
                }
            }
        }

        private double _configuredPowerRampUpTime;
        public double ConfiguredPowerRampUpTime
        {
            get => _configuredPowerRampUpTime;
            set
            {
                if (SetProperty(ref _configuredPowerRampUpTime, value) && !_isLoading)
                {
                    UpdateCurrentConfig(c => c.PowerRampUpTime = value);
                }
            }
        }

        private double _configuredPowerStabilizationDelay;
        public double ConfiguredPowerStabilizationDelay
        {
            get => _configuredPowerStabilizationDelay;
            set
            {
                if (SetProperty(ref _configuredPowerStabilizationDelay, value) && !_isLoading)
                {
                    UpdateCurrentConfig(c => c.PowerStabilizationDelay = value);
                }
            }
        }

        public LaserConfigViewModel(List<LaserConfig> configs, List<CardConfig> cardConfigs)
        {
            _configs = configs ?? new List<LaserConfig>();
            _cardConfigs = cardConfigs ?? new List<CardConfig>();

            foreach (var model in Enum.GetValues<LaserModel>())
            {
                LaserModels.Add(model);
            }

            foreach (var type in Enum.GetValues<LaserType>())
            {
                LaserTypes.Add(type);
            }

            RefreshMarkCardBindingData();
            LoadCurrentConfig();
        }

        public void RefreshMarkCardBindingData()
        {
            var currentType = SelectedMarkCardType;
            var currentCardNo = SelectedCardNo;

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
                    foreach (var type in _configs.Select(x => x.MarkCardType).Distinct())
                    {
                        MarkCardTypes.Add(type);
                    }
                }

                if (MarkCardTypes.Count == 0)
                {
                    foreach (var type in Enum.GetValues<MarkCardType>())
                    {
                        MarkCardTypes.Add(type);
                    }
                }

                SelectedMarkCardType = MarkCardTypes.Contains(currentType) ? currentType : MarkCardTypes.FirstOrDefault();
                RefreshCardNos();
                SelectedCardNo = CardNos.Contains(currentCardNo) ? currentCardNo : CardNos.FirstOrDefault();
            }
            finally
            {
                _isLoading = false;
            }
        }

        private void RefreshCardNos()
        {
            CardNos.Clear();

            var cardCount = (uint)_cardConfigs
                .Where(x => x.MarkCardType == SelectedMarkCardType)
                .Sum(x => (int)Math.Max(1u, x.CardCount));

            if (cardCount == 0)
            {
                cardCount = (uint)_configs
                    .Where(x => x.MarkCardType == SelectedMarkCardType)
                    .Select(x => x.CardNo)
                    .DefaultIfEmpty(1u)
                    .Max();
            }

            if (cardCount == 0)
            {
                cardCount = 1;
            }

            for (uint i = 1; i <= cardCount; i++)
            {
                CardNos.Add(i);
            }
        }

        private void LoadCurrentConfig()
        {
            var current = GetOrCreateCurrentConfig();

            _isLoading = true;
            try
            {
                SelectedLaserModel = current.LaserModel;
                SelectedLaserType = current.LaserType;
                ConfiguredTheoreticalPower = current.TheoreticalPower;
                ConfiguredPowerRampUpTime = current.PowerRampUpTime;
                ConfiguredPowerStabilizationDelay = current.PowerStabilizationDelay;
            }
            finally
            {
                _isLoading = false;
            }
        }

        private LaserConfig GetOrCreateCurrentConfig()
        {
            var current = _configs.FirstOrDefault(x => x.MarkCardType == SelectedMarkCardType && x.CardNo == SelectedCardNo);
            if (current != null)
            {
                return current;
            }

            current = new LaserConfig
            {
                MarkCardType = SelectedMarkCardType,
                CardNo = SelectedCardNo,
                LaserModel = LaserModels.FirstOrDefault(),
                LaserType = LaserTypes.FirstOrDefault()
            };
            _configs.Add(current);
            return current;
        }

        private void UpdateCurrentConfig(Action<LaserConfig> updater)
        {
            if (_isLoading)
                return;

            updater(GetOrCreateCurrentConfig());
        }
    }
}
