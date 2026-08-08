using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using DrSoft.MarkCard.Interface;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.MarkCommand;
using System.Drawing;

namespace DrSoft.MarkCard.Impl.ShapeCommandGenerators
{
    /// <summary>
    /// 圆形/渿圆形命令生成器
    /// </summary>
    public class CircleCommandGenerator : ShapeCommandGeneratorBase<ICircleShapeData>
    {
        public override ShapeType SupportedType => ShapeType.Circle;

        protected override IEnumerable<IMarkCommand> GenerateCore(ICircleShapeData drawCircle, ProcessParam processParam, AdvancedFeatureParam? advancedFeatureParam,
            ref ProcessParam? currentProcessParam)
        {
            return drawCircle.IsEllipse
                ? GenerateEllipse(drawCircle, processParam, ref currentProcessParam)
                : GenerateCircle(drawCircle, processParam, ref currentProcessParam);
        }

        private static IEnumerable<IMarkCommand> GenerateCircle(ICircleShapeData drawCircle, ProcessParam processParam,
            ref ProcessParam? currentProcessParam)
        {
            var commands = EnsureParamCommands(processParam, ref currentProcessParam);

            float cx = drawCircle.CenterX;
            float cy = drawCircle.CenterY;
            float radius = drawCircle.RadiusX;

            commands.Add(new JumpCommand { Point = new PointF(cx + radius, cy) });

            for (int j = 0; j < Math.Max(1, processParam.RepeatCount); j++)
            {
                var c = new MarkCircleCommand
                {
                    StartPoint = new PointF(cx + radius, cy),
                    Center = new PointF(cx, cy),
                    Radius = radius,
                };
                c.Angle = drawCircle.IsClockwise ? -360 : 360;
                commands.Add(c);
            }

            return commands;
        }

        private static IEnumerable<IMarkCommand> GenerateEllipse(ICircleShapeData drawEllipse, ProcessParam processParam,
            ref ProcessParam? currentProcessParam)
        {
            var commands = EnsureParamCommands(processParam, ref currentProcessParam);

            float cx = drawEllipse.CenterX;
            float cy = drawEllipse.CenterY;

            //上游传递的角度是反的            
            float alpha = -drawEllipse.Rotation * (float)(Math.PI / 180);

            // 计算加工起点：椭圆长轴端点经旋转后的世界坐标
            float a = drawEllipse.RadiusX;
            float startX = cx + a * (float)Math.Cos(alpha);
            float startY = cy + a * (float)Math.Sin(alpha);

            commands.Add(new JumpCommand { Point = new PointF(startX, startY) });

            for (int j = 0; j < Math.Max(1, processParam.RepeatCount); j++)
            {
                commands.Add(new MarkEllipseCommand
                {
                    Center = new PointF(cx, cy),
                    MajorRadius = drawEllipse.RadiusX,
                    MinorRadius = drawEllipse.RadiusY,
                    Alpha = -drawEllipse.Rotation
                });
            }

            return commands;
        }

        protected override bool ValidateCore(ICircleShapeData draw)
        {
            return true;
        }
    }
}
