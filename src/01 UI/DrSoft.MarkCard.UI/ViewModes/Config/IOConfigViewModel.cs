using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.Config;
using DrSoft.MarkCard.Model.Enum;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace DrSoft.MarkCard.UI.ViewModes.Config
{
    public partial class IOConfigViewModel : ObservableObject
    {
        public IOInputViewModel InputVm { get; }
        public IOOutputViewModel OutputVm { get; }

        public IOConfigViewModel(System.Collections.Generic.List<IOConfig> ioConfigs, System.Collections.Generic.List<CardConfig> cardConfigs)
        {
            InputVm = new IOInputViewModel(ioConfigs, cardConfigs);
            OutputVm = new IOOutputViewModel(ioConfigs, cardConfigs);
        }
    }

    public partial class IOInputViewModel : ObservableObject
    {
        private readonly System.Collections.Generic.List<IOConfig> _ioConfigs;
        private readonly System.Collections.Generic.List<CardConfig> _cardConfigs;
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

        private MarkCardType _selectedMarkCardType;
        public MarkCardType SelectedMarkCardType
        {
            get => _selectedMarkCardType;
            set
            {
                if (SetProperty(ref _selectedMarkCardType, value) && !_isLoading)
                {
                    RefreshCardNos();
                    LoadCurrentConfig();
                }
            }
        }

        private uint _selectedCardNo;
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

        private ObservableCollection<IOInputItemViewModel> _items = new();
        public ObservableCollection<IOInputItemViewModel> Items
        {
            get => _items;
            set => SetProperty(ref _items, value);
        }

        public IOInputViewModel(System.Collections.Generic.List<IOConfig> ioConfigs, System.Collections.Generic.List<CardConfig> cardConfigs)
        {
            _ioConfigs = ioConfigs ?? new System.Collections.Generic.List<IOConfig>();
            _cardConfigs = cardConfigs ?? new System.Collections.Generic.List<CardConfig>();
            RefreshMarkCardBindingData();
        }

        public void RefreshMarkCardBindingData()
        {
            _isLoading = true;
            try
            {
                var currentType = SelectedMarkCardType;
                var currentCardNo = SelectedCardNo;

                _markCardTypes.Clear();
                var distinctTypes = _cardConfigs.Select(c => c.MarkCardType).Distinct().ToList();
                if (distinctTypes.Any())
                {
                    foreach (var type in distinctTypes)
                    {
                        _markCardTypes.Add(type);
                    }
                }
                else
                {
                    foreach (var type in Enum.GetValues<MarkCardType>())
                    {
                        _markCardTypes.Add(type);
                    }
                }

                _selectedMarkCardType = _markCardTypes.Contains(currentType) ? currentType : _markCardTypes.FirstOrDefault();
                OnPropertyChanged(nameof(SelectedMarkCardType));

                RefreshCardNos();

                _selectedCardNo = _cardNos.Contains(currentCardNo) ? currentCardNo : _cardNos.FirstOrDefault();
                OnPropertyChanged(nameof(SelectedCardNo));
            }
            finally
            {
                _isLoading = false;
            }

            LoadCurrentConfig();
        }

        private void RefreshCardNos()
        {
            _cardNos.Clear();

            var cardCount = (uint)_cardConfigs
                .Where(x => x.MarkCardType == _selectedMarkCardType)
                .Sum(x => (int)Math.Max(1u, x.CardCount));

            if (cardCount == 0)
            {
                cardCount = (uint)_ioConfigs
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
                _cardNos.Add(i);
            }
        }

        private void LoadCurrentConfig()
        {
            if (_isLoading || SelectedCardNo == 0)
            {
                return;
            }

            var config = GetOrCreateCurrentConfig();

            _isLoading = true;
            try
            {
                _items.Clear();

                for (int i = 0; i < config.InputCount; i++)
                {
                    var item = new IOInputItemViewModel(i, config.InputFunctions[i], config.InputCustomNames[i]);
                    item.PropertyChanged += (s, e) =>
                    {
                        if (_isLoading) return;

                        if (e.PropertyName == nameof(IOInputItemViewModel.SelectedSystemFunction))
                        {
                            if (s is IOInputItemViewModel vm)
                            {
                                config.InputFunctions[vm.Index] = vm.SelectedSystemFunction;
                            }
                        }
                        else if (e.PropertyName == nameof(IOInputItemViewModel.CustomName))
                        {
                            if (s is IOInputItemViewModel vm)
                            {
                                config.InputCustomNames[vm.Index] = vm.CustomName ?? string.Empty;
                            }
                        }
                    };
                    _items.Add(item);
                }
            }
            finally
            {
                _isLoading = false;
            }
        }

        private IOConfig GetOrCreateCurrentConfig()
        {
            var config = _ioConfigs.FirstOrDefault(c => c.MarkCardType == SelectedMarkCardType && c.CardNo == SelectedCardNo);
            if (config == null)
            {
                config = new IOConfig
                {
                    MarkCardType = SelectedMarkCardType,
                    CardNo = SelectedCardNo,
                    EnableIO = false,
                    InputCount = 16,
                    OutputCount = 16,
                    InputFunctions = new IOInputFunctionEnum[16],
                    OutputFunctions = new IOOutputFunctionEnum[16],
                    InputCustomNames = new string[16],
                    OutputCustomNames = new string[16]
                };
                _ioConfigs.Add(config);
            }

            NormalizeConfig(config);
            return config;
        }

        private static void NormalizeConfig(IOConfig config)
        {
            config.InputCount = config.InputCount > 0 ? config.InputCount : 16;
            config.OutputCount = config.OutputCount > 0 ? config.OutputCount : 16;

            if (config.InputFunctions == null)
            {
                config.InputFunctions = new IOInputFunctionEnum[config.InputCount];
            }
            else if (config.InputFunctions.Length != config.InputCount)
            {
                var inputFunctions = config.InputFunctions;
                Array.Resize(ref inputFunctions, config.InputCount);
                config.InputFunctions = inputFunctions;
            }

            if (config.OutputFunctions == null)
            {
                config.OutputFunctions = new IOOutputFunctionEnum[config.OutputCount];
            }
            else if (config.OutputFunctions.Length != config.OutputCount)
            {
                var outputFunctions = config.OutputFunctions;
                Array.Resize(ref outputFunctions, config.OutputCount);
                config.OutputFunctions = outputFunctions;
            }

            if (config.InputCustomNames == null)
            {
                config.InputCustomNames = new string[config.InputCount];
            }
            else if (config.InputCustomNames.Length != config.InputCount)
            {
                var inputCustomNames = config.InputCustomNames;
                Array.Resize(ref inputCustomNames, config.InputCount);
                config.InputCustomNames = inputCustomNames;
            }

            if (config.OutputCustomNames == null)
            {
                config.OutputCustomNames = new string[config.OutputCount];
            }
            else if (config.OutputCustomNames.Length != config.OutputCount)
            {
                var outputCustomNames = config.OutputCustomNames;
                Array.Resize(ref outputCustomNames, config.OutputCount);
                config.OutputCustomNames = outputCustomNames;
            }
        }
    }

    public partial class IOOutputViewModel : ObservableObject
    {
        private readonly System.Collections.Generic.List<IOConfig> _ioConfigs;
        private readonly System.Collections.Generic.List<CardConfig> _cardConfigs;
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

        private MarkCardType _selectedMarkCardType;
        public MarkCardType SelectedMarkCardType
        {
            get => _selectedMarkCardType;
            set
            {
                if (SetProperty(ref _selectedMarkCardType, value) && !_isLoading)
                {
                    RefreshCardNos();
                    LoadCurrentConfig();
                }
            }
        }

        private uint _selectedCardNo;
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

        private ObservableCollection<IOOutputItemViewModel> _items = new();
        public ObservableCollection<IOOutputItemViewModel> Items
        {
            get => _items;
            set => SetProperty(ref _items, value);
        }

        public IOOutputViewModel(System.Collections.Generic.List<IOConfig> ioConfigs, System.Collections.Generic.List<CardConfig> cardConfigs)
        {
            _ioConfigs = ioConfigs ?? new System.Collections.Generic.List<IOConfig>();
            _cardConfigs = cardConfigs ?? new System.Collections.Generic.List<CardConfig>();
            RefreshMarkCardBindingData();
        }

        public void RefreshMarkCardBindingData()
        {
            _isLoading = true;
            try
            {
                var currentType = SelectedMarkCardType;
                var currentCardNo = SelectedCardNo;

                _markCardTypes.Clear();
                var distinctTypes = _cardConfigs.Select(c => c.MarkCardType).Distinct().ToList();
                if (distinctTypes.Any())
                {
                    foreach (var type in distinctTypes)
                    {
                        _markCardTypes.Add(type);
                    }
                }
                else
                {
                    foreach (var type in Enum.GetValues<MarkCardType>())
                    {
                        _markCardTypes.Add(type);
                    }
                }

                _selectedMarkCardType = _markCardTypes.Contains(currentType) ? currentType : _markCardTypes.FirstOrDefault();
                OnPropertyChanged(nameof(SelectedMarkCardType));

                RefreshCardNos();

                _selectedCardNo = _cardNos.Contains(currentCardNo) ? currentCardNo : _cardNos.FirstOrDefault();
                OnPropertyChanged(nameof(SelectedCardNo));
            }
            finally
            {
                _isLoading = false;
            }

            LoadCurrentConfig();
        }

        private void RefreshCardNos()
        {
            _cardNos.Clear();

            var cardCount = (uint)_cardConfigs
                .Where(x => x.MarkCardType == _selectedMarkCardType)
                .Sum(x => (int)Math.Max(1u, x.CardCount));

            if (cardCount == 0)
            {
                cardCount = (uint)_ioConfigs
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
                _cardNos.Add(i);
            }
        }

        private void LoadCurrentConfig()
        {
            if (_isLoading || SelectedCardNo == 0)
            {
                return;
            }

            var config = GetOrCreateCurrentConfig();

            _isLoading = true;
            try
            {
                _items.Clear();

                for (int i = 0; i < config.OutputCount; i++)
                {
                    var item = new IOOutputItemViewModel(i, config.OutputFunctions[i], config.OutputCustomNames[i]);
                    item.PropertyChanged += (s, e) =>
                    {
                        if (_isLoading) return;

                        if (e.PropertyName == nameof(IOOutputItemViewModel.SelectedSystemFunction))
                        {
                            if (s is IOOutputItemViewModel vm)
                            {
                                config.OutputFunctions[vm.Index] = vm.SelectedSystemFunction;
                            }
                        }
                        else if (e.PropertyName == nameof(IOOutputItemViewModel.CustomName))
                        {
                            if (s is IOOutputItemViewModel vm)
                            {
                                config.OutputCustomNames[vm.Index] = vm.CustomName ?? string.Empty;
                            }
                        }
                    };
                    _items.Add(item);
                }
            }
            finally
            {
                _isLoading = false;
            }
        }

        private IOConfig GetOrCreateCurrentConfig()
        {
            var config = _ioConfigs.FirstOrDefault(c => c.MarkCardType == SelectedMarkCardType && c.CardNo == SelectedCardNo);
            if (config == null)
            {
                config = new IOConfig
                {
                    MarkCardType = SelectedMarkCardType,
                    CardNo = SelectedCardNo,
                    EnableIO = false,
                    InputCount = 16,
                    OutputCount = 16,
                    InputFunctions = new IOInputFunctionEnum[16],
                    OutputFunctions = new IOOutputFunctionEnum[16],
                    InputCustomNames = new string[16],
                    OutputCustomNames = new string[16]
                };
                _ioConfigs.Add(config);
            }

            NormalizeConfig(config);
            return config;
        }

        private static void NormalizeConfig(IOConfig config)
        {
            config.InputCount = config.InputCount > 0 ? config.InputCount : 16;
            config.OutputCount = config.OutputCount > 0 ? config.OutputCount : 16;

            if (config.InputFunctions == null)
            {
                config.InputFunctions = new IOInputFunctionEnum[config.InputCount];
            }
            else if (config.InputFunctions.Length != config.InputCount)
            {
                var inputFunctions = config.InputFunctions;
                Array.Resize(ref inputFunctions, config.InputCount);
                config.InputFunctions = inputFunctions;
            }

            if (config.OutputFunctions == null)
            {
                config.OutputFunctions = new IOOutputFunctionEnum[config.OutputCount];
            }
            else if (config.OutputFunctions.Length != config.OutputCount)
            {
                var outputFunctions = config.OutputFunctions;
                Array.Resize(ref outputFunctions, config.OutputCount);
                config.OutputFunctions = outputFunctions;
            }

            if (config.InputCustomNames == null)
            {
                config.InputCustomNames = new string[config.InputCount];
            }
            else if (config.InputCustomNames.Length != config.InputCount)
            {
                var inputCustomNames = config.InputCustomNames;
                Array.Resize(ref inputCustomNames, config.InputCount);
                config.InputCustomNames = inputCustomNames;
            }

            if (config.OutputCustomNames == null)
            {
                config.OutputCustomNames = new string[config.OutputCount];
            }
            else if (config.OutputCustomNames.Length != config.OutputCount)
            {
                var outputCustomNames = config.OutputCustomNames;
                Array.Resize(ref outputCustomNames, config.OutputCount);
                config.OutputCustomNames = outputCustomNames;
            }
        }
    }

    public partial class IOInputItemViewModel : ObservableObject
    {
        private static readonly ObservableCollection<IOInputFunctionEnum> _systemFunctionOptions;

        static IOInputItemViewModel()
        {
            _systemFunctionOptions = new ObservableCollection<IOInputFunctionEnum>(
                Enum.GetValues(typeof(IOInputFunctionEnum)).Cast<IOInputFunctionEnum>()
            );
        }

        public ObservableCollection<IOInputFunctionEnum> SystemFunctionOptions => _systemFunctionOptions;

        private int _index;
        public int Index
        {
            get => _index;
            set => SetProperty(ref _index, value);
        }

        private IOInputFunctionEnum _selectedSystemFunction;
        public IOInputFunctionEnum SelectedSystemFunction
        {
            get => _selectedSystemFunction;
            set => SetProperty(ref _selectedSystemFunction, value);
        }

        private string _customName = string.Empty;
        public string CustomName
        {
            get => _customName;
            set => SetProperty(ref _customName, value ?? string.Empty);
        }

        public IOInputItemViewModel(int index, IOInputFunctionEnum function, string customName)
        {
            Index = index;
            SelectedSystemFunction = function;
            CustomName = customName ?? string.Empty;
        }
    }

    public partial class IOOutputItemViewModel : ObservableObject
    {
        private static readonly ObservableCollection<IOOutputFunctionEnum> _systemFunctionOptions;

        static IOOutputItemViewModel()
        {
            _systemFunctionOptions = new ObservableCollection<IOOutputFunctionEnum>(
                Enum.GetValues(typeof(IOOutputFunctionEnum)).Cast<IOOutputFunctionEnum>()
            );
        }

        public ObservableCollection<IOOutputFunctionEnum> SystemFunctionOptions => _systemFunctionOptions;

        private int _index;
        public int Index
        {
            get => _index;
            set => SetProperty(ref _index, value);
        }

        private IOOutputFunctionEnum _selectedSystemFunction;
        public IOOutputFunctionEnum SelectedSystemFunction
        {
            get => _selectedSystemFunction;
            set => SetProperty(ref _selectedSystemFunction, value);
        }

        private string _customName = string.Empty;
        public string CustomName
        {
            get => _customName;
            set => SetProperty(ref _customName, value ?? string.Empty);
        }

        public IOOutputItemViewModel(int index, IOOutputFunctionEnum function, string customName)
        {
            Index = index;
            SelectedSystemFunction = function;
            CustomName = customName ?? string.Empty;
        }
    }
}
