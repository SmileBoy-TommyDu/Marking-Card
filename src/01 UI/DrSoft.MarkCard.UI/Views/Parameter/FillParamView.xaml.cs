using System.Windows.Controls;
using DrSoft.Drawing.Model;
using DrSoft.MarkCard.UI.ViewModes;

namespace DrSoft.MarkCard.UI.Views
{
    /// <summary>
    /// FillParamView.xaml 的交互逻辑
    /// </summary>
    public partial class FillParamView : UserControl
    {
        public FillParamView()
        {
            InitializeComponent();
        }

        public FillParamView(bool update) : this()
        {
            if (update)
            {
                ApplyButton.Visibility = System.Windows.Visibility.Visible;
                ParamViewHelper.InitializeParameter<FillParamViewModel, Model.FillParameter>(this);

                // 通过 ViewModel 的 LoadFromHatch 从接口加载数据，不直接访问画布
                (DataContext as FillParamViewModel)?.LoadFromHatch();
            }
        }
    }
}
