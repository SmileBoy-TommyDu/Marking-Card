namespace DrSoft.MarkCard.Model
{
    public record EngravingParameter : ParameterBase, IMarkingParameter
    {
        // 加工次数下拉框
        public int ProcessingIndex { get; set; } = 0;

        // 一般选项卡内容
        public bool IsOutline { get; set; } = true;
        public bool IsFill { get; set; } = false;
        public bool IsFillPriority { get; set; } = true;
        public double Speed { get; set; } = 1;
        public double Power { get; set; } = 20.0;
        public double Frequency { get; set; } = 20.0;

        // 右侧参数
        public int EngraveCount { get; set; } = 1;
        public double DotEngraveTime { get; set; } = 0.1;

        // --- 进阶 (Advanced) ---
        public double EndPointDotTime { get; set; } = 0.000; // 雕刻终止点加点时间
        public int SpeedModeIndex { get; set; } = 0;         // 速度模式 (0: 一般模式)

        // --- 进阶2 (Advanced 2) ---
        public int PrecisionFactor { get; set; } = 10000;    // 精度系数
        public double OverlapRatio { get; set; } = 100.000;  // 重迭区比率
        public bool IsOverlapEnabled { get; set; } = false;  // 是否启用比率调整
    }
}
