using SkiaSharp;
using System.Numerics;

namespace DrSoft.Drawing.Model
{
    public interface ITransformService
    {
        void Translate(float dx, float dy, bool commit = false);
        /// <summary>
        /// 世界坐标缩放。directionRad 为缩放方向角（世界坐标，弧度）：
        /// 0 表示沿世界 X/Y 轴缩放；传入 OBB 方向角时沿 OBB 的 X/Y 方向缩放（保形）。
        /// </summary>
        void Scale(float scaleX, float scaleY, SKPoint anchor, float directionRad = 0f, bool commit = false);
        void Rotate(float deltaAngle, SKPoint center, bool commit = false);
        void Skew(float skewX, float skewY, SKPoint anchor, bool commit = false);
    }

    public interface IBoundable
    {
        (SKPoint[] Corners, SKPoint Center) GetAABB2();
        (SKPoint[] Corners, SKPoint Center) GetOBB();

        (SKPoint[] Corners, SKPoint Center) GetPreviewAABB();
        (SKPoint[] Corners, SKPoint Center) GetPreviewOBB();
    }

    public static class BoundingExtensions
    {
        /// <summary>
        /// 计算一组图形的合并 AABB 包围圈
        /// </summary>
        /// <param name="geometries">实现了 IBoundable 的图形集合</param>
        /// <returns>合并后的 SKRect；集合为空或没有任何有效角点时返回 SKRect.Empty</returns>
        public static SKRect GetUnionAABB(this IEnumerable<IBoundable> geometries)
            => GetUnionRect(geometries, static item => item.GetAABB2().Corners);

        public static (SKPoint[] Corners, SKPoint Center) GetUnionOBB(this IEnumerable<IBoundable> geometries)
        {
            if (geometries.Count() == 1)
            {
                return geometries.FirstOrDefault()!.GetOBB();
            }
            else
            {
                return geometries.GetUnionAABB().CreateBoundsGeometry();
            }
        }

        /// <summary>
        /// 计算一组图形的合并 预览AABB 包围圈
        /// </summary>
        /// <param name="geometries">实现了 IBoundable 的图形集合</param>
        /// <returns>合并后的 SKRect；集合为空或没有任何有效角点时返回 SKRect.Empty</returns>
        public static SKRect GetUnionPreviewAABB(this IEnumerable<IBoundable> geometries)
            => GetUnionRect(geometries, static item => item.GetPreviewAABB().Corners);
        public static (SKPoint[] Corners, SKPoint Center) GetUnionPreviewOBB(this IEnumerable<IBoundable> geometries)
        {
            if (geometries.Count() == 1)
            {
                return geometries.FirstOrDefault()!.GetPreviewOBB();
            }
            else
            {
                return geometries.GetUnionPreviewAABB().CreateBoundsGeometry();
            }
        }

        /// <summary>两个 Union 方法共用的合并逻辑，corners 的取法由调用方通过 selector 指定。</summary>
        private static SKRect GetUnionRect(
            IEnumerable<IBoundable> geometries,
            Func<IBoundable, SKPoint[]?> selector)
        {
            if (geometries == null) return SKRect.Empty;

            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            bool hasData = false;

            foreach (var item in geometries)
            {
                if (item == null) continue;

                var cornerArray = selector(item);
                if (cornerArray == null || cornerArray.Length == 0) continue;

                hasData = true;
                ReadOnlySpan<SKPoint> corners = cornerArray;

                for (int i = 0; i < corners.Length; i++)
                {
                    ref readonly var pt = ref corners[i];
                    if (pt.X < minX) minX = pt.X;
                    if (pt.X > maxX) maxX = pt.X;
                    if (pt.Y < minY) minY = pt.Y;
                    if (pt.Y > maxY) maxY = pt.Y;
                }
            }

            return hasData ? new SKRect(minX, minY, maxX, maxY) : SKRect.Empty;
        }

        public static SKPoint[] ToCorners(this SKRect rect)
        {
            return new SKPoint[]
            {
                new SKPoint(rect.Left, rect.Top),     // 左上
                new SKPoint(rect.Right, rect.Top),    // 右上
                new SKPoint(rect.Right, rect.Bottom), // 右下
                new SKPoint(rect.Left, rect.Bottom)   // 左下
            };
        }

        public static SKRect ToRect(this SKPoint[] corners)
        {
            if (corners == null || corners.Length == 0) return SKRect.Empty;
            float left = corners.Min(o => o.X);
            float right = corners.Max(o => o.X);
            float top = corners.Min(o => o.Y);
            float bottom = corners.Max(o => o.Y);
            return new SKRect(left, top, right, bottom);
        }

        public static SKPoint Center(this SKRect rect) => new(rect.MidX, rect.MidY);

        public static SKPoint[] ToOffsetCorners(this SKPoint[] corners, float offset)
        {

            // offset 在世界坐标中沿 OBB 方向扩展，不参与缩放变换。
            // 这样缩放时图形边缘固定，选择框边缘 = 图形边缘 ± 固定 offset，两者都卯住。
            if (offset != 0)
            {
                float w = SKPoint.Distance(corners[0], corners[1]);
                float h = SKPoint.Distance(corners[0], corners[3]);
                if (w > 0.001f && h > 0.001f)
                {
                    var dirR = new SKPoint((corners[1].X - corners[0].X) / w, (corners[1].Y - corners[0].Y) / w);
                    var dirD = new SKPoint((corners[3].X - corners[0].X) / h, (corners[3].Y - corners[0].Y) / h);
                    corners[0] = new SKPoint(corners[0].X - dirR.X * offset - dirD.X * offset, corners[0].Y - dirR.Y * offset - dirD.Y * offset);
                    corners[1] = new SKPoint(corners[1].X + dirR.X * offset - dirD.X * offset, corners[1].Y + dirR.Y * offset - dirD.Y * offset);
                    corners[2] = new SKPoint(corners[2].X + dirR.X * offset + dirD.X * offset, corners[2].Y + dirR.Y * offset + dirD.Y * offset);
                    corners[3] = new SKPoint(corners[3].X - dirR.X * offset + dirD.X * offset, corners[3].Y - dirR.Y * offset + dirD.Y * offset);
                }
            }
            return corners;
        }


        public static (SKPoint[] Corners, SKPoint Center) CreateBoundsGeometry(this SKRect bounds)
        {
            if (bounds.IsEmpty)
                return (Array.Empty<SKPoint>(), SKPoint.Empty);

            var corners = bounds.ToCorners();
            var center = new SKPoint(bounds.MidX, bounds.MidY);
            return (corners, center);
        }

        public static SKPoint[] CloneEx(this SKPoint[] points)
        {
            List<SKPoint> sKPoints = new List<SKPoint>();
            if (points == null || points.Length == 0) return sKPoints.ToArray();

            foreach (var p in points)
            {
                SKPoint point = new SKPoint(p.X, p.Y);
                sKPoints.Add(point);
            }

            return sKPoints.ToArray();
        }
    }


    public static class MultiTransformExtensions
    {
        public static void ApplyMirror(this IEnumerable<IShape> geometries, bool isHorizontal, SKPoint anchor, bool commit = true)
        {
            foreach (var child in geometries)
            {
                child.ApplyMirror(isHorizontal, anchor, commit: commit);
            }
        }

        public static void ApplyTranslate(this IEnumerable<IShape> geometries, float dx, float dy, bool commit = true)
        {
            foreach (var child in geometries)
            {
                child.Translate(dx, dy, commit: commit);
            }
        }

        public static void ApplyScale(this IEnumerable<IShape> geometries, float scaleX, float scaleY, SKPoint anchor, float directionRad = 0f, bool commit = true)
        {
            foreach (var child in geometries)
            {
                child.Scale(scaleX, scaleY, anchor, child.GetWorldRotationRad(), commit);
            }
        }

        public static void ApplyRotation(this IEnumerable<IShape> geometries, float deltaAngle, SKPoint center, bool commit = true)
        {
            foreach (var child in geometries)
            {
                child.Rotate(deltaAngle, center, commit);
            }
        }
    }
}
