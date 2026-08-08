using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DrSoft.Drawing.Model;
using DrSoft.MarkCard.Interface;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.MarkCommand;
using System.Drawing;

namespace DrSoft.MarkCard.Impl.ShapeCommandGenerators
{
    public class ArbitraryCurveCommandGenerator : ShapeCommandGeneratorBase<IArbitraryCurveShapeData>
    {
        public override ShapeType SupportedType => ShapeType.ArbitraryCurve;

        protected override IEnumerable<IMarkCommand> GenerateCore(IArbitraryCurveShapeData draw, ProcessParam processParam, AdvancedFeatureParam? advancedFeatureParam, ref ProcessParam? currentProcessParam)
        {
            return GeneratePolyLineCommands(draw, processParam, draw.IsClosed, advancedFeatureParam, ref currentProcessParam);
        }

     

        protected override bool ValidateCore(IArbitraryCurveShapeData draw)
        {
            return draw.OutlinePoints != null && draw.OutlinePoints.Count >= 2;
        }

      
    }
}
