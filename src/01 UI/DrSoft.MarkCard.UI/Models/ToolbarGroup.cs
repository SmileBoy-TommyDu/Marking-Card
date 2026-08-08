using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace DrSoft.MarkCard.UI.Models;

/// <summary>单个工具栏组的数据模型</summary>
public partial class ToolbarGroup : ObservableObject
{
    /// <summary>组的唯一标识（不可变）</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>组标题，显示在分隔符上方</summary>
    [ObservableProperty] private string _title = "";

    /// <summary>是否在工具栏中可见</summary>
    [ObservableProperty] private bool _isVisible = true;

    ///// <summary>该组包含的所有按钮</summary>
    //public ObservableCollection<ToolbarButton> Buttons { get; } = new();

    ///// <summary>该组包含的所有按钮</summary>
    //public ObservableCollection<CommandItem> Buttons { get; } = new();

    /// <summary>该组包含的所有按钮</summary>
    public ObservableCollection<ToolButton> Buttons { get; } = new();

    /// <summary>在工具栏中的排列顺序（越小越靠前）</summary>
    [ObservableProperty] private int _order;
}

/// <summary>单个工具按钮</summary>
public partial class ToolbarButton : ObservableObject
{
    public string Icon { get; init; } = "";   // Unicode 字符作为图标
    public string Tooltip { get; init; } = "";
    public string Label { get; init; } = "";

    [ObservableProperty] private bool _isChecked;
    [ObservableProperty] private bool _isEnabled = true;

    public bool IsToggle { get; init; } = false;
    public Action? Command { get; init; }
}

public class CommandItem
{
    public string Label { get; set; }      // 显示文字
    public string Tooltip { get; set; }    // 提示
    public object Icon { get; set; }       // 可以是字符、图片路径或图标对象
    public int Width { get; set; } = 20;
    public ICommand Command { get; set; }  // 实际执行的命令
    public string CommandParameter { get; set; } // 可选参数
}

public class ToolButton
{
    /// <summary>
    /// 任意 UI 元素（Button、ComboBox、自定义控件等）
    /// </summary>
    public UIElement Content { get; set; }

    /// <summary>
    /// 工具提示（可选）
    /// </summary>
    public string Tooltip { get; set; }
}
