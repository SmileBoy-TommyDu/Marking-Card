using DrSoft.Drawing.DTO;
using Newtonsoft.Json;


namespace DrSoft.MarkCard.Model.DTO
{
    /// <summary>
    /// 纯 DTO 版本的序列化对象（与业务类解耦）
    /// 用于保存和加载图形及其加工参数
    /// </summary>
 
    public class ShapeSerializableDto
    {
        /// <summary>
        /// 绘图对象（画布、图层、图形）- 纯 DTO 版本
        /// </summary>
 
        public CanvasSnapshotDto? CanvasSnapshot { get; set; }

        /// <summary>
        /// 图形参数关系
        /// key:对象ID（图层ID、群组ID、组合ID、图形ID），value:加工参数列表
        /// </summary>

        [JsonProperty(TypeNameHandling = TypeNameHandling.Auto)]
        public Dictionary<int, IList<ParameterBase>>? MarkingParams { get; set; }
    }
}
