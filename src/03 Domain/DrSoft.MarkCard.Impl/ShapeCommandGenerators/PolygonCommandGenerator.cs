using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using DrSoft.MarkCard.Interface;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.MarkCommand;

namespace DrSoft.MarkCard.Impl.ShapeCommandGenerators
{
    public class PolygonCommandGenerator : ShapeCommandGeneratorBase<IPolygonShapeData>
    {
        public override ShapeType SupportedType => ShapeType.Polygon;

        protected override IEnumerable<IMarkCommand> GenerateCore(IPolygonShapeData draw, ProcessParam processParam, AdvancedFeatureParam? advancedFeatureParam,
            ref ProcessParam? currentProcessParam)
        {
            if (draw.OutlineStyle == OutlineStyle.Dashed || draw.OutlineStyle == OutlineStyle.Dotted)
            {
                return GenerateDashedPolyLineCommands(draw, processParam, true, advancedFeatureParam, ref currentProcessParam);
            }

            return GeneratePolyLineCommands(draw, processParam, true, advancedFeatureParam, ref currentProcessParam);
        }

        protected override bool ValidateCore(IPolygonShapeData draw)
        {
            return draw.OutlinePoints != null && draw.OutlinePoints.Count >= 3;
        }
    }
}
