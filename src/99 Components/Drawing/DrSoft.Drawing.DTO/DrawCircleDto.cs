



namespace DrSoft.Drawing.DTO
{
    public class DrawCircleDto : DrawObjectDto
    {
        //[ProtoMember(100)] public  Point2D Center { get; set; }
        /// <summary>
        /// 默认X=长半径
        /// </summary>
     public float RadiusX { get; set; }
        /// <summary>
        /// 默认Y=短半径
        /// </summary>
   public float RadiusY { get; set; }
     public bool IsEllipse { get; set; }

    }
}
