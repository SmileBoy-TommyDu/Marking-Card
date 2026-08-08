

namespace DrSoft.Drawing.DTO
{
   
    public record struct DrawingColorDto(
     byte R,
       byte G,
       byte B,
       byte A = 255)
    {
        public static DrawingColorDto Black => new(0, 0, 0);
        public static DrawingColorDto Gray => new(128, 128, 128);
        public static DrawingColorDto Blue => new(0, 100, 220);
        public static DrawingColorDto Red => new(220, 50, 50);
    }
}
