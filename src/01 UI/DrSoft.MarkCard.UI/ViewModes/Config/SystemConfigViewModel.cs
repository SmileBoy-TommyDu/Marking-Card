using CommunityToolkit.Mvvm.ComponentModel;
using DrSoft.MarkCard.Model.Config;

namespace DrSoft.MarkCard.UI.ViewModes.Config
{
    public partial class SystemConfigViewModel : ObservableObject
    {
        public LogSettingsViewModel LogSettingsVm { get; }
        public GridMicroAdjustViewModel GridMicroAdjustVm { get; }
        public AutomationProcessViewModel AutomationProcessVm { get; }

        public SystemConfigViewModel(SystemConfig config)
        {
            LogSettingsVm = new LogSettingsViewModel(config);
            GridMicroAdjustVm = new GridMicroAdjustViewModel(config);
            AutomationProcessVm = new AutomationProcessViewModel(config);
        }
    }
}
