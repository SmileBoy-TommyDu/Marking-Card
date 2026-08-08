using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.Drawing.Controls.Helpers
{
    internal static class SKPointExtension
    {
        /// <summary>
        /// 判断2个点近似相等
        /// </summary>
        /// <param name="first"></param>
        /// <param name="second"></param>
        /// <returns></returns>
        public static bool ArePointsClose(this SKPoint first, params SKPoint[] points)
        {
            const float epsilon = 1e-4f;
            float epsilonSquared = epsilon * epsilon;

            foreach (var second in points)
            {

                float dx = first.X - second.X;
                float dy = first.Y - second.Y;
                float distanceSquared = dx * dx + dy * dy;

                var areClose = distanceSquared <= epsilonSquared;

                if (areClose)
                {
                    return true;
                }
            }

            return false;
        }

    }
}
