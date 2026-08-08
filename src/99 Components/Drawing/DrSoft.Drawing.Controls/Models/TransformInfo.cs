using DrSoft.Drawing.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.Drawing.Controls.Models
{
    public class TransformInfo
    {
        public double RotationAngle { get; set; }
        public double RotationDegrees { get; set; }
        public (double X,double Y) Skew { get; set; }
        public Point2D Center { get; set; }
        public Point2D GeometricCenter { get; set; }
        public Point2D RotationCenter { get; set; }
        public (double Width, double Height) Dimensions { get; set; }
        public bool HasCustomRotationCenter { get; set; }
        public bool IsTransformed { get; set; }
        public Matrix3x2 TransformMatrix { get; set; }

        public override string ToString()
        {
            return $"旋转: {RotationDegrees:F2}°, " +
                   $"倾斜: ({Skew.X:F4}, {Skew.Y:F4}), " +
                   $"中心: ({Center.X:F2}, {Center.Y:F2}), " +
                   $"尺寸: {Dimensions.Width:F2}×{Dimensions.Height:F2}";
        }
    }
}
