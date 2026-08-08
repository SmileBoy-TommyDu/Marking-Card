namespace DrSoft.MarkCard.Model
{
    /// <summary>
    /// 延迟与位移参数实体
    /// </summary>
    public record DelayParameter : ParameterBase, IMarkingParameter
    {
        // --- 雕刻延迟参数 ---
        public double StartDelay { get; set; } = 0.200;  // 起始点延迟
        public double CornerDelay { get; set; } = 0.100; // 转角延迟
        public double EndDelay { get; set; } = 0.300;    // 终止点延迟
        public double EngraveDelay { get; set; } = 0.100; // 雕刻延迟

        // --- 位移参数 ---
        public double JumpSpeed { get; set; } = 1.2;  // 位移速度
        public double JumpDelay { get; set; } = 0.2;   // 位移延迟
    }
}
