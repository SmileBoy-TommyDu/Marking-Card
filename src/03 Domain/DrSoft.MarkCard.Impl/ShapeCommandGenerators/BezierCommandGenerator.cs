using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using DrSoft.MarkCard.Interface;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.MarkCommand;
using System.Drawing;

namespace DrSoft.MarkCard.Impl.ShapeCommandGenerators
{
    public class BezierCommandGenerator : ShapeCommandGeneratorBase<IBezierShapeData>
    {
        public override ShapeType SupportedType => ShapeType.Bezier;

        protected override IEnumerable<IMarkCommand> GenerateCore(IBezierShapeData draw, ProcessParam processParam, AdvancedFeatureParam? advancedFeatureParam, ref ProcessParam? currentProcessParam)
        {
            var closed = draw.IsClosed;
            return GeneratePolyLineCommands(draw, processParam, draw.IsClosed, advancedFeatureParam, ref currentProcessParam);
        }

        protected override bool ValidateCore(IBezierShapeData draw)
        {
            return draw.OutlinePoints != null && draw.OutlinePoints.Count >= 2;
        }
    }
}
