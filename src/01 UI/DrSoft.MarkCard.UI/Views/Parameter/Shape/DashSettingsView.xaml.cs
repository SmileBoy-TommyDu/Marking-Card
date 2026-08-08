using DrSoft.MarkCard.UI.ViewModes.Parameter;
using System.Windows.Controls;

namespace DrSoft.MarkCard.UI.Views.Parameter.Shape
{
    /// <summary>
    /// DashSettingsView.xaml 的交互逻辑
    /// </summary>
    public partial class DashSettingsView : UserControl
    {
        public DashSettingsView()
        {
            InitializeComponent();
            ParamViewHelper.InitializeParameter<DashSettingsViewModel, Model.DashSettingParameter>(this);
        }
    }
}
