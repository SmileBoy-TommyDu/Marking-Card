

namespace DrSoft.Drawing.DTO
{
 
    public class SelectedSharpsDto
    {
      
        public int Id { get; set; }

        public string? Name { get; set; }

        // 当且仅当选中单个图形时，属性才有值；其他情况下为null
     
        public DrawObjectDto? EditingObject { get; set; }

    
        public List<int> SelectionIds { get; set; } = new List<int>();

        public DrawObjectDto DrawObjectDtoData { get; set; } = new DrawObjectDto();

        public Boolean IsAllLock { get; set; }

        /// <summary>
        /// 选区级缩放约束。外围 UI 只消费能力语义，不关心是哪类图元触发了这些规则。
        /// </summary>
        public SelectionResizeConstraint ResizeConstraint { get; set; }

        public bool RequiresHatchRegeneration { get; set; }
    }
}
