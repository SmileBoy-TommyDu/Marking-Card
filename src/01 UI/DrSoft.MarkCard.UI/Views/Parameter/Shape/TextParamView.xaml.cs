using DrSoft.MarkCard.UI.ViewModes.Parameter;
using System.Windows.Controls;

namespace DrSoft.MarkCard.UI.Views.Shape
{
    public partial class TextParamView : UserControl
    {
        public TextParamView()
        {
            InitializeComponent();
            ParamViewHelper.InitializeParameter<TextParamViewModel, Model.TextParameter>(this);
        }
    }
}
