using DrSoft.MarkCard.UI.ViewModes.Parameter;
using System.Windows.Controls;

namespace DrSoft.MarkCard.UI.Views.Shape
{
    public partial class PolygonParamView : UserControl
    {
        public PolygonParamView()
        {
            InitializeComponent();
            ParamViewHelper.InitializeParameter<PolygonParamViewModel, Model.PolygonParameter>(this);
        }
    }
}
