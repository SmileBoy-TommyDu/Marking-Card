// 新增事件：NewFileEvent
// 说明：表示新建文件处理的事件定义。没有传入参数，包含回传结果 IsSuccess。

using DrSoft.Drawing.Model;

namespace DrSoft.Drawing.Event
{
    public class FileMenuEvent : IEvent
    {

        public string Path { get; init; } = string.Empty;

        // 可选的错误/状态消息
        public string Message { get; init; } = string.Empty;

        public CanvasSnapshot Snapshot { get; init; } = new();


        public FileOrderEnum Order { get; init; } 
    }

   
}
