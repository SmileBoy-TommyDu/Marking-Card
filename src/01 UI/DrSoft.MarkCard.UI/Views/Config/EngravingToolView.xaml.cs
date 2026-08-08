using System.Windows.Controls;
using DrSoft.MarkCard.UI.ViewModes.Config;

namespace DrSoft.MarkCard.UI.Views.Config;

public partial class EngravingToolView : UserControl
{
    public EngravingToolView()
    {
        InitializeComponent();
        DataContext = App.GetService<EngravingToolViewModel>();
    }
}
