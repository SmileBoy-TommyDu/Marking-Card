using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using DrSoft.MarkCard.Interface;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.MarkCommand;
using System.Drawing;

namespace DrSoft.MarkCard.Impl.ShapeCommandGenerators
{
    /// <summary>
    /// 矩形图形命令生成器（支持直角和圆角）
    /// </summary>
    public class RectangleCommandGenerator : ShapeCommandGeneratorBase<IRectangleShapeData>
    {
        public override ShapeType SupportedType => ShapeType.Rectangle;

        protected override IEnumerable<IMarkCommand> GenerateCore(IRectangleShapeData draw, ProcessParam processParam, AdvancedFeatureParam? advancedFeatureParam,
            ref ProcessParam? currentProcessParam)
        {
            if (draw.CornerRadiusTopLeft <= 1e-9f && draw.CornerRadiusTopRight <= 1e-9f
                && draw.CornerRadiusBottomLeft <= 1e-9f && draw.CornerRadiusBottomRight <= 1e-9f)
            {
                return GeneratePolyLineCommands(draw, processParam, true, advancedFeatureParam, ref currentProcessParam);
            }

            return GenerateRoundRectangle(draw, processParam, ref currentProcessParam);
        }

        private static IEnumerable<IMarkCommand> GenerateRoundRectangle(IRectangleShapeData rectangle, ProcessParam processParam,
            ref ProcessParam? currentProcessParam)
        {
            var commands = EnsureParamCommands(processParam, ref currentProcessParam);

            var pts = rectangle.OutlinePoints;
            if (pts == null || pts.Count < 4) return commands;

            var p0 = pts[0]; var p1 = pts[1]; var p2 = pts[2]; var p3 = pts[3];

            var radius = NormalizeCornerRadius(new List<float>
            {
                rectangle.CornerRadiusTopLeft,
                rectangle.CornerRadiusTopRight,
                rectangle.CornerRadiusBottomRight,
                rectangle.CornerRadiusBottomLeft
            });

            // ── 预约束：防止共边上相邻两角的倒角偏移量之和超出边长（CSS border-radius 缩放算法）──
            var corners = new (float X, float Y)[] { p0, p1, p2, p3 };
            double scaleFactor = 1.0;
            for (int i = 0; i < 4; i++)
            {
                int prev = (i + 3) % 4;
                int next = (i + 1) % 4;
                int nextNext = (i + 2) % 4;

                double edgeLen = Distance(corners[i], corners[next]);
                if (edgeLen < 1e-9) continue;

                // 角 i 在边 i→next 上消耗的偏移量
                double offsetI = ComputeCornerOffset(corners[prev], corners[i], corners[next], radius[i]);
                // 角 next 在边 i→next 上消耗的偏移量
                double offsetNext = ComputeCornerOffset(corners[i], corners[next], corners[nextNext], radius[next]);

                double total = offsetI + offsetNext;
                if (total > edgeLen && total > 1e-9)
                {
                    scaleFactor = Math.Min(scaleFactor, edgeLen / total);
                }
            }

            if (scaleFactor < 1.0)
            {
                for (int i = 0; i < 4; i++)
                {
                    radius[i] = (float)(radius[i] * scaleFactor);
                }
            }

            var c0 = BuildCornerFillet(p3, p0, p1, radius[0]);
            var c1 = BuildCornerFillet(p0, p1, p2, radius[1]);
            var c2 = BuildCornerFillet(p1, p2, p3, radius[2]);
            var c3 = BuildCornerFillet(p2, p3, p0, radius[3]);

            for (int i = 0; i < Math.Max(1, processParam.RepeatCount); i++)
            {
                // 起点：c0 圆弧终点（与直角矩形起点一致，位于 p0 附近）
                commands.Add(new JumpCommand { Point = ToPointF(c0.End) });
                // c0.End → c1.Start：沿 p0→p1 边
                if (Distance(c0.End, c1.Start) > 1e-6)
                    commands.Add(new MarkLineCommand { EndPoint = ToPointF(c1.Start) });
                AddCornerArc(commands, c1);
                // c1.End → c2.Start：沿 p1→p2 边
                if (Distance(c1.End, c2.Start) > 1e-6)
                    commands.Add(new MarkLineCommand { EndPoint = ToPointF(c2.Start) });
                AddCornerArc(commands, c2);
                // c2.End → c3.Start：沿 p2→p3 边
                if (Distance(c2.End, c3.Start) > 1e-6)
                    commands.Add(new MarkLineCommand { EndPoint = ToPointF(c3.Start) });
                AddCornerArc(commands, c3);
                // c3.End → c0.Start：沿 p3→p0 边
                if (Distance(c3.End, c0.Start) > 1e-6)
                    commands.Add(new MarkLineCommand { EndPoint = ToPointF(c0.Start) });
                AddCornerArc(commands, c0);
            }

            return commands;
        }

        /// <summary>
        /// 计算角 current 在边 current→next 上消耗的偏移长度
        /// </summary>
        private static double ComputeCornerOffset((float X, float Y) prev, (float X, float Y) current, (float X, float Y) next, float cornerRadius)
        {
            if (cornerRadius <= 1e-9f) return 0;

            var prevVec = Normalize(prev.X - current.X, prev.Y - current.Y);
            var nextVec = Normalize(next.X - current.X, next.Y - current.Y);

            double dot = Clamp(prevVec.X * nextVec.X + prevVec.Y * nextVec.Y, -1.0, 1.0);
            double theta = Math.Acos(dot);

            if (theta < 1e-6 || Math.Abs(Math.PI - theta) < 1e-6) return 0;

            double tanHalf = Math.Tan(theta / 2.0);
            if (tanHalf < 1e-9) return 0;

            return cornerRadius / tanHalf;
        }

        protected override bool ValidateCore(IRectangleShapeData draw)
        {
            return draw.OutlinePoints != null && draw.OutlinePoints.Count >= 4;
        }
    }
}
