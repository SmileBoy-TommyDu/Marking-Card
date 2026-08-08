namespace DrSoft.Drawing.Event
{
    /// <summary>
    /// 复制图层数量请求事件：由图层 ViewModel 发布，UI 层订阅并弹出输入对话框，返回用户输入的复制数量。
    /// </summary>
    public record CopyLayerCountRequestEvent : IEvent
    {
        /// <summary>被复制图层的名称，用于对话框提示</summary>
        public string LayerName { get; init; } = string.Empty;
    }

    /// <summary>
    /// 复制图层参数事件：由图层 ViewModel 在完成图形复制后发布，
    /// UI 层订阅后将原图形绑定的加工参数复制到新图形上。
    /// </summary>
    public record CopyLayerParametersEvent : IEvent
    {
        /// <summary>画布ID</summary>
        public int CanvasId { get; init; }

        /// <summary>原图形UId → 新图形UId 的映射（包含容器内子图形的映射）</summary>
        public Dictionary<int, int> OldToNewUIdMap { get; init; } = new();
    }
}
