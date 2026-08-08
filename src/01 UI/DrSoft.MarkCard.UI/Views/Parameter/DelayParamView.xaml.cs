using DrSoft.MarkCard.UI.ViewModes;
using System.Windows.Controls;

namespace DrSoft.MarkCard.UI.Views
{
    /// <summary>
    /// DelayParamView.xaml 的交互逻辑
    /// </summary>
    public partial class DelayParamView : UserControl
    {
        public DelayParamView()
        {
            InitializeComponent();
            ParamViewHelper.InitializeParameter<DelayParamViewModel, Model.DelayParameter>(this);
        }
    }
}
