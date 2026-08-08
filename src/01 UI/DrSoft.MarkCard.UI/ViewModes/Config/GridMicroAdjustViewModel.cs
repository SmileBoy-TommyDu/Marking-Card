using CommunityToolkit.Mvvm.ComponentModel;
using DrSoft.MarkCard.Model.Config;
using Org.BouncyCastle.Tsp;

namespace DrSoft.MarkCard.UI.ViewModes.Config
{
    public partial class GridMicroAdjustViewModel : ObservableObject
    {
        private readonly SystemConfig _config;

        [ObservableProperty]
        private double _gridSpacingX;

        [ObservableProperty]
        private double _gridSpacingY;

        [ObservableProperty]
        private double _microAdjustStepX;

        [ObservableProperty]
        private double _microAdjustStepY;

        [ObservableProperty]
        private double _resolution;


        public GridMicroAdjustViewModel(SystemConfig config)
        {
            _config = config;
            GridSpacingX = config.GridSpacingX;
            GridSpacingY = config.GridSpacingY;
            Resolution = config.Resolution;
            MicroAdjustStepX = config.MicroAdjustStepX;
            MicroAdjustStepY = config.MicroAdjustStepY;
        }

        partial void OnGridSpacingXChanged(double value) {
            _config.GridSpacingX = value;
         }
        partial void OnGridSpacingYChanged(double value) => _config.GridSpacingY = value;
        partial void OnMicroAdjustStepXChanged(double value) => _config.MicroAdjustStepX = value;
        partial void OnMicroAdjustStepYChanged(double value) => _config.MicroAdjustStepY = value;
    }
}
