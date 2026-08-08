using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;

namespace DrSoft.MarkCard.Model.EditMenu
{
    public class ConvertToPointCircleSettingsModel
    {
        public ShapeType SelectedShapeType { get; set; }
        public float Gap { get; set; }
        public float Diameter { get; set; }
        public bool NeedPointAtCornner { get; set; } = false;
        /// <summary>
        /// 度
        /// </summary>
        public float IncludedAngle { get; set; } = 0;
    }
}
