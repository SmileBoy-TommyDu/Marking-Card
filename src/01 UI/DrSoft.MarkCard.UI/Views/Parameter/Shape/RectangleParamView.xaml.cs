using DrSoft.MarkCard.UI.ViewModes.Parameter;
using System.Windows.Controls;

namespace DrSoft.MarkCard.UI.Views.Shape
{
    public partial class RectangleParamView : UserControl
    {
        public RectangleParamView()
        {
            InitializeComponent();
            ParamViewHelper.InitializeParameter<RectangleParamViewModel, Model.RectangleParameter>(this);
        }
    }
}
