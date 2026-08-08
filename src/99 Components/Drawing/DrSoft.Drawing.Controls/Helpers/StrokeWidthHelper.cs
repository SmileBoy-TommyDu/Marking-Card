using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Model;
using DrSoft.Drawing.Utility;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.Helpers;

/// <summary>
/// 统一管理“视觉恒定线宽”的计算。
/// 注意这里有两种不同语义：
/// 1. 仅补偿 viewport 缩放：
///    适用于“先把路径变到世界坐标，再描边”的 renderer。
///    这类 renderer 中，对象自己的 rotation/scale/skew 已经只作用在几何上，
///    不会再次作用到描边宽度，因此不能再额外补对象矩阵。
/// 2. 同时补偿 viewport 和对象矩阵缩放：
///    适用于“canvas.Concat(matrix) 后直接描局部路径”的 renderer。
///    这类 renderer 中，对象矩阵仍会继续作用到描边宽度，所以需要再抵消一次对象缩放。
///
/// 如果对象存在 skew，优先考虑把 renderer 改成“world-path 描边”而不是继续堆补偿公式。
/// 仅靠一个 StrokeWidth 标量无法完整抵消 skew 对描边形态的影响。
/// </summary>
internal static class StrokeWidthHelper
{
    private const float BaseStrokeWidthScale = 6.83f;
    private const float MinimumObjectScale = 0.001f;

    /// <summary>
    /// 只抵消 viewport 缩放，保留对象几何本身的世界坐标结果。
    /// 适用于 renderer 先生成 world path、再直接描边的场景。
    /// </summary>
    internal static float ResolveViewportInvariantStrokeWidth(DrawObject drawObject, IViewport viewport)
    {
        float baseStrokeWidth = drawObject.Pen.StrokeWidth * BaseStrokeWidthScale / viewport.Scale;
        return baseStrokeWidth;
    }

    /// <summary>
    /// 同时抵消 viewport 缩放和对象矩阵带来的额外缩放。
    /// 仅适用于 renderer 仍然通过 canvas.Concat(matrix) 在局部坐标中描边的场景。
    /// 如果该 renderer 已经改成 world-path 描边，不应再调用这个方法。
    /// </summary>
    internal static float ResolveScreenInvariantStrokeWidth(DrawObject drawObject, IViewport viewport)
    {
        float baseStrokeWidth = ResolveViewportInvariantStrokeWidth(drawObject, viewport);
        float objectScale = ResolveObjectStrokeScale(drawObject.GetTransformMatrix());
        float safeObjectScale = MathF.Max(objectScale, MinimumObjectScale);
        float adjustedStrokeWidth = baseStrokeWidth / safeObjectScale;
        return adjustedStrokeWidth;
    }

    /// <summary>
    /// 从矩阵线性部分提取“面积缩放”。
    /// 这里刻意不用列向量长度：
    /// - 列长度会把纯 skew 也误判成整体缩放
    /// - determinant 才能区分“真正缩放了面积”和“只是改变了方向”
    /// </summary>
    internal static float ResolveObjectStrokeScale(SKMatrix matrix)
    {
        bool isIdentityLinearPart = matrix.ScaleX.Eq(1f) &&
                                    matrix.ScaleY.Eq(1f) &&
                                    matrix.SkewX.Eq(0f) &&
                                    matrix.SkewY.Eq(0f);
        if (isIdentityLinearPart)
        {
            return 1f;
        }

        bool hasNoSkew = matrix.SkewX.Eq(0f) && matrix.SkewY.Eq(0f);
        bool isUniformScale = matrix.ScaleX.Eq(matrix.ScaleY);
        if (hasNoSkew && isUniformScale)
        {
            float uniformScale = MathF.Abs(matrix.ScaleX);
            return uniformScale;
        }

        float determinant = matrix.ScaleX * matrix.ScaleY - matrix.SkewX * matrix.SkewY;
        float areaScale = MathF.Sqrt(MathF.Abs(determinant));

        if (float.IsNaN(areaScale) || float.IsInfinity(areaScale))
        {
            return 1f;
        }

        return areaScale;
    }
}
