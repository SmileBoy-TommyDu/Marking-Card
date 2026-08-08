//using ProtoBuf;

//namespace DrSoft.Drawing.DTO
//{
//    /// <summary>
//    /// 雕刻参数 DTO
//    /// </summary>
//    [ProtoContract]
//    public record EngravingParameterDto : MarkingParameterBaseDto
//    {
//        // 加工次数下拉框
//        [ProtoMember(1)]
//        public int ProcessingIndex { get; set; } = 0;

//        // 一般选项卡内容
//        [ProtoMember(2)]
//        public bool IsOutline { get; set; } = true;
//        [ProtoMember(3)]
//        public bool IsFill { get; set; } = false;
//        [ProtoMember(4)]
//        public bool IsFillPriority { get; set; } = true;
//        [ProtoMember(5)]
//        public double Speed { get; set; } = 800.0;
//        [ProtoMember(6)]
//        public double Power { get; set; } = 20.0;
//        [ProtoMember(7)]
//        public double Frequency { get; set; } = 20.0;

//        // 右侧参数
//        [ProtoMember(8)]
//        public int EngraveCount { get; set; } = 1;
//        [ProtoMember(9)]
//        public double DotEngraveTime { get; set; } = 0.1;

//        // --- 进阶 (Advanced) ---
//        [ProtoMember(10)]
//        public double EndPointDotTime { get; set; } = 0.000; // 雕刻终止点加点时间
//        [ProtoMember(11)]
//        public int SpeedModeIndex { get; set; } = 0;         // 速度模式 (0: 一般模式)

//        // --- 进阶2 (Advanced 2) ---
//        [ProtoMember(12)]
//        public int PrecisionFactor { get; set; } = 10000;    // 精度系数
//        [ProtoMember(13)]
//        public double OverlapRatio { get; set; } = 100.000;  // 重迭区比率
//        [ProtoMember(14)]
//        public bool IsOverlapEnabled { get; set; } = false;  // 是否启用比率调整
//    }
//}
