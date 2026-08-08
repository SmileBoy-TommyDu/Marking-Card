using System.Globalization;
using System.Text.RegularExpressions;

namespace DrSoft.Drawing.Utility;

/// <summary>
/// 提供类型扩展方法
/// </summary>
public static class TypeExtensions
{
    /// <summary>
    /// 字符串转decimal
    /// </summary>
    /// <param name="str">字符串</param>
    /// <param name="defaultVal">默认值</param>
    /// <returns></returns>
    public static decimal AsDecimal(this string str, decimal defaultVal = 0)
    {
        decimal d;
        return decimal.TryParse(str, out d) ? d : defaultVal;
    }

    /// <summary>
    /// ToDecimal，失败返回默认值
    /// </summary>
    /// <param name="value"></param>
    /// <param name="defaultValue"></param>
    /// <returns></returns>
    public static decimal AsDecimal(this decimal? value, decimal defaultValue = 0)
    {
        return value == null ? defaultValue : value.Value;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="str"></param>
    /// <returns></returns>
    public static decimal? AsNullableDecimal(this string str)
    {
        if (string.IsNullOrEmpty(str))
        {
            return null;
        }

        return str.AsDecimal();
    }

    /// <summary>
    /// 字符串转int
    /// </summary>
    /// <param name="str">字符串</param>
    /// <param name="defaultVal">默认值</param>
    /// <returns></returns>
    public static int AsInt(this string str, int defaultVal = 0)
    {
        int d;
        return int.TryParse(str, out d) ? d : defaultVal;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="str"></param>
    /// <param name="defaultVal"></param>
    /// <returns></returns>
    public static long AsLong(this string str, int defaultVal = 0)
    {
        long d;
        return long.TryParse(str, out d) ? d : defaultVal;
    }

    /// <summary>
    /// ToString，失败返回默认值
    /// </summary>
    /// <param name="value"></param>
    /// <param name="defaultValue"></param>
    /// <returns></returns>
    public static string AsString(this object value, string defaultValue = "")
    {
        string result;
        try
        {
            result = Convert.ToString(value);
        }
        catch (Exception)
        {
            result = defaultValue;
        }

        return result;
    }

    /// <summary>
    /// 字符串转bool
    /// </summary>
    /// <param name="str"></param>
    /// <returns></returns>
    public static bool AsBool(this string str)
    {
        bool d;
        return bool.TryParse(str, out d) ? d : false;
    }

    /// <summary>
    /// 将字符串转换为时间
    /// </summary>
    /// <param name="str">字符串</param>
    /// <returns></returns>
    public static DateTime AsDateTime(this string str)
    {
        DateTime time;
        return DateTime.TryParse(str, out time) ? time : DateTime.MinValue;
    }

    /// <summary>
    /// 重载字符串转时间，这里可以定义日期格式
    /// </summary>
    /// <param name="str"></param>
    /// <param name="format"></param>
    /// <returns></returns>
    public static DateTime AsDateTime(this string str, string format)
    {
        DateTime time;
        DateTime.TryParseExact(str, format, CultureInfo.CurrentCulture, DateTimeStyles.None, out time);

        return time;
    }

    /// <summary>
    /// 将字符串转换为时间
    /// </summary>
    /// <param name="str">字符串</param>
    /// <returns></returns>
    public static DateTime? AsNullableDateTime(this string str)
    {
        if (string.IsNullOrWhiteSpace(str))
        {
            return null;
        }

        DateTime dt;
        if (DateTime.TryParse(str, out dt))
        {
            return dt;
        }

        // 格式：43478.3333333333
        double d;
        if (double.TryParse(str, out d))
        {
            return DateTime.FromOADate(d);
        }

        // 格式：/OADate(43477)/
        var rg = new Regex(@"(?<=\()[^\(\)]+(?=\))", RegexOptions.Multiline | RegexOptions.Singleline);
        if (double.TryParse(rg.Match(str).Value, out d))
        {
            return DateTime.FromOADate(d);
        }

        return null;
    }
}