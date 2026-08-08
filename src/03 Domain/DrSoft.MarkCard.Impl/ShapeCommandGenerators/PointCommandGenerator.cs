using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using DrSoft.MarkCard.Interface;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.MarkCommand;
using System.Drawing;

namespace DrSoft.MarkCard.Impl.ShapeCommandGenerators
{
    public class PointCommandGenerator : ShapeCommandGeneratorBase<IDotShapeData>
    {
        public override ShapeType SupportedType => ShapeType.Point;

        protected override IEnumerable<IMarkCommand> GenerateCore(IDotShapeData draw, ProcessParam processParam, AdvancedFeatureParam? advancedFeatureParam,
            ref ProcessParam? currentProcessParam)
        {
            var commands = EnsureParamCommands(processParam, ref currentProcessParam);
            var pts = draw.OutlinePoints;
            var point = new PointF(pts[0].X, pts[0].Y);

            commands.Add(new JumpCommand { Point = point });
            for (int j = 0; j < Math.Max(1, processParam.RepeatCount); j++)
                commands.Add(new MarkPointCommand { Point = point, DotDuration = processParam.DotDuration });

            return commands;
        }

        protected override bool ValidateCore(IDotShapeData draw)
        {
            return draw.OutlinePoints != null && draw.OutlinePoints.Count > 0;
        }
    }
}
