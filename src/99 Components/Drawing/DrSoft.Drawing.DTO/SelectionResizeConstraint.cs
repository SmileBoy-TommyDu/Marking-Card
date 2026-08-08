namespace DrSoft.Drawing.DTO;

/// <summary>
/// 描述当前选区在缩放交互上的统一约束。
/// UI、句柄命中、拖拽缩放和尺寸输入都应消费同一组约束，而不是各自按图元类型分支。
/// </summary>
[System.Flags]
public enum SelectionResizeConstraint
{
    /// <summary>当前选区没有额外缩放约束。</summary>
    None = 0,

    /// <summary>选框不暴露上下左右边中点句柄。</summary>
    HideEdgeMidpointHandles = 1 << 0,

    /// <summary>控制点拖拽和程序化尺寸调整都必须保持等比缩放。</summary>
    RequireUniformScale = 1 << 1
}
