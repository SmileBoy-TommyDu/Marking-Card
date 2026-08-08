using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrSoft.MarkCard.UI.Views.Config;
using System.Windows;

namespace DrSoft.MarkCard.UI.ViewModes;

public partial class LaserToolbarViewModel : ObservableObject
{
    public LaserToolbarViewModel()
    {
        System.Diagnostics.Debug.WriteLine("[LaserToolbarViewModel] 构造函数被调用");
    }

    [ObservableProperty]
    private bool _isEngravingToolChecked;

    [ObservableProperty]
    private bool _isTestMarkingToolChecked;

    /// <summary>
    /// 雕刻工具命令
    /// </summary>
    [RelayCommand]
    private void EngravingTool()
    {
        System.Diagnostics.Debug.WriteLine("[雕刻工具] 命令已执行");
        
        var window = new EngravingToolWindow
        {
            Owner = Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    /// <summary>
    /// 打样工具命令
    /// </summary>
    [RelayCommand]
    private void TestMarkingTool()
    {
        System.Diagnostics.Debug.WriteLine("[打样工具] 命令已执行");
        // TODO: 实现打样工具
    }
}
