using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.UI.ViewModes;
using System.Windows.Controls;

namespace DrSoft.MarkCard.UI.Views
{
    /// <summary>
    /// EngravingParamView.xaml 的交互逻辑
    /// </summary>
    public partial class EngravingParamView : UserControl
    {
        public EngravingParamView()
        {
            InitializeComponent();
            ParamViewHelper.InitializeParameter<EngravingParamViewModel, EngravingParameter>(this);
        }
    }
}
