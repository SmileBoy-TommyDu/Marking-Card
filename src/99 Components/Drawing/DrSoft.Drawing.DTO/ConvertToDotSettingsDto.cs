namespace DrSoft.Drawing.DTO
{
    /// <summary>
    /// 转成点/圆的设置参数
    /// </summary>
    public class ConvertToDotSettingsDto
    {
        /// <summary>点/圆之间的间距（mm）</summary>
        public float Gap { get; set; }

        /// <summary>点/圆的直径（mm）</summary>
        public float Diameter { get; set; }

        /// <summary>true=转为圆，false=转为点</summary>
        public bool IsCircleType { get; set; }

        /// <summary>转角是否要落点</summary>
        public bool NeedPointAtCorner { get; set; }

        /// <summary>转角检测夹角上限（度），超过该角度才视为转角</summary>
        public float IncludedAngle { get; set; }
    }
}
