using CommunityToolkit.Mvvm.ComponentModel;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.Config;
using DrSoft.MarkCard.Service;
using DrSoft.MarkCard.UI.ViewModes.Calibrate;
using ConfigModel = DrSoft.MarkCard.Model.Config.Config;

namespace DrSoft.MarkCard.UI.ViewModes.Calibrate
{
    public partial class CalibrationToolViewModel : ObservableObject
    {
        [ObservableProperty]
        private int _selectedTabIndex;

        [ObservableProperty]
        private CalibrateProcessViewModel _calibrateProcessViewModel;

        [ObservableProperty]
        private GalvoParamViewModel _galvoParamViewModel;

        [ObservableProperty]
        private BarrelDistortionViewModel _barrelDistortionViewModel;

        [ObservableProperty]
        private MultiStageCalibrationViewModel _multiStageCalibrationViewModel;

        [ObservableProperty]
        private PTCalibrationViewModel _ptCalibration;

        [ObservableProperty]
        private CalibrationAnalysisViewModel _calibrationAnalysisViewModel;

        [ObservableProperty]
        private bool isDisplayBarrelColibration = false;




        public CalibrationToolViewModel(ScanHeadConfig scanHeadConfig)
        {
            CalibrateProcessViewModel = new CalibrateProcessViewModel(scanHeadConfig);
            GalvoParamViewModel = new GalvoParamViewModel(scanHeadConfig);
            BarrelDistortionViewModel = new BarrelDistortionViewModel(scanHeadConfig);
            MultiStageCalibrationViewModel = new MultiStageCalibrationViewModel(scanHeadConfig);
            _ptCalibration = new PTCalibrationViewModel();
            CalibrationAnalysisViewModel = new CalibrationAnalysisViewModel();
            var config = App.GetService<ConfigModel>();
            if (config != null&&config.CardConfigs.Any()) { 
            
                var cardConfigs = config.CardConfigs.Find(x=>x.IsActive);
                if(cardConfigs.MarkCardType != MarkCardType.RTC6)
                {
                    IsDisplayBarrelColibration = true;
                }
                else
                {
                    IsDisplayBarrelColibration = false;
                }
            }
           
        }


    }
}
