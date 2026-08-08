using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DrSoft.MarkCard.UI.ViewModes.Calibrate
{
    public partial class PTCalibrationViewModel : ObservableObject
    {
        [ObservableProperty]
        private double _ptParam1;

        [ObservableProperty]
        private double _ptParam2;

        [ObservableProperty]
        private double _ptParam3;

        [RelayCommand]
        private void DryRun()
        {
            // 空走逻辑
        }

        [RelayCommand]
        private void MarkGraphics()
        {
            // 标刻图形逻辑
        }

        [RelayCommand]
        private void Apply()
        {
            // 应用PT校正逻辑
        }

        [RelayCommand]
        private void StopProcess()
        {
            // 停止加工逻辑
        }
    }
}
