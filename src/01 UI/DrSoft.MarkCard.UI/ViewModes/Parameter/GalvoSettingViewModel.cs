using CommunityToolkit.Mvvm.ComponentModel;
using DrSoft.Drawing.Event;
using DrSoft.MarkCard.Event;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.Config;
using DrSoft.MarkCard.Model.Enum;
using DrSoft.MarkCard.Model.Parameter;
using DrSoft.MarkCard.Service;
using DrSoft.MarkCard.UI.UIConfig;
using System.Collections.ObjectModel;
using ConfigModel = DrSoft.MarkCard.Model.Config.Config;

namespace DrSoft.MarkCard.UI.ViewModes
{
    public partial class GalvoSettingViewModel: BaseParamViewModel<GalvoConfig>
    {
        [ObservableProperty]
        private MarkCardType _markCardType;

        [ObservableProperty]
        private ObservableCollection<MarkCardType> _markCardTypes = new();

        [ObservableProperty] private uint _cardNo = 1;
        [ObservableProperty] private ObservableCollection<uint> _cardNos = new();
        [ObservableProperty] private uint _scanHeadNo = 1;
        [ObservableProperty] private ObservableCollection<uint> _scanHeadNos = new();
        [ObservableProperty] private ScanHeadProtocol _protocol;
        [ObservableProperty] private ObservableCollection<ScanHeadProtocol> _protocols = new();


        private  ConfigModel _config;
        private  List<ScanHeadConfig> _configs;
        private  List<CardConfig> _cardConfigs;
        private CanvasSystemConfig canvasSystemConfig;
        IEventBus? eventBus => EventBus.Instance;

        SystemParaForGalvoService forGalvoService;

        public GalvoSettingViewModel():base()
        {
            EventBus.Instance.Subscribe<ParaSaveEvent>(OnSaveAll);
            forGalvoService = App.GetService<SystemParaForGalvoService>();
            canvasSystemConfig = App.GetService<CanvasSystemConfig>();
            _config = App.GetService<DrSoft.MarkCard.Model.Config.Config>();
            _configs = _config.ScanHeadConfigs;
            _cardConfigs =_config.CardConfigs;
            RefreshMarkCardBindingData();
          

            foreach (var protocol in Enum.GetValues<ScanHeadProtocol>())
            {
                Protocols.Add(protocol);
            }
            forGalvoService.BindGalvoParas(Model);
            eventBus?.Subscribe<MarkCardConfigEvent<ConfigModel>>(OnMarkCardConfigChanged);
        }
        private void OnSaveAll(ParaSaveEvent @event)
        {
            if (@event.ParaSaveType == ParaSaveType.Canvas && @event.Trigger)
            {
                SaveFun();
            }
        }
        private void OnMarkCardConfigChanged(MarkCardConfigEvent<ConfigModel> e)
        {
            if (e.Data != null)
            {
                _config=e.Data;

                RefreshMarkCardBindingData();
            }
        }

        partial void OnMarkCardTypeChanged(MarkCardType value)
        {
            RefreshCardNos();
            CardNo = CardNos.Contains(CardNo) ? CardNo : CardNos.FirstOrDefault();
            Model.MarkCardType = MarkCardType;
            RefreshScanHeadNos(CardNo);
            ScanHeadNo = ScanHeadNos.Contains(ScanHeadNo) ? ScanHeadNo : ScanHeadNos.FirstOrDefault();
           
        }

        partial void OnCardNoChanged(uint value)
        {
            Model.MarkCardNo = value;
            RefreshScanHeadNos(value);
            ScanHeadNo = ScanHeadNos.Contains(ScanHeadNo) ? ScanHeadNo : ScanHeadNos.FirstOrDefault();
          
        }

        partial void OnScanHeadNoChanged(uint value)
        {
            Model.LensNo = value;
            //LoadCurrentConfig();
        }
        public void RefreshMarkCardBindingData()
        {
            var currentType = MarkCardType;
            var currentCardNo = CardNo;
            var currentScanHeadNo = ScanHeadNo;

      
            try
            {
                MarkCardTypes.Clear();

                var markCard = _cardConfigs.Where(x => x.IsActive).First();
                if (markCard != null) {
                    MarkCardTypes.Add(markCard.MarkCardType);
                }
                

                MarkCardType = MarkCardTypes.Contains(currentType) ? currentType : MarkCardTypes.FirstOrDefault();
                RefreshCardNos();
                CardNo = CardNos.Contains(currentCardNo) ? currentCardNo : CardNos.FirstOrDefault();
                RefreshScanHeadNos(CardNo);
                ScanHeadNo = ScanHeadNos.Contains(currentScanHeadNo) ? currentScanHeadNo : ScanHeadNos.FirstOrDefault();
            }
            finally
            {
               
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

        protected override Task ExecuteApplyAsync()
        {
            SaveFun();
            return Task.CompletedTask;
        }

        private void SaveFun()
        {
            forGalvoService.BindGalvoParas(Model);
            canvasSystemConfig.GalvoConfig = Model;
            canvasSystemConfig.SaveToFile();
        }
    }
}
