using DrSoft.Drawing.Model;
using System.Numerics;

namespace DrSoft.Drawing.DTO
{
   
    public class DrawRectangleDto : DrawObjectDto
    {
        public bool HasRoundedCorners { get; set; } = false;
        public bool HasChamfer { get; set; } = false;
        public float CornerRadiusTopLeft { get; set; } = 0;
       public float CornerRadiusTopRight { get; set; } = 0;
         public float CornerRadiusBottomRight { get; set; } = 0;
      public float CornerRadiusBottomLeft { get; set; } = 0;

       public float ChamferTopLeft { get; set; } = 0;
       public float ChamferTopRight { get; set; } = 0;
       public float ChamferBottomRight { get; set; } = 0;
     public float ChamferBottomLeft { get; set; } = 0;

     public List<Point2D> Vertices { get; set; } = new List<Point2D>();
     

    }
}
