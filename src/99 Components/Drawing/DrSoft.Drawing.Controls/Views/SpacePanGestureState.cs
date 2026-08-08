using System.Windows.Input;

namespace DrSoft.Drawing.Controls.Views;

internal enum SpacePanCursorMode
{
    None,
    Ready,
    Active
}

/// <summary>
/// 管理“按住空格临时抓手”的短生命周期状态。
/// 负责输入状态与抓手光标阶段判定，不直接依赖具体视图或画布实现。
/// </summary>
internal sealed class SpacePanGestureState
{
    public bool IsSpacePressed { get; private set; }
    public bool IsPanningWithSpace { get; private set; }
    public SpacePanCursorMode CursorMode =>
        IsPanningWithSpace
            ? SpacePanCursorMode.Active
            : IsSpacePressed
                ? SpacePanCursorMode.Ready
                : SpacePanCursorMode.None;

    public bool HandleKeyDown(Key key)
    {
        if (key != Key.Space)
            return false;

        IsSpacePressed = true;
        return true;
    }

    public bool HandleKeyUp(Key key)
    {
        if (key != Key.Space)
            return false;

        IsSpacePressed = false;
        return true;
    }

    public bool TryStartPan(MouseButton changedButton)
    {
        if (!IsSpacePressed || changedButton != MouseButton.Left)
            return false;

        IsPanningWithSpace = true;
        return true;
    }

    public bool TryEndPan(MouseButton changedButton)
    {
        if (!IsPanningWithSpace || changedButton != MouseButton.Left)
            return false;

        IsPanningWithSpace = false;
        return true;
    }
}
