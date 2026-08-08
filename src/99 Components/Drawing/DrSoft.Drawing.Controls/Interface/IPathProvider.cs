using SkiaSharp;

namespace DrSoft.Drawing.Controls
{
    public interface IPathProvider
    {
        /// <summary>
        /// 获取图形对应的 SkiaSharp 路径
        /// </summary>
        /// <returns>返回一个全新的或缓存的 SKPath</returns>
        SKPath GetPath();
    }

    /// <summary>
    /// 可封闭图形接口，提供 IsClosed 属性指示图形是否为封闭状态。
    /// </summary>
    public interface IClosable
    {
        /// <summary>
        /// 获取或设置图形是否处于封闭状态。
        /// 对于多段线，True 表示起点和终点自动连接。
        /// </summary>
        bool IsClosed { get; set; }
    }
}
