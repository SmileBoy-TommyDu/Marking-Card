using System.Windows.Controls;
using DrSoft.MarkCard.UI.ViewModes;

namespace DrSoft.MarkCard.UI.Views;

public partial class LaserToolbarView : UserControl
{
    public LaserToolbarView()
    {
        InitializeComponent();
        DataContext = App.GetService<LaserToolbarViewModel>();
    }
}
