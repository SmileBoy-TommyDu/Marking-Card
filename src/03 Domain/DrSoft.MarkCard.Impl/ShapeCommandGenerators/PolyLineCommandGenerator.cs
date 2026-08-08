using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using DrSoft.MarkCard.Interface;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.MarkCommand;
using System.Drawing;

namespace DrSoft.MarkCard.Impl.ShapeCommandGenerators
{
    public class PolyLineCommandGenerator : ShapeCommandGeneratorBase<IPolyLineShapeData>
    {
        public override ShapeType SupportedType => ShapeType.PolyLine;

        protected override IEnumerable<IMarkCommand> GenerateCore(IPolyLineShapeData draw, ProcessParam processParam, AdvancedFeatureParam? advancedFeatureParam,
            ref ProcessParam? currentProcessParam)
        {
            // 优先使用预计算的虚线段（CurveParameter.OutputAsDashed 生成）
            var dashSegments = draw.DashSegments;
            if (dashSegments != null && dashSegments.Count > 0)
            {
                return GenerateFromDashSegments(draw, dashSegments, processParam, advancedFeatureParam, ref currentProcessParam);
            }

            //if (draw.LineStyle == LineStyle.Dashed || draw.LineStyle == LineStyle.Dotted)
            //{
            //    return GenerateDashedPolyLineCommands(draw, processParam, draw.IsClosed, advancedFeatureParam, ref currentProcessParam);
            //}

            return GeneratePolyLineCommands(draw, processParam, draw.IsClosed, advancedFeatureParam, ref currentProcessParam);
        }

        /// <summary>
        /// 从预计算的 DashSegments 生成打标命令。
        /// 每条 DashSegment 为一个实线段，段间以 JumpCommand 断开。
        /// </summary>
        private static List<IMarkCommand> GenerateFromDashSegments(
            IPolyLineShapeData draw,
            IReadOnlyList<((float X, float Y) Start, (float X, float Y) End)> dashSegments,
            ProcessParam processParam,
            AdvancedFeatureParam? advancedFeatureParam,
            ref ProcessParam? currentProcessParam)
        {
            List<((float X, float Y) Start, (float X, float Y) End)> lines = dashSegments.ToList();
            //加工方向反转
            if (!draw.IsClockwise)
            {
                lines.Reverse();

                //头尾延伸也反转
                if(advancedFeatureParam != null)
                {
                    var runIn = advancedFeatureParam.RunInCompensationLength;
                    advancedFeatureParam.RunInCompensationLength = advancedFeatureParam.RunOutCompensationLength;
                    advancedFeatureParam.RunOutCompensationLength = runIn;
                }
            }
            var commands = EnsureParamCommands(processParam, ref currentProcessParam);

            // 入刀补偿
            PointF? firstOverride = null;
            if (advancedFeatureParam != null && Math.Abs(advancedFeatureParam.RunInCompensationLength) > 0.00001 && lines.Count > 0)
            {
                var seg = lines[0];
                var cv = GetCompensationVector(
                    new PointF(seg.Start.X, seg.Start.Y),
                    new PointF(seg.End.X, seg.End.Y),
                    advancedFeatureParam.RunInCompensationLength);
                firstOverride = new PointF(seg.Start.X + cv.X, seg.Start.Y + cv.Y);
            }

            // 出刀补偿
            PointF? lastOverride = null;
            if (advancedFeatureParam != null && Math.Abs(advancedFeatureParam.RunOutCompensationLength) > 0.00001 && lines.Count > 0)
            {
                var seg = lines[lines.Count - 1];
                var cv = GetCompensationVector(
                    new PointF(seg.Start.X, seg.Start.Y),
                    new PointF(seg.End.X, seg.End.Y),
                    advancedFeatureParam.RunOutCompensationLength);
                lastOverride = new PointF(seg.End.X + cv.X, seg.End.Y + cv.Y);
            }

            int repeatCount = Math.Max(1, processParam.RepeatCount);
            for (int rep = 0; rep < repeatCount; rep++)
            {
                //

                List<PointF> dashArray = new List<PointF>();

                for (int i = 0; i < lines.Count; i++)
                {
                    var (start, end) = lines[i];
                    var startPoint = (i == 0 && firstOverride.HasValue) ? firstOverride.Value : new PointF(start.X, start.Y);
                    var endPoint = (i == lines.Count - 1 && lastOverride.HasValue) ? lastOverride.Value : new PointF(end.X, end.Y);
                    dashArray.Add(startPoint);
                    dashArray.Add(endPoint);

                    //commands.Add(new JumpCommand { Point = startPoint });
                    //commands.Add(new MarkLineCommand { EndPoint = endPoint });

                    
                }

                commands.Add(new MarkDashedLineCommand { DashArray = dashArray });
            }

            return commands;
        }

        protected override bool ValidateCore(IPolyLineShapeData draw)
        {
            return draw.OutlinePoints != null && draw.OutlinePoints.Count >= 2;
        }
    }
}
