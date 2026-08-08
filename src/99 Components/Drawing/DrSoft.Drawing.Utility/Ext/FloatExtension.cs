namespace DrSoft.Drawing.Utility;

/// <summary>
/// 浮点数扩展方法，提供带容差的比较操作
/// 适用于 WPF + SkiaSharp 打标软件等需要处理浮点精度的场景
/// </summary>
public static class FloatExtension
{
    /// <summary> 默认比较容差：0.001（千分之一像素）</summary>
    private const float DefaultTolerance = 0.00011f;
    
    /// <summary> 判断两个浮点数是否相等（在容差范围内视为相等）</summary>
    public static bool Eq(this float a, float b, float tol = DefaultTolerance)
    {
        if (float.IsNaN(a) || float.IsNaN(b))
            return float.IsNaN(a) && float.IsNaN(b);
        if (float.IsInfinity(a) || float.IsInfinity(b))
            return a == b;
        return MathF.Abs(a - b) < tol;
    }
    
    /// <summary> 判断 a 是否严格大于 b（差值必须超过容差）</summary>
    public static bool Gt(this float a, float b, float tol = DefaultTolerance)
        => a - b > tol;
    
    /// <summary> 判断 a 是否大于或等于 b（在容差范围内视为相等）</summary>
    public static bool Gte(this float a, float b, float tol = DefaultTolerance)
        => a - b >= -tol;
    
    /// <summary> 判断 a 是否严格小于 b（差值必须超过容差）</summary>
    public static bool Lt(this float a, float b, float tol = DefaultTolerance)
        => b - a > tol;
    
    /// <summary> 判断 a 是否小于或等于 b（在容差范围内视为相等）</summary>
    public static bool Lte(this float a, float b, float tol = DefaultTolerance)
        => b - a >= -tol;
    
    /// <summary> 判断浮点数是否等于0（在容差范围内视为相等）</summary>
    public static bool IsZero(this float a, float tol = DefaultTolerance)
        => MathF.Abs(a) < tol;
    
    /// <summary> 判断浮点数是否不等于0（差值超过容差视为不等）</summary>
    public static bool IsNotZero(this float a, float tol = DefaultTolerance)
        => MathF.Abs(a) >= tol;

    /// <summary> 判断浮点数是否等于1（在容差范围内视为相等）</summary>
    public static bool IsOne(this float a, float tol = DefaultTolerance)
        => a.Eq(1f, tol);

    /// <summary> 判断浮点数是否不等于1（差值超过容差视为不等）</summary>
    public static bool IsNotOne(this float a, float tol = DefaultTolerance)
        => !a.IsOne(tol);
}
