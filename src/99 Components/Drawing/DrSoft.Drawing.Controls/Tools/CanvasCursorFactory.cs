using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Windows.Input;
using Application = System.Windows.Application;
using Cursor = System.Windows.Input.Cursor;
using Cursors = System.Windows.Input.Cursors;

namespace DrSoft.Drawing.Controls.Tools;

/// <summary>
/// 统一管理画布交互使用的自定义光标，避免在高频 hover 路径里重复加载资源。
/// </summary>
internal static class CanvasCursorFactory
{
    private static readonly ConcurrentDictionary<string, Lazy<Cursor>> CursorCache = new();

    public static Cursor GetMoveCursor(bool isActive = false)
        => GetCursor(isActive ? "MoveActive" : "Move", Cursors.SizeAll);

    public static Cursor GetCursor(string cursorName, Cursor fallback)
    {
        if (string.IsNullOrWhiteSpace(cursorName))
            return fallback;

        return CursorCache.GetOrAdd(
            cursorName,
            name => new Lazy<Cursor>(
                () => LoadCursor(name, fallback),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private static Cursor LoadCursor(string cursorName, Cursor fallback)
    {
        try
        {
            var streamInfo = Application.GetResourceStream(
                new Uri($"pack://application:,,,/DrSoft.Drawing.Controls;component/Resources/{cursorName}.cur"));
            if (streamInfo?.Stream != null)
            {
                using var stream = streamInfo.Stream;
                return new Cursor(stream);
            }
        }
        catch
        {
        }

        return fallback;
    }
}