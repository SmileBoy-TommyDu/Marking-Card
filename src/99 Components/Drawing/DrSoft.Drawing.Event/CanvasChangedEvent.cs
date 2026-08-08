namespace DrSoft.Drawing.Event
{
    // Generic version with typed Data
    public class CanvasChangedEvent<T> : IEvent
    {
        public int? CanvasId { get; init; }
        public string? CanvasName { get; init; }

        public T? Data { get; init; }

        public CanvasChangeType ChangeType { get; init; }
    }

    // Backwards-compatible non-generic alias (uses object)
    public class CanvasChangedEvent : CanvasChangedEvent<object>
    {
    }

    public enum CanvasChangeType
    {
        NoCanvas,
        Created,
        Renamed,
        BeforeRemove,
        Removed,
        Switched,
        Command,
        // 兼容历史命名。Undo/Redo 现统一走 Command 变更主通路，
        // 保留旧枚举别名避免合并后测试/旧调用点编译失败。
        UnRedo = Command,
        SelectChanged,
        SelectSharps,
        TransformChanged,// 形状变换（移动、旋转、缩放等）
        SelectStateChanged,//选择状态变换事件
        // 其他类型...
    }
}
