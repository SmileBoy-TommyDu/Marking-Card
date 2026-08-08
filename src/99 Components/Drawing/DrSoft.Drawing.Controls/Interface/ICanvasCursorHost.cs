namespace DrSoft.Drawing.Controls.Interface;

/// <summary>
/// 交互内核请求宿主切换光标的最小接口。
/// 用于把编辑器内核与具体 WPF 控件解耦。
/// </summary>
public interface ICanvasCursorHost
{
    void SetCursor(System.Windows.Input.Cursor cursor);
}
