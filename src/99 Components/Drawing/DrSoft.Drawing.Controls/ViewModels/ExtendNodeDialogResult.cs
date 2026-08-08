namespace DrSoft.Drawing.Controls.ViewModels;

/// <summary>
/// 延伸节点坐标输入对话框结果。
/// </summary>
public sealed class ExtendNodeDialogResult
{
    public float X { get; set; }

    public float Y { get; set; }

    public bool IsRelativeToPrevious { get; set; }

    public bool Confirmed { get; set; }
}
