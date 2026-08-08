using System;

namespace DrSoft.Drawing.Controls.Tools;

internal static class UniformScaleHelper
{
    // 当角点拖拽同时带来 X/Y 两轴变化时，
    // 直接在 rawScaleX/rawScaleY 之间二选一会在主导轴切换时产生离散跳变。
    // 这里按原始宽高对两轴缩放做加权投影，得到连续的统一缩放量。
    public static float ResolveProjectedUniformScale(
        float rawScaleX,
        float rawScaleY,
        float originalWidth,
        float originalHeight)
    {
        var widthWeight = originalWidth * originalWidth;
        var heightWeight = originalHeight * originalHeight;
        var totalWeight = widthWeight + heightWeight;
        if (totalWeight <= 0.0001f)
        {
            return ResolveDominantScale(rawScaleX, rawScaleY);
        }

        var weightedScaleX = rawScaleX * widthWeight;
        var weightedScaleY = rawScaleY * heightWeight;
        var projectedScale = (weightedScaleX + weightedScaleY) / totalWeight;
        return projectedScale;
    }

    public static float ResolveDominantScale(float scaleX, float scaleY)
    {
        var widthDelta = Math.Abs(scaleX - 1f);
        var heightDelta = Math.Abs(scaleY - 1f);

        if (widthDelta >= heightDelta)
        {
            return scaleX;
        }

        return scaleY;
    }

    public static float ClampUniformScale(
        float scale,
        float originalWidth,
        float originalHeight,
        float minDimension)
    {
        var minimumWidthScale = originalWidth > 0.0001f
            ? minDimension / originalWidth
            : 1f;
        var minimumHeightScale = originalHeight > 0.0001f
            ? minDimension / originalHeight
            : 1f;
        var minimumScale = Math.Max(minimumWidthScale, minimumHeightScale);
        var clampedScale = Math.Max(scale, minimumScale);
        return clampedScale;
    }

    public static (float Width, float Height) ResolveUniformDimensions(
        float originalWidth,
        float originalHeight,
        float requestedWidth,
        float requestedHeight,
        float minDimension)
    {
        var rawScaleX = originalWidth > 0.0001f
            ? requestedWidth / originalWidth
            : 1f;
        var rawScaleY = originalHeight > 0.0001f
            ? requestedHeight / originalHeight
            : 1f;
        var dominantScale = ResolveProjectedUniformScale(
            rawScaleX,
            rawScaleY,
            originalWidth,
            originalHeight);
        var uniformScale = ClampUniformScale(
            dominantScale,
            originalWidth,
            originalHeight,
            minDimension);
        var resolvedWidth = Math.Max(minDimension, originalWidth * uniformScale);
        var resolvedHeight = Math.Max(minDimension, originalHeight * uniformScale);
        return (resolvedWidth, resolvedHeight);
    }
}
