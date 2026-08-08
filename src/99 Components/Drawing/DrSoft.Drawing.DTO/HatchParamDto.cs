

namespace DrSoft.Drawing.DTO
{
    /// <summary>
    /// 填满参数 DTO
    /// </summary>

    public record HatchParamDto
    {
        // --- 顶部基础属性 ---
    
        public string OutlineColor { get; set; } = "#000000"; // 十六进制颜色

        public string FillColor { get; set; } = "#000000";
   
        public int OutlineStyleIndex { get; set; } = 0;
    
        public int FillStyleIndex { get; set; } = 0;

        // --- 填满参数 (左侧列) ---
   
        public double Margin { get; set; } = 0.00000;      // 边距
      
        public double RingSpacing { get; set; } = 0.08000;  // 圈距
    
        public double LineSpacing { get; set; } = 0.08000;  // 间距

        public int Count { get; set; } = 1;                 // 次数
    
        public double StartAngle { get; set; } = 0.00000;   // 起始角度

        public double IncrementalAngle { get; set; } = 0.00000; // 累进角度

        public double Extension { get; set; } = 0.0000;     // 延伸

        // --- 填满参数 (右侧选项) ---
    
        public int FillTypeIndex { get; set; } = 0;         // 形式 (S型/弓字型等)
     
        public bool AverageDistribute { get; set; } = false; // 平均分配
   
        public int InternalRings { get; set; } = 0;         // 内圈数
     
        public int DirectionTypeIndex { get; set; } = 0;    // 方向 (向内/向外等)

        public bool RelativeToAngle { get; set; } = false;  // 相对物件角度
    
        public bool ReverseFillLine { get; set; } = false;  // 填满线反向
    }
}
