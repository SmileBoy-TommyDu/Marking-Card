using DrSoft.MarkCard.Model.Parameter;
using DrSoft.MarkCard.UI.ViewModes;
using System.Windows.Controls;

namespace DrSoft.MarkCard.UI.Views
{
    /// <summary>
    /// OutlineParamView.xaml 的交互逻辑
    /// </summary>
    public partial class OutlineParamView : UserControl
    {
        public OutlineParamView()
        {
            InitializeComponent();
            ParamViewHelper.InitializeParameter<OutlineParamViewModel, OutlineParameter>(this);
        }
    }
}
