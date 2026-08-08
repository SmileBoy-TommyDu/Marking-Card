using DrSoft.Drawing.Model;

namespace DrSoft.MarkCard.Model.DTO
{
    public class MarkingJobDto
    {
        /// <summary>
        /// 加工次数，默认为1
        /// </summary>
        public int ProcessTimes { get; set; } = 1;

        // 打标任务图形数据（直接使用只读接口，零拷贝，无需 DTO 转换）
        public IReadOnlyList<IShapeData> Shapes { get; set; }

        // 图形ID -> 最终生效的加工参数 (ProcessParam)
        public Dictionary<int, ProcessParam> ParameterMap { get; set; }

        // 图形ID -> 参数 (AdvancedFeatureParam)
        public Dictionary<int, AdvancedFeatureParam> AdvancedFeatureParamMap { get; set; }
    }
}
