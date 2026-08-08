namespace DrSoft.MarkCard.Model
{
    public enum CopyMode {None, Matrix, Circular }

    // 矩阵复制参数
    public record MatrixCopyParameter : ParameterBase
    {
        public CopyMode Mode { get; set; } = CopyMode.None;

        #region 矩阵复制参数
        public int RowCount { get; init; } = 1;
        public double RowSpacing { get; init; } = 0.0000;
        public int ColumnCount { get; init; } = 1;
        public double ColumnSpacing { get; init; } = 0.0000;
        public int OrderIndex { get; init; } = 0; // 复制顺序图标索引
        #endregion

        #region 环状复制参数
        public double Radius { get; init; } = 0.0000;
        public int Count { get; init; } = 1;
        public double StartAngle { get; init; } = 0.0000;
        public double IntervalAngle { get; init; } = 0.0000;
        public bool IsAverageDistribute { get; init; } = false;
        public bool IsObjectRotate { get; init; } = false;
        public bool IsCounterClockwise { get; init; } = true;
        #endregion
    }
}
