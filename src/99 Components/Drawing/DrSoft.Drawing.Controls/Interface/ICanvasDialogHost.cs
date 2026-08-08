using DrSoft.Drawing.Controls.ViewModels;

namespace DrSoft.Drawing.Controls.Interface;

/// <summary>
/// 交互流程需要临时弹框时使用的宿主接口。
/// 仅暴露内核当前实际需要的对话框能力，避免直接依赖具体 ViewModel 或控件。
/// </summary>
public interface ICanvasDialogHost
{
    MoveNodeDialogResult? ShowMoveNodeDialog(float currentX, float currentY);

    ExtendNodeDialogResult? ShowExtendNodeDialog();

    SeparateNodeDialogResult? ShowSeparateNodeDialog(float currentDistance);
}
