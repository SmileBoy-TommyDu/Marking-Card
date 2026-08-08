using SkiaSharp;
using System.Windows.Input;
using Cursor = System.Windows.Input.Cursor;

namespace DrSoft.Drawing.Controls.Tools;

public interface IToolSelectSession
{
    string Name { get; }

    bool TryMouseDown(SKPoint point, out string message);

    bool TryMouseMove(SKPoint point, out string message);

    bool TryMouseUp(SKPoint point, out string message);

    bool TryRightMouseDown(SKPoint point, out string message);

    bool IsActive { get; }

    Cursor? SuggestedCursor { get; }

    ControlPointType? CompletedControlPoint { get; }

    void Cancel();
}
