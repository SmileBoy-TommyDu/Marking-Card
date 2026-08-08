using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using DrSoft.MarkCard.Interface;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.MarkCommand;
using System.Drawing;

namespace DrSoft.MarkCard.Impl.ShapeCommandGenerators
{
    /// <summary>
    /// 圆弧图形命令生成器
    /// </summary>
    public class ArcCommandGenerator : ShapeCommandGeneratorBase<IArcShapeData>
    {
        public override ShapeType SupportedType => ShapeType.Arc;

        protected override IEnumerable<IMarkCommand> GenerateCore(IArcShapeData arc, ProcessParam processParam, AdvancedFeatureParam? advancedFeatureParam, ref ProcessParam? currentProcessParam)
        {
            var commands = EnsureParamCommands(processParam, ref currentProcessParam);

            float sweepAngle = arc.SweepAngle;
            var pts = arc.OutlinePoints;

            if (Math.Abs(sweepAngle) <= 1e-9f || pts.Count == 0)
                return commands;

            float radiusX = arc.RadiusX;
            float radiusY = arc.RadiusY;
            bool isEllipse = Math.Abs(radiusX - radiusY) > 1e-6f;

            for (int i = 0; i < Math.Max(1, processParam.RepeatCount); i++)
            {
                commands.Add(new JumpCommand { Point = new PointF(pts[0].X, pts[0].Y) });

                if (isEllipse)
                {
                    commands.Add(new MarkEllipseCommand
                    {
                        Center = new PointF(arc.CircumcircleCenterX, arc.CircumcircleCenterY),
                        MajorRadius = radiusX,
                        MinorRadius = radiusY,
                        Alpha = -arc.Rotation,
                        StartAngle = arc.StartAngle,
                        SweepAngle = sweepAngle
                    });
                }
                else
                {
                    commands.Add(new MarkCircleCommand
                    {
                        Center = new PointF(arc.CircumcircleCenterX, arc.CircumcircleCenterY),
                        Radius = radiusX,
                        Angle = sweepAngle
                    });
                }
            }

            return commands;
        }

        protected override bool ValidateCore(IArcShapeData draw)
        {
            return draw.OutlinePoints != null && draw.OutlinePoints.Count > 0;
        }
    }
}
