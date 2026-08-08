using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Rougamo;
using Rougamo.Context;
using SkiaSharp;

namespace DrSoft.Drawing.Utility.AOP;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class LogAttribute : MoAttribute
{
    private static volatile bool _enable = false;

    public static bool IsEnabled => _enable;

    public static void Enable()
    {
        SetEnable(true);
    }

    public static void SetEnable(bool enable)
    {
        _enable = enable;
        WriteMessage(enable ? "[AOP-SYSTEM] 开启调试" : "[AOP-SYSTEM] 关闭调试", true);
    }

    public override void OnEntry(MethodContext context)
    {
        if (!_enable)
        {
            return;
        }

        var method = context.Method;
        var args = context.Arguments;
        
        var sb = new StringBuilder();
        sb.Append($"[AOP-ENTRY] {method.DeclaringType?.Name}.{method.Name}(");
        
        if (args != null && args.Length > 0)
        {
            var parameters = method.GetParameters();
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                var paramName = parameters.Length > i ? parameters[i].Name : $"arg{i}";
                sb.Append($"{paramName}={FormatArgument(args[i])}");
            }
        }
        
        sb.Append(")");
        WriteMessage(sb.ToString());
    }

    public override void OnSuccess(MethodContext context)
    {
        if (!_enable)
        {
            return;
        }
        
        var method = context.Method;
        var result = context.ReturnValue;
        
        WriteMessage($"[AOP-SUCCESS] {method.DeclaringType?.Name}.{method.Name}, 返回值: {FormatArgument(result)}");
    }

    public override void OnException(MethodContext context)
    {
        if (!_enable)
        {
            return;
        }
        
        var method = context.Method;
        var exception = context.Exception;
        
        WriteMessage($"[AOP-EXCEPTION] {method.DeclaringType?.Name}.{method.Name}: {exception?.GetType().Name} - {exception?.Message}");
    }

    public override void OnExit(MethodContext context)
    {
        if (!_enable)
        {
            return;
        }
        
        var method = context.Method;
        WriteMessage($"[AOP-EXIT] {method.DeclaringType?.Name}.{method.Name}");
    }

    private static void WriteMessage(string message, bool publishWhenDisabled = false)
    {
        var formattedMessage = $"[{DateTime.Now:HH:mm:ss.fff}][T{Environment.CurrentManagedThreadId}] {message}";

        Debug.WriteLine(formattedMessage);

        if (_enable || publishWhenDisabled)
        {
            DebugLogHub.Append(formattedMessage);
        }
    }

    private string FormatArgument(object? arg)
    {
        if (arg == null)
            return "null";

        var type = arg.GetType();
        
        if (type.IsValueType || type == typeof(string))
            return arg.ToString() ?? "null";

        if (arg is SKPoint point)
            return $"SKPoint({point.X:F2}, {point.Y:F2})";

        if (arg is SKPointI pointI)
            return $"SKPointI({pointI.X}, {pointI.Y})";

        if (arg is SKPoint3 point3)
            return $"SKPoint3({point3.X:F2}, {point3.Y:F2}, {point3.Z:F2})";

        if (arg is SKRect rect)
            return $"SKRect(L={rect.Left:F2}, T={rect.Top:F2}, R={rect.Right:F2}, B={rect.Bottom:F2})";

        if (arg is SKRectI rectI)
            return $"SKRectI(L={rectI.Left}, T={rectI.Top}, R={rectI.Right}, B={rectI.Bottom})";

        if (arg is SKRoundRect roundRect)
            return $"SKRoundRect(L={roundRect.Rect.Left:F2}, T={roundRect.Rect.Top:F2}, W={roundRect.Rect.Width:F2}, H={roundRect.Rect.Height:F2})";

        if (arg is SKSize size)
            return $"SKSize({size.Width:F2}, {size.Height:F2})";

        if (arg is SKSizeI sizeI)
            return $"SKSizeI({sizeI.Width}, {sizeI.Height})";

        if (arg is SKColor color)
            return $"SKColor(#{color.Alpha:X2}{color.Red:X2}{color.Green:X2}{color.Blue:X2}, A={color.Alpha}, R={color.Red}, G={color.Green}, B={color.Blue})";

        if (arg is SKColorF colorF)
            return $"SKColorF(R={colorF.Red:F2}, G={colorF.Green:F2}, B={colorF.Blue:F2}, A={colorF.Alpha:F2})";

        if (arg is SKMatrix matrix)
        {
            return $"SKMatrix({matrix.ToString()})";
        }

        if (arg is SKPath path)
            return $"SKPath(PointCount={path.PointCount}, IsEmpty={path.IsEmpty}, IsConvex={path.IsConvex}, FillType={path.FillType})";

        if (arg is SKPaint paint)
            return $"SKPaint(Color=#{paint.Color.Alpha:X2}{paint.Color.Red:X2}{paint.Color.Green:X2}{paint.Color.Blue:X2}, StrokeWidth={paint.StrokeWidth:F2}, " +
                   $"Style={paint.Style}, TextSize={paint.TextSize:F2})";

        if (arg is SKRegion region)
            return $"SKRegion(IsEmpty={region.IsEmpty}, Bounds={FormatArgument(region.Bounds)})";

        if (arg is SKImageInfo imageInfo)
            return $"SKImageInfo({imageInfo.Width}x{imageInfo.Height}, {imageInfo.ColorType}, {imageInfo.AlphaType})";

        if (arg is SKImage image)
            return $"SKImage({image.Width}x{image.Height})";

        if (arg is SKBitmap bitmap)
            return $"SKBitmap({bitmap.Width}x{bitmap.Height})";

        if (arg is SKCanvas canvas)
            return $"SKCanvas()";

        if (arg is SKShader shader)
            return $"SKShader()";

        if (arg is SKMaskFilter maskFilter)
            return $"SKMaskFilter()";

        if (arg is SKColorFilter colorFilter)
            return $"SKColorFilter()";

        if (arg is SKStrokeCap strokeCap)
            return $"SKStrokeCap.{strokeCap}";

        if (arg is SKStrokeJoin strokeJoin)
            return $"SKStrokeJoin.{strokeJoin}";

        if (arg is SKPaintStyle paintStyle)
            return $"SKPaintStyle.{paintStyle}";

        if (arg is SKColorType colorType)
            return $"SKColorType.{colorType}";

        if (arg is SKAlphaType alphaType)
            return $"SKAlphaType.{alphaType}";

        if (arg is SKPathFillType fillType)
            return $"SKPathFillType.{fillType}";

        if (arg is SKPathDirection pathDirection)
            return $"SKPathDirection.{pathDirection}";

        if (type.IsArray)
        {
            var array = (Array)arg;
            var elements = new StringBuilder("[");
            for (int i = 0; i < Math.Min(array.Length, 10); i++)
            {
                if (i > 0) elements.Append(", ");
                elements.Append(FormatArgument(array.GetValue(i)));
            }
            if (array.Length > 10) elements.Append(", ...");
            elements.Append("]");
            return elements.ToString();
        }

        if (arg is IEnumerable enumerable && arg is not string)
        {
            var elements = new StringBuilder("[");
            int count = 0;
            foreach (var item in enumerable)
            {
                if (count > 0) elements.Append(", ");
                elements.Append(FormatArgument(item));
                count++;
                if (count >= 10)
                {
                    elements.Append(", ...");
                    break;
                }
            }
            elements.Append("]");
            return elements.ToString();
        }

        try
        {
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            if (props.Length > 0)
            {
                var sb = new StringBuilder("{");
                int propCount = 0;
                foreach (var prop in props.Take(5))
                {
                    if (propCount > 0) sb.Append(", ");
                    try
                    {
                        var value = prop.GetValue(arg);
                        sb.Append($"{prop.Name}={FormatArgument(value)}");
                    }
                    catch
                    {
                        sb.Append($"{prop.Name}=?");
                    }
                    propCount++;
                }
                if (props.Length > 5) sb.Append(", ...");
                sb.Append("}");
                return sb.ToString();
            }
            
            return $"{{{type.Name}}}";
        }
        catch
        {
            return arg.ToString() ?? "object";
        }
    }
}
