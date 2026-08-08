using DrSoft.MarkCard.UI.ViewModes;
using System.Windows.Controls;

namespace DrSoft.MarkCard.UI.Views
{
    /// <summary>
    /// MatrixCopyParamView.xaml 的交互逻辑
    /// </summary>
    public partial class MatrixCopyParamView : UserControl
    {
        public MatrixCopyParamView()
        {
            InitializeComponent();
            ParamViewHelper.InitializeParameter<MatrixCopyParamViewModel, Model.MatrixCopyParameter>(this);
        }
    }
}
