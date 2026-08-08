

namespace DrSoft.Drawing.DTO
{
  
    public record struct PenDto(
       DrawingColorDto Color,
         float Width = 2.0f,
        PenStyleDto Style = PenStyleDto.Solid)
    {
    }

 
}
