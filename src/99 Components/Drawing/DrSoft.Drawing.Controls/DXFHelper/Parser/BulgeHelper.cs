using System;
using System.Runtime.CompilerServices;

namespace DrSoft.Drawing.Controls.DXFHelper.Parser
{
    /// <summary>
    /// LWPOLYLINE Bulge → Arc 几何推导。
    ///
    ///   bulge = tan(θ/4)，θ 为圆心角（有符号：正=逆时针，负=顺时针）
    ///   半径   r = chord * (1 + b²) / (4|b|)
    ///   弦中点到圆心距离 = chord * (1 - b²) / (4|b|)
    ///   圆心在弦左侧（b>0）或右侧（b<0）
    /// </summary>
    internal static class BulgeHelper
    {
        private const double Tol = 1e-12;

        /// <summary>
        /// 将 (p1→p2, bulge) 转为 DxfArc。
        /// 返回 false 表示退化（两点重合）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryMakeArc(
            double x1, double y1,
            double x2, double y2,
            double bulge,
            string layer, string handle,
            out DxfArc arc)
        {
            double dx = x2 - x1, dy = y2 - y1;
            double chord2 = dx * dx + dy * dy;

            if (chord2 < Tol) { arc = null!; return false; }

            double chord = Math.Sqrt(chord2);
            double ab    = Math.Abs(bulge);
            double b2    = bulge * bulge;

            double r            = chord * (1.0 + b2) / (4.0 * ab);
            double distToCenter = chord * (1.0 - b2) / (4.0 * ab);

            // 垂直弦的单位向量（逆时针 90°）
            double perpX = -dy / chord;
            double perpY =  dx / chord;

            double sign = bulge > 0 ? 1.0 : -1.0;
            double cx   = (x1 + x2) * 0.5 + sign * distToCenter * perpX;
            double cy   = (y1 + y2) * 0.5 + sign * distToCenter * perpY;

            double sa = NormDeg(Math.Atan2(y1 - cy, x1 - cx) * (180.0 / Math.PI));
            double ea = NormDeg(Math.Atan2(y2 - cy, x2 - cx) * (180.0 / Math.PI));

            // 顺时针弧：交换，保持 DXF 逆时针存储约定
            if (bulge < 0) { (sa, ea) = (ea, sa); }

            arc = new DxfArc
            {
                Layer      = layer,
                Handle     = handle,
                Cx = cx, Cy = cy, R = r,
                StartAngle = sa,
                EndAngle   = ea,
                ExactStartX = x1,
                ExactStartY = y1,
                ExactEndX   = x2,
                ExactEndY   = y2
            };
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double NormDeg(double d)
        {
            d %= 360.0;
            return d < 0 ? d + 360.0 : d;
        }
    }
}
