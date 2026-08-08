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
    public partial class MarkCardConfigViewModel : ObservableObject
    {
        private readonly List<CardConfig> _configs;

        [ObservableProperty]
        private MarkCardType _markCardType;
        partial void OnMarkCardTypeChanged(MarkCardType value)
        {
            _cardConfig.MarkCardType = value;
        }

        [ObservableProperty]
        private ConnectType _connectionType;
        partial void OnConnectionTypeChanged(ConnectType value)
        {
            _cardConfig.ConnectionType = value;

            // PCIe 方式不需要通讯设置，禁用并清空各卡的通讯设置
            bool editable = value != ConnectType.PCIe;
            foreach (var card in Cards)
            {
                card.ConnectionType = value;
                card.IsConnectionSettingEditable = editable;
                if (!editable)
                {
                    card.ConnectionSetting = string.Empty;
                }
                else
                {
                    // 切换通讯类型后过滤已有内容，只保留当前类型允许的字符
                    card.ConnectionSetting = MarkCardItemViewModel.FilterConnectionSetting(card.ConnectionSetting, value);
                }
            }
        }

        [ObservableProperty]
        private int _markingTimeout;
        partial void OnMarkingTimeoutChanged(int value)
        {
            _cardConfig.MarkingTimeout = value;
        }

        [ObservableProperty]
        private int _initTimeout;
        partial void OnInitTimeoutChanged(int value)
        {
            _cardConfig.InitTimeout = value;
        }

        [ObservableProperty]
        private bool _isActivated;
        partial void OnIsActivatedChanged(bool value)
        {
           
            _cardConfig.IsActive = value;
            if (value)
            {
                foreach(var g in _configs)
                {
                    if (g != _cardConfig)
                    {
                        g.IsActive = false;
                    }
                }
            }
        }

        [ObservableProperty]
        private IOTriggerType _ioTriggerMode;

        partial void OnIoTriggerModeChanged(IOTriggerType value)
        {
            _cardConfig.IOTriggerType = value;
        }

        [ObservableProperty]
        private bool _ioTriggerEnabled;

        partial void OnIoTriggerEnabledChanged(bool value) => _cardConfig.EnableIOTrigger = value;

        [ObservableProperty]
        private uint _cardCount;

        [ObservableProperty]
        private ObservableCollection<int> _cardTypeCounts;

        [ObservableProperty]
        private int _selectedCardTypeIndex = 0;

        [ObservableProperty]
        private ObservableCollection<MarkCardType> _markCardTypes = new ObservableCollection<MarkCardType>();

        [ObservableProperty]
        private ObservableCollection<ConnectType> _connectTypes = new ObservableCollection<ConnectType>();

        [ObservableProperty]
        private ObservableCollection<uint> _cardCounts = new ObservableCollection<uint>() { 1, 2, 3, 4, 5, 6 };

        [ObservableProperty]
        private ObservableCollection<IOTriggerType> _iOTriggerTypes = new ObservableCollection<IOTriggerType>();

        [ObservableProperty]
        private MarkCardItemViewModel? _selectedCard;

        [ObservableProperty]
        private ObservableCollection<MarkCardItemViewModel> _cards = new ObservableCollection<MarkCardItemViewModel>();

        private CardConfig _cardConfig;

        public MarkCardConfigViewModel(List<CardConfig> configs)
        {
            _configs = configs ?? new List<CardConfig>();
            if (_configs.Count == 0)
            {
                _configs.Add(new CardConfig());
            }

            _cardConfig = _configs[_selectedCardTypeIndex];
            _cardTypeCounts = new ObservableCollection<int>();

            for (int i = 0; i < Enum.GetValues<MarkCardType>().Length; i++)
            {
                MarkCardTypes.Add((MarkCardType)i);
            }

            for (int i = 0; i < Enum.GetValues<ConnectType>().Length; i++)
            {
                ConnectTypes.Add((ConnectType)i);
            }

            for (int i = 0; i < Enum.GetValues<IOTriggerType>().Length; i++)
            {
                IOTriggerTypes.Add((IOTriggerType)i);
            }

            for (int i = 0; i < _configs.Count; i++)
            {
                CardTypeCounts.Add(i + 1);
            }

            LoadSelectedCardConfig();
        }

        private void LoadSelectedCardConfig()
        {
            if (SelectedCardTypeIndex < 0 || SelectedCardTypeIndex >= _configs.Count)
                return;

            _cardConfig = _configs[SelectedCardTypeIndex];
            _cardConfig.CardDescConfigs ??= new List<CardDescConfig>();

            MarkCardType = _cardConfig.MarkCardType;
            ConnectionType = _cardConfig.ConnectionType;
            MarkingTimeout = _cardConfig.MarkingTimeout;
            InitTimeout = _cardConfig.InitTimeout;
            IsActivated = _cardConfig.IsActive;
            IoTriggerMode = _cardConfig.IOTriggerType;
            IoTriggerEnabled = _cardConfig.EnableIOTrigger;
            CardCount = _cardConfig.CardCount;

            InitializeCards();
        }

        private void InitializeCards()
        {
            Cards.Clear();

            _cardConfig.CardDescConfigs ??= new List<CardDescConfig>();

            while (_cardConfig.CardDescConfigs.Count < _cardCount)
            {
                _cardConfig.CardDescConfigs.Add(new CardDescConfig
                {
                    ScanHeadCount = 1,
                    ConnectStr = string.Empty,
                    IsMaster = _cardConfig.CardDescConfigs.Count == 0
                });
            }

            if (_cardConfig.CardDescConfigs.Count > _cardCount)
            {
                _cardConfig.CardDescConfigs.RemoveRange((int)_cardCount, _cardConfig.CardDescConfigs.Count - (int)_cardCount);
            }

            // 计算当前卡配置在同类型卡中的全局起始卡号
            uint globalStartNo = 1;
            for (int idx = 0; idx < SelectedCardTypeIndex; idx++)
            {
                if (_configs[idx].MarkCardType == _cardConfig.MarkCardType)
                {
                    globalStartNo += Math.Max(_configs[idx].CardCount, 1u);
                }
            }

            bool editable = ConnectionType != ConnectType.PCIe;
        
            // 主卡互斥：同一类型至多只能保留一张主卡（若存储中有多张，仅保留第一张）
            bool masterAssigned = false;
            for (int i = 0; i < _cardCount; i++)
            {
                var desc = _cardConfig.CardDescConfigs[i];
                if (desc.IsMaster)
                {
                    if (masterAssigned)
                        desc.IsMaster = false;
                    else
                        masterAssigned = true;
                }
            }
        
            for (int i = 0; i < _cardCount; i++)
            {
                var cardDescConfig = _cardConfig.CardDescConfigs[i];
                uint globalCardNo = globalStartNo + (uint)i;
                Cards.Add(new MarkCardItemViewModel(cardDescConfig, i + 1, globalCardNo, OnCardMasterSelected)
                {
                    ConnectionType = ConnectionType,
                    IsConnectionSettingEditable = editable
                });
            }
        
            SelectedCard = Cards.FirstOrDefault();
        }
        
        /// <summary>
        /// 主卡互斥回调：当某张卡被设为主卡时，自动取消同一类型下其他卡的主卡标记
        /// </summary>
        private void OnCardMasterSelected(MarkCardItemViewModel current)
        {
            foreach (var card in Cards)
            {
                if (!ReferenceEquals(card, current) && card.IsMaster)
                {
                    card.IsMaster = false;
                }
            }
        }

        partial void OnCardCountChanged(uint value)
        {
            _cardConfig.CardCount = value;
            InitializeCards();
        }

        partial void OnSelectedCardTypeIndexChanged(int value)
        {
            LoadSelectedCardConfig();
        }

        /// <summary>
        /// 获取指定索引的卡配置
        /// </summary>
        public CardConfig GetCardConfig(int index)
        {
            if (index >= 0 && index < _configs.Count)
            {
                return _configs[index];
            }
            return null;
        }

        [RelayCommand]
        private void AddCard()
        {
            _configs.Add(new CardConfig());
            _cardTypeCounts.Add(_cardTypeCounts.Count + 1);
            SelectedCardTypeIndex = _configs.Count - 1;
        }

        [RelayCommand]
        private void RemoveCard(int index)
        {
            var targetIndex = index > 0 ? index - 1 : index;
            if (_configs.Count <= 1 || targetIndex < 0 || targetIndex >= _configs.Count)
            {
                return;
            }

            _configs.RemoveAt(targetIndex);
            _cardTypeCounts.Clear();
            for (int i = 0; i < _configs.Count; i++)
            {
                _cardTypeCounts.Add(i + 1);
            }

            if (SelectedCardTypeIndex > targetIndex)
            {
                SelectedCardTypeIndex--;
            }
            else if (SelectedCardTypeIndex == targetIndex)
            {
                SelectedCardTypeIndex = Math.Min(targetIndex, _configs.Count - 1);
                LoadSelectedCardConfig();
            }
        }

        [RelayCommand]
        private void SelectCard(int index)
        {
            var targetIndex = index > 0 ? index - 1 : index;
            if (targetIndex >= 0 && targetIndex < _configs.Count)
            {
                SelectedCardTypeIndex = targetIndex;
            }
        }
    }

    public partial class MarkCardItemViewModel : ObservableObject
    {
        private readonly CardDescConfig _cardDescConfig;
        private readonly uint _globalCardNo;
        private readonly Action<MarkCardItemViewModel>? _onMasterSelected;

        [ObservableProperty]
        private int _sequenceNumber;

        [ObservableProperty]
        private string _connectionSetting;

        partial void OnConnectionSettingChanged(string value)
        {
            // 根据当前通讯类型过滤字符，非法字符被丢弃
            string filtered = FilterConnectionSetting(value, _connectionType);
            if (filtered != value)
            {
                ConnectionSetting = filtered;
                return;
            }
            _cardDescConfig.ConnectStr = filtered;
        }

        /// <summary>
        /// 根据通讯类型过滤输入字符：Ethernet 只保留数字和点，Com 只保留字母数字并转大写
        /// </summary>
        internal static string FilterConnectionSetting(string value, ConnectType type)
        {
            if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
            switch (type)
            {
                case ConnectType.Ethernet:
                    return new string(value.Where(c => char.IsDigit(c) || c == '.').ToArray());
                case ConnectType.Com:
                    return new string(value.Where(c => char.IsLetterOrDigit(c)).ToArray()).ToUpperInvariant();
                default:
                    return value;
            }
        }

        /// <summary>
        /// 通讯类型（由父 ViewModel 同步）
        /// </summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ConnectionSettingHint))]
        private ConnectType _connectionType;

        /// <summary>
        /// 通讯设置输入提示
        /// </summary>
        public string ConnectionSettingHint => ConnectionType switch
        {
            ConnectType.Ethernet => "请输入 IP 地址（例如 192.168.1.1）",
            ConnectType.Com => "请输入 COM 端口（例如 COM1）",
            _ => string.Empty
        };

        /// <summary>
        /// 是否允许编辑通讯设置（PCIe 方式时不允许输入）
        /// </summary>
        [ObservableProperty]
        private bool _isConnectionSettingEditable = true;

        [ObservableProperty]
        private uint _scanHeadCount;

        partial void OnScanHeadCountChanged(uint value)
        {
            // 兜底，防止出现非法数量
            if (value == 0)
            {
                value = 1;
                ScanHeadCount = value;
                return;
            }

            _cardDescConfig.ScanHeadCount = value;

            var config = App.GetService<DrSoft.MarkCard.Model.Config.Config>();
            if (config == null)
            {
                return;
            }

            config.ScanHeadConfigs ??= new List<ScanHeadConfig>();
            var list = config.ScanHeadConfigs;

            // 当数量减少时，移除超出范围的扫描头配置
            list.RemoveAll(x => x.CardNo == _globalCardNo && x.ScanHeadNo > value);

            // 当数量增加时，补齐缺失的扫描头配置
            for (uint i = 1; i <= value; i++)
            {
                if (list.Find(x => x.CardNo == _globalCardNo && x.ScanHeadNo == i) == null)
                {
                    list.Add(new ScanHeadConfig { CardNo = _globalCardNo, ScanHeadNo = i });
                }
            }
        }

        [ObservableProperty]
        private bool _isMaster;
        
        partial void OnIsMasterChanged(bool value)
        {
            _cardDescConfig.IsMaster = value;
            // 主卡互斥：勾选为主卡时通知父 VM 取消其他卡的主卡标记
            if (value)
            {
                _onMasterSelected?.Invoke(this);
            }
        }

        [ObservableProperty]
        private ObservableCollection<uint> _scanHeadCounts = new ObservableCollection<uint>() { 1, 2 };

        public MarkCardItemViewModel(CardDescConfig cardDescConfig, int sequence, uint globalCardNo, Action<MarkCardItemViewModel>? onMasterSelected = null)
        {
            _cardDescConfig = cardDescConfig;
            _globalCardNo = globalCardNo;
            _onMasterSelected = onMasterSelected;
            SequenceNumber = sequence;
            ConnectionSetting = cardDescConfig.ConnectStr;
            ScanHeadCount = cardDescConfig.ScanHeadCount;
            IsMaster = cardDescConfig.IsMaster;
        }
    }
}
