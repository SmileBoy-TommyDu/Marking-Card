namespace DrSoft.Drawing.Controls.Interface;

/// <summary>
/// 交互层访问宿主 UI 的聚合接口。
/// 当前承载状态、光标、对话框和输入态查询能力，避免工具直接依赖 ViewModel/WPF 控件。
/// </summary>
public interface ICanvasInteractionHost : ICanvasStatusHost, ICanvasCursorHost, ICanvasDialogHost
{
    bool IsShiftPressed();
}
