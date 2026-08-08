

using System.Security.AccessControl;

namespace DrSoft.Drawing.DTO
{

    public class DrawArcDto : DrawObjectDto
    {

        public double Radius { get; set; }
        public double StartAngle { get; set; }
        public double SweepAngle { get; set; }

        public ArcTypeDto TypeOfArc = ArcTypeDto.ThreePoint;

        public double RotitionAngle { get; set; }


        public bool UseCenter = false;
    }

    public enum ArcTypeDto
    {
        ThreePoint,     // 三点圆弧
        CenterRadius    // 圆心半径模式
    }
}
