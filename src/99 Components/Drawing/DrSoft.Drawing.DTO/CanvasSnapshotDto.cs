using DrSoft.Drawing.Model;

namespace DrSoft.Drawing.DTO
{
    /// <summary>
    /// CanvasSnapshot — 内部流转对象（不考虑 protobuf 序列化）。
    /// Layers 直接使用 ILayerData 接口，导入时无需 DTO→图形对象转换。
    /// </summary>

    public class CanvasSnapshotDto
    {
   
        public int Id { get; set; }
        

        public string? Name { get; set; }
        
     
        public List<ILayerData> Layers { get; set; } = new List<ILayerData>();
    }
}
