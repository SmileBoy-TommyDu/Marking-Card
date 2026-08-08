using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using DrSoft.MarkCard.Interface;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.MarkCommand;
using System.Drawing;

namespace DrSoft.MarkCard.Impl.ShapeCommandGenerators
{
    /// <summary>
    /// 直线图形命令生成器
    /// </summary>
    public class LineCommandGenerator : ShapeCommandGeneratorBase<ILineShapeData>
    {
        public override ShapeType SupportedType => ShapeType.Line;

        protected override IEnumerable<IMarkCommand> GenerateCore(ILineShapeData draw, ProcessParam processParam, AdvancedFeatureParam? advancedFeatureParam, ref ProcessParam? currentProcessParam)
        {
            var commands = EnsureParamCommands(processParam, ref currentProcessParam);
            var pts = draw.OutlinePoints;

            PointF p0 = new PointF(pts[0].X, pts[0].Y);
            PointF p1 = new PointF(pts[1].X, pts[1].Y);

            if (!draw.IsClockwise)
            {
                p0 = new PointF(pts[1].X, pts[1].Y);
                p1 = new PointF(pts[0].X, pts[0].Y);
            }

            if (advancedFeatureParam != null)
            {
                if (Math.Abs(advancedFeatureParam.RunInCompensationLength) > 0.00001)
                {
                    var cv = GetCompensationVector(p0, p1, advancedFeatureParam.RunInCompensationLength);
                    p0 = new PointF(p0.X + cv.X, p0.Y + cv.Y);
                }
                if (Math.Abs(advancedFeatureParam.RunOutCompensationLength) > 0.00001)
                {
                    var cv = GetCompensationVector(p1, p0, advancedFeatureParam.RunOutCompensationLength);
                    p1 = new PointF(p1.X + cv.X, p1.Y + cv.Y);
                }
            }

            commands.Add(new JumpCommand { Point = p0 });
            for (int j = 0; j < Math.Max(1, processParam.RepeatCount); j++)
            {
                commands.Add(j % 2 == 0
                    ? new MarkLineCommand { EndPoint = p1 }
                    : new MarkLineCommand { EndPoint = p0 });
            }
            return commands;
        }

        protected override bool ValidateCore(ILineShapeData draw)
        {
            return draw.OutlinePoints != null && draw.OutlinePoints.Count >= 2;
        }
    }
}
