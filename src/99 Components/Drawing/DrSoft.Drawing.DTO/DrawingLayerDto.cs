

namespace DrSoft.Drawing.DTO
{
    /// <summary>
    /// DrawingLayer 
    /// /// </summary>
 
    public class DrawingLayerDto
    {
      
        public int Id { get; set; }
        
     
        public string? Name { get; set; }

 
        public bool IsVisible { get; set; } = true;
        
   
        public string? Color { get; set; }
        
      
        public bool IsLocked { get; set; }

  
        public int SortId { get; set; }

       
        public List<DrawObjectDto>? Shapes { get; set; }
    }
}
