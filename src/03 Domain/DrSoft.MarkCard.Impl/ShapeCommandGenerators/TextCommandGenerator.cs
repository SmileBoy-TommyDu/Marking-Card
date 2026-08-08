using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using DrSoft.MarkCard.Interface;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.MarkCommand;
using System.Drawing;

namespace DrSoft.MarkCard.Impl.ShapeCommandGenerators
{
    /// <summary>
    /// 文本图形命令生成器
    /// 文本对象的点集包含了文本的轮廓信息，轮廓之间用NaN分隔
    /// </summary>
    public class TextCommandGenerator : ShapeCommandGeneratorBase<ITextShapeData>
    {
        public override ShapeType SupportedType => ShapeType.Text;

        protected override IEnumerable<IMarkCommand> GenerateCore(ITextShapeData value, ProcessParam processParam, AdvancedFeatureParam? advancedFeatureParam,
            ref ProcessParam? currentProcessParam)
        {
            var commands = EnsureParamCommands(processParam, ref currentProcessParam);
            var pts = value.OutlinePoints;

            commands.Add(new JumpCommand { Point = new PointF(pts[0].X, pts[0].Y) });

            (float X, float Y) lastPoint = pts[0];
            bool isJump = false;

            for (int i = 1; i < pts.Count; i++)
            {
                var point = pts[i];
                if (float.IsNaN(point.X) || float.IsNaN(point.Y))
                {
                    isJump = true;
                    continue;
                }

                if (DistanceTo(point, lastPoint) > 0.001)
                {
                    if (isJump)
                    {
                        commands.Add(new JumpCommand { Point = new PointF(point.X, point.Y) });
                        isJump = false;
                    }
                    else
                    {
                        commands.Add(new MarkLineCommand { EndPoint = new PointF(point.X, point.Y) });
                    }
                    lastPoint = point;
                }
            }

            return commands;
        }

        protected override bool ValidateCore(ITextShapeData draw)
        {
            return draw.OutlinePoints != null && draw.OutlinePoints.Count >= 2;
        }
    }
}
