using DrSoft.MarkCard.UI.ViewModes.Parameter;
using System.Windows.Controls;

namespace DrSoft.MarkCard.UI.Views.Shape
{
    public partial class CurveParamView : UserControl
    {
        public CurveParamView()
        {
            InitializeComponent();
            ParamViewHelper.InitializeParameter<CurveParamViewModel, Model.CurveParameter>(this);
        }
    }
}
