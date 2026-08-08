using DrSoft.MarkCard.UI.ViewModes.Parameter;
using System.Windows.Controls;

namespace DrSoft.MarkCard.UI.Views.Shape
{
    public partial class ArcParamView : UserControl
    {
        public ArcParamView()
        {
            InitializeComponent();
            ParamViewHelper.InitializeParameter<ArcParamViewModel, Model.ArcParameter>(this);
        }
    }
}
